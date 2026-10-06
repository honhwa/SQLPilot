// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Xml;
using System.Xml.Linq;

namespace SqlPilot.Setup;
public sealed class InstalledCopy
{
    public string Folder { get; set; } = "";
    public string Version { get; set; } = "Unknown";
    public bool AllUsers
    {
        get; set;
    }
}
public static class InstalledVersions
{
    public static string Current => SqlPilot.ProductInfo.Version;
    public static int Compare(string left, string right)
    {
        if (!Version.TryParse(left, out var a) || !Version.TryParse(right, out var b))
            throw new ArgumentException("Invalid SqlPilot version.");
        return new Version(a.Major, a.Minor, Math.Max(0, a.Build), Math.Max(0, a.Revision))
            .CompareTo(new Version(b.Major, b.Minor, Math.Max(0, b.Build), Math.Max(0, b.Revision)));
    }
    public static string Action(IEnumerable<InstalledCopy> copies, string? target = null)
    {
        target ??= Current;
        var all = copies.ToList();
        if (all.Count == 0)
            return "Install";
        var known = all.Where(c => Version.TryParse(c.Version, out _)).ToList();
        if (known.Any(c => Compare(c.Version, target) > 0))
            return "Downgrade";
        if (known.Count != all.Count)
            return "Reinstall (version unknown)";
        return known.Any(c => Compare(c.Version, target) == 0) ? "Reinstall" : "Upgrade";
    }
    public static InstalledCopy? Read(string manifest, int major, bool allUsers)
    {
        try
        {
            using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader);
            var identity = root.Descendants().FirstOrDefault(e => (e.Name.LocalName == "Identity" || e.Name.LocalName == "Identifier") &&
                string.Equals((string?)e.Attribute("Id"), "SqlPilot.Ssms." + major, StringComparison.OrdinalIgnoreCase));
            if (identity == null)
                return null;
            string value = (string?)identity.Attribute("Version") ?? identity.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value ?? "Unknown";
            return new InstalledCopy { Folder = Path.GetDirectoryName(manifest)!, Version = value.Trim(), AllUsers = allUsers };
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException) { return null; }
    }
    public static List<(string Folder, bool AllUsers)> ExtensionRoots(Host host)
    {
        var roots = new List<(string Folder, bool AllUsers)> { (Path.Combine(host.Ide, "Extensions"), true) };
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (host.Major < 21)
            roots.Add((Path.Combine(local, "Microsoft", "SQL Server Management Studio", host.Major + ".0_IsoShell", "Extensions"), false));
        else if (!string.IsNullOrEmpty(host.InstanceId))
            roots.Add((Path.Combine(local, "Microsoft", "SSMS", host.Major + ".0_" + host.InstanceId, "Extensions"), false));
        return roots;
    }
    public static List<InstalledCopy> Find(Host host)
    {
        var roots = ExtensionRoots(host);
        var result = new List<InstalledCopy>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Folder))
                continue;
            foreach (var manifest in Directory.EnumerateFiles(root.Folder, "extension.vsixmanifest", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var copy = Read(manifest, host.Major, root.AllUsers);
                if (copy != null)
                    result.Add(copy);
            }
        }
        return result;
    }
    public static void Refresh(Host host) => host.InstalledCopies = Find(host);
    public static void EnsureAllowed(Host host, bool allowDowngrade)
    {
        Refresh(host);
        if (host.Operation == "Downgrade" && !allowDowngrade)
            throw new InvalidOperationException($"Downgrade blocked: {host.Name} has SqlPilot {host.InstalledVersion}; setup contains {Current}. Confirm the downgrade in setup, or explicitly use --allow-downgrade for a headless installation.");
    }
}
