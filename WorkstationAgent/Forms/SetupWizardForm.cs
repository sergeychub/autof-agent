using System.Diagnostics;
using System.Windows.Forms;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;
using WorkstationAgent.Services;

namespace WorkstationAgent.Forms;

internal sealed class SetupWizardForm : Form
{
    private readonly AgentSettingsStore _settingsStore;
    private readonly AgentPaths _paths;
    private readonly AgentSettings _initialSettings;
    private readonly bool _isFirstRun;
    private readonly ComboBox _printerComboBox;
    private readonly ComboBox _transportModeComboBox;
    private readonly TextBox _usbVendorIdTextBox;
    private readonly TextBox _usbProductIdTextBox;
    private readonly ComboBox _imageCommandModeComboBox;
    private readonly TextBox _apiBaseUrlTextBox;
    private readonly TextBox _registrationTokenTextBox;
    private readonly TextBox _agentNameTextBox;
    private readonly CheckBox _startWithWindowsCheckBox;
    private readonly CheckBox _printerEnabledCheckBox;
    private readonly Label _statusLabel;

    public SetupWizardForm(AgentSettings initialSettings, AgentSettingsStore settingsStore, AgentPaths paths, bool isFirstRun)
    {
        _initialSettings = Clone(initialSettings);
        _settingsStore = settingsStore;
        _paths = paths;
        _isFirstRun = isFirstRun;

        Text = isFirstRun ? $"{AvtoforwardBranding.AppName} Setup" : $"{AvtoforwardBranding.AppName} Settings";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 780);
        Size = new Size(1100, 900);
        Icon = AvtoforwardBranding.CreateTrayIcon();

