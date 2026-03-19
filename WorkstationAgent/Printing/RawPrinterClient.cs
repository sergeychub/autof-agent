using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkstationAgent.Printing;

internal sealed class RawPrinterClient
{
    public void Send(string printerName, byte[] bytes, string documentName)
    {
        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open printer '{printerName}'.");
        }

        try
        {
            var docInfo = new DocInfo
            {
                DocumentName = documentName,
                DataType = "RAW"
            };

            if (!StartDocPrinter(printerHandle, 1, docInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to start RAW document on '{printerName}'.");
            }

            try
            {
                if (!StartPagePrinter(printerHandle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to start RAW page on '{printerName}'.");
                }

                try
                {
                    if (!WritePrinter(printerHandle, bytes, bytes.Length, out var written) || written != bytes.Length)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to write RAW bytes to '{printerName}'.");
                    }
                }
                finally
                {
                    EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            ClosePrinter(printerHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? DocumentName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? OutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? DataType;
    }

    [DllImport("winspool.Drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DocInfo docInfo);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);
}
