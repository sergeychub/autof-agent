using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WorkstationAgent.Printing;

internal sealed class DirectUsbPrinterClient
{
    public void Send(UsbPrinterDeviceInfo device, byte[] bytes, int writeTimeoutMs)
    {
        using var handle = CreateFile(
            device.DevicePath,
            FileAccess.Write,
            FileShare.ReadWrite,
            IntPtr.Zero,
            FileMode.Open,
            FileAttributes.Normal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open USB printer device '{device.DevicePath}'.");
        }

        if (!WriteFile(handle, bytes, bytes.Length, out var written, IntPtr.Zero) || written != bytes.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to write USB payload to '{device.DevicePath}'.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        FileAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        IntPtr lpOverlapped);
}
