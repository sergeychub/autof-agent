using System.ComponentModel;
using System.Runtime.InteropServices;
using WorkstationAgent.Infrastructure;

namespace WorkstationAgent.Services;

internal sealed class CashDrawerHotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xAF08;
    private const int WmHotkey = 0x0312;
    private const int WmSessionChange = 0x02B1;
    private readonly CashDrawerService _drawer;
    private readonly FileLogger _logger;
    private bool _registered;
    private bool _sessionNotifications;
    private bool _sessionLocked;
    private bool _disposed;

    public CashDrawerHotkeyWindow(CashDrawerService drawer, FileLogger logger)
    {
        _drawer = drawer;
        _logger = logger;
    }

    public bool Register()
    {
        CreateHandle(new CreateParams { Caption = "Autof Cash Drawer F8", Parent = new IntPtr(-3) });
        _sessionNotifications = WTSRegisterSessionNotification(Handle, 0);
        _registered = RegisterHotKey(Handle, HotkeyId, 0x4000, (uint)Keys.F8);
        if (_registered)
            _logger.Info("Local cash drawer F8 hotkey registered (no key repeat).");
        else
            _logger.Error("Cash drawer F8 hotkey registration failed; F8 may be used by another application.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        return _registered;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSessionChange)
        {
            if (message.WParam.ToInt32() == 7) _sessionLocked = true;
            if (message.WParam.ToInt32() == 8) _sessionLocked = false;
        }
        if (!_disposed && !_sessionLocked && message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
            _ = _drawer.TryOpenAsync();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered) UnregisterHotKey(Handle, HotkeyId);
        if (_sessionNotifications) WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr window, int flags);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
}
