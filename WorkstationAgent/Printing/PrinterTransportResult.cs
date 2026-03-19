namespace WorkstationAgent.Printing;

internal sealed class PrinterTransportResult
{
    public static PrinterTransportResult Ready(string message)
    {
        return new PrinterTransportResult { IsReady = true, Message = message };
    }

    public static PrinterTransportResult Unavailable(string message)
    {
        return new PrinterTransportResult { IsReady = false, Message = message };
    }

    public bool IsReady { get; init; }

    public string Message { get; init; } = string.Empty;
}
