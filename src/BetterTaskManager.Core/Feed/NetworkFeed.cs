using System.IO.Pipes;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using BetterTaskManager.Core.History;
using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Feed;

/// <summary>Per-app traffic as measured by the history service.</summary>
public readonly record struct ProcessTraffic(int Pid, long CreateTime, double ReceivePerSecond, double SendPerSecond, long ReceivedTotal, long SentTotal);

/// <summary>Current rate of one connection or UDP socket with traffic.</summary>
public readonly record struct SocketTraffic(int Pid, string Protocol, int LocalPort, string RemoteAddress, int RemotePort, double ReceivePerSecond, double SendPerSecond);

public sealed record NetworkFeedMessage(DateTime TimestampUtc, List<ProcessTraffic> Processes, List<SocketTraffic> Sockets)
{
    /// <summary>Builds the message from a service snapshot: only processes and sockets that moved data.</summary>
    public static NetworkFeedMessage From(MonitorSnapshot snapshot) => new(
        snapshot.Timestamp.ToUniversalTime(),
        snapshot.Processes
            .Where(process => process.NetworkReceivedTotal + process.NetworkSentTotal > 0)
            .Select(process => new ProcessTraffic(process.Pid, process.CreateTime, process.NetworkReceiveBytesPerSecond,
                process.NetworkSendBytesPerSecond, process.NetworkReceivedTotal, process.NetworkSentTotal))
            .ToList(),
        snapshot.Connections
            .Where(connection => connection.ReceiveBytesPerSecond + connection.SendBytesPerSecond > 0)
            .Select(connection => new SocketTraffic(connection.Pid, connection.Protocol, connection.LocalPort, connection.RemoteAddress,
                connection.RemotePort, connection.ReceiveBytesPerSecond, connection.SendBytesPerSecond))
            .ToList());
}

/// <summary>
/// Lets the app show per-app network speed without administrator rights: the history service (LocalSystem, which
/// may run the kernel network trace) publishes each sample on a local named pipe. Frames are a 4-byte little-endian
/// length followed by UTF-8 JSON of <see cref="NetworkFeedMessage"/>. The pipe is one-way, service to app.
/// </summary>
public static class NetworkFeed
{
    public const string PipeName = "NaxTaskManager.NetworkFeed";
    internal const int MaxFrameBytes = 16 * 1024 * 1024;
}

/// <summary>Source-generated serializer: no reflection, so it also works trimmed or AOT-compiled.</summary>
[JsonSerializable(typeof(NetworkFeedMessage))]
public sealed partial class NetworkFeedJson : JsonSerializerContext;

/// <summary>Service side: accepts any number of readers and sends every published message to each of them.</summary>
public sealed class NetworkFeedServer : IDisposable
{
    private readonly object gate = new();
    private readonly List<Channel<byte[]>> readers = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Action<string> log;
    private Task? acceptLoop;

    public NetworkFeedServer(Action<string> log) => this.log = log;

    public int ReaderCount
    {
        get { lock (gate) return readers.Count; }
    }

    public void Start() => acceptLoop ??= Task.Run(AcceptAsync);

    public void Publish(NetworkFeedMessage message)
    {
        List<Channel<byte[]>> targets;
        lock (gate)
        {
            if (readers.Count == 0) return;
            targets = readers.ToList();
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, NetworkFeedJson.Default.NetworkFeedMessage);
        byte[] frame = new byte[4 + json.Length];
        BitConverter.TryWriteBytes(frame, json.Length);
        json.CopyTo(frame, 4);
        // Slow readers lose old frames rather than holding up the service.
        foreach (Channel<byte[]> reader in targets) reader.Writer.TryWrite(frame);
    }

    private async Task AcceptAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(NetworkFeed.PipeName, PipeDirection.Out, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 64 * 1024, CreateSecurity());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log("Network feed pipe could not be created: " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(30), shutdown.Token); } catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(shutdown.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await pipe.DisposeAsync();
                if (shutdown.IsCancellationRequested) return;
                continue;
            }
            _ = ServeAsync(pipe);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (gate) readers.Add(channel);
        try
        {
            await foreach (byte[] frame in channel.Reader.ReadAllAsync(shutdown.Token))
            {
                await pipe.WriteAsync(frame, shutdown.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Reader went away or the service is stopping.
        }
        finally
        {
            lock (gate) readers.Remove(channel);
            await pipe.DisposeAsync();
        }
    }

    /// <summary>SYSTEM and administrators own the pipe; signed-in users may only read it.</summary>
    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.Read | PipeAccessRights.WriteAttributes | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }

    public void Dispose()
    {
        shutdown.Cancel();
        lock (gate)
        {
            foreach (Channel<byte[]> reader in readers) reader.Writer.TryComplete();
        }
        try { acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}

/// <summary>
/// App side: keeps a connection to the service's feed while the service runs and exposes the newest message.
/// Only a pipe served by the history service's own process is trusted, so another program cannot feed fake data
/// by creating the pipe name first.
/// </summary>
public sealed class NetworkFeedClient : IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource shutdown = new();
    private volatile NetworkFeedMessage? latest;
    private long receivedAt;
    private Task? loop;

    public void Start() => loop ??= Task.Run(RunAsync);

    /// <summary>The newest message, or null when the service is not running or has gone quiet.</summary>
    public NetworkFeedMessage? Current =>
        latest is { } message && System.Diagnostics.Stopwatch.GetElapsedTime(Interlocked.Read(ref receivedAt)) < MaxAge ? message : null;

    private async Task RunAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                if (HistoryServiceControl.QueryState() == HistoryServiceState.Running) await ReadAsync();
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or JsonException or InvalidDataException or ManagementException)
            {
                // Service restarting, pipe closed or not trusted: try again shortly.
            }
            latest = null;
            try { await Task.Delay(RetryDelay, shutdown.Token); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task ReadAsync()
    {
        await using var pipe = new NamedPipeClientStream(".", NetworkFeed.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, shutdown.Token);
        if (!IsServedByHistoryService(pipe)) throw new UnauthorizedAccessException("The network feed pipe is not served by the history service.");

        byte[] header = new byte[4];
        while (!shutdown.IsCancellationRequested)
        {
            await pipe.ReadExactlyAsync(header, shutdown.Token);
            int length = BitConverter.ToInt32(header);
            if (length <= 0 || length > NetworkFeed.MaxFrameBytes) throw new InvalidDataException("Invalid feed frame length " + length);
            byte[] body = new byte[length];
            await pipe.ReadExactlyAsync(body, shutdown.Token);
            latest = JsonSerializer.Deserialize(body, NetworkFeedJson.Default.NetworkFeedMessage);
            Interlocked.Exchange(ref receivedAt, System.Diagnostics.Stopwatch.GetTimestamp());
        }
    }

    private static bool IsServedByHistoryService(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid)) return false;
        using var searcher = new ManagementObjectSearcher($"SELECT ProcessId FROM Win32_Service WHERE Name = '{HistoryServiceControl.ServiceName}'");
        foreach (ManagementBaseObject service in searcher.Get())
        {
            using (service)
            {
                if (Convert.ToUInt32(service["ProcessId"]) == serverPid) return true;
            }
        }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

    public void Dispose()
    {
        shutdown.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}
