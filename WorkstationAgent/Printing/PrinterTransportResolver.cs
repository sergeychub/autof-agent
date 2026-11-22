using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal sealed class PrinterTransportResolver
{
    private readonly IPrinterTransport _windowsSpoolerTransport;
    private readonly IPrinterTransport _directUsbTransport;

    public PrinterTransportResolver(
        IPrinterTransport windowsSpoolerTransport,
        IPrinterTransport directUsbTransport)
    {
        _windowsSpoolerTransport = windowsSpoolerTransport;
        _directUsbTransport = directUsbTransport;
    }

    public IPrinterTransport Resolve(PrinterEndpointSettings settings)
    {
        return PrinterTransportMode.IsDirectUsb(settings.TransportMode)
            ? _directUsbTransport
            : _windowsSpoolerTransport;
    }
}
