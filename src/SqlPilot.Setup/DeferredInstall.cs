// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SqlPilot.Setup;
public sealed record DeferredStatus(bool Finished, bool Success, string Message);

public static class DeferredInstall
{
    public static string Queue(Host host, bool allowDowngrade)
    {
        InstalledVersions.EnsureAllowed(host, allowDowngrade);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "PendingSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string executable = Path.Combine(folder, "SqlPilotSetup.exe");
        File.Copy(Environment.ProcessPath!, executable);
        string report = Path.Combine(folder, "status.json");
        Write(report, new(false, false, "Waiting for SSMS to close"));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--deferred-install", "--selection", HostSelection.Encode(new[] { host.Ide }), "--report", report })
            start.ArgumentList.Add(argument);
        if (allowDowngrade)
            start.ArgumentList.Add("--allow-downgrade");
        using var process = Process.Start(start) ?? throw new Exception("Could not start background setup.");
        return report;
    }
    public static DeferredStatus? Read(string report)
    {
        try
        {
            return JsonSerializer.Deserialize<DeferredStatus>(File.ReadAllText(report));
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }
    static void Write(string report, DeferredStatus status)
    {
        string temporary = report + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(status));
        File.Move(temporary, report, true);
    }
    internal static async Task WaitUntilClosed(Func<bool> running, Func<bool> cancelled, TimeSpan timeout, TimeSpan interval)
    {
        var watch = Stopwatch.StartNew();
        while (running())
        {
            if (cancelled())
                throw new OperationCanceledException("Pending update cancelled. No installation changes were made.");
            if (watch.Elapsed >= timeout)
                throw new TimeoutException("Pending update expired. Run setup again after closing SSMS.");
            await Task.Delay(interval);
        }
        if (cancelled())
            throw new OperationCanceledException("Pending update cancelled. No installation changes were made.");
    }
    public static void Run(string[] args)
    {
        string report = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? throw new ArgumentException("Missing background setup report.");
        try
        {
            if (!Program.Elevated())
                throw new Exception("Background setup requires administrator access.");
            var selection = HostSelection.Decode(args) ?? throw new Exception("Missing selected SSMS version.");
            var host = Detection.Find().GetAwaiter().GetResult().Single(h => selection.Contains(h.Ide));
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host.Ide.ToUpperInvariant())));
            using var gate = new Mutex(false, "Global\\SqlPilot.Setup." + key);
            bool acquired = false;
            try
            {
                try
                {
                    acquired = gate.WaitOne(0);
                }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                    throw new Exception("Another pending setup already owns this SSMS version. No changes were made.");
                WaitUntilClosed(() => Detection.Running(host), () => File.Exists(report + ".cancel"), TimeSpan.FromHours(24), TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                Write(report, new(false, false, "Applying update. Keep SSMS closed."));
                InstalledVersions.Refresh(host);
                var installer = new Installer();
                var messages = new List<string>();
                string result = installer.Install(host, messages.Add, args.Contains("--allow-downgrade")).GetAwaiter().GetResult();
                File.WriteAllLines(Path.Combine(Path.GetDirectoryName(report)!, "installation.log"), messages);
                Write(report, new(true, true, result));
            }
            finally { if (acquired) gate.ReleaseMutex(); }
        }
        catch (Exception ex) { Write(report, new(true, false, ex.Message)); Environment.ExitCode = 1; }
    }
    internal static int Checks(string root)
    {
        int passed = 0;
        WaitUntilClosed(() => false, () => false, TimeSpan.Zero, TimeSpan.Zero).GetAwaiter().GetResult();
        passed++;
        int calls = 0;
        WaitUntilClosed(() => ++calls < 3, () => false, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)).GetAwaiter().GetResult();
        if (calls != 3)
            throw new Exception("Background setup did not wait for all SSMS processes.");
        passed++;
        try
        {
            WaitUntilClosed(() => true, () => true, TimeSpan.FromSeconds(1), TimeSpan.Zero).GetAwaiter().GetResult();
            throw new Exception("Cancellation was ignored.");
        }
        catch (OperationCanceledException) { passed++; }
        try
        {
            WaitUntilClosed(() => false, () => true, TimeSpan.FromSeconds(1), TimeSpan.Zero).GetAwaiter().GetResult();
            throw new Exception("Last-moment cancellation was ignored.");
        }
        catch (OperationCanceledException) { passed++; }
        try
        {
            WaitUntilClosed(() => true, () => false, TimeSpan.Zero, TimeSpan.Zero).GetAwaiter().GetResult();
            throw new Exception("Timeout was ignored.");
        }
        catch (TimeoutException) { passed++; }
        string report = Path.Combine(root, "deferred-check.json");
        Write(report, new(false, false, "Waiting"));
        if (Read(report)?.Finished != false)
            throw new Exception("Waiting state was reported as complete.");
        passed++;
        Write(report, new(true, true, "Complete"));
        if (Read(report)?.Success != true || Read(report)?.Finished != true)
            throw new Exception("Completed state missing.");
        passed++;
        File.WriteAllText(report, "invalid");
        if (Read(report) != null)
            throw new Exception("Incomplete status was accepted.");
        passed++;
        return passed;
    }
}
