using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace BetterTaskManager
{
    internal static class UiText
    {
        private sealed class HookMarker { }

        private static readonly ConditionalWeakTable<Control, HookMarker> HookedControls = new ConditionalWeakTable<Control, HookMarker>();

        private static readonly Dictionary<string, string> German = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Apps"] = "Anwendungen",
            ["Processes"] = "Prozesse",
            ["Network"] = "Netzwerk",
            ["History"] = "Verlauf",
            ["Memory"] = "Arbeitsspeicher",
            ["Live monitoring"] = "Live-Überwachung",
            ["Paused"] = "Pausiert",
            ["Standard mode"] = "Standardmodus",
            ["Restart as Admin"] = "Als Admin starten",
            ["Refresh"] = "Aktualisieren",
            ["Refresh Apps"] = "Aktualisieren",
            ["Force Kill"] = "Beenden",
            ["Export CSV"] = "CSV exportieren",
            ["Open Folder"] = "Ordner öffnen",
            ["Copy Path"] = "Pfad kopieren",
            ["Block App"] = "Blockieren",
            ["Unblock App"] = "Freigeben",
            ["View Processes"] = "Prozesse anzeigen",
            ["Clear History"] = "Verlauf löschen",
            ["Previous"] = "Zurück",
            ["Next"] = "Weiter",
            ["Record history"] = "Verlauf aufzeichnen",
            ["Trim App Memory"] = "App-Arbeitsspeicher trimmen",
            ["Clear Standby Cache"] = "Standbycache leeren",
            ["Release System Cache"] = "Systemcache freigeben",
            ["Maintenance actions"] = "Wartungsaktionen",
            ["Application"] = "Anwendung",
            ["Procs"] = "Proz.",
            ["Conn"] = "Verb.",
            ["Process"] = "Prozess",
            ["User"] = "Benutzer",
            ["Protocol"] = "Protokoll",
            ["Local"] = "Lokal",
            ["Remote"] = "Remote",
            ["Local Address"] = "Lokale Adresse",
            ["Local Port"] = "Lokaler Port",
            ["Remote Address"] = "Remoteadresse",
            ["Remote Port"] = "Remoteport",
            ["State"] = "Status",
            ["Application Path"] = "Anwendungspfad",
            ["Private Bytes MB"] = "Private Bytes (MB)",
            ["Working Set MB"] = "Arbeitssatz (MB)",
            ["Peak Working Set MB"] = "Max. Arbeitssatz (MB)",
            ["Threads"] = "Threads",
            ["Timestamp"] = "Zeitstempel",
            ["Search apps"] = "Anwendungen suchen",
            ["Filter:"] = "Filter:",
            ["Select an app"] = "Anwendung auswählen",
            ["Connections"] = "Verbindungen",
            ["Group Connections"] = "Gruppenverbindungen",
            ["Sum Private Bytes"] = "Summe Private Bytes",
            ["Sum Working Set"] = "Summe Arbeitssatz",
            ["Firewall"] = "Firewall",
            ["Unknown"] = "Unbekannt",
            ["BTM Blocked"] = "Durch BTM blockiert",
            ["Not blocked by BTM"] = "Nicht durch BTM blockiert",
            ["Established"] = "Hergestellt",
            ["Listening"] = "Abhören",
            ["Time Wait"] = "Wartend",
            ["Close Wait"] = "Schließen wartet",
            ["Fin Wait 1"] = "FIN-Warten 1",
            ["Fin Wait 2"] = "FIN-Warten 2",
            ["System CPU"] = "System-CPU",
            ["Physical Load"] = "Physische Auslastung",
            ["Used RAM"] = "Belegter RAM",
            ["Available RAM"] = "Verfügbarer RAM",
            ["Commit / Limit"] = "Commit / Limit",
            ["System Cache"] = "Systemcache",
            ["System CPU - last 60 samples"] = "System-CPU – letzte 60 Messwerte",
            ["Physical RAM Load - last 60 samples"] = "RAM-Auslastung – letzte 60 Messwerte",
            ["waiting for samples"] = "Warte auf Messwerte",
            ["Administrator"] = "Administrator",
            ["Administrator · memory privilege ready"] = "Administrator · Speicherrecht bereit",
            ["Administrator · memory privilege unavailable"] = "Administrator · Speicherrecht nicht verfügbar",
            ["Running as administrator"] = "Als Administrator ausgeführt",
            ["Confirm"] = "Bestätigen",
            ["Export complete"] = "Export abgeschlossen",
            ["Better Task Manager Error"] = "Better Task Manager – Fehler",
            ["Process refresh failed"] = "Prozessaktualisierung fehlgeschlagen",
            ["Network refresh failed"] = "Netzwerkaktualisierung fehlgeschlagen",
            ["Clear History failed"] = "Löschen des Verlaufs fehlgeschlagen",
            ["Process exited"] = "Prozess beendet",
            ["Stale process selection"] = "Veraltete Prozessauswahl",
            ["Confirm force kill"] = "Beenden erzwingen bestätigen",
            ["Export Processes CSV"] = "Prozesse als CSV exportieren",
            ["Export Network CSV"] = "Netzwerk als CSV exportieren",
            ["Export Apps CSV"] = "Anwendungen als CSV exportieren",
            ["Export History CSV"] = "Verlauf als CSV exportieren",
            ["CSV files (*.csv)|*.csv|All files (*.*)|*.*"] = "CSV-Dateien (*.csv)|*.csv|Alle Dateien (*.*)|*.*",
            ["Toggle Live monitoring (Ctrl+L)"] = "Live-Überwachung umschalten (Strg+L)",
            ["Open Apps (Ctrl+1)"] = "Anwendungen öffnen (Strg+1)",
            ["Open Processes (Ctrl+2)"] = "Prozesse öffnen (Strg+2)",
            ["Open Network (Ctrl+3)"] = "Netzwerk öffnen (Strg+3)",
            ["Open History (Ctrl+4)"] = "Verlauf öffnen (Strg+4)",
            ["Open Memory (Ctrl+5)"] = "Arbeitsspeicher öffnen (Strg+5)",
            ["Previous History page (Page Up)"] = "Vorherige Verlaufsseite (Bild auf)",
            ["Next History page (Page Down)"] = "Nächste Verlaufsseite (Bild ab)",
            ["Refresh active view (F5)"] = "Aktive Ansicht aktualisieren (F5)",
            ["Export active view (Ctrl+E)"] = "Aktive Ansicht exportieren (Strg+E)",
            ["Open the selected executable's folder"] = "Ordner der ausgewählten Programmdatei öffnen",
            ["Copy the selected executable path"] = "Pfad der ausgewählten Programmdatei kopieren",
            ["Focus search (Ctrl+F); clear search (Escape)"] = "Suche fokussieren (Strg+F); Suche leeren (Esc)",
            ["Purge the Windows standby list for troubleshooting."] = "Windows-Standbyliste zur Fehlerbehebung leeren.",
            ["Empty system working sets for troubleshooting."] = "Systemarbeitssätze zur Fehlerbehebung leeren.",
            ["Modify Better Task Manager's outbound block rule for the selected executable."] = "Ausgehende BTM-Blockierregel für die ausgewählte Programmdatei ändern.",
            ["Modify Better Task Manager's outbound block rule; Windows will request administrator approval."] = "Ausgehende BTM-Blockierregel ändern; Windows fordert Administratorbestätigung an.",
            ["Shows new and changed connections from the last 30 days (newest first)."] = "Zeigt neue und geänderte Verbindungen der letzten 30 Tage (neueste zuerst).",
            ["Live ports and destinations."] = "Live-Ports und Ziele.",
            ["Total bandwidth: waiting for second sample"] = "Gesamtbandbreite: Warte auf zweiten Messwert",
            ["Snapshot unavailable"] = "Momentaufnahme nicht verfügbar",
            ["Visible rows: 0"] = "Sichtbare Zeilen: 0",
            ["Refresh to load application activity"] = "Aktualisieren, um Anwendungsaktivität zu laden",
            ["Select an app to inspect its Better Task Manager firewall rule."] = "Anwendung auswählen, um ihre BTM-Firewallregel zu prüfen.",
            ["No executable path is available for a program-specific rule."] = "Für eine programmspezifische Regel ist kein Programmpfad verfügbar.",
            ["Windows uses available RAM for cache intentionally. Cleanup actions are troubleshooting tools, not routine optimization."] = "Windows nutzt verfügbaren RAM absichtlich als Cache. Bereinigungen dienen der Fehlerbehebung, nicht der regelmäßigen Optimierung.",
            ["System-memory privilege active."] = "Systemspeicherrecht aktiv.",
            ["There are no rows to export."] = "Es sind keine Zeilen zum Exportieren vorhanden.",
            ["The selected app has no active process IDs in this snapshot."] = "Die ausgewählte Anwendung hat in dieser Momentaufnahme keine aktiven Prozess-IDs.",
            ["This app has no usable executable path, so a Windows Firewall app rule cannot be created."] = "Diese Anwendung hat keinen nutzbaren Programmpfad; eine Windows-Firewallregel kann nicht erstellt werden.",
            ["Select a process first."] = "Wählen Sie zuerst einen Prozess aus.",
            ["Better Task Manager cannot force-kill its own process."] = "Better Task Manager kann den eigenen Prozess nicht zwangsweise beenden.",
            ["The selected process has already exited. Refresh the Process view."] = "Der ausgewählte Prozess wurde bereits beendet. Aktualisieren Sie die Prozessansicht.",
            ["This PID now belongs to a different process. Refresh and select the process again."] = "Diese PID gehört jetzt zu einem anderen Prozess. Aktualisieren Sie und wählen Sie den Prozess erneut aus.",
            ["The selected executable folder is unavailable."] = "Der Ordner der ausgewählten Programmdatei ist nicht verfügbar.",
            ["The selected row has no executable path."] = "Die ausgewählte Zeile hat keinen Programmpfad.",
            ["The Windows clipboard remained busy. Try again."] = "Die Windows-Zwischenablage blieb belegt. Versuchen Sie es erneut.",
            ["Administrator approval was cancelled. The firewall was not changed."] = "Die Administratorbestätigung wurde abgebrochen. Die Firewall wurde nicht geändert.",
            ["Blocked outbound network access for this app."] = "Ausgehenden Netzwerkzugriff für diese Anwendung blockiert.",
            ["Removed this app's Better Task Manager block rule."] = "BTM-Blockierregel dieser Anwendung entfernt.",
            ["Clear Windows standby cache?"] = "Windows-Standbycache leeren?",
            ["Connection history cleared. Live monitoring can record new changes."] = "Verbindungsverlauf gelöscht. Die Live-Überwachung kann neue Änderungen aufzeichnen.",
            ["Connection history cleared. Recording remains off."] = "Verbindungsverlauf gelöscht. Die Aufzeichnung bleibt deaktiviert.",
            ["Recording off."] = "Aufzeichnung aus.",
            ["Success."] = "Erfolgreich.",
            ["1 sec"] = "1 Sek.",
            ["2 sec"] = "2 Sek.",
            ["5 sec"] = "5 Sek.",
            ["15 sec"] = "15 Sek."
        };

        private static readonly KeyValuePair<string, string>[] GermanReplacements = new[]
        {
            Pair("Force kill this process and its child processes?", "Diesen Prozess und seine untergeordneten Prozesse zwangsweise beenden?"),
            Pair("Group Connections", "Gruppenverbindungen"),
            Pair("Not blocked by BTM", "Nicht durch BTM blockiert"),
            Pair("BTM Blocked", "Durch BTM blockiert"),
            Pair("Sum Private Bytes", "Summe Private Bytes"),
            Pair("Sum Working Set", "Summe Arbeitssatz"),
            Pair("Physical Load", "Physische Auslastung"),
            Pair("Available RAM", "Verfügbarer RAM"),
            Pair("Used RAM", "Belegter RAM"),
            Pair("System CPU", "System-CPU"),
            Pair("System Cache", "Systemcache"),
            Pair("Unknown", "Unbekannt"),
            Pair("Trim memory for all accessible apps? Better Task Manager itself is excluded.", "Arbeitsspeicher aller zugänglichen Anwendungen trimmen? Better Task Manager selbst wird ausgeschlossen."),
            Pair("This can reduce visible RAM use, but apps may reload data afterward.", "Dies kann die sichtbare RAM-Nutzung reduzieren; Anwendungen können Daten danach erneut laden."),
            Pair("Release system cache/working sets? Use this only for troubleshooting memory pressure.", "Systemcache/Arbeitssätze freigeben? Nur zur Fehlerbehebung bei Speicherdruck verwenden."),
            Pair("Block outbound network access for:", "Ausgehenden Netzwerkzugriff blockieren für:"),
            Pair("The selected connection has no executable path.", "Die ausgewählte Verbindung hat keinen Programmpfad."),
            Pair("Some protected processes hide path data; try Restart as Admin and refresh.", "Einige geschützte Prozesse verbergen Pfaddaten; starten Sie als Administrator neu und aktualisieren Sie."),
            Pair("No Better Task Manager outbound block rule.", "Keine ausgehende BTM-Blockierregel."),
            Pair("Other Windows Firewall policies may still apply.", "Andere Windows-Firewallrichtlinien können weiterhin gelten."),
            Pair("Active outbound block on all profiles:", "Aktive ausgehende Blockierung für alle Profile:"),
            Pair("Better Task Manager could not read the rule state.", "Better Task Manager konnte den Regelstatus nicht lesen."),
            Pair("Rule name:", "Regelname:"),
            Pair("Loading processes...", "Prozesse werden geladen..."),
            Pair("Loading network connections...", "Netzwerkverbindungen werden geladen..."),
            Pair("Loading apps...", "Anwendungen werden geladen..."),
            Pair("Collecting processes and connections", "Prozesse und Verbindungen werden erfasst"),
            Pair("Refreshing processes, usernames, and executable paths...", "Prozesse, Benutzernamen und Programmpfade werden aktualisiert..."),
            Pair("Refreshing Better Task Manager firewall rule state...", "BTM-Firewallregelstatus wird aktualisiert..."),
            Pair("Loading recent connection changes...", "Letzte Verbindungsänderungen werden geladen..."),
            Pair("Refreshing live connection history...", "Live-Verbindungsverlauf wird aktualisiert..."),
            Pair("Clearing connection history...", "Verbindungsverlauf wird gelöscht..."),
            Pair("Clearing standby cache...", "Standbycache wird geleert..."),
            Pair("Releasing system cache/working sets...", "Systemcache/Arbeitssätze werden freigegeben..."),
            Pair("Process identity refresh failed", "Aktualisierung der Prozessidentitäten fehlgeschlagen"),
            Pair("Process refresh failed:", "Prozessaktualisierung fehlgeschlagen:"),
            Pair("Network refresh failed:", "Netzwerkaktualisierung fehlgeschlagen:"),
            Pair("Memory snapshot failed:", "Arbeitsspeicher-Momentaufnahme fehlgeschlagen:"),
            Pair("Working-set trim failed:", "Arbeitssatz-Trimmen fehlgeschlagen:"),
            Pair("Clear standby cache:", "Standbycache leeren:"),
            Pair("System working set cleanup:", "Systemarbeitssätze bereinigen:"),
            Pair("Working-set trim:", "Arbeitssatz-Trimmen:"),
            Pair("protected/access denied", "geschützt/Zugriff verweigert"),
            Pair("exited during scan", "während der Prüfung beendet"),
            Pair("other failures", "andere Fehler"),
            Pair("skipped (System/BTM)", "übersprungen (System/BTM)"),
            Pair("Restart as Admin may reduce denials; protected services can still refuse.", "Als Administrator neu starten kann Ablehnungen reduzieren; geschützte Dienste können weiterhin verweigern."),
            Pair("Protected services and security processes can still refuse trimming.", "Geschützte Dienste und Sicherheitsprozesse können das Trimmen weiterhin verweigern."),
            Pair("System-memory actions require Restart as Admin and SeProfileSingleProcessPrivilege.", "Systemspeicheraktionen erfordern einen Administratorneustart und SeProfileSingleProcessPrivilege."),
            Pair("the two system-memory actions are unavailable", "die beiden Systemspeicheraktionen sind nicht verfügbar"),
            Pair("This elevated token does not contain SeProfileSingleProcessPrivilege", "Dieses erhöhte Token enthält SeProfileSingleProcessPrivilege nicht"),
            Pair("Windows could not enable SeProfileSingleProcessPrivilege", "Windows konnte SeProfileSingleProcessPrivilege nicht aktivieren"),
            Pair("Windows did not grant SeProfileSingleProcessPrivilege to this process.", "Windows hat diesem Prozess SeProfileSingleProcessPrivilege nicht gewährt."),
            Pair("Windows denied access.", "Windows hat den Zugriff verweigert."),
            Pair("Windows returned native status", "Windows gab den nativen Status zurück"),
            Pair("Administrator restart was cancelled.", "Administratorneustart wurde abgebrochen."),
            Pair("Elevation cancelled", "Erhöhung abgebrochen"),
            Pair("Elevation failed", "Erhöhung fehlgeschlagen"),
            Pair("Could not restart as administrator.", "Neustart als Administrator nicht möglich."),
            Pair("Could not open the executable folder.", "Ordner der Programmdatei konnte nicht geöffnet werden."),
            Pair("Could not copy the executable path.", "Programmpfad konnte nicht kopiert werden."),
            Pair("CSV export failed.", "CSV-Export fehlgeschlagen."),
            Pair("Exported ", "Exportiert: "),
            Pair(" process rows to:", " Prozesszeilen nach:"),
            Pair(" network rows to:", " Netzwerkzeilen nach:"),
            Pair(" app rows to:", " Anwendungszeilen nach:"),
            Pair(" history rows to:", " Verlaufszeilen nach:"),
            Pair("Snapshot ", "Momentaufnahme "),
            Pair("Snapshot unavailable", "Momentaufnahme nicht verfügbar"),
            Pair("processes aggregated", "Prozesse zusammengefasst"),
            Pair("CPU sampling...", "CPU wird gemessen..."),
            Pair("sampling...", "Messung..."),
            Pair("sampled", "gemessen"),
            Pair("CPU partial", "CPU teilweise"),
            Pair("User unknown", "Benutzer unbekannt"),
            Pair("Path unavailable", "Pfad nicht verfügbar"),
            Pair("Visible rows:", "Sichtbare Zeilen:"),
            Pair("Sum CPU:", "Summe CPU:"),
            Pair("Sum Private Bytes:", "Summe Private Bytes:"),
            Pair("Sum Working Set:", "Summe Arbeitssatz:"),
            Pair("Working-set sums can overlap shared pages.", "Arbeitssatzsummen können sich bei gemeinsam genutzten Seiten überschneiden."),
            Pair("Same Apps snapshot:", "Gleiche Anwendungs-Momentaufnahme:"),
            Pair("Running as administrator - identities refreshed", "Als Administrator ausgeführt – Identitäten aktualisiert"),
            Pair("Standard mode - identities refreshed where accessible", "Standardmodus – zugängliche Identitäten aktualisiert"),
            Pair("Standard mode: protected identities may be unavailable", "Standardmodus: Geschützte Identitäten können fehlen"),
            Pair("connections shown.", "Verbindungen angezeigt."),
            Pair("Per-app bandwidth needs ETW/WFP collector.", "Bandbreite pro Anwendung erfordert einen ETW/WFP-Sammler."),
            Pair("Total adapter bandwidth:", "Gesamte Adapterbandbreite:"),
            Pair("Down ", "Download "),
            Pair(", Up ", ", Upload "),
            Pair("waiting for stable per-adapter sample", "Warte auf stabilen Messwert pro Adapter"),
            Pair("stable adapters", "stabile Adapter"),
            Pair("stable adapter", "stabiler Adapter"),
            Pair("Network data partial:", "Netzwerkdaten unvollständig:"),
            Pair("native table warnings", "Warnungen nativer Tabellen"),
            Pair("native table warning", "Warnung nativer Tabelle"),
            Pair("Live error", "Live-Fehler"),
            Pair("Connection history cleared.", "Verbindungsverlauf gelöscht."),
            Pair("Live monitoring can record new changes.", "Die Live-Überwachung kann neue Änderungen aufzeichnen."),
            Pair("Recording remains off.", "Die Aufzeichnung bleibt deaktiviert."),
            Pair("Recording off.", "Aufzeichnung aus."),
            Pair("matches", "Treffer"),
            Pair("export includes all matches", "Export enthält alle Treffer"),
            Pair("new/changed connections", "neue/geänderte Verbindungen"),
            Pair("recorded", "aufgezeichnet"),
            Pair("retained rows", "beibehaltene Zeilen"),
            Pair("filtered", "gefiltert"),
            Pair("newest", "neueste"),
            Pair("Failed:", "Fehlgeschlagen:"),
            Pair(" failed.", " fehlgeschlagen."),
            Pair(" failed:", " fehlgeschlagen:"),
            Pair("Unknown error", "Unbekannter Fehler"),
            Pair("A crash log was written to", "Ein Absturzprotokoll wurde geschrieben nach")
        };

        internal static bool IsGerman => string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase);

        internal static string Translate(string text)
        {
            return IsGerman ? TranslateToGerman(text) : text ?? "";
        }

        internal static string TranslateToGerman(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string exact;
            if (German.TryGetValue(text, out exact)) return exact;
            string translated = text;
            foreach (KeyValuePair<string, string> replacement in GermanReplacements)
            {
                translated = translated.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
            }
            return translated;
        }

        internal static void ApplyTo(Control root, ToolTip toolTip, IEnumerable<ContextMenuStrip> menus)
        {
            if (!IsGerman || root == null) return;
            LocalizeControlTree(root, toolTip);
            foreach (ContextMenuStrip menu in menus ?? Enumerable.Empty<ContextMenuStrip>()) LocalizeToolStrip(menu.Items);
        }

        private static void LocalizeControlTree(Control control, ToolTip toolTip)
        {
            if (!(control is TextBoxBase)) control.Text = Translate(control.Text);

            var textBox = control as TextBox;
            if (textBox != null) textBox.PlaceholderText = Translate(textBox.PlaceholderText);
            var centeredTextBox = control as VerticallyCenteredTextBox;
            if (centeredTextBox != null) centeredTextBox.PlaceholderText = Translate(centeredTextBox.PlaceholderText);

            string tip = toolTip == null ? "" : toolTip.GetToolTip(control);
            if (!string.IsNullOrEmpty(tip)) toolTip.SetToolTip(control, Translate(tip));

            var grid = control as DataGridView;
            if (grid != null)
            {
                foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = Translate(column.HeaderText);
                grid.CellFormatting -= TranslateGridCell;
                grid.CellFormatting += TranslateGridCell;
            }

            var list = control as ListView;
            if (list != null)
            {
                foreach (ColumnHeader column in list.Columns) column.Text = Translate(column.Text);
            }

            var combo = control as ComboBox;
            if (combo != null)
            {
                for (int index = 0; index < combo.Items.Count; index++)
                {
                    if (combo.Items[index] is string) combo.Items[index] = Translate(Convert.ToString(combo.Items[index], CultureInfo.InvariantCulture));
                }
            }

            if (!(control is TextBoxBase) && !HookedControls.TryGetValue(control, out _))
            {
                HookedControls.Add(control, new HookMarker());
                control.TextChanged += TranslateControlText;
            }

            foreach (Control child in control.Controls) LocalizeControlTree(child, toolTip);
        }

        private static void TranslateControlText(object sender, EventArgs e)
        {
            if (!IsGerman) return;
            var control = sender as Control;
            if (control == null) return;
            string translated = TranslateToGerman(control.Text);
            if (!string.Equals(translated, control.Text, StringComparison.Ordinal)) control.Text = translated;
        }

        private static void TranslateGridCell(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (!IsGerman || !(e.Value is string)) return;
            string original = (string)e.Value;
            string translated = TranslateToGerman(original);
            if (!string.Equals(original, translated, StringComparison.Ordinal))
            {
                e.Value = translated;
                e.FormattingApplied = true;
            }
        }

        private static void LocalizeToolStrip(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.Text = Translate(item.Text);
                if (item is ToolStripMenuItem menuItem && menuItem.DropDownItems.Count > 0) LocalizeToolStrip(menuItem.DropDownItems);
            }
        }

        private static KeyValuePair<string, string> Pair(string english, string german)
        {
            return new KeyValuePair<string, string>(english, german);
        }
    }

    internal static class LocalizedMessageBox
    {
        internal static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return System.Windows.Forms.MessageBox.Show(owner, UiText.Translate(text), UiText.Translate(caption), buttons, icon);
        }

        internal static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return System.Windows.Forms.MessageBox.Show(UiText.Translate(text), UiText.Translate(caption), buttons, icon);
        }
    }
}
