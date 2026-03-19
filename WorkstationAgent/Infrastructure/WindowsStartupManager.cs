using Microsoft.Win32;

namespace WorkstationAgent.Infrastructure;

internal sealed class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _appName;
    private readonly string _executablePath;

    public WindowsStartupManager(string appName, string executablePath)
    {
        _appName = appName;
        _executablePath = executablePath;
    }

    public void EnsureStartup(bool enabled)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (runKey is null)
        {
            throw new InvalidOperationException("Unable to open Windows startup registry key.");
        }

        if (enabled)
        {
            runKey.SetValue(_appName, $"\"{_executablePath}\"");
            return;
        }

        if (runKey.GetValue(_appName) is not null)
        {
            runKey.DeleteValue(_appName, throwOnMissingValue: false);
        }
    }
}
