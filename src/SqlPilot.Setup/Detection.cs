// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SqlPilot.Setup;
public sealed class Host
{
    public string Name { get; set; } = "";
    public string Ide { get; set; } = "";
    public int Major
    {
        get; set;
    }
    public string InstanceId { get; set; } = "";
    public bool Complete { get; set; } = true;
    public List<InstalledCopy> InstalledCopies { get; set; } = new();
    public string InstalledVersion => InstalledCopies.Count == 0 ? "Not installed" : string.Join(", ", InstalledCopies.Select(c => c.Version).Distinct());
    public string Operation => InstalledVersions.Action(InstalledCopies);
    public string Executable => Path.Combine(Ide, "ssms.exe");
    public string Status => !Complete || !File.Exists(Executable) ? "SSMS installation is incomplete or updating" : Operation + " → " + InstalledVersions.Current;
}
public static class Detection
{
    public static async Task<List<Host>> Find()
    {
        var found = new Dictionary<string, Host>(StringComparer.OrdinalIgnoreCase);
        void Add(Host host)
        {
            if (string.IsNullOrWhiteSpace(host.Ide))
                return;
            host.Ide = Path.GetFullPath(host.Ide).TrimEnd(Path.DirectorySeparatorChar);
            if (!found.TryGetValue(host.Ide, out var old) || !string.IsNullOrEmpty(host.InstanceId))
                found[host.Ide] = host;
        }
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            var start = new ProcessStartInfo(vswhere) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-all", "-prerelease", "-products", "*", "-format", "json", "-utf8" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stderr;
            if (process.ExitCode == 0)
                foreach (var host in ParseSetupJson(await stdout))
                    Add(host);
        }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null)
                continue;
            foreach (var sub in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(sub);
                string name = key?.GetValue("DisplayName") as string ?? "";
                if (!name.Contains("SQL Server Management Studio", StringComparison.OrdinalIgnoreCase) || name.Contains("Language Pack", StringComparison.OrdinalIgnoreCase))
                    continue;
                string location = (key?.GetValue("InstallLocation") as string ?? "").Trim('"');
                if (Directory.Exists(location))
                    foreach (var ide in new[] { location, Path.Combine(location, "Common7", "IDE"), Path.Combine(location, "Release", "Common7", "IDE") })
                        if (File.Exists(Path.Combine(ide, "ssms.exe")))
                            Add(FromFile(ide, name));
            }
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct())
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var folder in Directory.EnumerateDirectories(root, "Microsoft SQL Server Management Studio*"))
                foreach (var ide in new[] { Path.Combine(folder, "Common7", "IDE"), Path.Combine(folder, "Release", "Common7", "IDE"), Path.Combine(folder, "Preview", "Common7", "IDE") })
                    if (File.Exists(Path.Combine(ide, "ssms.exe")) && !found.ContainsKey(Path.GetFullPath(ide)))
                        Add(FromFile(ide));
            foreach (var version in new[] { "90", "100", "110", "120", "130", "140" })
            {
                string ide = Path.Combine(root, "Microsoft SQL Server", version, "Tools", "Binn", "ManagementStudio");
                if (File.Exists(Path.Combine(ide, "ssms.exe")))
                    Add(FromFile(ide));
            }
        }
        foreach (var host in found.Values)
            InstalledVersions.Refresh(host);
        return found.Values.OrderBy(h => h.Major).ThenBy(h => h.Ide).ToList();
    }
    public static List<Host> ParseSetupJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<Host>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string Get(string key) => item.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            if (Get("productId") != "Microsoft.VisualStudio.Product.Ssms")
                continue;
            string path = Get("productPath");
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Get("installationPath"), "Common7", "IDE", "ssms.exe");
            int.TryParse(Get("installationVersion").Split('.')[0], out int major);
            result.Add(new Host { Name = Get("displayName"), Ide = Path.GetDirectoryName(path)!, Major = major, InstanceId = Get("instanceId"), Complete = item.TryGetProperty("isComplete", out var c) && c.GetBoolean() && item.TryGetProperty("isLaunchable", out var l) && l.GetBoolean() });
        }
        return result;
    }
    static Host FromFile(string ide, string? name = null)
    {
        var version = FileVersionInfo.GetVersionInfo(Path.Combine(ide, "ssms.exe"));
        var match = Regex.Match(name ?? ide, @"Management Studio\s*[- ]?\s*(\d+)", RegexOptions.IgnoreCase);
        int major = match.Success ? int.Parse(match.Groups[1].Value) : version.ProductMajorPart;
        return new Host { Name = "SSMS " + major, Ide = ide, Major = major };
    }
    public static bool Running(Host host) => Process.GetProcessesByName("ssms").Any(p =>
    {
        try
        {
            return string.Equals(p.MainModule?.FileName, host.Executable, StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
        finally { p.Dispose(); }
    });
}
