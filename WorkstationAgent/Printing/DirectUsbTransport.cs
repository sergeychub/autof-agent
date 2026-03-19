using System.ComponentModel;
using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal sealed class DirectUsbTransport : IPrinterTransport
{
    private readonly UsbPrinterDiscoveryService _discoveryService;
    private readonly DirectUsbPrinterClient _printerClient;

    public DirectUsbTransport(UsbPrinterDiscoveryService discoveryService, DirectUsbPrinterClient printerClient)
    {
        _discoveryService = discoveryService;
        _printerClient = printerClient;
    }

    public string Mode => PrinterTransportMode.DirectUsb;

    public bool SupportsImages => true;

    public PrinterTransportResult Probe(AgentSettings settings)
    {
        try
        {
            var device = _discoveryService.FindBestMatch(settings.UsbVendorId, settings.UsbProductId);
            if (device is null)
            {
                return PrinterTransportResult.Unavailable(
                    "Direct USB device was not found. Configure USB VID/PID or reconnect the printer.");
            }

            return PrinterTransportResult.Ready(
                $"Text print ready via Direct USB: VID={device.VendorId ?? "n/a"} PID={device.ProductId ?? "n/a"}");
        }
        catch (Win32Exception ex)
        {
            return PrinterTransportResult.Unavailable($"Direct USB probe failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return PrinterTransportResult.Unavailable($"Direct USB probe failed: {ex.Message}");
        }
    }

    public void Send(AgentSettings settings, byte[] bytes, string documentName)
    {
        var device = _discoveryService.FindBestMatch(settings.UsbVendorId, settings.UsbProductId);
        if (device is null)
        {
            throw new InvalidOperationException(
                "Direct USB printer was not found. Configure usbVendorId/usbProductId or reconnect the device.");
        }

        _printerClient.Send(device, bytes, settings.UsbWriteTimeoutMs);
    }
}
