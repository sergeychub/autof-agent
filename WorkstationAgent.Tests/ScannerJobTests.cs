using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Scanning;

namespace WorkstationAgent.Tests;

[TestClass]
public class ScannerJobTests
{
    private sealed class Scanner : IDocumentScanner
    {
        public int Calls;
        public bool Single;
        public string? Source;
        public Task<ScannerCapability[]> Discover(CancellationToken ct = default) => Task.FromResult(Array.Empty<ScannerCapability>());
        public async Task<int> Scan(string key, string mode, string source, bool single, string output, Action<int>? progress, CancellationToken ct)
        {
            Calls++; Source = source; Single = single;
            await File.WriteAllTextAsync(output, single ? "PNG" : "%PDF-1.4 batch", ct);
            return single ? 1 : 3;
        }
        public void Dispose() { }
    }
    private sealed class Server : HttpMessageHandler
    {
        public string State = "PENDING";
        public bool Replace;
        public int Uploads;
        public bool Retry;
        public HttpStatusCode? Reject;
        public bool CancelBeforeUpload;
        public long UploadedBytes;
        private int _gets;
        public string? ContentType;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual("device", request.Headers.GetValues("x-device-id").Single());
            Assert.AreEqual("key", request.Headers.GetValues("x-api-key").Single());
            if (request.Method == HttpMethod.Get)
            {
                if (++_gets > 1 && CancelBeforeUpload) State = "CANCELLED";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = State, scannerKey = "fi7160", mode = "Color", replacesPageId = Replace ? (int?)42 : null }) };
            }
            Uploads++;
            var multipart = (MultipartFormDataContent)request.Content!;
            ContentType = multipart.Single().Headers.ContentType!.MediaType;
            var input = await multipart.Single().ReadAsStreamAsync(ct);
            Assert.IsTrue(input.Length > 0);
            UploadedBytes += input.Length;
            await input.CopyToAsync(Stream.Null, ct);
            if (Reject is { } rejected) return new HttpResponseMessage(rejected);
            if (Retry && Uploads == 1) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            State = "COMPLETED";
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
    private static JsonElement Command(Guid id, bool uploadOnly = false) => JsonSerializer.SerializeToElement(new { jobId = id, source = "Duplex", uploadOnly });
    private static ScannerJobService Service(string dir, Scanner scanner, Server server, List<JsonElement>? statuses = null,
        Action<JsonElement>? onStatus = null) => new(
        new AgentSettings { DeviceId = "device", ApiKey = "key", ApiBaseUrl = "https://test.invalid" },
        new FileLogger(Path.Combine(dir, "test.log")), dir,
        status => { var payload = JsonSerializer.SerializeToElement(status); statuses?.Add(payload); onStatus?.Invoke(payload); return Task.CompletedTask; }, scanner, new HttpClient(server));
    private static string DirectoryForTest() => Path.Combine(Path.GetTempPath(), "autof-scanner-tests", Guid.NewGuid().ToString());

    [TestMethod]
    public async Task DuplicateDeliveryAndCompletedReplayNeverRescan()
    {
        var scanner = new Scanner(); var server = new Server(); var dir = DirectoryForTest();
        await using var service = Service(dir, scanner, server);
        var command = Command(Guid.NewGuid());
        service.Start(command); service.Start(command);
        await service.WaitForIdleAsync();
        service.Start(command); await service.WaitForIdleAsync();
        Assert.AreEqual(1, scanner.Calls); Assert.AreEqual(1, server.Uploads);
        Assert.AreEqual("Duplex", scanner.Source); Assert.AreEqual("application/pdf", server.ContentType);
    }
    [TestMethod]
    public async Task FailedUploadRetriesSavedBatchWithoutMovingPaperAgain()
    {
        var scanner = new Scanner(); var server = new Server { Retry = true };
        await using var service = Service(DirectoryForTest(), scanner, server);
        service.Start(Command(Guid.NewGuid())); await service.WaitForIdleAsync();
        Assert.AreEqual(1, scanner.Calls); Assert.AreEqual(2, server.Uploads);
    }
    [TestMethod]
    public async Task CancelledRemoteJobNeverStartsHardware()
    {
        var scanner = new Scanner(); var server = new Server { State = "CANCELLED" };
        await using var service = Service(DirectoryForTest(), scanner, server);
        service.Start(Command(Guid.NewGuid())); await service.WaitForIdleAsync();
        Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(0, server.Uploads);
    }
    [TestMethod]
    public async Task RestartAfterFeedingDoesNotConsumeAnotherBatch()
    {
        var scanner = new Scanner(); var server = new Server { State = "RUNNING" }; var dir = DirectoryForTest();
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid(); File.WriteAllText(Path.Combine(dir, id + ".pdf.started"), "");
        await using var service = Service(dir, scanner, server);
        service.Start(Command(id)); await service.WaitForIdleAsync();
        Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(0, server.Uploads);
    }
    [TestMethod]
    public async Task ReplacementOnlyCapturesOnePageAndUploadsPng()
    {
        var scanner = new Scanner(); var server = new Server { Replace = true };
        await using var service = Service(DirectoryForTest(), scanner, server);
        service.Start(Command(Guid.NewGuid())); await service.WaitForIdleAsync();
        Assert.IsTrue(scanner.Single); Assert.AreEqual("image/png", server.ContentType);
    }

    private static string SavedBatch(string dir, Guid id, long length)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".pdf");
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) output.SetLength(length);
        File.WriteAllText(path + ".ready", "500");
        File.WriteAllText(path + ".started", "");
        return path;
    }

    [TestMethod]
    [DataRow(51L * 1024 * 1024)]
    [DataRow(250L * 1024 * 1024)]
    public async Task UploadOnlyReusesLargeReadyBatchAndPreservesReceiptMarkers(long length)
    {
        var dir = DirectoryForTest(); var id = Guid.NewGuid(); var path = SavedBatch(dir, id, length);
        try
        {
            var scanner = new Scanner(); var server = new Server();
            await using var service = Service(dir, scanner, server);
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(1, server.Uploads);
            Assert.AreEqual(length, server.UploadedBytes);
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(File.Exists(path + ".ready")); Assert.IsTrue(File.Exists(path + ".started"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task OverLimitRecoveryKeepsFullBatchWithoutUploadingOrRescanning()
    {
        var dir = DirectoryForTest(); var id = Guid.NewGuid();
        var path = SavedBatch(dir, id, 250L * 1024 * 1024 + 1);
        try
        {
            var scanner = new Scanner(); var server = new Server(); var statuses = new List<JsonElement>();
            await using var service = Service(dir, scanner, server, statuses);
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(0, server.Uploads);
            Assert.AreEqual(250L * 1024 * 1024 + 1, new FileInfo(path).Length);
            Assert.IsTrue(File.Exists(path + ".ready")); Assert.IsTrue(File.Exists(path + ".started"));
            Assert.AreEqual("FAILED", statuses.Last().GetProperty("status").GetString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [DataRow("missing-ready")]
    [DataRow("missing-file")]
    [DataRow("empty-file")]
    [DataRow("partial-only")]
    [DataRow("nothing")]
    public async Task UploadOnlyNeverFeedsPaperWhenTheCompletedBatchIsUnavailable(string scenario)
    {
        var dir = DirectoryForTest(); var id = Guid.NewGuid(); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".pdf");
        if (scenario is "missing-ready" or "empty-file") File.WriteAllText(path, scenario == "empty-file" ? "" : "%PDF-saved");
        if (scenario is "missing-file" or "empty-file") File.WriteAllText(path + ".ready", "3");
        if (scenario == "partial-only") File.WriteAllText(Path.ChangeExtension(path, "partial.pdf"), "%PDF-partial");
        var before = Directory.GetFiles(dir).ToDictionary(name => name, File.ReadAllBytes);
        try
        {
            var scanner = new Scanner(); var server = new Server(); var statuses = new List<JsonElement>();
            await using var service = Service(dir, scanner, server, statuses);
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(0, server.Uploads);
            Assert.AreEqual("FAILED", statuses.Last().GetProperty("status").GetString());
            StringAssert.Contains(statuses.Last().GetProperty("error").GetString()!, "повторное сканирование не запускалось");
            Assert.IsFalse(File.Exists(path + ".started"));
            foreach (var (name, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(name));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task RejectedRecoveryCanRetryTheSameSavedFileWithoutRescanning()
    {
        var dir = DirectoryForTest(); var id = Guid.NewGuid(); var path = SavedBatch(dir, id, 64);
        try
        {
            var scanner = new Scanner(); var server = new Server { Reject = HttpStatusCode.RequestEntityTooLarge };
            await using var service = Service(dir, scanner, server);
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.IsTrue(File.Exists(path)); Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(1, server.Uploads);
            server.State = "FAILED";
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(1, server.Uploads); // Agent cannot reopen a failed job itself.
            server.State = "PENDING"; server.Reject = null;
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(2, server.Uploads); Assert.AreEqual(0, scanner.Calls); Assert.IsFalse(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task CancelBeforeRecoveryUploadRetainsBatchAndDoesNotUpload()
    {
        var dir = DirectoryForTest(); var id = Guid.NewGuid(); var path = SavedBatch(dir, id, 64);
        try
        {
            var scanner = new Scanner(); var server = new Server { CancelBeforeUpload = true };
            await using var service = Service(dir, scanner, server);
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(0, server.Uploads); Assert.IsTrue(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task UploadOnlyCannotStartHardwareIfReadyMarkerDisappearsAfterInitialCheck()
    {
        var dir = DirectoryForTest(); var id = Guid.NewGuid(); var path = SavedBatch(dir, id, 64);
        File.Delete(path + ".started");
        try
        {
            var scanner = new Scanner(); var server = new Server(); var statuses = new List<JsonElement>();
            await using var service = Service(dir, scanner, server, statuses, payload =>
            {
                if (payload.GetProperty("status").GetString() == "RUNNING") File.Delete(path + ".ready");
            });
            service.Start(Command(id, uploadOnly: true)); await service.WaitForIdleAsync();
            Assert.AreEqual(0, scanner.Calls); Assert.AreEqual(0, server.Uploads);
            Assert.IsTrue(File.Exists(path)); Assert.IsFalse(File.Exists(path + ".started"));
            Assert.AreEqual("FAILED", statuses.Last().GetProperty("status").GetString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
