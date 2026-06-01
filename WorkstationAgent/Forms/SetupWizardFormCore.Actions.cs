using System.Windows.Forms;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;
using WorkstationAgent.Services;

namespace WorkstationAgent.Forms;

internal partial class SetupWizardFormCore
{
    private void LoadInitialValues()
    {
        ApiBaseUrlTextBox.Text = InitialSettings.ApiBaseUrl;
        AgentNameTextBox.Text = string.IsNullOrWhiteSpace(InitialSettings.AgentName) ? Environment.MachineName : InitialSettings.AgentName;
        StartWithWindowsCheckBox.Checked = InitialSettings.StartWithWindows;

        ReceiptPrinterEnabledCheckBox.Checked = InitialSettings.ReceiptPrinter.Enabled;
        ReceiptTransportModeComboBox.SelectedItem = string.IsNullOrWhiteSpace(InitialSettings.ReceiptPrinter.TransportMode)
            ? PrinterTransportMode.WindowsSpooler
            : InitialSettings.ReceiptPrinter.TransportMode;
        ReceiptUsbVendorIdTextBox.Text = InitialSettings.ReceiptPrinter.UsbVendorId ?? string.Empty;
        ReceiptUsbProductIdTextBox.Text = InitialSettings.ReceiptPrinter.UsbProductId ?? string.Empty;
        ReceiptImageCommandModeComboBox.SelectedItem = string.IsNullOrWhiteSpace(InitialSettings.ReceiptPrinter.ImageCommandMode)
            ? "gs-v-0"
            : InitialSettings.ReceiptPrinter.ImageCommandMode;

        LabelPrinterEnabledCheckBox.Checked = InitialSettings.LabelPrinter.Enabled;
        LabelTransportModeComboBox.SelectedItem = string.IsNullOrWhiteSpace(InitialSettings.LabelPrinter.TransportMode)
            ? PrinterTransportMode.WindowsSpooler
            : InitialSettings.LabelPrinter.TransportMode;
        LabelUsbVendorIdTextBox.Text = InitialSettings.LabelPrinter.UsbVendorId ?? string.Empty;
        LabelUsbProductIdTextBox.Text = InitialSettings.LabelPrinter.UsbProductId ?? string.Empty;
        LabelCharacterEncodingTextBox.Text = string.IsNullOrWhiteSpace(InitialSettings.LabelPrinter.CharacterEncoding)
            ? "ascii"
            : InitialSettings.LabelPrinter.CharacterEncoding;
        LabelCodePageTextBox.Text = InitialSettings.LabelPrinter.CodePage ?? string.Empty;
        TsplLabelWidthBox.Value = (decimal)InitialSettings.LabelPrinter.TsplLabelWidthMm;
        TsplLabelHeightBox.Value = (decimal)InitialSettings.LabelPrinter.TsplLabelHeightMm;
        TsplLabelGapBox.Value = (decimal)InitialSettings.LabelPrinter.TsplLabelGapMm;
        TsplDirectionComboBox.SelectedIndex = Math.Clamp(InitialSettings.LabelPrinter.TsplDirection, 0, 1);
        TsplSpeedBox.Value = Math.Clamp(InitialSettings.LabelPrinter.TsplSpeed, 1, 5);
        TsplDensityBox.Value = Math.Clamp(InitialSettings.LabelPrinter.TsplDensity, 1, 15);

        PosTerminalEnabledCheckBox.Checked = InitialSettings.PosTerminal.Enabled;
        PosTerminalHostTextBox.Text = string.IsNullOrWhiteSpace(InitialSettings.PosTerminal.Host)
            ? "192.168.0.103"
            : InitialSettings.PosTerminal.Host;
        PosTerminalPortBox.Value = Math.Clamp(InitialSettings.PosTerminal.Port, 1, 65535);
        PosTerminalMerchantIdTextBox.Text = string.IsNullOrWhiteSpace(InitialSettings.PosTerminal.MerchantId)
            ? "1"
            : InitialSettings.PosTerminal.MerchantId;
        PosTerminalTimeoutBox.Value = Math.Clamp(InitialSettings.PosTerminal.TimeoutSeconds, 10, 600);
    }

