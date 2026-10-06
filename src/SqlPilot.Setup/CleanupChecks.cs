// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

namespace SqlPilot.Setup;
internal static class CleanupChecks
{
    internal static int Run(string root)
    {
        int count = 0;
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            count++;
        }
        string fixture = Installer.SafePath(root, "replacement-test");
        var host = new Host { Major = 98, Ide = Path.Combine(fixture, "IDE"), Name = "Cleanup fixture" };
        InstalledCopy Copy(string folder, string version = "0.9.0", int major = 98)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "extension.vsixmanifest"), $"<PackageManifest><Metadata><Identity Id='SqlPilot.Ssms.{major}' Version='{version}'/></Metadata></PackageManifest>");
            File.WriteAllText(Path.Combine(folder, "SqlPilot.Ssms.dll"), "fixture");
            return new InstalledCopy { Folder = folder, Version = version, AllUsers = true };
        }
        var old = Copy(Path.Combine(host.Ide, "Extensions", "random-old"));
        var newer = Copy(Path.Combine(host.Ide, "Extensions", "random-new"), InstalledVersions.Current);
        var foreign = Copy(Path.Combine(host.Ide, "Extensions", "another-extension"), "1.0.0", 97);
        Check(ReplacementCleanup.Required(new[] { old }) && !ReplacementCleanup.Required(Array.Empty<InstalledCopy>()), "Upgrade replacement policy failed");
        var copies = InstalledVersions.Find(host);
        Check(copies.Count == 2, "Duplicate detection fixture failed");
        var moved = ReplacementCleanup.Quarantine(host, copies, Path.Combine(fixture, "retired"), _ => { });
        Check(moved.Count == 2 && InstalledVersions.Find(host).Count == 0 && moved.All(m => File.Exists(Path.Combine(m.Saved, "SqlPilot.Ssms.dll"))), "Old copies were not retained outside discovery");
        Check(Directory.Exists(foreign.Folder), "Another host extension was modified");
        ReplacementCleanup.Restore(moved, _ => { });
        Check(InstalledVersions.Find(host).Count == 2, "Replacement rollback failed");
        bool rejected = false;
        try
        {
            ReplacementCleanup.VerifySingle(host);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Duplicate installation verification accepted leftovers");
        var outside = Copy(Path.Combine(fixture, "outside"));
        rejected = false;
        try
        {
            ReplacementCleanup.Quarantine(host, new[] { outside }, Path.Combine(fixture, "bad"), _ => { });
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && Directory.Exists(outside.Folder), "Out-of-host path was moved");
        rejected = false;
        try
        {
            ReplacementCleanup.Quarantine(host, new[] { old, foreign }, Path.Combine(fixture, "partial"), _ => { });
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && Directory.Exists(old.Folder) && Directory.Exists(foreign.Folder), "Partial quarantine did not restore previous copy");
        moved = ReplacementCleanup.Quarantine(host, new[] { old }, Path.Combine(fixture, "last"), _ => { });
        ReplacementCleanup.VerifySingle(host);
        Check(host.InstalledCopies.Count == 1, "Single current version verification failed");
        return count;
    }
}
