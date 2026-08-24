using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkstationAgent.Ubuntu;

internal sealed class CashDrawerHotkeyService
{
    private readonly AgentSettings _settings;
    private readonly Func<CancellationToken, Task<bool>> _openCashDrawer;
    private readonly Func<CancellationToken, Task<bool>> _hasActiveUserSession;
    private readonly AgentLogger _logger;
    private readonly IGlobalF6Source? _source;
    private int _opening;

    public CashDrawerHotkeyService(
        AgentSettings settings,
        PrinterService printerService,
        AgentLogger logger,
        IGlobalF6Source? source = null)
        : this(
            settings,
            printerService.OpenCashDrawerAsync,
            new LogindUserSessionProbe().HasActiveUserSessionAsync,
            logger,
            source)
    {
    }

    internal CashDrawerHotkeyService(
        AgentSettings settings,
        Func<CancellationToken, Task<bool>> openCashDrawer,
        Func<CancellationToken, Task<bool>> hasActiveUserSession,
        AgentLogger logger,
        IGlobalF6Source? source = null)
    {
        _settings = settings;
        _openCashDrawer = openCashDrawer;
        _hasActiveUserSession = hasActiveUserSession;
        _logger = logger;
        _source = source;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_settings.CashDrawerHotkeyEnabled)
        {
            _logger.Info("Local cash drawer F6 hotkey is disabled.");
            await WaitForShutdownAsync(cancellationToken);
            return;
        }

        try
        {
            if (_source is not null)
            {
                _logger.Info($"Local cash drawer F6 hotkey enabled through {_source.DisplayName}.");
                await _source.RunAsync(HandleF6Async, cancellationToken);
            }
            else
            {
                await RunPlatformSourceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Local cash drawer F6 hotkey stopped unexpectedly.", ex);
            await WaitForShutdownAsync(cancellationToken);
        }
    }

    internal async Task HandleF6Async(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _hasActiveUserSession(cancellationToken))
            {
                _logger.Info("Ignored F6 because no active local user session is logged in.");
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Unable to verify the active local user session; ignoring F6.", ex);
            return;
        }

        if (Interlocked.CompareExchange(ref _opening, 1, 0) != 0)
        {
            _logger.Info("Ignored repeated F6 while cash drawer opening is in progress.");
            return;
        }

        try
        {
            await _openCashDrawer(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _opening, 0);
        }
    }

    private async Task RunPlatformSourceAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            var x11 = new X11GlobalF6Source();
            try
            {
                _logger.Info($"Local cash drawer F6 hotkey enabled through {x11.DisplayName}.");
                await x11.RunAsync(HandleF6Async, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Error("X11 F6 hotkey is unavailable; falling back to Linux input events.", ex);
            }
        }

        var linuxInput = new LinuxInputF6Source();
        _logger.Info($"Local cash drawer F6 hotkey enabled through {linuxInput.DisplayName}.");
        await linuxInput.RunAsync(HandleF6Async, cancellationToken);
    }

    private static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

internal sealed class LogindUserSessionProbe
{
    private const string LoginctlPath = "/usr/bin/loginctl";

