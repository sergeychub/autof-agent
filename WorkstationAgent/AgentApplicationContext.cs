using System.Diagnostics;
using System.Drawing;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Forms;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Services;

namespace WorkstationAgent;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly FileLogger _logger;
    private readonly AgentWebSocketClient _webSocketClient;
    private readonly ThermalPrinterService _thermalPrinterService;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SynchronizationContext _uiContext;
    private readonly AgentSettingsStore _settingsStore;
    private readonly AgentPaths _paths;
    private AgentSettings _settings;

    public AgentApplicationContext(AgentSettingsStore settingsStore, AgentPaths paths, AgentSettings settings)
    {
        _settingsStore = settingsStore;
        _paths = paths;
        _settings = settings;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _logger = new FileLogger(settings.LogFilePath);
        var discoveryService = new Printing.PrinterDiscoveryService();
        var rawPrinterClient = new Printing.RawPrinterClient();
        var transportResolver = new Printing.PrinterTransportResolver(
            new Printing.WindowsSpoolerTransport(discoveryService, rawPrinterClient),
            new Printing.DirectUsbTransport(new Printing.UsbPrinterDiscoveryService(), new Printing.DirectUsbPrinterClient()));
        var imageRenderer = new Printing.EscPosImageRenderer();
        _thermalPrinterService = new ThermalPrinterService(
            settings,
            paths,
            _logger,
            discoveryService,
            new Printing.EscPosTestReceiptBuilder(),
            new Printing.EscPosPayloadBuilder(),
            imageRenderer,
            new Printing.EscPosDocumentBuilder(imageRenderer),
            transportResolver);
        _webSocketClient = new AgentWebSocketClient(settings, _logger, _thermalPrinterService);
        _webSocketClient.StatusChanged += HandleStatusChanged;
        _webSocketClient.MessageReceived += HandleMessageReceived;

        var startupManager = new WindowsStartupManager(
            appName: "AvtoforwardAgent",
            executablePath: Application.ExecutablePath);

        startupManager.EnsureStartup(settings.StartWithWindows);

        _notifyIcon = new NotifyIcon
        {
            Icon = AvtoforwardBranding.CreateTrayIcon(),
            Visible = true,
            Text = AvtoforwardBranding.AppName,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsWindow();
        _notifyIcon.ShowBalloonTip(
            3000,
            AvtoforwardBranding.AppName,
            "Agent started and is connecting to the API.",
            ToolTipIcon.Info);
        _logger.Info(_thermalPrinterService.GetAvailabilityStatus());

        _ = Task.Run(() => _webSocketClient.RunAsync(_shutdown.Token));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdown.Cancel();
            _webSocketClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettingsWindow());
        menu.Items.Add("Open config file", null, (_, _) => OpenPath(_settingsStore.SettingsPath));
        menu.Items.Add("Print test receipt", null, (_, _) => PrintTestReceipt());
        menu.Items.Add("Print test logo", null, (_, _) => PrintTestLogo());
        menu.Items.Add("Open logs", null, (_, _) => OpenLogsFolder());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void HandleStatusChanged(string status)
    {
        _uiContext.Post(_ =>
        {
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
                    "Print command received from server.",
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

    private void OpenSettingsWindow()
    {
        using var setupWizard = new SetupWizardForm(_settings, _settingsStore, _paths, isFirstRun: false);
        var result = setupWizard.ShowDialog();
        if (result != DialogResult.OK || setupWizard.SavedSettings is null)
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
