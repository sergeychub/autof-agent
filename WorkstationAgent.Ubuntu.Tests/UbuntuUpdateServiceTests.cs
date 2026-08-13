using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkstationAgent.Update;

namespace WorkstationAgent.Ubuntu.Tests;

[TestClass]
public sealed class UbuntuUpdateServiceTests
{
    [TestMethod]
    public async Task SignedReleaseIsInstalledAndRestarted()
    {
        using var rsa = RSA.Create(3072);
        var archive = CreateArchive("new-linux-agent");
        var manifest = CreateManifest(archive);
        manifest.Signature = Sign(rsa, manifest);
        var handler = new UpdateHttpHandler(manifest, archive);
        using var httpClient = new HttpClient(handler);
        var testDirectory = CreateTestDirectory();

        try
        {
            var targetBinary = Path.Combine(testDirectory, "install", "WorkstationAgent.Ubuntu");
            Directory.CreateDirectory(Path.GetDirectoryName(targetBinary)!);
            await File.WriteAllTextAsync(targetBinary, "old-linux-agent");
            var restarted = false;
            var stateStore = new UpdateStateStore(Path.Combine(testDirectory, "updates", "state.json"));
            var service = CreateService(
                httpClient,
                stateStore,
                testDirectory,
                targetBinary,
                rsa.ExportSubjectPublicKeyInfoPem(),
                _ =>
                {
                    restarted = true;
                    return Task.CompletedTask;
                });

            var outcome = await service.RunOnceAsync(CancellationToken.None);

            Assert.AreEqual(UbuntuUpdateOutcome.Updated, outcome);
            Assert.IsTrue(restarted);
            Assert.AreEqual("new-linux-agent", await File.ReadAllTextAsync(targetBinary));
            Assert.AreEqual("old-linux-agent", await File.ReadAllTextAsync(targetBinary + ".previous"));
            Assert.AreEqual(UpdateStatuses.Success, stateStore.ReadStatus());
            CollectionAssert.AreEqual(
                new[]
                {
                    UpdateStatuses.Available,
                    UpdateStatuses.Downloading,
                    UpdateStatuses.Installing,
                    UpdateStatuses.Success
                },
                handler.ReportedStatuses.ToArray());
            Assert.IsTrue(handler.AllRequestsAuthenticated);
            Assert.IsTrue(handler.AllRequestsUseHttps);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvalidManifestSignatureDoesNotReplaceBinary()
    {
        using var rsa = RSA.Create(3072);
        var archive = CreateArchive("untrusted-linux-agent");
        var manifest = CreateManifest(archive);
        manifest.Signature = Convert.ToBase64String(new byte[384]);
        var handler = new UpdateHttpHandler(manifest, archive);
        using var httpClient = new HttpClient(handler);
        var testDirectory = CreateTestDirectory();

        try
        {
            var targetBinary = Path.Combine(testDirectory, "install", "WorkstationAgent.Ubuntu");
            Directory.CreateDirectory(Path.GetDirectoryName(targetBinary)!);
            await File.WriteAllTextAsync(targetBinary, "old-linux-agent");
            var restarted = false;
            var stateStore = new UpdateStateStore(Path.Combine(testDirectory, "updates", "state.json"));
            var service = CreateService(
                httpClient,
                stateStore,
                testDirectory,
                targetBinary,
                rsa.ExportSubjectPublicKeyInfoPem(),
                _ =>
                {
                    restarted = true;
                    return Task.CompletedTask;
                });

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.RunOnceAsync(CancellationToken.None));

            Assert.IsFalse(restarted);
            Assert.AreEqual("old-linux-agent", await File.ReadAllTextAsync(targetBinary));
            Assert.AreEqual(UpdateStatuses.Failure, stateStore.ReadStatus());
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FailedRestartRestoresPreviousBinary()
    {
        using var rsa = RSA.Create(3072);
        var archive = CreateArchive("new-linux-agent");
        var manifest = CreateManifest(archive);
        manifest.Signature = Sign(rsa, manifest);
        var handler = new UpdateHttpHandler(manifest, archive);
        using var httpClient = new HttpClient(handler);
        var testDirectory = CreateTestDirectory();

        try
        {
            var targetBinary = Path.Combine(testDirectory, "install", "WorkstationAgent.Ubuntu");
            Directory.CreateDirectory(Path.GetDirectoryName(targetBinary)!);
            await File.WriteAllTextAsync(targetBinary, "old-linux-agent");
            var stateStore = new UpdateStateStore(Path.Combine(testDirectory, "updates", "state.json"));
            var service = CreateService(
                httpClient,
                stateStore,
                testDirectory,
                targetBinary,
                rsa.ExportSubjectPublicKeyInfoPem(),
                _ => throw new InvalidOperationException("restart failed"));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.RunOnceAsync(CancellationToken.None));

            Assert.AreEqual("old-linux-agent", await File.ReadAllTextAsync(targetBinary));
            Assert.AreEqual(UpdateStatuses.Failure, stateStore.ReadStatus());
            Assert.AreEqual(UpdateStatuses.Failure, handler.ReportedStatuses.Last());
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static UbuntuUpdateService CreateService(
        HttpClient httpClient,
        UpdateStateStore stateStore,
        string testDirectory,
        string targetBinary,
        string publicKey,
        Func<CancellationToken, Task> restartAgent)
    {
        return new UbuntuUpdateService(
            new AgentSettings
            {
                ApiBaseUrl = "https://api.test",
                UpdateChannel = "main",
                AutoUpdateEnabled = true
            },
            new AgentIdentity
            {
                DeviceId = "device-1",
                AgentName = "ubuntu-test",
                ApiKey = "api-key-1",
                SocketIoUrl = "https://api.test/workstation-agent"
            },
            new AgentLogger(null),
            httpClient,
            stateStore,
            Path.Combine(testDirectory, "updates"),
            targetBinary,
            "0.1.1+old",
            publicKey,
            restartAgent);
    }

    private static UpdateManifest CreateManifest(byte[] archive)
    {
        return new UpdateManifest
        {
            ReleaseId = "linux-main-2-abcdef123456",
            Channel = "main",
            Runtime = UbuntuUpdateService.Runtime,
            Version = "0.1.2+abcdef123456",
            CommitSha = "abcdef1234567890",
            PublishedAtUtc = "2026-08-13T00:00:00.0000000Z",
            SizeBytes = archive.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant(),
            DownloadUrl = "/workstation-agent/update/download/linux-main-2-abcdef123456"
        };
    }

    private static string Sign(RSA rsa, UpdateManifest manifest)
    {
        return Convert.ToBase64String(
            rsa.SignData(
                UpdateManifestVerifier.BuildSigningPayload(manifest),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss));
    }

    private static byte[] CreateArchive(string binaryContent)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("WorkstationAgent.Ubuntu", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(binaryContent);
        }
        return output.ToArray();
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "autof-ubuntu-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class UpdateHttpHandler : HttpMessageHandler
    {
        private readonly UpdateManifest _manifest;
        private readonly byte[] _archive;

        public UpdateHttpHandler(UpdateManifest manifest, byte[] archive)
        {
            _manifest = manifest;
            _archive = archive;
        }

        public List<string> ReportedStatuses { get; } = [];

        public bool AllRequestsAuthenticated { get; private set; } = true;

        public bool AllRequestsUseHttps { get; private set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AllRequestsAuthenticated &=
                request.Headers.Authorization?.Scheme == "Bearer" &&
                request.Headers.Authorization.Parameter == "api-key-1" &&
                request.Headers.TryGetValues("X-Api-Key", out var values) &&
                values.Single() == "api-key-1";
            AllRequestsUseHttps &= request.RequestUri?.Scheme == Uri.UriSchemeHttps;

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/update/latest") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(_manifest)
                };
            }
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.Contains("/update/download/") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_archive)
                };
            }
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/update/report") == true)
            {
                var report = await request.Content!.ReadFromJsonAsync<UpdateReportRequest>(
                    cancellationToken: cancellationToken);
                ReportedStatuses.Add(report!.Status);
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
