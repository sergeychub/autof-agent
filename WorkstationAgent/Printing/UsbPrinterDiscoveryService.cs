using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace WorkstationAgent.Printing;

internal sealed class UsbPrinterDiscoveryService
{
    private static readonly Guid UsbPrintInterfaceGuid = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    private static readonly Regex VidRegex = new("VID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PidRegex = new("PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<UsbPrinterDeviceInfo> GetDevices()
    {
        var devices = new List<UsbPrinterDeviceInfo>();
        var interfaceGuid = UsbPrintInterfaceGuid;
        var infoSet = SetupDiGetClassDevs(ref interfaceGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (infoSet == IntPtr.Zero || infoSet == InvalidHandleValue)
        {
            return devices;
        }

        try
        {
            var index = 0;
            while (true)
            {
                var interfaceData = SP_DEVICE_INTERFACE_DATA.Create();
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Unable to enumerate USB printer interfaces.");
                }

                var detailDeviceInfoData = SP_DEVINFO_DATA.Create();
                SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, ref detailDeviceInfoData);
                if (requiredSize <= 0)
                {
                    index++;
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize, out _, ref detailDeviceInfoData))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resolve USB printer interface details.");
                    }

                    var devicePath = Marshal.PtrToStringAuto(detailBuffer + 4) ?? string.Empty;
                    var instanceId = GetDeviceInstanceId(infoSet, ref detailDeviceInfoData);
                    devices.Add(new UsbPrinterDeviceInfo
                    {
                        DevicePath = devicePath,
                        InstanceId = instanceId,
                        VendorId = ExtractId(VidRegex, $"{devicePath} {instanceId}"),
                        ProductId = ExtractId(PidRegex, $"{devicePath} {instanceId}")
                    });
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }

                index++;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }

        return devices;
    }

    public UsbPrinterDeviceInfo? FindBestMatch(string? vendorId, string? productId)
    {
        var normalizedVid = NormalizeHex(vendorId);
        var normalizedPid = NormalizeHex(productId);
        var devices = GetDevices();

        if (!string.IsNullOrWhiteSpace(normalizedVid) || !string.IsNullOrWhiteSpace(normalizedPid))
        {
            return devices.FirstOrDefault(device =>
                (string.IsNullOrWhiteSpace(normalizedVid) || string.Equals(device.VendorId, normalizedVid, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(normalizedPid) || string.Equals(device.ProductId, normalizedPid, StringComparison.OrdinalIgnoreCase)));
        }

        return devices.FirstOrDefault();
    }

    private static string GetDeviceInstanceId(IntPtr infoSet, ref SP_DEVINFO_DATA deviceInfoData)
    {
        Span<char> buffer = stackalloc char[512];
        if (!SetupDiGetDeviceInstanceId(infoSet, ref deviceInfoData, ref MemoryMarshal.GetReference(buffer), buffer.Length, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ERROR_INSUFFICIENT_BUFFER)
            {
                var resized = new char[requiredSize];
                if (!SetupDiGetDeviceInstanceId(infoSet, ref deviceInfoData, ref resized[0], resized.Length, out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read USB printer device instance ID.");
                }

                return new string(resized).TrimEnd('\0');
            }

            throw new Win32Exception(error, "Unable to read USB printer device instance ID.");
        }

        return new string(buffer).TrimEnd('\0');
    }

    private static string? ExtractId(Regex regex, string source)
    {
        var match = regex.Match(source);
        return match.Success ? NormalizeHex(match.Groups[1].Value) : null;
    }

    private static string? NormalizeHex(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        int memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        ref char deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;

        public static SP_DEVICE_INTERFACE_DATA Create()
        {
            return new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;

        public static SP_DEVINFO_DATA Create()
        {
            return new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
        }
    }
}
