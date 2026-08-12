using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkstationAgent.Ubuntu.Tests;

[TestClass]
public sealed class PrinterTransportClientTests
{
    [TestMethod]
    public async Task DeviceTransportWritesPayloadWithoutModification()
    {
        using var temporary = new TemporaryDirectory();
        var devicePath = Path.Combine(temporary.Path, "lp0");
        File.WriteAllBytes(devicePath, []);
        var payload = new byte[] { 0x1B, 0x40, 0xD2, 0xE5, 0xF1, 0xF2, 0x0A };
        var endpoint = new PrinterEndpointSettings
        {
            TransportMode = PrinterTransportMode.Device,
            DevicePath = devicePath
        };

        await new PrinterTransportClient().SendAsync(endpoint, payload, "device-test", CancellationToken.None);

        CollectionAssert.AreEqual(payload, File.ReadAllBytes(devicePath));
    }

    [TestMethod]
    public async Task TcpTransportWritesPayloadToRaw9100Socket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = new byte[] { 0x53, 0x49, 0x5A, 0x45, 0x20, 0x33, 0x30, 0x0D, 0x0A };
        var endpoint = new PrinterEndpointSettings
        {
            TransportMode = PrinterTransportMode.Tcp,
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ConnectTimeoutSeconds = 2
        };

        var acceptTask = listener.AcceptTcpClientAsync();
        await new PrinterTransportClient().SendAsync(endpoint, payload, "tcp-test", CancellationToken.None);
        using var accepted = await acceptTask;
        using var received = new MemoryStream();
        await accepted.GetStream().CopyToAsync(received);

        CollectionAssert.AreEqual(payload, received.ToArray());
    }

    [TestMethod]
    public async Task CupsTransportUsesRawQueueAndStandardInput()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The CUPS command shim test requires Linux executable permissions.");
            return;
        }

        using var temporary = new TemporaryDirectory();
        var executable = Path.Combine(temporary.Path, "lp");
        var argumentsPath = Path.Combine(temporary.Path, "arguments.txt");
        var payloadPath = Path.Combine(temporary.Path, "payload.bin");
        File.WriteAllText(
            executable,
            $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{argumentsPath}'\ncat > '{payloadPath}'\necho 'request id is mock-1 (1 file(s))'\n");
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var payload = new byte[] { 0x1B, 0x40, 0x54, 0x45, 0x53, 0x54, 0x0A };
        var endpoint = new PrinterEndpointSettings
        {
            TransportMode = PrinterTransportMode.Cups,
            PrinterName = "mock_queue"
        };

        await new PrinterTransportClient(executable)
            .SendAsync(endpoint, payload, "CUPS integration", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "-d", "mock_queue", "-o", "raw", "-t", "CUPS integration", "-" },
            File.ReadAllLines(argumentsPath));
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(payloadPath));
    }

    [TestMethod]
    public async Task CupsTransportReturnsBackendError()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The CUPS command shim test requires Linux executable permissions.");
            return;
        }

        using var temporary = new TemporaryDirectory();
        var executable = Path.Combine(temporary.Path, "lp-failure");
        File.WriteAllText(
            executable,
            "#!/bin/sh\ncat > /dev/null\necho 'mock queue unavailable' >&2\nexit 7\n");
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var endpoint = new PrinterEndpointSettings
        {
            TransportMode = PrinterTransportMode.Cups,
            PrinterName = "mock_queue"
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new PrinterTransportClient(executable)
                .SendAsync(endpoint, [0x1B, 0x40], "failure-test", CancellationToken.None));

        StringAssert.Contains(exception.Message, "mock queue unavailable");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autof-agent-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
