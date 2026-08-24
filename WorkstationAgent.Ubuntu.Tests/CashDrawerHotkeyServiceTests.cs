using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkstationAgent.Ubuntu.Tests;

[TestClass]
public sealed class CashDrawerHotkeyServiceTests
{
    [TestMethod]
    public async Task F6SendsTheFixedDrawerCommandToTheReceiptPrinter()
    {
        using var temporary = new TemporaryDirectory();
        var devicePath = Path.Combine(temporary.Path, "receipt-device");
        File.WriteAllBytes(devicePath, []);
        var settings = new AgentSettings
        {
            ReceiptPrinter = new PrinterEndpointSettings
            {
                Enabled = true,
                TransportMode = PrinterTransportMode.Device,
                DevicePath = devicePath
            },
            LabelPrinter = new PrinterEndpointSettings { Enabled = false }
        };
        var printerService = new PrinterService(
            settings,
            new AgentLogger(null),
            new PrintPayloadBuilder(new ImageMagickRasterizer()));
        var service = new CashDrawerHotkeyService(
            settings,
            printerService.OpenCashDrawerAsync,
            _ => Task.FromResult(true),
            new AgentLogger(null));

        await service.HandleF6Async(CancellationToken.None);

        CollectionAssert.AreEqual(
            new byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA },
            File.ReadAllBytes(devicePath));
    }

    [TestMethod]
    public async Task RepeatedF6IsIgnoredWhileOpeningIsInProgress()
    {
        var openingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOpening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var settings = new AgentSettings { CashDrawerHotkeyEnabled = true };
        var service = new CashDrawerHotkeyService(
            settings,
            async cancellationToken =>
            {
                Interlocked.Increment(ref callCount);
                openingStarted.SetResult();
                await releaseOpening.Task.WaitAsync(cancellationToken);
                return true;
            },
            _ => Task.FromResult(true),
            new AgentLogger(null));

        var firstPress = service.HandleF6Async(CancellationToken.None);
        await openingStarted.Task;
        await service.HandleF6Async(CancellationToken.None);

        Assert.AreEqual(1, callCount);
        releaseOpening.SetResult();
        await firstPress;
    }

    [TestMethod]
    public async Task F6IsIgnoredWhenNoUserIsLoggedIn()
    {
        var callCount = 0;
        var service = new CashDrawerHotkeyService(
            new AgentSettings { CashDrawerHotkeyEnabled = true },
            _ =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(true);
            },
            _ => Task.FromResult(false),
            new AgentLogger(null));

        await service.HandleF6Async(CancellationToken.None);

        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public async Task F6IsIgnoredWhenSessionStatusCannotBeDetermined()
    {
        var callCount = 0;
        var service = new CashDrawerHotkeyService(
            new AgentSettings { CashDrawerHotkeyEnabled = true },
            _ =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(true);
            },
            _ => throw new InvalidOperationException("logind unavailable"),
            new AgentLogger(null));

        await service.HandleF6Async(CancellationToken.None);

        Assert.AreEqual(0, callCount);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autof-agent-hotkey-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
