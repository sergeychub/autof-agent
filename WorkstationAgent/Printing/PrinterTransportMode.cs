namespace WorkstationAgent.Printing;

internal static class PrinterTransportMode
{
    public const string WindowsSpooler = "windows-spooler";
    public const string DirectUsb = "direct-usb";

    public static bool IsDirectUsb(string? value)
    {
        return string.Equals(value, DirectUsb, StringComparison.OrdinalIgnoreCase);
    }
}
