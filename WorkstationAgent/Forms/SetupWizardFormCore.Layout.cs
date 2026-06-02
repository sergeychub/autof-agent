using Krypton.Toolkit;
using System.Windows.Forms;
using WorkstationAgent.Branding;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Forms;

internal partial class SetupWizardFormCore
{
    private Control BuildSettingsShell()
    {
        var shell = new KryptonPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navigation = new KryptonListBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        navigation.Items.Add("General");
        navigation.Items.Add("Receipt Printer");
        navigation.Items.Add("Label Printer");
        navigation.Items.Add("POS Terminal");

        var navigationPanel = new KryptonPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 12, 0)
        };
        navigationPanel.Controls.Add(navigation);

        var contentHost = new KryptonPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var pages = new[]
        {
            BuildGeneralPage(),
            BuildSettingsPage("Receipt Printer", BuildReceiptPrinterSection()),
            BuildSettingsPage("Label Printer", BuildLabelPrinterSection()),
            BuildSettingsPage("POS Terminal", BuildPosTerminalSection())
        };

        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            contentHost.Controls.Add(page);
        }

        void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Length)
            {
                return;
            }

            for (var i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == index;
            }

            pages[index].BringToFront();
        }

        navigation.SelectedIndexChanged += (_, _) => ShowPage(navigation.SelectedIndex);

        layout.Controls.Add(navigationPanel, 0, 0);
        layout.Controls.Add(contentHost, 1, 0);
        shell.Controls.Add(layout);

        navigation.SelectedIndex = 0;
        return shell;
    }

    private Control BuildGeneralPage()
    {
        var page = CreateSettingsPage();
        var layout = CreatePageLayout("General");
        layout.Controls.Add(BuildConnectionSection(), 0, 1);
        layout.Controls.Add(BuildIdentitySection(), 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private static Control BuildSettingsPage(string title, Control content)
    {
        var page = CreateSettingsPage();
        var layout = CreatePageLayout(title);
        content.Dock = DockStyle.Top;
        content.Margin = Padding.Empty;
        layout.Controls.Add(content, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private static KryptonPanel CreateSettingsPage()
    {
        return new KryptonPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(8),
            Margin = Padding.Empty
        };
    }

    private static TableLayoutPanel CreatePageLayout(string title)
    {
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
        layout.Controls.Add(new KryptonLabel
        {
            Text = title,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        return layout;
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
        textPanel.Controls.Add(new KryptonLabel
        {
            Text = AvtoforwardBranding.AppName,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            AutoSize = true
        });
        textPanel.Controls.Add(new KryptonLabel
        {
            Text = "Thermal printing and device integration workstation agent.",
            AutoSize = true,
            MaximumSize = new Size(760, 0)
        });
        textPanel.Controls.Add(new KryptonLabel
        {
            Text = $"Version: v{AgentVersionDisplay}",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.FromArgb(16, 70, 133)
        });
        textPanel.Controls.Add(new KryptonLabel
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
        var group = CreateSectionGroup("Connection");

        var layout = CreateFormTable();
        layout.Controls.Add(CreateFieldLabel("API Base URL"), 0, 0);
        layout.Controls.Add(new KryptonTextBox { Name = "ApiBaseUrlTextBox", Width = 420 }, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Registration Token"), 0, 1);
        layout.Controls.Add(new KryptonTextBox { Name = "RegistrationTokenTextBox", Width = 420, UseSystemPasswordChar = true }, 1, 1);
        group.Panel.Controls.Add(layout);
        return group;
    }

    private Control BuildIdentitySection()
    {
        var group = CreateSectionGroup("Identity");

        var layout = CreateFormTable();
        layout.Controls.Add(CreateFieldLabel("Agent Name"), 0, 0);
        layout.Controls.Add(new KryptonTextBox { Name = "AgentNameTextBox", Width = 320 }, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Device ID"), 0, 1);
        layout.Controls.Add(new KryptonLabel
        {
            Text = InitialSettings.DeviceId,
            AutoSize = true,
            ForeColor = Color.DimGray
        }, 1, 1);
        layout.Controls.Add(new KryptonCheckBox
        {
            Name = "StartWithWindowsCheckBox",
            Text = "Start automatically with Windows",
            AutoSize = true
        }, 1, 2);
        group.Panel.Controls.Add(layout);
        return group;
    }

    private Control BuildReceiptPrinterSection()
    {
        var group = CreateSectionGroup("Receipt Printer (ESC/POS)");

        var layout = CreateFormTable();
        layout.Controls.Add(new KryptonCheckBox { Name = "ReceiptPrinterEnabledCheckBox", Text = "Enable receipt printer integration", AutoSize = true }, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Transport"), 0, 1);

        var transportComboBox = new KryptonComboBox { Name = "ReceiptTransportModeComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        transportComboBox.Items.Add(PrinterTransportMode.WindowsSpooler);
        transportComboBox.Items.Add(PrinterTransportMode.DirectUsb);
        transportComboBox.SelectedIndexChanged += (_, _) => UpdateTransportFields();
        layout.Controls.Add(transportComboBox, 1, 1);

        var printerComboBox = new KryptonComboBox { Name = "ReceiptPrinterComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        layout.Controls.Add(CreateFieldLabel("Printer"), 0, 2);
        layout.Controls.Add(printerComboBox, 1, 2);

        layout.Controls.Add(CreateFieldLabel("USB Vendor ID"), 0, 3);
        layout.Controls.Add(new KryptonTextBox { Name = "ReceiptUsbVendorIdTextBox", Width = 180 }, 1, 3);
        layout.Controls.Add(CreateFieldLabel("USB Product ID"), 0, 4);
        layout.Controls.Add(new KryptonTextBox { Name = "ReceiptUsbProductIdTextBox", Width = 180 }, 1, 4);
        layout.Controls.Add(CreateFieldLabel("Image Command"), 0, 5);

        var imageCommandModeComboBox = new KryptonComboBox { Name = "ReceiptImageCommandModeComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        imageCommandModeComboBox.Items.Add("gs-v-0");
        imageCommandModeComboBox.Items.Add("esc-star");
        layout.Controls.Add(imageCommandModeComboBox, 1, 5);

        var testButton = new KryptonButton { Text = "Test Receipt Print", AutoSize = true };
        testButton.Click += (_, _) => TestPrint(printerComboBox.Text);
        layout.Controls.Add(testButton, 1, 6);

        var logoTestButton = new KryptonButton { Text = "Test Logo Print", AutoSize = true };
        logoTestButton.Click += (_, _) => TestLogoPrint(printerComboBox.Text);
        layout.Controls.Add(logoTestButton, 1, 7);

        var openLogsButton = new KryptonButton { Text = "Open Logs", AutoSize = true };
        openLogsButton.Click += (_, _) => OpenPath(Paths.LogsDirectory);
        layout.Controls.Add(openLogsButton, 1, 8);

        group.Panel.Controls.Add(layout);
        return group;
    }

    private Control BuildLabelPrinterSection()
    {
        var group = CreateSectionGroup("Label Printer (TSPL / XP-365B)");

        var layout = CreateFormTable();
        layout.Controls.Add(new KryptonCheckBox { Name = "LabelPrinterEnabledCheckBox", Text = "Enable label printer integration", AutoSize = true }, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Transport"), 0, 1);

        var transportComboBox = new KryptonComboBox { Name = "LabelTransportModeComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        transportComboBox.Items.Add(PrinterTransportMode.WindowsSpooler);
        transportComboBox.Items.Add(PrinterTransportMode.DirectUsb);
        transportComboBox.SelectedIndexChanged += (_, _) => UpdateTransportFields();
        layout.Controls.Add(transportComboBox, 1, 1);

        var printerComboBox = new KryptonComboBox { Name = "LabelPrinterComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        layout.Controls.Add(CreateFieldLabel("Printer"), 0, 2);
        layout.Controls.Add(printerComboBox, 1, 2);

        layout.Controls.Add(CreateFieldLabel("USB Vendor ID"), 0, 3);
        layout.Controls.Add(new KryptonTextBox { Name = "LabelUsbVendorIdTextBox", Width = 180 }, 1, 3);
        layout.Controls.Add(CreateFieldLabel("USB Product ID"), 0, 4);
        layout.Controls.Add(new KryptonTextBox { Name = "LabelUsbProductIdTextBox", Width = 180 }, 1, 4);
        layout.Controls.Add(CreateFieldLabel("Character Encoding"), 0, 5);
        layout.Controls.Add(new KryptonTextBox { Name = "LabelCharacterEncodingTextBox", Width = 180 }, 1, 5);
        layout.Controls.Add(CreateFieldLabel("Code Page"), 0, 6);
        layout.Controls.Add(new KryptonTextBox { Name = "LabelCodePageTextBox", Width = 180 }, 1, 6);
        layout.Controls.Add(CreateFieldLabel("Label Width (mm)"), 0, 7);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "TsplLabelWidthBox", Minimum = 10, Maximum = 300, DecimalPlaces = 1, Increment = 1, Width = 100 }, 1, 7);
        layout.Controls.Add(CreateFieldLabel("Label Height (mm)"), 0, 8);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "TsplLabelHeightBox", Minimum = 10, Maximum = 300, DecimalPlaces = 1, Increment = 1, Width = 100 }, 1, 8);
        layout.Controls.Add(CreateFieldLabel("Gap (mm)"), 0, 9);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "TsplLabelGapBox", Minimum = 0, Maximum = 20, DecimalPlaces = 1, Increment = (decimal)0.5, Width = 100 }, 1, 9);
        layout.Controls.Add(CreateFieldLabel("Direction"), 0, 10);

        var directionComboBox = new KryptonComboBox { Name = "TsplDirectionComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        directionComboBox.Items.Add("0 - Top to bottom (normal)");
        directionComboBox.Items.Add("1 - Bottom to top (mirrored)");
        layout.Controls.Add(directionComboBox, 1, 10);

        layout.Controls.Add(CreateFieldLabel("Print Speed (1-5)"), 0, 11);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "TsplSpeedBox", Minimum = 1, Maximum = 5, DecimalPlaces = 0, Width = 80 }, 1, 11);
        layout.Controls.Add(CreateFieldLabel("Density (1-15)"), 0, 12);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "TsplDensityBox", Minimum = 1, Maximum = 15, DecimalPlaces = 0, Width = 80 }, 1, 12);

        var testLabelButton = new KryptonButton { Text = "Test TSPL Label Print", AutoSize = true };
        testLabelButton.Click += (_, _) => TestTsplLabelPrint(printerComboBox.Text);
        layout.Controls.Add(testLabelButton, 1, 13);

        group.Panel.Controls.Add(layout);
        return group;
    }

    private Control BuildPosTerminalSection()
    {
        var group = CreateSectionGroup("PrivatBank POS Terminal");

        var layout = CreateFormTable();
        layout.Controls.Add(new KryptonCheckBox { Name = "PosTerminalEnabledCheckBox", Text = "Enable POS terminal integration", AutoSize = true }, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Host"), 0, 1);
        layout.Controls.Add(new KryptonTextBox { Name = "PosTerminalHostTextBox", Width = 220 }, 1, 1);
        layout.Controls.Add(CreateFieldLabel("Port"), 0, 2);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "PosTerminalPortBox", Minimum = 1, Maximum = 65535, Width = 100 }, 1, 2);
        layout.Controls.Add(CreateFieldLabel("Merchant ID"), 0, 3);
        layout.Controls.Add(new KryptonTextBox { Name = "PosTerminalMerchantIdTextBox", Width = 120 }, 1, 3);
        layout.Controls.Add(CreateFieldLabel("Timeout (seconds)"), 0, 4);
        layout.Controls.Add(new KryptonNumericUpDown { Name = "PosTerminalTimeoutBox", Minimum = 10, Maximum = 600, Width = 100 }, 1, 4);

        var testButton = new KryptonButton { Text = "Test Connection", AutoSize = true };
        testButton.Click += async (_, _) => await TestPosTerminalConnectionAsync();
        layout.Controls.Add(testButton, 1, 5);

        group.Panel.Controls.Add(layout);
        return group;
    }

    private static KryptonGroupBox CreateSectionGroup(string title)
    {
        return new KryptonGroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };
    }

    private static KryptonLabel CreateFieldLabel(string text)
    {
        return new KryptonLabel
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(3, 6, 12, 3)
        };
    }
}
