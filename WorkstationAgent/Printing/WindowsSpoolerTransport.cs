using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal sealed class WindowsSpoolerTransport : IPrinterTransport
{
    private readonly PrinterDiscoveryService _discoveryService;
    private readonly RawPrinterClient _rawPrinterClient;

    public WindowsSpoolerTransport(PrinterDiscoveryService discoveryService, RawPrinterClient rawPrinterClient)
    {
        _discoveryService = discoveryService;
        _rawPrinterClient = rawPrinterClient;
    }

    public string Mode => PrinterTransportMode.WindowsSpooler;

    public bool SupportsImages => true;

    public PrinterTransportResult Probe(AgentSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PrinterName))
        {
            return PrinterTransportResult.Unavailable("Printer name is not configured.");
        }

        return _discoveryService.PrinterExists(settings.PrinterName)
            ? PrinterTransportResult.Ready($"Text print ready via Windows spooler: {settings.PrinterName}")
            : PrinterTransportResult.Unavailable($"Printer not found in Windows: {settings.PrinterName}");
    }

    public void Send(AgentSettings settings, byte[] bytes, string documentName)
    {
        _rawPrinterClient.Send(settings.PrinterName, bytes, documentName);
    }
}
