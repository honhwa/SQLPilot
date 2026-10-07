// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SqlPilot.Setup;
internal static class SetupStorage
{
    internal static bool SkipBundleCache;
    internal const long LogLimit = 128L * 1024 * 1024;
    internal static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot");
    static readonly Regex JobName = new("^[a-f0-9]{32}$", RegexOptions.IgnoreCase);
    static readonly Regex BackupName = new("^(legacy-backup|copy-backup|previous-package|retired-copies|discovery-cache-backup|unregistered-copy)-[a-f0-9]{32}(\\.vsix)?$", RegexOptions.IgnoreCase);
    internal static void Maintain()
    {
        if (OtherSetupRunning())
            return;
        foreach (var root in Roots())
        {
            try
            {
                Sweep(root, DateTime.UtcNow);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try
        {
            if (!SkipBundleCache)
                SweepBundleCache(Path.Combine(Path.GetTempPath(), ".net"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    internal static void SweepBundleCache(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (string app in Directory.EnumerateDirectories(root).ToArray())
        {
            if (!Regex.IsMatch(Path.GetFileName(app), "^SqlPilotSetup(-[0-9]+\\.[0-9]+\\.[0-9]+)?$", RegexOptions.IgnoreCase) || !Safe(root, app))
                continue;
            using var process = Process.GetCurrentProcess();
            var loaded = process.Modules.Cast<ProcessModule>().Select(m => m.FileName).ToArray();
            foreach (string bundle in Directory.EnumerateDirectories(app).ToArray())
            {
                if (!Regex.IsMatch(Path.GetFileName(bundle), "^[a-zA-Z0-9_-]+$") || !Safe(app, bundle))
                    continue;
                if (loaded.Any(m => m.StartsWith(bundle + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    continue;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories))
                    {
                        using var probe = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    Directory.Delete(bundle, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (!Directory.EnumerateFileSystemEntries(app).Any())
                Directory.Delete(app);
        }
    }
    static IEnumerable<string> Roots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DataRoot };
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        roots.Add(Path.Combine(local, "SqlPilot"));
        string packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages))
            foreach (string package in Directory.EnumerateDirectories(packages, "OpenAI.Codex_*"))
                roots.Add(Path.Combine(package, "LocalCache", "Local", "SqlPilot"));
        return roots;
    }
    static bool OtherSetupRunning()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(p => p.Id != Environment.ProcessId && p.ProcessName.StartsWith("SqlPilotSetup", StringComparison.OrdinalIgnoreCase));
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    internal static void Sweep(string root, DateTime now)
    {
        // User settings, SQL Library, History and Sessions are outside these installer-owned subfolders.
        string work = Path.Combine(root, "SetupWork");
        if (Directory.Exists(work))
            foreach (var folder in Directory.EnumerateDirectories(work).ToArray())
                if (JobName.IsMatch(Path.GetFileName(folder)) && Directory.GetLastWriteTimeUtc(folder) < now.AddMinutes(-10))
                    DeleteJob(root, folder, "SetupWork");
        string pending = Path.Combine(root, "PendingSetup");
        if (Directory.Exists(pending))
            foreach (var folder in Directory.EnumerateDirectories(pending).ToArray())
            {
                if (!JobName.IsMatch(Path.GetFileName(folder)))
                    continue;
                var status = DeferredInstall.Read(Path.Combine(folder, "status.json"));
                if (status?.Finished == true || Directory.GetLastWriteTimeUtc(folder) < now.AddHours(-25))
                    DeleteJob(root, folder, "PendingSetup");
            }
        if (!HasActiveJobs(root))
            TrimLogs(root, now);
    }
    static bool HasActiveJobs(string root)
    {
        foreach (string category in new[] { "SetupWork", "PendingSetup" })
        {
            string parent = Path.Combine(root, category);
            if (!Directory.Exists(parent))
                continue;
            foreach (string folder in Directory.EnumerateDirectories(parent))
            {
                if (!JobName.IsMatch(Path.GetFileName(folder)) || !Safe(parent, folder))
                    continue;
                string marker = Path.Combine(folder, ".active");
                if (!File.Exists(marker))
                    continue;
                try
                {
                    using var probe = File.Open(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) { return true; }
                catch (UnauthorizedAccessException) { return true; }
            }
        }
        return false;
    }
    internal static bool DeleteJob(string root, string folder, string category)
    {
        string parent = Path.Combine(root, category);
        if (category != "SetupWork" && category != "PendingSetup")
            return false;
        if (!JobName.IsMatch(Path.GetFileName(folder)) || !Safe(parent, folder))
            return false;
        if (Environment.ProcessPath is string executable && executable.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            string lease = Path.Combine(folder, ".active");
            if (File.Exists(lease))
            {
                using var probe = File.Open(lease, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            // Probe every file before deleting anything, so a locked worker keeps its status/log intact.
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                using var probe = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            Directory.Delete(folder, true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    static bool Safe(string root, string path)
    {
        string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string target = Path.GetFullPath(path);
        if (!target.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        // Validate ancestors as well as descendants; never traverse directory links during cleanup.
        for (string? part = target; part != null; part = Path.GetDirectoryName(part))
            if (Directory.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                return false;
        if (!Directory.Exists(target))
            return File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) == 0;
        var queue = new Queue<string>();
        queue.Enqueue(target);
        while (queue.Count > 0)
            foreach (string item in Directory.EnumerateFileSystemEntries(queue.Dequeue()))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
                if ((attributes & FileAttributes.Directory) != 0)
                    queue.Enqueue(item);
            }
        return true;
    }
    static long Size(string path) => File.Exists(path) ? new FileInfo(path).Length : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    internal static void TrimLogs(string root, DateTime now, long budget = LogLimit)
    {
        string logs = Path.Combine(root, "SetupLogs");
        if (!Directory.Exists(logs))
            return;
        var candidates = Directory.EnumerateFileSystemEntries(logs).Where(p => BackupName.IsMatch(Path.GetFileName(p)) || Regex.IsMatch(Path.GetFileName(p), "^SSMS[0-9]+-[a-f0-9]{32}\\.log$", RegexOptions.IgnoreCase))
            .Where(p => Safe(logs, p)).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        long keptBytes = 0;
        var kept = new Dictionary<string, int>();
        foreach (string path in candidates)
        {
            string group = Regex.Replace(Path.GetFileName(path), "[a-f0-9]{32}.*$", "", RegexOptions.IgnoreCase);
            long bytes = Size(path);
            int limit = group == "previous-package-" ? 2 : group.StartsWith("SSMS") ? 10 : 1;
            kept.TryGetValue(group, out int count);
            // Maintain runs outside installer transactions and skips other active setup processes.
            if (count < limit && keptBytes + bytes <= budget && File.GetLastWriteTimeUtc(path) >= now.AddDays(-7))
            {
                kept[group] = count + 1;
                keptBytes += bytes;
                continue;
            }
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
