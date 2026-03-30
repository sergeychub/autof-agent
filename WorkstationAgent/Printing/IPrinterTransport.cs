using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal interface IPrinterTransport
{
    string Mode { get; }

    bool SupportsImages { get; }

    PrinterTransportResult Probe(PrinterEndpointSettings settings);

    void Send(PrinterEndpointSettings settings, byte[] bytes, string documentName);
}
