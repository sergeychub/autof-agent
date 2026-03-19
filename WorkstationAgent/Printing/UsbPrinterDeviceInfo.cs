namespace WorkstationAgent.Printing;

internal sealed class UsbPrinterDeviceInfo
{
    public string DevicePath { get; init; } = string.Empty;

    public string InstanceId { get; init; } = string.Empty;

    public string? VendorId { get; init; }

    public string? ProductId { get; init; }
}
