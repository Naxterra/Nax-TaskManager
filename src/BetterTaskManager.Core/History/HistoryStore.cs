using System.Globalization;
using Microsoft.Data.Sqlite;

namespace BetterTaskManager.Core.History;

/// <summary>Network use of one app over a time range.</summary>
public sealed record AppUsage(string AppKey, string AppName, string AppPath, long BytesIn, long BytesOut, int Connections)
{
    public long Total => BytesIn + BytesOut;
}

/// <summary>One recorded connection or UDP flow.</summary>
public sealed record ConnectionRecord(
    long Id,
    DateTime FirstSeen,
    DateTime LastSeen,
    string AppKey,
    string AppName,
    string AppPath,
    int Pid,
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string? RemoteHost,
    bool RemoteHostIsReverse,
    long BytesIn,
    long BytesOut,
    string State);

/// <summary>
/// SQLite history of connections and daily per-app traffic. The background service is the only writer; the UI
/// opens the file read-only. Rollback-journal mode (not WAL) because WAL readers need write access to the -shm
/// file, and standard users only get read access to the data folder.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection connection;
    private SqliteCommand? insertConnection, updateConnection, addUsage;

    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NaxTaskManager");
    public static string DefaultPath => Path.Combine(DataFolder, "history.db");

    private HistoryStore(SqliteConnection connection) => this.connection = connection;

    public static HistoryStore OpenForWriting(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10
        }.ToString());
        connection.Open();
        var store = new HistoryStore(connection);
        store.Execute("PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL;");
        store.CreateSchema();
        return store;
    }

    /// <summary>Opens an existing history read-only, or returns null when nothing has been recorded yet.</summary>
    public static HistoryStore? OpenReadOnly(string path)
    {
        if (!File.Exists(path)) return null;
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return new HistoryStore(connection);
    }

    private void CreateSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS connections (
                id INTEGER PRIMARY KEY,
                first_seen INTEGER NOT NULL,
                last_seen INTEGER NOT NULL,
                app_key TEXT NOT NULL,
                app_name TEXT NOT NULL,
                app_path TEXT NOT NULL,
                pid INTEGER NOT NULL,
                protocol TEXT NOT NULL,
                local_address TEXT NOT NULL,
                local_port INTEGER NOT NULL,
                remote_address TEXT NOT NULL,
                remote_port INTEGER NOT NULL,
                remote_host TEXT,
                remote_host_reverse INTEGER NOT NULL DEFAULT 0,
                bytes_in INTEGER NOT NULL DEFAULT 0,
                bytes_out INTEGER NOT NULL DEFAULT 0,
                state TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS connections_last_seen ON connections(last_seen);
            CREATE INDEX IF NOT EXISTS connections_app ON connections(app_key, last_seen);
            CREATE TABLE IF NOT EXISTS app_usage (
                day TEXT NOT NULL,
                app_key TEXT NOT NULL,
                app_name TEXT NOT NULL,
                app_path TEXT NOT NULL,
                bytes_in INTEGER NOT NULL DEFAULT 0,
                bytes_out INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (day, app_key));
            """);
        SetMeta("schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes new and changed connections plus traffic per (local day, app) in one transaction.</summary>
    internal void Write(IReadOnlyList<TrackedConnection> changed, IReadOnlyDictionary<(string Day, string AppKey), UsageDelta> usage, DateTime nowUtc)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        insertConnection ??= Prepare("""
            INSERT INTO connections (first_seen, last_seen, app_key, app_name, app_path, pid, protocol, local_address, local_port,
                remote_address, remote_port, remote_host, remote_host_reverse, bytes_in, bytes_out, state)
            VALUES ($first, $last, $key, $name, $path, $pid, $protocol, $laddr, $lport, $raddr, $rport, $host, $reverse, $in, $out, $state)
            RETURNING id
            """, "$first", "$last", "$key", "$name", "$path", "$pid", "$protocol", "$laddr", "$lport", "$raddr", "$rport", "$host", "$reverse", "$in", "$out", "$state");
        updateConnection ??= Prepare("""
            UPDATE connections SET last_seen = $last, local_address = $laddr, remote_host = $host, remote_host_reverse = $reverse,
                bytes_in = $in, bytes_out = $out, state = $state
            WHERE id = $id
            """, "$last", "$laddr", "$host", "$reverse", "$in", "$out", "$state", "$id");
        addUsage ??= Prepare("""
            INSERT INTO app_usage (day, app_key, app_name, app_path, bytes_in, bytes_out) VALUES ($day, $key, $name, $path, $in, $out)
            ON CONFLICT (day, app_key) DO UPDATE SET app_name = excluded.app_name, app_path = excluded.app_path,
                bytes_in = bytes_in + excluded.bytes_in, bytes_out = bytes_out + excluded.bytes_out
            """, "$day", "$key", "$name", "$path", "$in", "$out");
        insertConnection.Transaction = updateConnection.Transaction = addUsage.Transaction = transaction;

        foreach (TrackedConnection item in changed)
        {
            if (item.Id == 0)
            {
                Bind(insertConnection, ToUnixMs(item.FirstSeen), ToUnixMs(item.LastSeen), item.App.Key, item.App.Name, item.App.Path, item.Pid,
                    item.Protocol, item.LocalAddress, item.LocalPort, item.RemoteAddress, item.RemotePort, (object?)item.RemoteHost ?? DBNull.Value,
                    item.RemoteHostIsReverse ? 1 : 0, item.BytesIn, item.BytesOut, item.State);
                item.Id = (long)insertConnection.ExecuteScalar()!;
            }
            else
            {
                Bind(updateConnection, ToUnixMs(item.LastSeen), item.LocalAddress, (object?)item.RemoteHost ?? DBNull.Value,
                    item.RemoteHostIsReverse ? 1 : 0, item.BytesIn, item.BytesOut, item.State, item.Id);
                updateConnection.ExecuteNonQuery();
            }
        }

        foreach (var ((day, key), delta) in usage)
        {
            Bind(addUsage, day, key, delta.App.Name, delta.App.Path, delta.BytesIn, delta.BytesOut);
            addUsage.ExecuteNonQuery();
        }

        SetMeta("last_write", ToUnixMs(nowUtc).ToString(CultureInfo.InvariantCulture), transaction);
        transaction.Commit();
    }

    /// <summary>Deletes connections and daily totals older than the retention period.</summary>
    public void Prune(TimeSpan retention, DateTime nowUtc)
    {
        DateTime cutoff = nowUtc - retention;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM connections WHERE last_seen < $cut; DELETE FROM app_usage WHERE day < $day;";
        command.Parameters.AddWithValue("$cut", ToUnixMs(cutoff));
        command.Parameters.AddWithValue("$day", Day(cutoff.ToLocalTime()));
        command.ExecuteNonQuery();
    }

    /// <summary>Per-app traffic and connection counts since the start of <paramref name="fromLocalDay"/>, biggest first.</summary>
    public List<AppUsage> ReadAppUsage(DateTime fromLocalDay)
    {
        var apps = new Dictionary<string, AppUsage>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT app_key, max(app_name), max(app_path), sum(bytes_in), sum(bytes_out)
                FROM app_usage WHERE day >= $day GROUP BY app_key
                """;
            command.Parameters.AddWithValue("$day", Day(fromLocalDay));
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                apps[reader.GetString(0)] = new AppUsage(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), 0);
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT app_key, max(app_name), max(app_path), count(*)
                FROM connections WHERE last_seen >= $since GROUP BY app_key
                """;
            command.Parameters.AddWithValue("$since", ToUnixMs(fromLocalDay.Date.ToUniversalTime()));
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                int count = reader.GetInt32(3);
                apps[key] = apps.TryGetValue(key, out AppUsage? known)
                    ? known with { Connections = count }
                    : new AppUsage(key, reader.GetString(1), reader.GetString(2), 0, 0, count);
            }
        }

        return apps.Values.OrderByDescending(app => app.Total).ThenByDescending(app => app.Connections).ToList();
    }

    /// <summary>Most recent connections first, optionally for one app and/or matching a search text.</summary>
    public List<ConnectionRecord> ReadConnections(DateTime sinceUtc, string? appKey, string? search, int limit)
    {
        using SqliteCommand command = connection.CreateCommand();
        var sql = new System.Text.StringBuilder("""
            SELECT id, first_seen, last_seen, app_key, app_name, app_path, pid, protocol, local_address, local_port,
                remote_address, remote_port, remote_host, remote_host_reverse, bytes_in, bytes_out, state
            FROM connections WHERE last_seen >= $since
            """);
        command.Parameters.AddWithValue("$since", ToUnixMs(sinceUtc));
        if (!string.IsNullOrEmpty(appKey))
        {
            sql.Append(" AND app_key = $key");
            command.Parameters.AddWithValue("$key", appKey);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql.Append(" AND (app_name LIKE $q ESCAPE '\\' OR remote_host LIKE $q ESCAPE '\\' OR remote_address LIKE $q ESCAPE '\\' OR app_path LIKE $q ESCAPE '\\')");
            string escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            command.Parameters.AddWithValue("$q", "%" + escaped + "%");
        }
        sql.Append(" ORDER BY last_seen DESC LIMIT $limit");
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = sql.ToString();

        var records = new List<ConnectionRecord>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new ConnectionRecord(
                reader.GetInt64(0),
                FromUnixMs(reader.GetInt64(1)),
                FromUnixMs(reader.GetInt64(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetInt64(13) != 0,
                reader.GetInt64(14),
                reader.GetInt64(15),
                reader.GetString(16)));
        }
        return records;
    }

    /// <summary>When the service last wrote, or null if it never has.</summary>
    public DateTime? ReadLastWrite()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'last_write'";
        return command.ExecuteScalar() is string value && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms)
            ? FromUnixMs(ms)
            : null;
    }

    internal static string Day(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static long ToUnixMs(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    private static DateTime FromUnixMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    private void SetMeta(string key, string value, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private SqliteCommand Prepare(string sql, params string[] names)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (string name in names) command.Parameters.Add(new SqliteParameter { ParameterName = name });
        return command;
    }

    private static void Bind(SqliteCommand command, params object[] values)
    {
        for (int index = 0; index < values.Length; index++) command.Parameters[index].Value = values[index];
    }

    public void Dispose()
    {
        insertConnection?.Dispose();
        updateConnection?.Dispose();
        addUsage?.Dispose();
        connection.Dispose();
    }
}