        var footer = BuildFooter();
        Controls.Add(footer);

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16, 16, 16, 8)
        };
        Controls.Add(contentHost);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentHost.Controls.Add(content);

        content.Controls.Add(BuildHeader(), 0, 0);
        content.Controls.Add(BuildConnectionSection(), 0, 1);
        content.Controls.Add(BuildIdentitySection(), 0, 2);

        var printerSection = BuildPrinterSection();
        content.Controls.Add(printerSection, 0, 3);
        _printerComboBox = FindControl<ComboBox>("PrinterComboBox");
        _transportModeComboBox = FindControl<ComboBox>("TransportModeComboBox");
        _usbVendorIdTextBox = FindControl<TextBox>("UsbVendorIdTextBox");
        _usbProductIdTextBox = FindControl<TextBox>("UsbProductIdTextBox");
        _imageCommandModeComboBox = FindControl<ComboBox>("ImageCommandModeComboBox");

        contentHost.Resize += (_, _) =>
        {
            content.Width = Math.Max(860, contentHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        };

        _apiBaseUrlTextBox = FindControl<TextBox>("ApiBaseUrlTextBox");
        _registrationTokenTextBox = FindControl<TextBox>("RegistrationTokenTextBox");
        _agentNameTextBox = FindControl<TextBox>("AgentNameTextBox");
        _startWithWindowsCheckBox = FindControl<CheckBox>("StartWithWindowsCheckBox");
        _printerEnabledCheckBox = FindControl<CheckBox>("PrinterEnabledCheckBox");
        _statusLabel = FindControl<Label>("StatusLabel");

        LoadInitialValues();
        LoadPrinters();
        UpdateTransportFields();
    }

    public AgentSettings? SavedSettings { get; private set; }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var logo = new PictureBox
        {
            Width = 190,
            Height = 136,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 20, 0),
            Image = AvtoforwardBranding.CreateHeaderLogoBitmap(190, 136)
        };

        var textPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            AutoSize = true
        };
        textPanel.Controls.Add(new Label
        {
            Text = AvtoforwardBranding.AppName,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            AutoSize = true
        });
        textPanel.Controls.Add(new Label
        {
            Text = "Production workstation agent for thermal printing and device integration.",
            AutoSize = true,
            MaximumSize = new Size(760, 0)
        });
        textPanel.Controls.Add(new Label
        {
            Text = $"Config path: {_settingsStore.SettingsPath}",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.DimGray
        });

        panel.Controls.Add(logo, 0, 0);
        panel.Controls.Add(textPanel, 1, 0);
        return panel;
    }

    private Control BuildConnectionSection()
    {
        var group = new GroupBox
        {
            Text = "Connection",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };

        var layout = CreateFormTable();
        layout.Controls.Add(new Label { Text = "API Base URL", AutoSize = true }, 0, 0);
        layout.Controls.Add(new TextBox { Name = "ApiBaseUrlTextBox", Width = 420 }, 1, 0);
        layout.Controls.Add(new Label { Text = "Registration Token", AutoSize = true }, 0, 1);
        layout.Controls.Add(new TextBox { Name = "RegistrationTokenTextBox", Width = 420, UseSystemPasswordChar = true }, 1, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildIdentitySection()
    {
        var group = new GroupBox
        {
            Text = "Identity",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };

        var layout = CreateFormTable();
        layout.Controls.Add(new Label { Text = "Agent Name", AutoSize = true }, 0, 0);
        layout.Controls.Add(new TextBox { Name = "AgentNameTextBox", Width = 320 }, 1, 0);
        layout.Controls.Add(new Label { Text = "Device ID", AutoSize = true }, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = _initialSettings.DeviceId,
            AutoSize = true,
            ForeColor = Color.DimGray
        }, 1, 1);
        layout.Controls.Add(new CheckBox
        {
            Name = "StartWithWindowsCheckBox",
            Text = "Start automatically with Windows",
            AutoSize = true
        }, 1, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildPrinterSection()
    {
        var group = new GroupBox
        {
            Text = "Printer",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0)
        };

        var layout = CreateFormTable();
        layout.Controls.Add(new CheckBox
        {
            Name = "PrinterEnabledCheckBox",
            Text = "Enable thermal printer integration",
            AutoSize = true
        }, 1, 0);
        layout.Controls.Add(new Label { Text = "Transport", AutoSize = true }, 0, 1);

        var transportComboBox = new ComboBox
        {
            Name = "TransportModeComboBox",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220
        };
        transportComboBox.Items.Add(PrinterTransportMode.WindowsSpooler);
        transportComboBox.Items.Add(PrinterTransportMode.DirectUsb);
        transportComboBox.SelectedIndexChanged += (_, _) => UpdateTransportFields();
        layout.Controls.Add(transportComboBox, 1, 1);

        var printerComboBox = new ComboBox
        {
            Name = "PrinterComboBox",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 320
        };
        layout.Controls.Add(new Label { Text = "Printer", AutoSize = true }, 0, 2);
        layout.Controls.Add(printerComboBox, 1, 2);

        layout.Controls.Add(new Label { Text = "USB Vendor ID", AutoSize = true }, 0, 3);
        layout.Controls.Add(new TextBox { Name = "UsbVendorIdTextBox", Width = 180 }, 1, 3);
        layout.Controls.Add(new Label { Text = "USB Product ID", AutoSize = true }, 0, 4);
        layout.Controls.Add(new TextBox { Name = "UsbProductIdTextBox", Width = 180 }, 1, 4);
        layout.Controls.Add(new Label { Text = "Image Command", AutoSize = true }, 0, 5);

        var imageCommandModeComboBox = new ComboBox
        {
            Name = "ImageCommandModeComboBox",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220
        };
        imageCommandModeComboBox.Items.Add("gs-v-0");
        imageCommandModeComboBox.Items.Add("esc-star");
        layout.Controls.Add(imageCommandModeComboBox, 1, 5);

        var testButton = new Button
        {
            Text = "Test Print",
            AutoSize = true
        };
        testButton.Click += (_, _) => TestPrint(printerComboBox.Text);
        layout.Controls.Add(testButton, 1, 6);

        var logoTestButton = new Button
        {
            Text = "Test Logo Print",
            AutoSize = true
        };
        logoTestButton.Click += (_, _) => TestLogoPrint(printerComboBox.Text);
        layout.Controls.Add(logoTestButton, 1, 7);

        var openLogsButton = new Button
        {
            Text = "Open Logs",
            AutoSize = true
        };
        openLogsButton.Click += (_, _) => OpenPath(_paths.LogsDirectory);
        layout.Controls.Add(openLogsButton, 1, 8);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildFooter()
    {
        var statusLabel = new Label
        {
            Name = "StatusLabel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(16, 70, 133),
            Text = _isFirstRun
                ? "Complete registration and save the production settings for this workstation."
                : "Update local settings or re-register this workstation.",
            MaximumSize = new Size(680, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var saveButton = new Button
        {
            Text = _isFirstRun ? "Register and Start" : "Save Settings",
            AutoSize = true
        };
        saveButton.Click += async (_, _) => await SaveAsync();

        var cancelButton = new Button
        {
            Text = _isFirstRun ? "Exit" : "Cancel",
            AutoSize = true
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 88,
            ColumnCount = 2,
            Padding = new Padding(16, 12, 16, 16),
            BackColor = Color.White
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(statusLabel, 0, 0);
        footer.Controls.Add(buttons, 1, 0);
        return footer;
    }

    private static TableLayoutPanel CreateFormTable()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private void LoadInitialValues()
    {
        _apiBaseUrlTextBox.Text = _initialSettings.ApiBaseUrl;
        _agentNameTextBox.Text = string.IsNullOrWhiteSpace(_initialSettings.AgentName)
            ? Environment.MachineName
            : _initialSettings.AgentName;
        _startWithWindowsCheckBox.Checked = _initialSettings.StartWithWindows;
        _printerEnabledCheckBox.Checked = _initialSettings.PrinterEnabled;
        _transportModeComboBox.SelectedItem = string.IsNullOrWhiteSpace(_initialSettings.TransportMode)
            ? PrinterTransportMode.WindowsSpooler
            : _initialSettings.TransportMode;
        _usbVendorIdTextBox.Text = _initialSettings.UsbVendorId ?? string.Empty;
        _usbProductIdTextBox.Text = _initialSettings.UsbProductId ?? string.Empty;
        _imageCommandModeComboBox.SelectedItem = string.IsNullOrWhiteSpace(_initialSettings.ImageCommandMode)
            ? "gs-v-0"
            : _initialSettings.ImageCommandMode;
    }

    private void LoadPrinters()
    {
        var discovery = new Printing.PrinterDiscoveryService();
        var printers = discovery.GetInstalledPrinters();
        _printerComboBox.Items.Clear();
        foreach (var printer in printers)
        {
            _printerComboBox.Items.Add(printer);
        }

        if (!string.IsNullOrWhiteSpace(_initialSettings.PrinterName))
        {
            var selected = printers.FirstOrDefault(name =>
                string.Equals(name, _initialSettings.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _printerComboBox.SelectedItem = selected;
            }
        }

        if (_printerComboBox.SelectedItem is null && _printerComboBox.Items.Count > 0)
        {
            _printerComboBox.SelectedIndex = 0;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            ToggleEnabled(false);
            _statusLabel.Text = "Saving workstation settings...";

            var settings = BuildSettings();
            if (!string.IsNullOrWhiteSpace(_registrationTokenTextBox.Text))
            {
                _statusLabel.Text = "Registering workstation on API...";
                var registrationClient = new WorkstationRegistrationClient();
                var registration = await registrationClient.RegisterAsync(
                    settings,
                    _registrationTokenTextBox.Text.Trim(),
                    CancellationToken.None);

                settings.DeviceId = registration.DeviceId;
                settings.AgentName = registration.AgentName;
                settings.ApiKey = registration.ApiKey;
                settings.SocketIoUrl = registration.SocketIoUrl;
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SocketIoUrl))
            {
                throw new InvalidOperationException(
                    "API registration is required before the agent can start in production mode.");
            }

            _settingsStore.Save(settings);
            SavedSettings = settings;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
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
            DeviceId = string.IsNullOrWhiteSpace(_initialSettings.DeviceId)
                ? Guid.NewGuid().ToString("N")
                : _initialSettings.DeviceId,
            AgentName = _agentNameTextBox.Text.Trim(),
            ApiBaseUrl = _apiBaseUrlTextBox.Text.Trim(),
            SocketIoUrl = _initialSettings.SocketIoUrl,
            ApiKey = _initialSettings.ApiKey,
            StartWithWindows = _startWithWindowsCheckBox.Checked,
            PrinterEnabled = _printerEnabledCheckBox.Checked,
            PrinterName = _printerComboBox.Text,
            TransportMode = _transportModeComboBox.Text,
            UsbVendorId = NullIfWhiteSpace(_usbVendorIdTextBox.Text),
            UsbProductId = NullIfWhiteSpace(_usbProductIdTextBox.Text),
            UsbInterfaceNumber = _initialSettings.UsbInterfaceNumber,
            UsbOutEndpoint = _initialSettings.UsbOutEndpoint,
            UsbWriteTimeoutMs = _initialSettings.UsbWriteTimeoutMs,
            ImageCommandMode = string.IsNullOrWhiteSpace(_imageCommandModeComboBox.Text)
                ? "gs-v-0"
                : _imageCommandModeComboBox.Text,
            MaxImageWidthDots = _initialSettings.MaxImageWidthDots,
            PaperWidth = _initialSettings.PaperWidth,
            CharacterEncoding = _initialSettings.CharacterEncoding,
            FeedLinesAfterPrint = _initialSettings.FeedLinesAfterPrint,
            ReconnectDelaySeconds = _initialSettings.ReconnectDelaySeconds,
            PingIntervalSeconds = _initialSettings.PingIntervalSeconds,
            LogFilePath = _paths.LogFilePath
        };
    }

    private void TestPrint(string printerName)
    {
        try
        {
            var settings = BuildSettings();
            settings.PrinterName = printerName;
            var logger = new FileLogger(_paths.LogFilePath);
            var thermalPrinterService = new ThermalPrinterService(
                settings,
                _paths,
                logger,
                new Printing.PrinterDiscoveryService(),
                new Printing.EscPosTestReceiptBuilder(),
                new Printing.EscPosPayloadBuilder(),
                new Printing.EscPosImageRenderer(),
                new Printing.EscPosDocumentBuilder(new Printing.EscPosImageRenderer()),
                new Printing.PrinterTransportResolver(
                    new Printing.WindowsSpoolerTransport(new Printing.PrinterDiscoveryService(), new Printing.RawPrinterClient()),
                    new Printing.DirectUsbTransport(new Printing.UsbPrinterDiscoveryService(), new Printing.DirectUsbPrinterClient())));
            var result = thermalPrinterService.PrintTestReceipt(Guid.NewGuid().ToString("N"));
            var message = result.Success
                ? $"Printed on {result.PrinterName}"
                : $"Print failed: {result.Error}";
            MessageBox.Show(this, message, AvtoforwardBranding.AppName, MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AvtoforwardBranding.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TestLogoPrint(string printerName)
    {
        try
        {
            var settings = BuildSettings();
            settings.PrinterName = printerName;
            var logger = new FileLogger(_paths.LogFilePath);
            var imageRenderer = new Printing.EscPosImageRenderer();
            var thermalPrinterService = new ThermalPrinterService(
                settings,
                _paths,
                logger,
                new Printing.PrinterDiscoveryService(),
                new Printing.EscPosTestReceiptBuilder(),
                new Printing.EscPosPayloadBuilder(),
                imageRenderer,
                new Printing.EscPosDocumentBuilder(imageRenderer),
                new Printing.PrinterTransportResolver(
                    new Printing.WindowsSpoolerTransport(new Printing.PrinterDiscoveryService(), new Printing.RawPrinterClient()),
                    new Printing.DirectUsbTransport(new Printing.UsbPrinterDiscoveryService(), new Printing.DirectUsbPrinterClient())));
            var result = thermalPrinterService.PrintLogoTest(Guid.NewGuid().ToString("N"));
            var message = result.Success
                ? $"Logo printed: {result.PrinterName}"
                : $"Logo print failed: {result.Error}";
            MessageBox.Show(this, message, AvtoforwardBranding.AppName, MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AvtoforwardBranding.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateTransportFields()
    {
        var isDirectUsb = string.Equals(_transportModeComboBox?.Text, PrinterTransportMode.DirectUsb, StringComparison.OrdinalIgnoreCase);
        if (_printerComboBox is not null)
        {
            _printerComboBox.Enabled = !isDirectUsb;
        }

        if (_usbVendorIdTextBox is not null)
        {
            _usbVendorIdTextBox.Enabled = isDirectUsb;
        }

        if (_usbProductIdTextBox is not null)
        {
            _usbProductIdTextBox.Enabled = isDirectUsb;
        }

        if (_statusLabel is not null)
        {
            _statusLabel.Text = isDirectUsb
                ? "Direct USB mode selected. Configure USB VID/PID and use Test Logo Print to validate image support."
                : (_isFirstRun
                    ? "Complete registration and save the production settings for this workstation."
                    : "Update local settings or re-register this workstation.");
        }
    }

    private void ToggleEnabled(bool enabled)
    {
        foreach (Control control in Controls)
        {
            control.Enabled = enabled;
        }
        Enabled = true;
    }

    private T FindControl<T>(string name) where T : Control
    {
        return Controls.Find(name, true).OfType<T>().Single();
    }

    private static AgentSettings Clone(AgentSettings settings)
    {
        return new AgentSettings
        {
            DeviceId = settings.DeviceId,
            AgentName = settings.AgentName,
            ApiBaseUrl = settings.ApiBaseUrl,
            SocketIoUrl = settings.SocketIoUrl,
            ApiKey = settings.ApiKey,
            StartWithWindows = settings.StartWithWindows,
            PrinterEnabled = settings.PrinterEnabled,
            PrinterName = settings.PrinterName,
            TransportMode = settings.TransportMode,
            UsbVendorId = settings.UsbVendorId,
            UsbProductId = settings.UsbProductId,
            UsbInterfaceNumber = settings.UsbInterfaceNumber,
            UsbOutEndpoint = settings.UsbOutEndpoint,
            UsbWriteTimeoutMs = settings.UsbWriteTimeoutMs,
            ImageCommandMode = settings.ImageCommandMode,
            MaxImageWidthDots = settings.MaxImageWidthDots,
            PaperWidth = settings.PaperWidth,
            CharacterEncoding = settings.CharacterEncoding,
            FeedLinesAfterPrint = settings.FeedLinesAfterPrint,
            ReconnectDelaySeconds = settings.ReconnectDelaySeconds,
            PingIntervalSeconds = settings.PingIntervalSeconds,
            LogFilePath = settings.LogFilePath
        };
    }

    private static void OpenPath(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