    private void LoadPrinters()
    {
        var discovery = new PrinterDiscoveryService();
        var printers = discovery.GetInstalledPrinters();
        PopulatePrinterCombo(ReceiptPrinterComboBox, printers, InitialSettings.ReceiptPrinter.PrinterName);
        PopulatePrinterCombo(LabelPrinterComboBox, printers, InitialSettings.LabelPrinter.PrinterName);
    }

    private static void PopulatePrinterCombo(ComboBox comboBox, IReadOnlyCollection<string> printers, string selectedPrinter)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(string.Empty);
        foreach (var printer in printers)
        {
            comboBox.Items.Add(printer);
        }

        if (!string.IsNullOrWhiteSpace(selectedPrinter))
        {
            var selected = printers.FirstOrDefault(name => string.Equals(name, selectedPrinter, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                comboBox.SelectedItem = selected;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private async Task SaveAsync()
    {
        try
        {
            ToggleEnabled(false);
            StatusLabel.Text = "Saving workstation settings...";

            var settings = BuildSettings();
            if (!string.IsNullOrWhiteSpace(RegistrationTokenTextBox.Text))
            {
                StatusLabel.Text = "Registering workstation on API...";
                var registrationClient = new WorkstationRegistrationClient();
                var registration = await registrationClient.RegisterAsync(settings, RegistrationTokenTextBox.Text.Trim(), CancellationToken.None);
                settings.DeviceId = registration.DeviceId;
                settings.AgentName = registration.AgentName;
                settings.ApiKey = registration.ApiKey;
                settings.SocketIoUrl = registration.SocketIoUrl;
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SocketIoUrl))
            {
                throw new InvalidOperationException("API registration is required before the agent can start in production mode.");
            }

            SettingsStore.Save(settings);
            SavedSettings = settings;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
            MessageBox.Show(this, ex.Message, AvtoforwardBranding.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleEnabled(true);
        }
    }

    private AgentSettings BuildSettings()
    {
        return new AgentSettings
        {
            DeviceId = string.IsNullOrWhiteSpace(InitialSettings.DeviceId) ? Guid.NewGuid().ToString("N") : InitialSettings.DeviceId,
            AgentName = AgentNameTextBox.Text.Trim(),
            ApiBaseUrl = ApiBaseUrlTextBox.Text.Trim(),
            SocketIoUrl = InitialSettings.SocketIoUrl,
            ApiKey = InitialSettings.ApiKey,
            StartWithWindows = StartWithWindowsCheckBox.Checked,
            ReceiptPrinter = new ReceiptPrinterSettings
            {
                Enabled = ReceiptPrinterEnabledCheckBox.Checked,
                PrinterName = ReceiptPrinterComboBox.Text,
                TransportMode = ReceiptTransportModeComboBox.Text,
                UsbVendorId = NullIfWhiteSpace(ReceiptUsbVendorIdTextBox.Text),
                UsbProductId = NullIfWhiteSpace(ReceiptUsbProductIdTextBox.Text),
                UsbInterfaceNumber = InitialSettings.ReceiptPrinter.UsbInterfaceNumber,
                UsbOutEndpoint = InitialSettings.ReceiptPrinter.UsbOutEndpoint,
                UsbWriteTimeoutMs = InitialSettings.ReceiptPrinter.UsbWriteTimeoutMs,
                ImageCommandMode = string.IsNullOrWhiteSpace(ReceiptImageCommandModeComboBox.Text) ? "gs-v-0" : ReceiptImageCommandModeComboBox.Text,
                MaxImageWidthDots = InitialSettings.ReceiptPrinter.MaxImageWidthDots,
                PaperWidth = InitialSettings.ReceiptPrinter.PaperWidth,
                CharacterEncoding = InitialSettings.ReceiptPrinter.CharacterEncoding,
                FeedLinesAfterPrint = InitialSettings.ReceiptPrinter.FeedLinesAfterPrint
            },
            LabelPrinter = new LabelPrinterSettings
            {
                Enabled = LabelPrinterEnabledCheckBox.Checked,
                PrinterName = LabelPrinterComboBox.Text,
                TransportMode = LabelTransportModeComboBox.Text,
                UsbVendorId = NullIfWhiteSpace(LabelUsbVendorIdTextBox.Text),
                UsbProductId = NullIfWhiteSpace(LabelUsbProductIdTextBox.Text),
                UsbInterfaceNumber = InitialSettings.LabelPrinter.UsbInterfaceNumber,
                UsbOutEndpoint = InitialSettings.LabelPrinter.UsbOutEndpoint,
                UsbWriteTimeoutMs = InitialSettings.LabelPrinter.UsbWriteTimeoutMs,
                CharacterEncoding = string.IsNullOrWhiteSpace(LabelCharacterEncodingTextBox.Text)
                    ? "ascii"
                    : LabelCharacterEncodingTextBox.Text.Trim(),
                CodePage = NullIfWhiteSpace(LabelCodePageTextBox.Text),
                TsplLabelWidthMm = (double)TsplLabelWidthBox.Value,
                TsplLabelHeightMm = (double)TsplLabelHeightBox.Value,
                TsplLabelGapMm = (double)TsplLabelGapBox.Value,
                TsplDirection = TsplDirectionComboBox.SelectedIndex,
                TsplSpeed = (int)TsplSpeedBox.Value,
                TsplDensity = (int)TsplDensityBox.Value
            },
            PosTerminal = new PosTerminalSettings
            {
                Enabled = PosTerminalEnabledCheckBox.Checked,
                Host = string.IsNullOrWhiteSpace(PosTerminalHostTextBox.Text)
                    ? "192.168.0.103"
                    : PosTerminalHostTextBox.Text.Trim(),
                Port = (int)PosTerminalPortBox.Value,
                MerchantId = string.IsNullOrWhiteSpace(PosTerminalMerchantIdTextBox.Text)
                    ? "1"
                    : PosTerminalMerchantIdTextBox.Text.Trim(),
                TimeoutSeconds = (int)PosTerminalTimeoutBox.Value
            },
            ReconnectDelaySeconds = InitialSettings.ReconnectDelaySeconds,
            PingIntervalSeconds = InitialSettings.PingIntervalSeconds,
            LogFilePath = Paths.LogFilePath,
            AutoUpdateEnabled = InitialSettings.AutoUpdateEnabled,
            UpdateChannel = InitialSettings.UpdateChannel,
            UpdateCheckIntervalMinutes = InitialSettings.UpdateCheckIntervalMinutes,
            UpdateJitterMinutes = InitialSettings.UpdateJitterMinutes
        };
    }

    private void TestPrint(string printerName)
    {
        TryRunPrinterAction(
            printerName,
            settings => settings.ReceiptPrinter.PrinterName = printerName,
            service => service.PrintTestReceipt(Guid.NewGuid().ToString("N")),
            result => result.Success,
            result => result.Success ? $"Printed on {result.PrinterName}" : $"Print failed: {result.Error}");
    }

    private void TestLogoPrint(string printerName)
    {
        TryRunPrinterAction(
            printerName,
            settings => settings.ReceiptPrinter.PrinterName = printerName,
            service => service.PrintLogoTest(Guid.NewGuid().ToString("N")),
            result => result.Success,
            result => result.Success ? $"Logo printed: {result.PrinterName}" : $"Logo print failed: {result.Error}");
    }

    private void TestTsplLabelPrint(string printerName)
    {
        TryRunPrinterAction(
            printerName,
            settings => settings.LabelPrinter.PrinterName = printerName,
            service => service.PrintTsplTestLabel(Guid.NewGuid().ToString("N")),
            result => result.Success,
            result => result.Success ? $"TSPL label sent to: {result.PrinterName}" : $"TSPL test failed: {result.Error}");
    }

    private async Task TestPosTerminalConnectionAsync()
    {
        try
        {
            ToggleEnabled(false);
            StatusLabel.Text = "Testing POS terminal connection...";

            var settings = BuildSettings();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var service = new PosTerminalService(settings, new FileLogger(Paths.LogFilePath));
            var result = await service.TestConnectionAsync(cts.Token);
            var success = string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase);
            var message = success
                ? $"POS terminal connection OK: {settings.PosTerminal.Host}:{settings.PosTerminal.Port}"
                : $"POS terminal test failed: {result.Message ?? result.ResponseCode ?? result.Status}";

            StatusLabel.Text = message;
            MessageBox.Show(
                this,
                message,
                AvtoforwardBranding.AppName,
                MessageBoxButtons.OK,
                success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
            MessageBox.Show(this, ex.Message, AvtoforwardBranding.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleEnabled(true);
        }
    }

    private void TryRunPrinterAction<T>(
        string printerName,
        Action<AgentSettings> configure,
        Func<ThermalPrinterService, T> action,
        Func<T, bool> isSuccess,
        Func<T, string> messageSelector)
    {
        try
        {
            var settings = BuildSettings();
            configure(settings);
            var result = action(BuildPrinterService(settings, new FileLogger(Paths.LogFilePath)));
            MessageBox.Show(this, messageSelector(result), AvtoforwardBranding.AppName, MessageBoxButtons.OK, isSuccess(result) ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AvtoforwardBranding.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateTransportFields()
    {
        UpdateTransportFields(ReceiptTransportModeComboBox, ReceiptPrinterComboBox, ReceiptUsbVendorIdTextBox, ReceiptUsbProductIdTextBox);
        UpdateTransportFields(LabelTransportModeComboBox, LabelPrinterComboBox, LabelUsbVendorIdTextBox, LabelUsbProductIdTextBox);

        StatusLabel.Text = (IsDirectUsb(ReceiptTransportModeComboBox), IsDirectUsb(LabelTransportModeComboBox)) switch
        {
            (true, true) => "Direct USB selected for both printers. Configure VID/PID carefully for each role.",
            (true, false) => "Receipt printer uses Direct USB. Configure its VID/PID and use receipt/logo tests to validate output.",
            (false, true) => "Label printer uses Direct USB. Configure its VID/PID and use TSPL test print to validate output.",
            _ => IsFirstRun
                ? "Complete registration and save the production settings for this workstation."
                : "Update local settings or re-register this workstation."
        };
    }

    private static void UpdateTransportFields(ComboBox transportComboBox, ComboBox printerComboBox, TextBox usbVendorIdTextBox, TextBox usbProductIdTextBox)
    {
        var isDirectUsb = IsDirectUsb(transportComboBox);
        printerComboBox.Enabled = !isDirectUsb;
        usbVendorIdTextBox.Enabled = isDirectUsb;
        usbProductIdTextBox.Enabled = isDirectUsb;
    }

    private static bool IsDirectUsb(ComboBox comboBox)
    {
        return string.Equals(comboBox.Text, PrinterTransportMode.DirectUsb, StringComparison.OrdinalIgnoreCase);
    }

    private ThermalPrinterService BuildPrinterService(AgentSettings settings, FileLogger logger)
    {
        var discoveryService = new PrinterDiscoveryService();
        var imageRenderer = new EscPosImageRenderer();
        var tsplPayloadBuilder = new TsplPayloadBuilder();

        return new ThermalPrinterService(
            settings,
            Paths,
            logger,
            discoveryService,
            new EscPosTestReceiptBuilder(),
            new EscPosPayloadBuilder(),
            imageRenderer,
            new EscPosDocumentBuilder(imageRenderer),
            tsplPayloadBuilder,
            new TsplTestLabelBuilder(tsplPayloadBuilder),
            new PrinterTransportResolver(
                new WindowsSpoolerTransport(discoveryService, new RawPrinterClient()),
                new DirectUsbTransport(new UsbPrinterDiscoveryService(), new DirectUsbPrinterClient())));
    }
}
