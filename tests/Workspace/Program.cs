using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SqlPilot.Ssms;
class Program
{
    [STAThread]
    static void Main()
    {
        int n = 0;
        Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); n++; };
        string root = Path.Combine(Path.GetTempPath(), "SqlPilot.WorkspaceTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "session.bin");
        var tabs = new[] { new SessionTab { Title = "one.sql", Path = "Query1.sql", Sql = "SELECT N'تست';\r\n-- kept\r\n", Connection = "Server=test;Database=one;User Id=u;Password=FAKE-FIXTURE-ONLY", Line = 2, Column = 4, Active = true, Dirty = true }, new SessionTab { Title = "two.sql", Sql = "SELECT 2;", Connection = "Server=test;Database=two;Integrated Security=true" } };
        SessionFiles.Save(path, tabs);
        var loaded = SessionFiles.Load(path);
        check(loaded.Count == 2 && loaded[0].Sql == tabs[0].Sql && loaded[0].Connection == tabs[0].Connection, "DPAPI session exact Unicode SQL and connection roundtrip");
        check(loaded[0].Line == 2 && loaded[0].Column == 4 && loaded[0].Active && loaded[0].Dirty && loaded[1].Title == "two.sql", "tab order caret and active state preserved");
        check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("FAKE-FIXTURE-ONLY"), "session file does not expose credentials as plain text");
        SessionFiles.Save(path, new SessionTab[0]);
        check(SessionFiles.Load(path).Count == 0 && SessionFiles.Load(path + ".bak").Count == 2, "empty session persists and prior checkpoint is recoverable");
        File.WriteAllText(path, "corrupt");
        bool rejected = false;
        try
        {
            SessionFiles.Load(path);
        }
        catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
        check(rejected, "corrupt encrypted session fails safely");
        KeyboardShortcuts.Validate(KeyboardShortcuts.Defaults);
        check(true, "all default command gestures parse");
        var keys = new Dictionary<string, string>(KeyboardShortcuts.Defaults);
        keys["Format"] = "Ctrl+Alt+F";
        KeyboardShortcuts.Validate(keys);
        check(true, "custom command gesture supported");
        keys["Format"] = "Ctrl+Shift+A";
        rejected = false;
        try
        {
            KeyboardShortcuts.Validate(keys);
        }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "duplicate command gestures rejected");
        keys["Format"] = "Ctrl+V";
        rejected = false;
        try
        {
            KeyboardShortcuts.Validate(keys);
        }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "editor clipboard gesture protected");
        check(SqlPilot.UI.Design.Resources().Count >= 6, "shared glass design resources parse on Framework WPF");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, k => k == System.Windows.Input.Key.F) == "Format", "shell accelerator maps Ctrl Shift F to Format");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control, k => k == System.Windows.Input.Key.F) == null, "ordinary Find accelerator is not hijacked");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, k => k == System.Windows.Input.Key.A) == "Analyze", "shell accelerator preserves distinct command mappings");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control, k => k == System.Windows.Input.Key.OemPeriod) == "QuickFix", "shell punctuation shortcut matches normalized WPF key");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Alt, k => k == System.Windows.Input.Key.Enter) == "QuickFixAlt", "Alt Enter maps to diagnostic actions");
        check(!KeyboardShortcuts.Defaults.Keys.Any(k => k.StartsWith("Codex")), "retired AI commands are not registered");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control, k => k == System.Windows.Input.Key.Enter) == null, "Ctrl Enter is released to SSMS");
        string diagnosticLog = Path.Combine(root, "extension.log");
        for (int i = 0; i < 40; i++)
            SqlPilot.BoundedLog.Append(diagnosticLog, new string('a', 12), 64);
        check(new FileInfo(diagnosticLog).Length <= 64 && new FileInfo(diagnosticLog + ".1").Length <= 64, "diagnostic log and rotated copy stay bounded");
        File.WriteAllText(diagnosticLog, new string('a', 1000));
        SqlPilot.BoundedLog.Append(diagnosticLog, "fresh", 64);
        check(new FileInfo(diagnosticLog).Length <= 64 && !File.Exists(diagnosticLog + ".1"), "oversized legacy diagnostic log is discarded");
        SqlPilot.BoundedLog.Append(diagnosticLog, new string('ژ', 1000), 64);
        check(new FileInfo(diagnosticLog).Length <= 64, "large Unicode diagnostic entry stays bounded");
        AboutChecks.Run(check);
        ExplorerChecks.Run(check);
        LibraryChecks.Run(check);
        Console.WriteLine(n + " workspace checks passed.");
    }
}
namespace SqlPilot.Ssms
{
    internal static class Brand
    {
        internal static System.Windows.Media.Imaging.BitmapImage Icon() => null;
    }
    internal static class Notifications
    {
        internal static void Show(string message)
        {
        }
    }
    internal static class Diagnostics
    {
        internal static void Write(string message)
        {
        }
    }
}
