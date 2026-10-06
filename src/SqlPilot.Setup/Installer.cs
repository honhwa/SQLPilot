// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SqlPilot.Setup;
public sealed class Installer
{
    public string Root
    {
        get;
    }
    public string LogFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "SetupLogs");
    public Installer()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "SetupWork", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogFolder);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SqlPilot.Payload.zip") ?? throw new Exception("Installer payload missing.");
        using var zip = new ZipArchive(stream);
        foreach (var entry in zip.Entries)
        {
            string path = SafePath(Root, entry.FullName);
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
    }
    public static string SafePath(string root, string relative)
    {
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package path escaped extraction folder.");
        return result;
    }
    public string Prepare(Host host)
    {
        if (!host.Complete || !File.Exists(host.Executable))
            throw new Exception("SSMS installation or update has not finished.");
        string editor = Path.Combine(host.Ide, "CommonExtensions", "Microsoft", "Editor");
        if (!Directory.Exists(editor))
            throw new Exception("This SSMS generation does not expose the required MEF editor APIs.");
        string package = Path.Combine(Root, "host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(package);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "payload"), "*", SearchOption.AllDirectories))
        {
            string destination = SafePath(package, Path.GetRelativePath(Path.Combine(Root, "payload"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var references = Directory.EnumerateFiles(Path.Combine(Root, "references"), "*.dll", SearchOption.AllDirectories).Where(IsManaged).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        foreach (var name in new[] { "CoreUtility", "Text.Data", "Text.Logic", "Text.UI", "Text.UI.Wpf", "Language.Intellisense", "Editor" })
        {
            string path = Path.Combine(editor, "Microsoft.VisualStudio." + name + ".dll");
            if (!File.Exists(path))
                throw new Exception("Editor API missing: " + name);
            references.Add(MetadataReference.CreateFromFile(path));
        }
        foreach (var name in new[] { "stdole", "envdte", "Microsoft.VisualStudio.CommandBars", "Microsoft.VisualStudio.OLE.Interop", "Microsoft.VisualStudio.TextManager.Interop", "Microsoft.VisualStudio.Shell.15.0", "Microsoft.VisualStudio.Shell.Framework", "Microsoft.VisualStudio.Shell.Interop", "Microsoft.VisualStudio.Shell.Interop.8.0", "Microsoft.VisualStudio.Shell.Interop.10.0", "Microsoft.VisualStudio.Interop" })
        {
            var path = Path.Combine(host.Ide, "PublicAssemblies", name + ".dll");
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }
        references.Add(MetadataReference.CreateFromFile(Path.Combine(package, "SqlPilot.Core.dll")));
        var sources = Directory.EnumerateFiles(Path.Combine(Root, "source"), "*.cs").Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), new CSharpParseOptions(LanguageVersion.Latest), Path.GetFileName(p))).Concat(new[] { CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyVersion(\"" + InstalledVersions.Current + ".0\")] [assembly: System.Reflection.AssemblyFileVersion(\"" + InstalledVersions.Current + ".0\")]") });
        var compilation = CSharpCompilation.Create("SqlPilot.Ssms", sources, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, platform: Platform.AnyCpu));
        using (var output = File.Create(Path.Combine(package, "SqlPilot.Ssms.dll")))
        {
            var icon = new ResourceDescription("SqlPilot.Icon.png", () => File.OpenRead(Path.Combine(Root, "assets", "sqlpilot.png")), true);
            var result = compilation.Emit(output, manifestResources: new[] { icon });
            if (!result.Success)
                throw new Exception("The host APIs are incompatible with the current adapter:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(8)));
        }
        File.Copy(Path.Combine(Root, "assets", "sqlpilot.png"), Path.Combine(package, "sqlpilot.png"));
        File.Copy(Path.Combine(Root, "assets", "LICENSE.txt"), Path.Combine(package, "LICENSE.txt"));
        File.Copy(Path.Combine(Root, "assets", "THIRD_PARTY_NOTICES.md"), Path.Combine(package, "THIRD_PARTY_NOTICES.md"));
        CopyTree(Path.Combine(Root, "assets", "licenses"), Path.Combine(package, "third-party", "licenses"));
        File.WriteAllText(Path.Combine(package, "extension.vsixmanifest"), Manifest(host));
        File.WriteAllText(Path.Combine(package, "SqlPilot.pkgdef"),
            "[$RootKey$\\Packages\\{c429a14e-8788-41a8-9e6f-95a2323c0fe3}]\n@=\"SqlPilot\"\n\"InprocServer32\"=\"$WinDir$\\\\System32\\\\mscoree.dll\"\n\"Class\"=\"SqlPilot.Ssms.ToolbarPackage\"\n\"CodeBase\"=\"$PackageFolder$\\\\SqlPilot.Ssms.dll\"\n" +
            "[$RootKey$\\AutoLoadPackages\\{adfc4e64-0397-11d1-9f4e-00a0c911004f}]\n\"{c429a14e-8788-41a8-9e6f-95a2323c0fe3}\"=dword:00000000\n" +
            "[$RootKey$\\AutoLoadPackages\\{f1536ef8-92ec-443c-9ed7-fdadf150da82}]\n\"{c429a14e-8788-41a8-9e6f-95a2323c0fe3}\"=dword:00000000\n");
        return package;
    }
    static string Manifest(Host host)
    {
        if (host.Major < 21)
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><Vsix Version=\"1.0.0\" xmlns=\"http://schemas.microsoft.com/developer/vsx-schema/2010\"><Identifier Id=\"SqlPilot.Ssms." + host.Major + "\"><Name>SqlPilot</Name><Author>Arash Ghasemi Rad</Author><Version>" + InstalledVersions.Current + "</Version><Description>SQL development assistant</Description><Locale>1033</Locale><InstalledByMsi>true</InstalledByMsi><SupportedProducts><IsolatedShell Version=\"1.0\">ssms</IsolatedShell></SupportedProducts><SupportedFrameworkRuntimeEdition MinVersion=\"4.0\" MaxVersion=\"4.8\"/><Icon>sqlpilot.png</Icon></Identifier><References/><Content><VsPackage>SqlPilot.pkgdef</VsPackage><MefComponent>SqlPilot.Ssms.dll</MefComponent><Assembly>SqlPilot.Core.dll</Assembly><Assembly>Microsoft.SqlServer.TransactSql.ScriptDom.dll</Assembly></Content></Vsix>";
        using var pe = new PEReader(File.OpenRead(host.Executable));
        string architecture = pe.PEHeaders.CoffHeader.Machine == Machine.Arm64 ? "arm64" : "amd64";
        XNamespace ns = "http://schemas.microsoft.com/developer/vsx-schema/2011";
        return new XDocument(new XElement(ns + "PackageManifest", new XAttribute("Version", "2.0.0"),
            new XElement(ns + "Metadata", new XElement(ns + "Identity", new XAttribute("Id", "SqlPilot.Ssms." + host.Major), new XAttribute("Version", InstalledVersions.Current), new XAttribute("Language", "en-US"), new XAttribute("Publisher", ProductInfo.Author)), new XElement(ns + "DisplayName", "SqlPilot"), new XElement(ns + "Description", "SQL development assistant"), new XElement(ns + "Icon", "sqlpilot.png")),
            new XElement(ns + "Installation", new XAttribute("AllUsers", "true"), new XElement(ns + "InstallationTarget", new XAttribute("Id", "Microsoft.VisualStudio.Ssms"), new XAttribute("Version", $"[{host.Major}.0,{host.Major + 1}.0)"), new XElement(ns + "ProductArchitecture", architecture))),
            new XElement(ns + "Assets", new XElement(ns + "Asset", new XAttribute("Type", "Microsoft.VisualStudio.VsPackage"), new XAttribute("Path", "SqlPilot.pkgdef")), new XElement(ns + "Asset", new XAttribute("Type", "Microsoft.VisualStudio.MefComponent"), new XAttribute("Path", "SqlPilot.Ssms.dll")), new XElement(ns + "Asset", new XAttribute("Type", "Microsoft.VisualStudio.Assembly"), new XAttribute("Path", "SqlPilot.Core.dll")), new XElement(ns + "Asset", new XAttribute("Type", "Microsoft.VisualStudio.Assembly"), new XAttribute("Path", "Microsoft.SqlServer.TransactSql.ScriptDom.dll"))))).ToString();
    }
    public async Task<string> Install(Host host, Action<string> report, bool allowDowngrade = false)
    {
        if (Detection.Running(host))
            throw new Exception("Save your queries and close " + host.Name + ". Setup does not close SSMS automatically.");
        InstalledVersions.EnsureAllowed(host, allowDowngrade);
        report(host.Operation + ": SqlPilot " + host.InstalledVersion + " → " + InstalledVersions.Current + " on " + host.Name);
        report("Building adapter for " + host.Name);
        string package = await Task.Run(() => Prepare(host));
        if (host.Major < 21)
        {
            // Machine-level MSI-style discovery is used by the isolated SSMS shell.
            string extensionRoot = Path.Combine(host.Ide, "Extensions");
            string destination = Path.Combine(extensionRoot, "SqlPilot");
            string? backup = null;
            if (Directory.Exists(destination))
            {
                string manifest = Path.Combine(destination, "extension.vsixmanifest");
                if (!File.Exists(manifest) || !File.ReadAllText(manifest).Contains("SqlPilot.Ssms."))
                    throw new Exception("The existing installation folder does not belong to SqlPilot.");
                backup = Path.Combine(LogFolder, "legacy-backup-" + Guid.NewGuid().ToString("N"));
                CopyTree(destination, backup);
            }
            try
            {
                CopyTree(package, destination);
                File.Delete(Path.Combine(destination, "SqlPilot-CodexPlugin.zip"));
            }
            catch
            {
                if (backup != null)
                {
                    try
                    {
                        CopyTree(backup, destination);
                    }
                    catch { }
                }
                throw new Exception("Installation failed. Administrator access is required. The previous files were backed up.");
            }
            if (!File.Exists(Path.Combine(destination, "SqlPilot.Ssms.dll")))
                throw new Exception("Extension file is missing after copying.");
            ResetLegacyCaches(host, report);
            InstalledVersions.Refresh(host);
            return "Files registered and editor discovery caches refreshed. Restart SSMS to verify loading.";
        }
        string vsix = package + ".vsix";
        ZipFile.CreateFromDirectory(package, vsix);
        using (var zip = ZipFile.Open(vsix, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open());
            writer.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"pkgdef\" ContentType=\"text/plain\"/><Default Extension=\"dll\" ContentType=\"application/octet-stream\"/><Default Extension=\"json\" ContentType=\"application/json\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"txt\" ContentType=\"text/plain\"/><Default Extension=\"md\" ContentType=\"text/markdown\"/><Default Extension=\"rtf\" ContentType=\"application/rtf\"/><Default Extension=\"vsixmanifest\" ContentType=\"text/xml\"/></Types>");
        }
        string installer = Path.Combine(host.Ide, "VSIXInstaller.exe");
        if (!File.Exists(installer))
            throw new Exception("The host VSIXInstaller is missing.");
        var previous = host.InstalledCopies.Where(c => Directory.Exists(c.Folder)).ToList();
        bool cleanReplace = ReplacementCleanup.Required(previous);
        var recovery = new List<(string Vsix, bool AllUsers)>();
        var quarantined = new List<(string Original, string Saved)>();
        bool registeredNew = false;
        foreach (var copy in previous)
        {
            ReplacementCleanup.ValidatePath(host, copy);
            string saved = Path.Combine(LogFolder, "copy-backup-" + Guid.NewGuid().ToString("N"));
            CopyTree(copy.Folder, saved);
            report("Previous extension files backed up: " + saved);
        }
        if (cleanReplace)
        {
            // Keep a restorable package before replacing an existing native VSIX registration.
            foreach (var group in previous.GroupBy(c => c.AllUsers))
            {
                var copy = group.OrderByDescending(c => Version.TryParse(c.Version, out var v) ? v : new Version(0, 0)).First();
                string saved = Path.Combine(LogFolder, "previous-package-" + Guid.NewGuid().ToString("N"));
                CopyTree(copy.Folder, saved);
                string recoveryVsix = saved + ".vsix";
                ZipFile.CreateFromDirectory(saved, recoveryVsix);
                EnsureContentTypes(recoveryVsix);
                recovery.Add((recoveryVsix, group.Key));
                report("Previous SqlPilot package saved: " + recoveryVsix);
            }
        }
        try
        {
            if (cleanReplace)
                foreach (var old in recovery)
                    await RegisterVsix(host, installer, "/uninstall:SqlPilot.Ssms." + host.Major, old.AllUsers, report);
            if (Detection.Running(host))
                throw new Exception("Close SSMS before replacing previous extension folders.");
            quarantined = ReplacementCleanup.Quarantine(host, previous, Path.Combine(LogFolder, "retired-copies-" + Guid.NewGuid().ToString("N")), report);
            await RegisterVsix(host, installer, vsix, true, report);
            registeredNew = true;
            ReplacementCleanup.VerifySingle(host);
        }
        catch
        {
            if (registeredNew)
            {
                try
                {
                    await RegisterVsix(host, installer, "/uninstall:SqlPilot.Ssms." + host.Major, true, report);
                }
                catch (Exception error) { report("New registration could not be removed during recovery: " + error.Message); }
            }
            if (cleanReplace)
                foreach (var old in recovery)
                {
                    try
                    {
                        await RegisterVsix(host, installer, old.Vsix, old.AllUsers, report);
                        report("Previous SqlPilot version restored.");
                    }
                    catch (Exception recoveryError) { report("Automatic recovery failed: " + recoveryError.Message + ". Previous package: " + old.Vsix); }
                }
            ReplacementCleanup.Restore(quarantined, report);
            throw;
        }
        InstalledVersions.Refresh(host);
        return "One SqlPilot copy verified and VSIX registered. Restart SSMS to verify loading.";
    }
    static void EnsureContentTypes(string vsix)
    {
        using var zip = ZipFile.Open(vsix, ZipArchiveMode.Update);
        if (zip.GetEntry("[Content_Types].xml") != null)
            return;
        using var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open());
        writer.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"pkgdef\" ContentType=\"text/plain\"/><Default Extension=\"dll\" ContentType=\"application/octet-stream\"/><Default Extension=\"json\" ContentType=\"application/json\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"txt\" ContentType=\"text/plain\"/><Default Extension=\"md\" ContentType=\"text/markdown\"/><Default Extension=\"rtf\" ContentType=\"application/rtf\"/><Default Extension=\"vsixmanifest\" ContentType=\"text/xml\"/></Types>");
    }
    async Task RegisterVsix(Host host, string installer, string operation, bool allUsers, Action<string> report)
    {
        if (Detection.Running(host))
            throw new Exception("Close " + host.Name + " before installing.");
        if (string.IsNullOrEmpty(host.InstanceId))
            throw new Exception("This SSMS instance ID was not found in Setup Configuration.");
        string log = Path.Combine(LogFolder, "SSMS" + host.Major + "-" + Guid.NewGuid().ToString("N") + ".log");
        var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/quiet");
        if (allUsers)
            start.ArgumentList.Add("/admin");
        start.ArgumentList.Add("/logFile:" + log);
        start.ArgumentList.Add("/instanceIds:" + host.InstanceId);
        start.ArgumentList.Add(operation);
        report((operation.StartsWith("/uninstall:") ? "Unregistering previous SqlPilot for " : "Registering VSIX for ") + host.Name);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new Exception($"VSIXInstaller returned error {process.ExitCode}. Log: {log}");
    }
    void ResetLegacyCaches(Host host, Action<string> report)
    {
        if (Detection.Running(host))
            throw new Exception("Close SSMS before refreshing editor discovery caches.");
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "SQL Server Management Studio", host.Major + ".0_IsoShell");
        string backup = Path.Combine(LogFolder, "discovery-cache-backup-" + Guid.NewGuid().ToString("N"));
        int count = BackupDiscoveryCaches(profile, backup);
        report($"Refreshed {count} discovery cache file(s) for {host.Name}. Backup: {backup}");
    }
    public static int BackupDiscoveryCaches(string profile, string backup)
    {
        // Move only generated discovery files. Query history, settings and extensions remain in place.
        var files = new List<string>();
        string components = SafePath(profile, "ComponentModelCache");
        if (Directory.Exists(components))
            files.AddRange(Directory.EnumerateFiles(components, "Microsoft.VisualStudio.Default.*", SearchOption.TopDirectoryOnly));
        string extensions = SafePath(profile, "Extensions");
        if (Directory.Exists(extensions))
            files.AddRange(Directory.EnumerateFiles(extensions, "*.cache", SearchOption.TopDirectoryOnly));
        foreach (string file in files)
        {
            string destination = SafePath(backup, Path.GetRelativePath(profile, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination);
        }
        return files.Count;
    }
    static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string path = SafePath(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(file, path, true);
        }
    }
    static bool IsManaged(string path)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(path));
            return pe.HasMetadata && pe.GetMetadataReader().IsAssembly;
        }
        catch (BadImageFormatException) { return false; }
    }
}
