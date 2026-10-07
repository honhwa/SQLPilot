// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

namespace SqlPilot.Setup;
internal static class StorageChecks
{
    internal static int Run(string workspace)
    {
        int passed = 0;
        void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
            passed++;
        }
        string root = Path.Combine(workspace, "storage-fixture");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.xml"), "user settings");
        foreach (string name in new[] { "History", "Library", "Sessions" })
        {
            Directory.CreateDirectory(Path.Combine(root, name));
            File.WriteAllText(Path.Combine(root, name, "user.sql"), "SELECT N'keep';");
        }
        string Job(string category, bool old = true)
        {
            string folder = Path.Combine(root, category, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "payload.dll"), "generated");
            if (old)
                Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddDays(-2));
            return folder;
        }
        string stale = Job("SetupWork");
        string active = Job("SetupWork");
        using (var lease = File.Open(Path.Combine(active, ".active"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            Directory.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-2));
            SetupStorage.Sweep(root, DateTime.UtcNow);
            Check(!Directory.Exists(stale), "Stale extraction was not removed.");
            Check(Directory.Exists(active), "Active extraction was deleted.");
        }
        Check(SetupStorage.DeleteJob(root, active, "SetupWork"), "Released work lease was not cleaned.");
        string recent = Job("SetupWork", false);
        string unknown = Path.Combine(root, "SetupWork", "user-folder");
        Directory.CreateDirectory(unknown);
        SetupStorage.Sweep(root, DateTime.UtcNow);
        Check(Directory.Exists(recent), "Recently created extraction was removed.");
        Check(Directory.Exists(unknown), "Unknown work folder was removed.");
        string pending = Job("PendingSetup");
        File.WriteAllText(Path.Combine(pending, "status.json"), "{\"Finished\":false,\"Success\":false,\"Message\":\"Waiting\"}");
        using (var lease = File.Open(Path.Combine(pending, ".active"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            Directory.SetLastWriteTimeUtc(pending, DateTime.UtcNow.AddDays(-2));
            SetupStorage.Sweep(root, DateTime.UtcNow);
            Check(Directory.Exists(pending) && File.Exists(Path.Combine(pending, "status.json")), "Active pending setup was removed.");
        }
        SetupStorage.Sweep(root, DateTime.UtcNow);
        Check(!Directory.Exists(pending), "Expired pending setup was not removed.");
        string completed = Job("PendingSetup", false);
        File.WriteAllText(Path.Combine(completed, "status.json"), "{\"Finished\":true,\"Success\":true,\"Message\":\"Complete\"}");
        SetupStorage.Sweep(root, DateTime.UtcNow);
        Check(!Directory.Exists(completed), "Completed pending setup was not removed.");
        string outside = Path.Combine(workspace, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        Check(!SetupStorage.DeleteJob(root, outside, "SetupWork") && Directory.Exists(outside), "Cleanup escaped its root.");
        Check(!SetupStorage.DeleteJob(root, Path.Combine(root, "History"), "History"), "User data category was accepted.");
        string logs = Path.Combine(root, "SetupLogs");
        Directory.CreateDirectory(logs);
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(logs, "copy-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "backup.dll"), "backup");
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-i));
        }
        string transaction = Job("SetupWork");
        using (var marker = File.Open(Path.Combine(transaction, ".active"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            SetupStorage.Sweep(root, DateTime.UtcNow);
            Check(Directory.EnumerateDirectories(logs, "copy-backup-*").Count() == 5, "Rollback files were pruned during an active transaction.");
        }
        SetupStorage.DeleteJob(root, transaction, "SetupWork");
        File.WriteAllText(Path.Combine(logs, "keep-notes.txt"), "user note");
        SetupStorage.Sweep(root, DateTime.UtcNow);
        Check(Directory.EnumerateDirectories(logs, "copy-backup-*").Count() == 1, "Duplicate backup retention failed.");
        Check(File.ReadAllText(Path.Combine(logs, "keep-notes.txt")) == "user note", "Unknown log file was deleted.");
        string budgetItem = Path.Combine(logs, "legacy-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(budgetItem);
        File.WriteAllText(Path.Combine(budgetItem, "backup.dll"), "1234567890");
        SetupStorage.TrimLogs(root, DateTime.UtcNow, 10);
        Check(Directory.Exists(budgetItem) && !Directory.EnumerateDirectories(logs, "copy-backup-*").Any(), "Backup budget did not evict older data.");
        Directory.SetLastWriteTimeUtc(budgetItem, DateTime.UtcNow.AddDays(-8));
        SetupStorage.TrimLogs(root, DateTime.UtcNow);
        Check(!Directory.Exists(budgetItem), "Expired backup was retained.");
        string locked = Job("PendingSetup", false);
        File.WriteAllText(Path.Combine(locked, "status.json"), "keep status");
        using (var file = File.Open(Path.Combine(locked, "payload.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(!SetupStorage.DeleteJob(root, locked, "PendingSetup") && File.ReadAllText(Path.Combine(locked, "status.json")) == "keep status", "Locked worker cleanup partially removed status.");
        foreach (string name in new[] { "History", "Library", "Sessions" })
            Check(File.ReadAllText(Path.Combine(root, name, "user.sql")) == "SELECT N'keep';", "User SQL changed during maintenance.");
        Check(File.ReadAllText(Path.Combine(root, "settings.xml")) == "user settings", "Settings changed during maintenance.");
        string nativeCache = Path.Combine(root, "native-cache");
        string retired = Path.Combine(nativeCache, "SqlPilotSetup-0.1.0");
        string busyCache = Path.Combine(nativeCache, "SqlPilotSetup-0.2.0");
        string foreignCache = Path.Combine(nativeCache, "AnotherApp");
        foreach (string cache in new[] { retired, busyCache, foreignCache })
        {
            Directory.CreateDirectory(Path.Combine(cache, "bundle"));
            File.WriteAllText(Path.Combine(cache, "bundle", "native.dll"), "cache");
        }
        using (var file = File.Open(Path.Combine(busyCache, "bundle", "native.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            SetupStorage.SweepBundleCache(nativeCache);
            Check(!Directory.Exists(retired) && Directory.Exists(busyCache), "Native cache cleanup did not distinguish inactive and locked copies.");
            Check(Directory.Exists(foreignCache), "Unrelated application native cache was deleted.");
        }
        return passed;
    }
}
