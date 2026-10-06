// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

namespace SqlPilot.Setup;
internal static class VersionChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
            checks++;
        }
        InstalledCopy Copy(string version) => new()
        {
            Version = version
        };
        Check(InstalledVersions.Action(Array.Empty<InstalledCopy>()) == "Install", "New installation action failed");
        Check(InstalledVersions.Action(new[] { Copy("0.9.0") }) == "Upgrade", "Upgrade action failed");
        Check(InstalledVersions.Action(new[] { Copy(InstalledVersions.Current + ".0") }) == "Reinstall", "Equal normalized version action failed");
        Check(InstalledVersions.Action(new[] { Copy("99.0.0") }) == "Downgrade", "Numeric version comparison failed");
        Check(InstalledVersions.Action(new[] { Copy("0.9.0"), Copy("1.0.0") }) == "Downgrade", "Multiple installed versions downgrade guard failed");
        Check(InstalledVersions.Action(new[] { Copy("unknown") }).Contains("unknown"), "Unknown version action failed");
        string folder = Path.Combine(root, "version-test", "Extensions", "random-folder");
        Directory.CreateDirectory(folder);
        string manifest = Path.Combine(folder, "extension.vsixmanifest");
        File.WriteAllText(manifest, "<Vsix xmlns='http://schemas.microsoft.com/developer/vsx-schema/2010'><Identifier Id='SqlPilot.Ssms.99'><Version>0.6.4</Version></Identifier></Vsix>");
        Check(InstalledVersions.Read(manifest, 99, true)?.Version == "0.6.4", "Legacy manifest detection failed");
        File.WriteAllText(manifest, "<PackageManifest xmlns='http://schemas.microsoft.com/developer/vsx-schema/2011'><Metadata><Identity Id='SqlPilot.Ssms.99' Version='99.0.0'/></Metadata></PackageManifest>");
        Check(InstalledVersions.Read(manifest, 99, false)?.Version == "99.0.0" && InstalledVersions.Read(manifest, 22, false) == null, "Modern manifest host isolation failed");
        var host = new Host { Major = 99, Ide = Path.GetDirectoryName(Path.GetDirectoryName(folder))!, Name = "Test host" };
        bool blocked = false;
        try
        {
            InstalledVersions.EnsureAllowed(host, false);
        }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked && host.InstalledVersion == "99.0.0", "Unconfirmed downgrade was not blocked");
        InstalledVersions.EnsureAllowed(host, true);
        Check(host.Operation == "Downgrade", "Explicit downgrade approval failed");
        File.WriteAllText(manifest, "<broken");
        Check(InstalledVersions.Read(manifest, 99, true) == null, "Malformed manifest isolation failed");
        return checks;
    }
}
