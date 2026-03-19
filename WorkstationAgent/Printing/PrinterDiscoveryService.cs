using System.Drawing.Printing;

namespace WorkstationAgent.Printing;

internal sealed class PrinterDiscoveryService
{
    public IReadOnlyList<string> GetInstalledPrinters()
    {
        return PrinterSettings.InstalledPrinters.Cast<string>().OrderBy(name => name).ToArray();
    }

    public bool PrinterExists(string? printerName)
    {
        return !string.IsNullOrWhiteSpace(printerName)
            && PrinterSettings.InstalledPrinters.Cast<string>().Any(name => string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase));
    }
}
