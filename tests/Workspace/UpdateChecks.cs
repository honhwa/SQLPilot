using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Ssms;

internal static class UpdateChecks
{
    sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, byte[]> Reply;
        internal int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri.AbsoluteUri.EndsWith(".exe"))
                Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(Reply(request)) });
        }
    }
    static byte[] Json(ReleaseInfo release)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(ReleaseInfo)).WriteObject(stream, release);
            return stream.ToArray();
        }
    }
    static ReleaseInfo Fixture(byte[] exe, string version = "0.13.8")
    {
        string name = "SqlPilotSetup-" + version + ".exe", prefix = "https://github.com/tyeety/SQLPilot/releases/download/v" + version + "/";
        return new ReleaseInfo { Tag = "v" + version, Assets = new[] { new ReleaseAsset { Name = name, Url = prefix + name, State = "uploaded", Size = exe.Length }, new ReleaseAsset { Name = "SHA256SUMS.txt", Url = prefix + "SHA256SUMS.txt", State = "uploaded", Size = 100 } } };
    }
    static bool Reject(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception) { return true; }
    }
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "SqlPilot.UpdateChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] exe = Encoding.UTF8.GetBytes("MZ-ISOLATED-TEST-NOT-EXECUTABLE");
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(exe)).Replace("-", "").ToLowerInvariant();
            var fixture = Fixture(exe);
            var parsed = ReleaseInfo.Parse(Json(fixture));
            check(parsed.IsNewerThan("0.13.7") && !parsed.IsNewerThan("0.13.8") && !parsed.IsNewerThan("0.14.0"), "Update compares stable versions without offering reinstall or downgrade");
            check(ReleaseInfo.Parse(Json(Fixture(exe, "10.0.0"))).IsNewerThan("2.99.99"), "Update compares multi-digit future versions numerically");
            fixture.Prerelease = true;
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update excludes prereleases");
            fixture.Prerelease = false;
            fixture.Draft = true;
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update excludes draft releases");
            fixture.Draft = false;
            fixture.Tag = "v0.13.8-beta";
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update rejects non-stable version tags");
            fixture = Fixture(exe);
            fixture.Assets[0].Url = "https://example.invalid/setup.exe";
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update accepts only the exact official repository installer asset");
            fixture = Fixture(exe);
            fixture.Assets[0].Size = UpdateClient.MaxInstallerBytes + 1;
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update rejects oversized installers");
            fixture = Fixture(exe);
            fixture.Assets[0].State = "new";
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update waits for asset upload completion");
            fixture = Fixture(exe);
            fixture.Assets = new[] { fixture.Assets[0] };
            check(Reject(() => ReleaseInfo.Parse(Json(fixture))), "Update requires a checksum asset");
            check(UpdateClient.ExpectedHash(Encoding.UTF8.GetBytes(hash.ToUpperInvariant() + "  SqlPilotSetup-0.13.8.exe\r\n"), "SqlPilotSetup-0.13.8.exe") == hash, "Update reads checksum filename and normalizes digest case");
            check(Reject(() => UpdateClient.ExpectedHash(Encoding.UTF8.GetBytes(hash + "  other.exe"), "SqlPilotSetup-0.13.8.exe")), "Update rejects missing filename checksum");
            fixture = Fixture(exe);
            bool badHash = false, truncated = false, offline = false;
            var handler = new Handler
            {
                Reply = request =>
            {
                if (offline)
                    throw new HttpRequestException("Isolated offline fixture");
                string url = request.RequestUri.AbsoluteUri;
                if (url == UpdateClient.LatestUrl)
                    return Json(fixture);
                if (url.EndsWith("SHA256SUMS.txt"))
                    return Encoding.UTF8.GetBytes((badHash ? new string('0', 64) : hash) + "  " + parsed.Installer.Name + "\n");
                return truncated ? new byte[2] : exe;
            }
            };
            using (var client = new UpdateClient(root, handler))
            {
                var remote = client.CheckAsync().GetAwaiter().GetResult();
                check(remote.Version.ToString(3) == "0.13.8", "Update client parses release metadata through its HTTP transport");
                string downloaded = client.DownloadAsync(remote, null).GetAwaiter().GetResult();
                check(File.ReadAllText(downloaded) == Encoding.UTF8.GetString(exe) && !File.Exists(Path.Combine(root, "download.part")), "Verified update is committed without leaving a partial file");
                client.DownloadAsync(remote, null).GetAwaiter().GetResult();
                check(handler.Downloads == 1, "Verified cached installer is reused instead of downloading another copy");
                badHash = true;
                check(Reject(() => client.DownloadAsync(remote, null).GetAwaiter().GetResult()) && !File.Exists(Path.Combine(root, "download.part")), "Checksum failure never commits a download and removes partial files");
                badHash = false;
                File.Delete(downloaded);
                truncated = true;
                check(Reject(() => client.DownloadAsync(remote, null).GetAwaiter().GetResult()) && !File.Exists(downloaded) && !File.Exists(Path.Combine(root, "download.part")), "Truncated update never becomes an installable cache entry");
                truncated = false;
                offline = true;
                check(Reject(() => client.CheckAsync().GetAwaiter().GetResult()), "Offline update check propagates failure rather than claiming up to date");
                offline = false;
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    check(Reject(() => client.DownloadAsync(remote, null, cancelled.Token).GetAwaiter().GetResult()), "Update download cancellation is respected");
                }
                File.WriteAllText(Path.Combine(root, "notes.sql"), "preserve");
                string old = Path.Combine(root, "SqlPilotSetup-0.1.0.exe"), active = Path.Combine(root, "SqlPilotSetup-0.2.0.exe");
                File.WriteAllText(old, "old");
                File.WriteAllText(active, "active");
                using (var lease = File.Open(active, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    client.CleanCache(downloaded);
                    check(!File.Exists(old) && File.Exists(active) && File.ReadAllText(Path.Combine(root, "notes.sql")) == "preserve", "Update cache removes retired installers but preserves active copies and unrelated files");
                }
                client.CleanCache(downloaded);
                check(!File.Exists(active), "Released retired update cache is removed on next cleanup");
            }
            if (Environment.GetEnvironmentVariable("SQLPILOT_UPDATE_LIVE") == "1")
            {
                using (var client = new UpdateClient(Path.Combine(root, "live")))
                {
                    var remote = client.CheckAsync().GetAwaiter().GetResult();
                    var downloaded = client.DownloadAsync(remote, null).GetAwaiter().GetResult();
                    check(File.Exists(downloaded) && new FileInfo(downloaded).Length == remote.Installer.Size, "Live GitHub release download and SHA256 verification succeed without executing installer");
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
