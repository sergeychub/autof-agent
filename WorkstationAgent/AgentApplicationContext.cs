using System.Diagnostics;
using System.Drawing;
using MaterialDesignThemes.Wpf;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Forms;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Services;
using WorkstationAgent.Update;

namespace WorkstationAgent;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly FileLogger _logger;
    private readonly AgentWebSocketClient _webSocketClient;
    private readonly UpdateCheckService _updateCheckService;
    private readonly UpdateStateStore _updateStateStore;
    private readonly ThermalPrinterService _thermalPrinterService;
    private readonly PosTerminalService _posTerminalService;
    private readonly CashDrawerHotkeyWindow? _cashDrawerHotkey;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SynchronizationContext _uiContext;
    private readonly AgentSettingsStore _settingsStore;
    private readonly AgentPaths _paths;
    private AgentSettings _settings;
    private SetupWizardForm? _settingsWindow;
    private string _connectionStatus = "Connecting";

    public AgentApplicationContext(AgentSettingsStore settingsStore, AgentPaths paths, AgentSettings settings)
    {
        _settingsStore = settingsStore;
        _paths = paths;
        _settings = settings;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _logger = new FileLogger(settings.LogFilePath);
        _updateStateStore = new UpdateStateStore(paths.UpdateStatePath);
        _thermalPrinterService = ThermalPrinterService.Create(settings, paths, _logger);
        _posTerminalService = new PosTerminalService(settings, _logger);
        _webSocketClient = new AgentWebSocketClient(
            settings,
            _logger,
            _thermalPrinterService,
            _posTerminalService,
            () => _updateStateStore.ReadStatus(),
            () => AgentVersionProvider.CurrentVersion);
        _updateCheckService = new UpdateCheckService(settings, paths, _logger, _updateStateStore);
        _updateCheckService.IsBusy = () => _webSocketClient.IsScannerBusy;
        _webSocketClient.StatusChanged += HandleStatusChanged;
        _webSocketClient.MessageReceived += HandleMessageReceived;

        if (!paths.IsDevelopment)
        {
            var startupManager = new WindowsStartupManager(
                appName: "AvtoforwardAgent",
                executablePath: Application.ExecutablePath);
            startupManager.EnsureStartup(settings.StartWithWindows);
        }

        _trayIcon = AvtoforwardBranding.CreateTrayIcon();
        _trayMenu = BuildMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = AvtoforwardBranding.AppName,
            ContextMenuStrip = _trayMenu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsWindow();
        _notifyIcon.ShowBalloonTip(
            3000,
            AvtoforwardBranding.AppName,
            "Agent started and is connecting to the API.",
            ToolTipIcon.Info);
        _logger.Info(_thermalPrinterService.GetAvailabilityStatus());

        var cashDrawerJournal = new CashDrawerJournal(Path.Combine(paths.BaseDirectory, "cash-drawer-outbox"), settings, _logger);
        _ = Task.Run(() => cashDrawerJournal.RunAsync(_shutdown.Token));
        if (settings.CashDrawerHotkeyEnabled)
        {
            var drawer = new CashDrawerService(settings, _logger, _thermalPrinterService.SendCashDrawerPulse, cashDrawerJournal.Enqueue);
            _cashDrawerHotkey = new CashDrawerHotkeyWindow(drawer, _logger);
            if (!_cashDrawerHotkey.Register())
                _notifyIcon.ShowBalloonTip(5000, AvtoforwardBranding.AppName,
                    "F8 could not be registered for the cash drawer. See the agent log.", ToolTipIcon.Error);
        }

        _ = Task.Run(() => _webSocketClient.RunAsync(_shutdown.Token));
        if (!paths.IsDevelopment)
            _ = Task.Run(() => _updateCheckService.RunAsync(_shutdown.Token));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdown.Cancel();
            _cashDrawerHotkey?.Dispose();
            _webSocketClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _updateCheckService.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _trayIcon.Dispose();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new TrayMenu();
        menu.AddCommand("Открыть AutoF Agent", PackIconKind.ViewDashboardOutline, (_, _) => OpenSettingsWindow());
        menu.AddCommand("Файл настроек", PackIconKind.FileCogOutline, (_, _) => OpenPath(_settingsStore.SettingsPath));
        menu.AddCommand("Папка журналов", PackIconKind.FolderOpenOutline, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.AddCommand("Напечатать пробный чек", PackIconKind.ReceiptTextOutline, (_, _) => PrintTestReceipt());
        menu.AddCommand("Напечатать логотип", PackIconKind.ImageOutline, (_, _) => PrintTestLogo());
        menu.AddCommand("Напечатать пробную этикетку", PackIconKind.TagOutline, (_, _) => PrintTestLabel());
        menu.AddCommand("Проверить терминал", PackIconKind.CreditCardOutline, async (_, _) => await TestPosTerminal());
        menu.Items.Add(new ToolStripSeparator());
        menu.AddCommand("Выйти из агента", PackIconKind.ExitToApp, (_, _) => ExitThread());
        return menu;
    }

    private void HandleStatusChanged(string status)
    {
        _uiContext.Post(_ =>
        {
            _connectionStatus = status;
            _settingsWindow?.SetConnectionStatus(status);
            _notifyIcon.Text = $"{AvtoforwardBranding.AppName} - {TrimForTooltip(status)}";
        }, null);
    }

    private void HandleMessageReceived(string message)
    {
        if (message.Contains("\"requestId\"", StringComparison.OrdinalIgnoreCase))
        {
            _uiContext.Post(_ =>
            {
                _notifyIcon.ShowBalloonTip(
                    2000,
                    AvtoforwardBranding.AppName,
                    "Command received from server.",
                    ToolTipIcon.Info);
            }, null);
        }
    }

    private void PrintTestReceipt()
    {
        var result = _thermalPrinterService.PrintTestReceipt(Guid.NewGuid().ToString("N"));
        var text = result.Success
            ? $"Printed on {result.PrinterName}"
            : $"Print failed: {result.Error}";

        _notifyIcon.ShowBalloonTip(
            3000,
            AvtoforwardBranding.AppName,
            text,
            result.Success ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private void PrintTestLogo()
    {
        var result = _thermalPrinterService.PrintLogoTest(Guid.NewGuid().ToString("N"));
        var text = result.Success
            ? $"Logo printed: {result.PrinterName}"
            : $"Logo print failed: {result.Error}";

        _notifyIcon.ShowBalloonTip(
            3000,
            AvtoforwardBranding.AppName,
            text,
            result.Success ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private void PrintTestLabel()
    {
        var result = _thermalPrinterService.PrintTsplTestLabel(Guid.NewGuid().ToString("N"));
        var text = result.Success
            ? $"Label printed: {result.PrinterName}"
            : $"Label print failed: {result.Error}";

        _notifyIcon.ShowBalloonTip(
            3000,
            AvtoforwardBranding.AppName,
            text,
            result.Success ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private async Task TestPosTerminal()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await _posTerminalService.TestConnectionAsync(cts.Token);
            var success = string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase);
            _notifyIcon.ShowBalloonTip(
                3000,
                AvtoforwardBranding.AppName,
                success ? "POS terminal connection OK." : $"POS terminal test failed: {result.Message ?? result.ResponseCode}",
                success ? ToolTipIcon.Info : ToolTipIcon.Error);
        }
        catch (Exception ex)
        {
            _logger.Error("POS terminal test failed.", ex);
            _notifyIcon.ShowBalloonTip(
                3000,
                AvtoforwardBranding.AppName,
                $"POS terminal test failed: {ex.Message}",
                ToolTipIcon.Error);
        }
    }

#if DEBUG
    internal void OpenSettingsWhenIdle()
    {
        Application.Idle += Open;

        void Open(object? sender, EventArgs e)
        {
            Application.Idle -= Open;
            OpenSettingsWindow();
        }
    }
#endif

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
                _settingsWindow.WindowState = System.Windows.WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var setupWizard = new SetupWizardForm(_settings, _settingsStore, _paths, isFirstRun: false);
        setupWizard.SetConnectionStatus(_connectionStatus);
        _settingsWindow = setupWizard;
        bool? result;
        try { result = setupWizard.ShowDialog(); }
        finally { _settingsWindow = null; }
        if (result != true || setupWizard.SavedSettings is null)
        {
            return;
        }

        _settings = setupWizard.SavedSettings;
        _notifyIcon.ShowBalloonTip(
            3000,
            AvtoforwardBranding.AppName,
            "Settings saved. Agent will restart to apply changes.",
            ToolTipIcon.Info);
        Application.Restart();
        ExitThread();
    }

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_paths.LogsDirectory);
        OpenPath(_paths.LogsDirectory);
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string TrimForTooltip(string value)
    {
        return value.Length <= 40 ? value : value[..40];
    }
}
