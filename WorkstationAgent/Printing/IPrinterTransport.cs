using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal interface IPrinterTransport
{
    string Mode { get; }

    bool SupportsImages { get; }

    PrinterTransportResult Probe(AgentSettings settings);

    void Send(AgentSettings settings, byte[] bytes, string documentName);
}
