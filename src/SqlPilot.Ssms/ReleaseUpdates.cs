// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPilot.Ssms
{
    [DataContract]
    internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name
        {
            get; set;
        }
        [DataMember(Name = "browser_download_url")]
        public string Url
        {
            get; set;
        }
        [DataMember(Name = "size")]
        public long Size
        {
            get; set;
        }
        [DataMember(Name = "state")]
        public string State
        {
            get; set;
        }
    }
    [DataContract]
    internal sealed class ReleaseInfo
    {
        [DataMember(Name = "tag_name", IsRequired = true)]
        public string Tag
        {
            get; set;
        }
        [DataMember(Name = "draft", IsRequired = true)]
        public bool Draft
        {
            get; set;
        }
        [DataMember(Name = "prerelease", IsRequired = true)]
        public bool Prerelease
        {
            get; set;
        }
        [DataMember(Name = "assets", IsRequired = true)]
        public ReleaseAsset[] Assets
        {
            get; set;
        }
        internal Version Version;
        internal ReleaseAsset Installer;
        internal ReleaseAsset Checksums;
        internal bool IsNewerThan(string installed) => Version > System.Version.Parse(installed);
        internal static ReleaseInfo Parse(byte[] json)
        {
            using (var stream = new MemoryStream(json))
            {
                var release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(stream);
                var match = Regex.Match(release.Tag ?? "", @"^v?(\d+\.\d+\.\d+)$");
                if (release.Draft || release.Prerelease || !match.Success || !Version.TryParse(match.Groups[1].Value, out release.Version))
                    throw new InvalidDataException("The latest release is not a stable SqlPilot version.");
                string name = "SqlPilotSetup-" + release.Version.ToString(3) + ".exe";
                string prefix = ProductInfo.RepositoryUrl + "/releases/download/" + release.Tag + "/";
                release.Installer = release.Assets?.SingleOrDefault(a => a.Name == name && a.State == "uploaded" && a.Url == prefix + name && a.Size > 0 && a.Size <= UpdateClient.MaxInstallerBytes);
                release.Checksums = release.Assets?.SingleOrDefault(a => a.Name == "SHA256SUMS.txt" && a.State == "uploaded" && a.Url == prefix + "SHA256SUMS.txt" && a.Size > 0 && a.Size <= 65536);
                if (release.Installer == null || release.Checksums == null)
                    throw new InvalidDataException("The release installer is not ready. Try again later.");
                return release;
            }
        }
    }
    internal sealed class UpdateClient : IDisposable
    {
        internal const long MaxInstallerBytes = 256L * 1024 * 1024;
        internal const string LatestUrl = "https://api.github.com/repos/tyeety/SQLPilot/releases/latest";
        readonly HttpClient http;
        readonly string root;
        internal UpdateClient(string root, HttpMessageHandler handler = null)
        {
            this.root = Path.GetFullPath(root);
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SqlPilot/" + ProductInfo.Version);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }
        static void ValidateResponse(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            var uri = response.RequestMessage?.RequestUri;
            if (uri == null || uri.Scheme != "https" || !new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected update download location.");
        }
        async Task<byte[]> SmallDownload(string url, int limit, CancellationToken token)
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                ValidateResponse(response);
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        if (output.Length + count > limit)
                            throw new InvalidDataException("Update metadata is too large.");
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
            }
        }
        internal async Task<ReleaseInfo> CheckAsync(CancellationToken token = default)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                return ReleaseInfo.Parse(await SmallDownload(LatestUrl, 1024 * 1024, timeout.Token).ConfigureAwait(false));
            }
        }
        internal static string ExpectedHash(byte[] bytes, string filename)
        {
            var lines = Encoding.UTF8.GetString(bytes).Split('\n');
            var matches = lines.Select(line => Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$")).Where(m => m.Success && m.Groups[2].Value == filename).ToList();
            if (matches.Count != 1)
                throw new InvalidDataException("The installer checksum is missing or ambiguous.");
            return matches[0].Groups[1].Value.ToLowerInvariant();
        }
        static bool HasHash(string path, string expected)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        void EnsureRoot()
        {
            Directory.CreateDirectory(root);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Update cache cannot be a link.");
        }
        internal void CleanCache(string keep = null)
        {
            if (!Directory.Exists(root))
                return;
            EnsureRoot();
            var files = Directory.GetFiles(root).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^SqlPilotSetup-\d+\.\d+\.\d+\.exe$") || Path.GetFileName(p) == "download.part").ToList();
            if (keep == null)
                keep = files.Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            foreach (var path in files.Where(p => !string.Equals(p, keep, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                    File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        internal async Task<string> DownloadAsync(ReleaseInfo release, IProgress<int> progress, CancellationToken token = default)
        {
            EnsureRoot();
            string target = Path.Combine(root, release.Installer.Name), part = Path.Combine(root, "download.part");
            string lockPath = Path.Combine(root, "download.lock");
            if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Update cache lock cannot be a link.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var lease = File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                CleanCache(target);
                if ((File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) || (File.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0))
                    throw new IOException("Update cache contains an unexpected link.");
                string expected = ExpectedHash(await SmallDownload(release.Checksums.Url, 65536, timeout.Token).ConfigureAwait(false), release.Installer.Name);
                if (File.Exists(target) && new FileInfo(target).Length == release.Installer.Size && HasHash(target, expected))
                {
                    progress?.Report(100);
                    return target;
                }
                try
                {
                    using (var response = await http.GetAsync(release.Installer.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        ValidateResponse(response);
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = File.Open(part, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            int count;
                            long total = 0;
                            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                total += count;
                                if (total > release.Installer.Size || total > MaxInstallerBytes)
                                    throw new InvalidDataException("Installer download exceeded its expected size.");
                                await output.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false);
                                progress?.Report((int)(total * 100 / release.Installer.Size));
                            }
                            if (total != release.Installer.Size)
                                throw new InvalidDataException("Installer download is incomplete.");
                        }
                    }
                    if (!HasHash(part, expected))
                        throw new InvalidDataException("Installer checksum verification failed. Nothing was installed.");
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(part, target);
                    CleanCache(target);
                    return target;
                }
                finally { if (File.Exists(part)) File.Delete(part); }
            }
        }
        public void Dispose() => http.Dispose();
    }
}
