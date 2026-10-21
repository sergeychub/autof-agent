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
        public string? ContentType;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual("device", request.Headers.GetValues("x-device-id").Single());
            Assert.AreEqual("key", request.Headers.GetValues("x-api-key").Single());
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = State, scannerKey = "fi7160", mode = "Color", replacesPageId = Replace ? (int?)42 : null }) };
            Uploads++;
            var multipart = (MultipartFormDataContent)request.Content!;
            ContentType = multipart.Single().Headers.ContentType!.MediaType;
            Assert.IsTrue((await multipart.Single().ReadAsByteArrayAsync(ct)).Length > 0);
            if (Retry && Uploads == 1) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            State = "COMPLETED";
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
    private static JsonElement Command(Guid id) => JsonSerializer.SerializeToElement(new { jobId = id, source = "Duplex" });
    private static ScannerJobService Service(string dir, Scanner scanner, Server server) => new(
        new AgentSettings { DeviceId = "device", ApiKey = "key", ApiBaseUrl = "https://test.invalid" },
        new FileLogger(Path.Combine(dir, "test.log")), dir, _ => Task.CompletedTask, scanner, new HttpClient(server));
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
}
