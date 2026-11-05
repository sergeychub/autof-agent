using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Services;

namespace WorkstationAgent.Tests;

[TestClass]
public sealed class CashDrawerServiceTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "autof-drawer-" + Guid.NewGuid().ToString("N"));
    private FileLogger Logger => new(Path.Combine(_directory, "agent.log"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static AgentSettings Configured() => new()
    {
        CashDrawerHotkeyEnabled = true,
        ReceiptPrinter = new() { Enabled = true, PrinterName = "Receipt" }
    };

    [TestMethod]
    public async Task DisabledHotkeyDoesNotWriteToAnyDevice()
    {
        var settings = Configured();
        settings.CashDrawerHotkeyEnabled = false;
        var calls = 0;
        var service = new CashDrawerService(settings, Logger, (_, _) => calls++);
        Assert.IsFalse(await service.TryOpenAsync());
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task DisabledReceiptPrinterDoesNotWriteToAnyDevice()
    {
        var settings = Configured();
        settings.ReceiptPrinter.Enabled = false;
        var calls = 0;
        var service = new CashDrawerService(settings, Logger, (_, _) => calls++);
        Assert.IsFalse(await service.TryOpenAsync());
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task PulseUsesOnlyTheSelectedReceiptEndpointAndContainsNoReceiptData()
    {
        var settings = Configured();
        settings.LabelPrinter.PrinterName = "Labels";
        ReceiptPrinterSettings? destination = null;
        byte[]? payload = null;
        var service = new CashDrawerService(settings, Logger, (endpoint, bytes) => { destination = endpoint; payload = bytes; });
        Assert.IsTrue(await service.TryOpenAsync());
        Assert.AreEqual("Receipt", destination!.PrinterName);
        Assert.AreNotSame(settings.ReceiptPrinter, destination);
        CollectionAssert.AreEqual(new byte[] { 0x1B, 0x70, 0, 0x19, 0xFA }, payload);
    }

    [TestMethod]
    public async Task UsbRequiresBothIdsInsteadOfSelectingTheFirstPrinter()
    {
        var settings = Configured();
        settings.ReceiptPrinter.TransportMode = "direct-usb";
        settings.ReceiptPrinter.UsbVendorId = "0483";
        var calls = 0;
        var service = new CashDrawerService(settings, Logger, (_, _) => calls++);
        Assert.IsFalse(await service.TryOpenAsync());
        Assert.AreEqual(0, calls);
        settings.ReceiptPrinter.UsbProductId = "070B";
        Assert.IsTrue(await service.TryOpenAsync());
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task SpoolerRequiresAnExplicitPrinterName()
    {
        var settings = Configured();
        settings.ReceiptPrinter.PrinterName = "";
        var calls = 0;
        var service = new CashDrawerService(settings, Logger, (_, _) => calls++);
        Assert.IsFalse(await service.TryOpenAsync());
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task RepeatedPressWhileWritingIsDroppedWithoutQueueing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var service = new CashDrawerService(Configured(), Logger, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        });
        var first = service.TryOpenAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(await service.TryOpenAsync());
            Assert.AreEqual(1, calls);
        }
        finally { release.Set(); }
        Assert.IsTrue(await first);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task FailedDeviceWriteDoesNotBlockTheNextPress()
    {
        var calls = 0;
        var service = new CashDrawerService(Configured(), Logger, (_, _) =>
        {
            if (++calls == 1) throw new IOException("USB disconnected");
        });
        Assert.IsFalse(await service.TryOpenAsync());
        Assert.IsTrue(await service.TryOpenAsync());
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public void ExistingSettingsDefaultToDisabledAndEnabledSettingSurvivesCloningAndStorage()
    {
        var old = JsonSerializer.Deserialize<AgentSettings>("{\"agentName\":\"Existing\"}")!;
        Assert.IsFalse(old.CashDrawerHotkeyEnabled);
        var enabled = Configured().Clone();
        var stored = JsonSerializer.Deserialize<AgentSettings>(JsonSerializer.Serialize(enabled))!;
        Assert.IsTrue(stored.CashDrawerHotkeyEnabled);
    }
}