    public async Task<bool> HasActiveUserSessionAsync(CancellationToken cancellationToken)
    {
        var sessionId = (await RunLoginctlAsync(
            ["show-seat", "seat0", "--property=ActiveSession", "--value"],
            cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var properties = await RunLoginctlAsync(
            [
                "show-session",
                sessionId,
                "--property=Class",
                "--property=Active",
                "--property=State"
            ],
            cancellationToken);
        var values = properties
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        return values.TryGetValue("Class", out var sessionClass) &&
               (string.Equals(sessionClass, "user", StringComparison.Ordinal) ||
                sessionClass.StartsWith("user-", StringComparison.Ordinal)) &&
               values.TryGetValue("Active", out var active) &&
               string.Equals(active, "yes", StringComparison.Ordinal) &&
               values.TryGetValue("State", out var state) &&
               string.Equals(state, "active", StringComparison.Ordinal);
    }

    private static async Task<string> RunLoginctlAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = LoginctlPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start loginctl.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"loginctl exited with code {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }
}

internal interface IGlobalF6Source
{
    string DisplayName { get; }

    Task RunAsync(Func<CancellationToken, Task> onPressed, CancellationToken cancellationToken);
}

internal sealed class X11GlobalF6Source : IGlobalF6Source
{
    private const ulong F6KeySym = 0xFFC3;
    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int GrabModeAsync = 1;
    private static readonly uint[] ModifierMasks = [0, 2, 16, 18];

    public string DisplayName => "X11";

    public async Task RunAsync(
        Func<CancellationToken, Task> onPressed,
        CancellationToken cancellationToken)
    {
        XInitThreads();
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to open the X11 display for the F6 hotkey.");
        }

        var eventBuffer = Marshal.AllocHGlobal(IntPtr.Size * 24);
        try
        {
            var rootWindow = XDefaultRootWindow(display);
            var keyCode = XKeysymToKeycode(display, F6KeySym);
            if (keyCode == 0)
            {
                throw new InvalidOperationException("X11 did not resolve the F6 key code.");
            }

            XkbSetDetectableAutoRepeat(display, 1, out _);
            foreach (var modifiers in ModifierMasks)
            {
                XGrabKey(display, keyCode, modifiers, rootWindow, 0, GrabModeAsync, GrabModeAsync);
            }
            XSync(display, 0);

            var keyDown = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, eventBuffer);
                    var eventType = Marshal.ReadInt32(eventBuffer);
                    if (eventType == KeyPress && !keyDown)
                    {
                        keyDown = true;
                        await onPressed(cancellationToken);
                    }
                    else if (eventType == KeyRelease)
                    {
                        keyDown = false;
                    }
                }

                await Task.Delay(20, cancellationToken);
            }
        }
        finally
        {
            var rootWindow = XDefaultRootWindow(display);
            var keyCode = XKeysymToKeycode(display, F6KeySym);
            foreach (var modifiers in ModifierMasks)
            {
                XUngrabKey(display, keyCode, modifiers, rootWindow);
            }
            XSync(display, 0);
            Marshal.FreeHGlobal(eventBuffer);
            XCloseDisplay(display);
        }
    }

    [DllImport("libX11.so.6")]
    private static extern int XInitThreads();

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, ulong keySym);

    [DllImport("libX11.so.6")]
    private static extern int XGrabKey(
        IntPtr display,
        int keyCode,
        uint modifiers,
        IntPtr grabWindow,
        int ownerEvents,
        int pointerMode,
        int keyboardMode);

    [DllImport("libX11.so.6")]
    private static extern int XUngrabKey(IntPtr display, int keyCode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport("libX11.so.6")]
    private static extern int XkbSetDetectableAutoRepeat(IntPtr display, int detectable, out int supported);
}

internal sealed class LinuxInputF6Source : IGlobalF6Source
{
    private const ushort EvKey = 0x01;
    private const ushort KeyF6 = 64;
    private const int KeyPressed = 1;
    private const int InputEventSize64 = 24;
    private readonly string _inputRoot;

    public LinuxInputF6Source(string inputRoot = "/dev/input")
    {
        _inputRoot = inputRoot;
    }

    public string DisplayName => "Linux input events";

    public async Task RunAsync(
        Func<CancellationToken, Task> onPressed,
        CancellationToken cancellationToken)
    {
        if (IntPtr.Size != 8)
        {
            throw new PlatformNotSupportedException("Linux input hotkeys require a 64-bit agent build.");
        }

        var activeReaders = new Dictionary<string, Task>(StringComparer.Ordinal);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!Directory.Exists(_inputRoot))
            {
                throw new InvalidOperationException($"Linux input directory '{_inputRoot}' is unavailable.");
            }

            foreach (var eventPath in Directory.EnumerateFiles(_inputRoot, "event*")
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                if (activeReaders.TryGetValue(eventPath, out var existing) && !existing.IsCompleted)
                {
                    continue;
                }

                activeReaders[eventPath] = ReadDeviceAsync(eventPath, onPressed, cancellationToken);
            }

            foreach (var completed in activeReaders.Where(pair => pair.Value.IsCompleted).ToArray())
            {
                try
                {
                    await completed.Value;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
                activeReaders.Remove(completed.Key);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static async Task ReadDeviceAsync(
        string path,
        Func<CancellationToken, Task> onPressed,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            InputEventSize64,
            FileOptions.Asynchronous);
        var buffer = new byte[InputEventSize64];
        while (!cancellationToken.IsCancellationRequested)
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            var eventType = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(16, 2));
            var eventCode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(18, 2));
            var eventValue = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20, 4));
            if (eventType == EvKey && eventCode == KeyF6 && eventValue == KeyPressed)
            {
                await onPressed(cancellationToken);
            }
        }
    }
}
