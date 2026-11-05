using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Services;

internal sealed class CashDrawerService
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly Action<ReceiptPrinterSettings, byte[]> _send;
    private int _busy;

    public CashDrawerService(AgentSettings settings, FileLogger logger, Action<ReceiptPrinterSettings, byte[]> send)
    {
        _settings = settings;
        _logger = logger;
        _send = send;
    }

    public async Task<bool> TryOpenAsync()
    {
        if (!_settings.CashDrawerHotkeyEnabled || !_settings.ReceiptPrinter.Enabled ||
            Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            var endpoint = _settings.ReceiptPrinter.Clone();
            if (PrinterTransportMode.IsDirectUsb(endpoint.TransportMode))
            {
                if (string.IsNullOrWhiteSpace(endpoint.UsbVendorId) || string.IsNullOrWhiteSpace(endpoint.UsbProductId))
                    throw new InvalidOperationException("Cash drawer requires explicit receipt printer USB VID/PID.");
            }
            else if (string.IsNullOrWhiteSpace(endpoint.PrinterName))
            {
                throw new InvalidOperationException("Cash drawer requires a named receipt printer.");
            }

            // ESC p: connector pin 2, 50 ms on / 500 ms off. No paper feed or cut.
            await Task.Run(() => _send(endpoint, [0x1B, 0x70, 0x00, 0x19, 0xFA])).ConfigureAwait(false);
            _logger.Info("Cash drawer F8 pulse sent to the configured receipt printer.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Cash drawer F8 failed.", ex);
            return false;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
