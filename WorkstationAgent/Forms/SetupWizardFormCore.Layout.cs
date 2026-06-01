using System.Windows.Forms;
using WorkstationAgent.Branding;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Forms;

internal partial class SetupWizardFormCore
{
    private Control BuildSettingsTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        tabs.TabPages.Add(BuildGeneralTabPage());
        tabs.TabPages.Add(BuildSettingsTabPage("Receipt Printer", BuildReceiptPrinterSection()));
        tabs.TabPages.Add(BuildSettingsTabPage("Label Printer", BuildLabelPrinterSection()));
        tabs.TabPages.Add(BuildSettingsTabPage("POS Terminal", BuildPosTerminalSection()));
        return tabs;
    }

    private TabPage BuildGeneralTabPage()
    {
        var page = CreateSettingsTabPage("General");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(BuildConnectionSection(), 0, 0);
        layout.Controls.Add(BuildIdentitySection(), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private static TabPage BuildSettingsTabPage(string title, Control content)
    {
        var page = CreateSettingsTabPage(title);
        content.Dock = DockStyle.Top;
        content.Margin = Padding.Empty;
        page.Controls.Add(content);
        return page;
    }

    private static TabPage CreateSettingsTabPage(string title)
    {
        return new TabPage
        {
            Text = title,
            AutoScroll = true,
            Padding = new Padding(8)
        };
    }

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
            RowCount = 4,
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
            Text = $"Version: v{AgentVersionDisplay}",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.FromArgb(16, 70, 133)
        });
        textPanel.Controls.Add(new Label
        {
            Text = $"Config path: {SettingsStore.SettingsPath}",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.DimGray
        });

        panel.Controls.Add(logo, 0, 0);
        panel.Controls.Add(textPanel, 1, 0);
        return panel;
    }

    private static string AgentVersionDisplay => FormatAgentVersion(AgentVersionProvider.CurrentVersion);

    private static string FormatAgentVersion(string version)
    {
        var trimmed = (version ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "0.0.0";
        }

        var parts = trimmed.Split('+', 2);
        if (parts.Length == 1)
        {
            return parts[0];
        }

        return $"{parts[0]}+{parts[1].Split('.', 2)[0]}";
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
            Text = InitialSettings.DeviceId,
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

    private Control BuildReceiptPrinterSection()
    {
        var group = new GroupBox
        {
            Text = "Receipt Printer (ESC/POS)",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };

        var layout = CreateFormTable();
        layout.Controls.Add(new CheckBox { Name = "ReceiptPrinterEnabledCheckBox", Text = "Enable receipt printer integration", AutoSize = true }, 1, 0);
        layout.Controls.Add(new Label { Text = "Transport", AutoSize = true }, 0, 1);

        var transportComboBox = new ComboBox { Name = "ReceiptTransportModeComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        transportComboBox.Items.Add(PrinterTransportMode.WindowsSpooler);
        transportComboBox.Items.Add(PrinterTransportMode.DirectUsb);
        transportComboBox.SelectedIndexChanged += (_, _) => UpdateTransportFields();
        layout.Controls.Add(transportComboBox, 1, 1);

        var printerComboBox = new ComboBox { Name = "ReceiptPrinterComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        layout.Controls.Add(new Label { Text = "Printer", AutoSize = true }, 0, 2);
        layout.Controls.Add(printerComboBox, 1, 2);

        layout.Controls.Add(new Label { Text = "USB Vendor ID", AutoSize = true }, 0, 3);
        layout.Controls.Add(new TextBox { Name = "ReceiptUsbVendorIdTextBox", Width = 180 }, 1, 3);
        layout.Controls.Add(new Label { Text = "USB Product ID", AutoSize = true }, 0, 4);
        layout.Controls.Add(new TextBox { Name = "ReceiptUsbProductIdTextBox", Width = 180 }, 1, 4);
        layout.Controls.Add(new Label { Text = "Image Command", AutoSize = true }, 0, 5);

        var imageCommandModeComboBox = new ComboBox { Name = "ReceiptImageCommandModeComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        imageCommandModeComboBox.Items.Add("gs-v-0");
        imageCommandModeComboBox.Items.Add("esc-star");
        layout.Controls.Add(imageCommandModeComboBox, 1, 5);

        var testButton = new Button { Text = "Test Receipt Print", AutoSize = true };
        testButton.Click += (_, _) => TestPrint(printerComboBox.Text);
        layout.Controls.Add(testButton, 1, 6);

        var logoTestButton = new Button { Text = "Test Logo Print", AutoSize = true };
        logoTestButton.Click += (_, _) => TestLogoPrint(printerComboBox.Text);
        layout.Controls.Add(logoTestButton, 1, 7);

        var openLogsButton = new Button { Text = "Open Logs", AutoSize = true };
        openLogsButton.Click += (_, _) => OpenPath(Paths.LogsDirectory);
        layout.Controls.Add(openLogsButton, 1, 8);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildLabelPrinterSection()
    {
        var group = new GroupBox
        {
            Text = "Label Printer (TSPL / XP-365B)",
            Dock = DockStyle.Top,
            AutoSize = true
        };

        var layout = CreateFormTable();
        layout.Controls.Add(new CheckBox { Name = "LabelPrinterEnabledCheckBox", Text = "Enable label printer integration", AutoSize = true }, 1, 0);
        layout.Controls.Add(new Label { Text = "Transport", AutoSize = true }, 0, 1);

        var transportComboBox = new ComboBox { Name = "LabelTransportModeComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        transportComboBox.Items.Add(PrinterTransportMode.WindowsSpooler);
        transportComboBox.Items.Add(PrinterTransportMode.DirectUsb);
        transportComboBox.SelectedIndexChanged += (_, _) => UpdateTransportFields();
        layout.Controls.Add(transportComboBox, 1, 1);

        var printerComboBox = new ComboBox { Name = "LabelPrinterComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        layout.Controls.Add(new Label { Text = "Printer", AutoSize = true }, 0, 2);
        layout.Controls.Add(printerComboBox, 1, 2);

        layout.Controls.Add(new Label { Text = "USB Vendor ID", AutoSize = true }, 0, 3);
        layout.Controls.Add(new TextBox { Name = "LabelUsbVendorIdTextBox", Width = 180 }, 1, 3);
        layout.Controls.Add(new Label { Text = "USB Product ID", AutoSize = true }, 0, 4);
        layout.Controls.Add(new TextBox { Name = "LabelUsbProductIdTextBox", Width = 180 }, 1, 4);
        layout.Controls.Add(new Label { Text = "Character Encoding", AutoSize = true }, 0, 5);
        layout.Controls.Add(new TextBox { Name = "LabelCharacterEncodingTextBox", Width = 180 }, 1, 5);
        layout.Controls.Add(new Label { Text = "Code Page", AutoSize = true }, 0, 6);
        layout.Controls.Add(new TextBox { Name = "LabelCodePageTextBox", Width = 180 }, 1, 6);
        layout.Controls.Add(new Label { Text = "Label Width (mm)", AutoSize = true }, 0, 7);
        layout.Controls.Add(new NumericUpDown { Name = "TsplLabelWidthBox", Minimum = 10, Maximum = 300, DecimalPlaces = 1, Increment = 1, Width = 100 }, 1, 7);
        layout.Controls.Add(new Label { Text = "Label Height (mm)", AutoSize = true }, 0, 8);
        layout.Controls.Add(new NumericUpDown { Name = "TsplLabelHeightBox", Minimum = 10, Maximum = 300, DecimalPlaces = 1, Increment = 1, Width = 100 }, 1, 8);
        layout.Controls.Add(new Label { Text = "Gap (mm)", AutoSize = true }, 0, 9);
        layout.Controls.Add(new NumericUpDown { Name = "TsplLabelGapBox", Minimum = 0, Maximum = 20, DecimalPlaces = 1, Increment = (decimal)0.5, Width = 100 }, 1, 9);
        layout.Controls.Add(new Label { Text = "Direction", AutoSize = true }, 0, 10);

        var directionComboBox = new ComboBox { Name = "TsplDirectionComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        directionComboBox.Items.Add("0 - Top to bottom (normal)");
        directionComboBox.Items.Add("1 - Bottom to top (mirrored)");
        layout.Controls.Add(directionComboBox, 1, 10);

        layout.Controls.Add(new Label { Text = "Print Speed (1-5)", AutoSize = true }, 0, 11);
        layout.Controls.Add(new NumericUpDown { Name = "TsplSpeedBox", Minimum = 1, Maximum = 5, DecimalPlaces = 0, Width = 80 }, 1, 11);
        layout.Controls.Add(new Label { Text = "Density (1-15)", AutoSize = true }, 0, 12);
        layout.Controls.Add(new NumericUpDown { Name = "TsplDensityBox", Minimum = 1, Maximum = 15, DecimalPlaces = 0, Width = 80 }, 1, 12);

        var testLabelButton = new Button { Text = "Test TSPL Label Print", AutoSize = true };
        testLabelButton.Click += (_, _) => TestTsplLabelPrint(printerComboBox.Text);
        layout.Controls.Add(testLabelButton, 1, 13);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildPosTerminalSection()
    {
        var group = new GroupBox
        {
            Text = "PrivatBank POS Terminal",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };

        var layout = CreateFormTable();
        layout.Controls.Add(new CheckBox { Name = "PosTerminalEnabledCheckBox", Text = "Enable POS terminal integration", AutoSize = true }, 1, 0);
        layout.Controls.Add(new Label { Text = "Host", AutoSize = true }, 0, 1);
        layout.Controls.Add(new TextBox { Name = "PosTerminalHostTextBox", Width = 220 }, 1, 1);
        layout.Controls.Add(new Label { Text = "Port", AutoSize = true }, 0, 2);
        layout.Controls.Add(new NumericUpDown { Name = "PosTerminalPortBox", Minimum = 1, Maximum = 65535, Width = 100 }, 1, 2);
        layout.Controls.Add(new Label { Text = "Merchant ID", AutoSize = true }, 0, 3);
        layout.Controls.Add(new TextBox { Name = "PosTerminalMerchantIdTextBox", Width = 120 }, 1, 3);
        layout.Controls.Add(new Label { Text = "Timeout (seconds)", AutoSize = true }, 0, 4);
        layout.Controls.Add(new NumericUpDown { Name = "PosTerminalTimeoutBox", Minimum = 10, Maximum = 600, Width = 100 }, 1, 4);

        var testButton = new Button { Text = "Test Connection", AutoSize = true };
        testButton.Click += async (_, _) => await TestPosTerminalConnectionAsync();
        layout.Controls.Add(testButton, 1, 5);

        group.Controls.Add(layout);
        return group;
    }
}
