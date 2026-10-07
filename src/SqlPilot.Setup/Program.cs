// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;

namespace SqlPilot.Setup;
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "SetupLogs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "setup-error.log"), ex.ToString());
            string? report = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault();
            if (report != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                File.WriteAllText(report, JsonSerializer.Serialize(new
                {
                    error = ex.ToString()
                }));
            }
            if (!args.Contains("--detect") && !args.Contains("--self-test") && !args.Contains("--install-headless") && !args.Contains("--deferred-install"))
                MessageBox.Show(ex.Message, "SqlPilot setup", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.ExitCode = 1;
        }
    }
    static void Run(string[] args)
    {
        SetupStorage.SkipBundleCache = args.Contains("--self-test") || args.Contains("--detect");
        SetupStorage.Maintain();
        if (args.Contains("--deferred-install"))
        {
            DeferredInstall.Run(args);
            return;
        }
        if (args.Contains("--detect") || args.Contains("--self-test"))
        {
            string report = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "setup-detection.json");
            var hosts = Detection.Find().GetAwaiter().GetResult();
            var output = new Dictionary<string, object> { { "hosts", hosts }, { "version", InstalledVersions.Current } };
            if (args.Contains("--self-test"))
            {
                using var installer = new Installer();
                int passed = 0;
                try
                {
                    Installer.SafePath(installer.Root, "../escape");
                    throw new Exception("Unsafe path accepted");
                }
                catch (InvalidDataException) { passed++; }
                var test = Detection.ParseSetupJson("[{\"productId\":\"Microsoft.VisualStudio.Product.Professional\"},{\"productId\":\"Microsoft.VisualStudio.Product.Ssms\",\"productPath\":\"C:\\\\Test\\\\SSMS.exe\",\"installationVersion\":\"99.1\",\"instanceId\":\"future\",\"isComplete\":true,\"isLaunchable\":true}]");
                if (test.Count != 1 || test[0].Major != 99)
                    throw new Exception("Future detection failed");
                passed++;
                string fixture = Path.Combine(installer.Root, "cache-test");
                string cache = Path.Combine(fixture, "profile", "ComponentModelCache");
                string extensions = Path.Combine(fixture, "profile", "Extensions");
                Directory.CreateDirectory(cache);
                Directory.CreateDirectory(extensions);
                File.WriteAllText(Path.Combine(cache, "Microsoft.VisualStudio.Default.cache"), "stale");
                File.WriteAllText(Path.Combine(extensions, "extensions.en-US.cache"), "stale");
                File.WriteAllText(Path.Combine(extensions, "query.sql"), "preserve");
                string backup = Path.Combine(fixture, "backup");
                if (Installer.BackupDiscoveryCaches(Path.Combine(fixture, "profile"), backup) != 2 ||
                    !File.Exists(Path.Combine(backup, "ComponentModelCache", "Microsoft.VisualStudio.Default.cache")) ||
                    !File.Exists(Path.Combine(backup, "Extensions", "extensions.en-US.cache")) ||
                    File.ReadAllText(Path.Combine(extensions, "query.sql")) != "preserve" ||
                    Directory.EnumerateFiles(cache).Any())
                    throw new Exception("Discovery cache refresh failed");
                passed++;
                foreach (var host in hosts.Where(h => h.Complete && File.Exists(h.Executable)))
                {
                    string package = installer.Prepare(host);
                    if (!File.Exists(Path.Combine(package, "SqlPilot.Ssms.dll")) || !File.ReadAllText(Path.Combine(package, "extension.vsixmanifest")).Contains("sqlpilot.png"))
                        throw new Exception("Adapter payload invalid");
                    passed++;
                }
                string encoded = HostSelection.Encode(new[] { @"C:\Program Files\SSMS Test", @"D:\SSMS Other" });
                var chosen = HostSelection.Decode(new[] { "--selection", encoded });
                if (chosen == null || chosen.Count != 2 || !chosen.Contains(@"c:\program files\SSMS Test"))
                    throw new Exception("Selected-host elevation roundtrip failed");
                passed++;
                if (SqlPilot.UI.Design.Resources().Count < 6)
                    throw new Exception("Shared design resources failed");
                passed++;
                if (Directory.EnumerateFiles(Path.Combine(installer.Root, "source"), "Codex*.cs").Any() || File.Exists(Path.Combine(installer.Root, "assets", "SqlPilot-CodexPlugin.zip")))
                    throw new Exception("Retired AI payload found");
                passed++;
                passed += StorageChecks.Run(installer.Root);
                passed += DeferredInstall.Checks(installer.Root);
                passed += SetupWindow.Checks(args.SkipWhile(a => a != "--preview").Skip(1).FirstOrDefault());
                passed += VersionChecks.Run(installer.Root);
                passed += CleanupChecks.Run(installer.Root);
                string scratch = installer.Root;
                installer.Dispose();
                if (Directory.Exists(scratch))
                    throw new Exception("Installer temporary files remained after disposal: " + scratch);
                passed++;
                output["temporaryFilesRemoved"] = true;
                output["selfTestChecksPassed"] = passed;
                output["installedByTest"] = false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            File.WriteAllText(report, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (args.Contains("--install-headless"))
        {
            if (!Elevated())
                throw new Exception("Administrator access is required.");
            string report = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "setup-install.json");
            var hosts = Detection.Find().GetAwaiter().GetResult();
            var selection = HostSelection.Decode(args);
            hosts = hosts.Where(h => selection == null || selection.Contains(h.Ide)).ToList();
            if (hosts.Count == 0)
                throw new Exception("No selected SSMS installation was found.");
            if (hosts.Any(Detection.Running))
                throw new Exception("Close the selected SSMS instances before installing.");
            using var installer = new Installer();
            var results = new List<object>();
            foreach (var host in hosts)
            {
                try
                {
                    results.Add(new
                    {
                        host = host.Name,
                        result = installer.Install(host, _ => { }, args.Contains("--allow-downgrade")).GetAwaiter().GetResult(),
                        success = true
                    });
                }
                catch (Exception ex) { results.Add(new { host = host.Name, result = ex.ToString(), success = false }); Environment.ExitCode = 1; }
            }
            File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        new Application().Run(new SetupWindow(args));
    }
    internal static bool Elevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
