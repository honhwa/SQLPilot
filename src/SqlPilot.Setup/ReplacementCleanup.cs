// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

namespace SqlPilot.Setup;
public static class ReplacementCleanup
{
    public static bool Required(IEnumerable<InstalledCopy> copies) => copies.Any();
    public static void ValidatePath(Host host, InstalledCopy copy)
    {
        string path = Path.GetFullPath(copy.Folder).TrimEnd(Path.DirectorySeparatorChar);
        var root = InstalledVersions.ExtensionRoots(host).FirstOrDefault(r => r.AllUsers == copy.AllUsers &&
            path.StartsWith(Path.GetFullPath(r.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (root.Folder == null)
            throw new InvalidOperationException("Refusing to move an extension outside the selected SSMS extension folders.");
        for (string? part = path; part != null; part = Path.GetDirectoryName(part))
        {
            if (Directory.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to move an extension through a linked directory.");
            if (string.Equals(part, Path.GetFullPath(root.Folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                break;
        }
    }
    public static List<(string Original, string Saved)> Quarantine(Host host, IEnumerable<InstalledCopy> copies, string backup, Action<string> report)
    {
        var moved = new List<(string Original, string Saved)>();
        try
        {
            foreach (var copy in copies.GroupBy(c => Path.GetFullPath(c.Folder), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                ValidatePath(host, copy);
                if (!Directory.Exists(copy.Folder))
                    continue; // native unregistration already removed it
                var actual = InstalledVersions.Read(Path.Combine(copy.Folder, "extension.vsixmanifest"), host.Major, copy.AllUsers);
                if (actual == null || actual.Version != copy.Version)
                    throw new InvalidOperationException("The previous SqlPilot copy changed during installation. Retry setup.");
                Directory.CreateDirectory(backup);
                string saved = Installer.SafePath(backup, "copy-" + Guid.NewGuid().ToString("N"));
                Directory.Move(Path.GetFullPath(copy.Folder), saved);
                moved.Add((Path.GetFullPath(copy.Folder), saved));
                report("Previous SqlPilot copy retained outside extension discovery: " + saved);
            }
            return moved;
        }
        catch
        {
            Restore(moved, report);
            throw;
        }
    }
    public static void Restore(IEnumerable<(string Original, string Saved)> copies, Action<string> report)
    {
        foreach (var copy in copies.Reverse())
        {
            try
            {
                if (!Directory.Exists(copy.Saved))
                    continue;
                if (Directory.Exists(copy.Original))
                    throw new IOException("The original folder is occupied.");
                Directory.CreateDirectory(Path.GetDirectoryName(copy.Original)!);
                Directory.Move(copy.Saved, copy.Original);
                report("Previous extension folder restored: " + copy.Original);
            }
            catch (Exception ex) { report("Folder recovery failed: " + ex.Message + ". Retained copy: " + copy.Saved); }
        }
    }
    public static void VerifySingle(Host host)
    {
        InstalledVersions.Refresh(host);
        if (host.InstalledCopies.Count != 1 || InstalledVersions.Compare(host.InstalledCopies[0].Version, InstalledVersions.Current) != 0)
            throw new InvalidOperationException("Installation verification failed: expected one SqlPilot " + InstalledVersions.Current + " copy. Found: " + host.InstalledVersion);
    }
}
