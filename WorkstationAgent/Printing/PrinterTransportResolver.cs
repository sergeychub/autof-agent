using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal sealed class PrinterTransportResolver
{
    private readonly WindowsSpoolerTransport _windowsSpoolerTransport;
    private readonly DirectUsbTransport _directUsbTransport;

    public PrinterTransportResolver(
        WindowsSpoolerTransport windowsSpoolerTransport,
        DirectUsbTransport directUsbTransport)
    {
        _windowsSpoolerTransport = windowsSpoolerTransport;
        _directUsbTransport = directUsbTransport;
    }

    public IPrinterTransport Resolve(AgentSettings settings)
    {
        return PrinterTransportMode.IsDirectUsb(settings.TransportMode)
            ? _directUsbTransport
            : _windowsSpoolerTransport;
    }
}
