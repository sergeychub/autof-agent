using System.ComponentModel;
using System.Diagnostics;
using Krypton.Toolkit;
using System.Windows.Forms;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Forms;

internal partial class SetupWizardFormCore : KryptonForm
{
    protected readonly AgentSettingsStore SettingsStore;
    protected readonly AgentPaths Paths;
    protected readonly AgentSettings InitialSettings;
    protected readonly bool IsFirstRun;
    private readonly List<Control> _toggleRootControls = [];

    protected readonly KryptonComboBox ReceiptPrinterComboBox;
    protected readonly KryptonComboBox ReceiptTransportModeComboBox;
    protected readonly KryptonTextBox ReceiptUsbVendorIdTextBox;
    protected readonly KryptonTextBox ReceiptUsbProductIdTextBox;
    protected readonly KryptonComboBox ReceiptImageCommandModeComboBox;
    protected readonly KryptonCheckBox ReceiptPrinterEnabledCheckBox;
    protected readonly KryptonComboBox LabelPrinterComboBox;
    protected readonly KryptonComboBox LabelTransportModeComboBox;
    protected readonly KryptonTextBox LabelUsbVendorIdTextBox;
    protected readonly KryptonTextBox LabelUsbProductIdTextBox;
    protected readonly KryptonCheckBox LabelPrinterEnabledCheckBox;
    protected readonly KryptonTextBox LabelCharacterEncodingTextBox;
    protected readonly KryptonTextBox LabelCodePageTextBox;
    protected readonly KryptonCheckBox PosTerminalEnabledCheckBox;
    protected readonly KryptonTextBox PosTerminalHostTextBox;
    protected readonly KryptonNumericUpDown PosTerminalPortBox;
    protected readonly KryptonTextBox PosTerminalMerchantIdTextBox;
    protected readonly KryptonNumericUpDown PosTerminalTimeoutBox;
    protected readonly KryptonTextBox ApiBaseUrlTextBox;
    protected readonly KryptonTextBox RegistrationTokenTextBox;
    protected readonly KryptonTextBox AgentNameTextBox;
    protected readonly KryptonCheckBox StartWithWindowsCheckBox;
    protected readonly KryptonLabel StatusLabel;
    protected readonly KryptonNumericUpDown TsplLabelWidthBox;
    protected readonly KryptonNumericUpDown TsplLabelHeightBox;
    protected readonly KryptonNumericUpDown TsplLabelGapBox;
    protected readonly KryptonComboBox TsplDirectionComboBox;
    protected readonly KryptonNumericUpDown TsplSpeedBox;
    protected readonly KryptonNumericUpDown TsplDensityBox;

    protected SetupWizardFormCore(AgentSettings initialSettings, AgentSettingsStore settingsStore, AgentPaths paths, bool isFirstRun)
    {
        SetInheritedControlOverride();

        InitialSettings = initialSettings.Clone();
        SettingsStore = settingsStore;
        Paths = paths;
        IsFirstRun = isFirstRun;

        Text = isFirstRun
            ? $"{AvtoforwardBranding.AppName} Setup - v{AgentVersionDisplay}"
            : $"{AvtoforwardBranding.AppName} Settings - v{AgentVersionDisplay}";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1020, 760);
        Size = new Size(1160, 860);
        Icon = AvtoforwardBranding.CreateTrayIcon();

        var footer = BuildFooter();
        _toggleRootControls.Add(footer);
        Controls.Add(footer);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 16, 16, 8),
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _toggleRootControls.Add(content);
        Controls.Add(content);
        content.Controls.Add(BuildHeader(), 0, 0);
        content.Controls.Add(BuildSettingsShell(), 0, 1);

        ReceiptPrinterComboBox = FindControl<KryptonComboBox>("ReceiptPrinterComboBox");
        ReceiptTransportModeComboBox = FindControl<KryptonComboBox>("ReceiptTransportModeComboBox");
        ReceiptUsbVendorIdTextBox = FindControl<KryptonTextBox>("ReceiptUsbVendorIdTextBox");
        ReceiptUsbProductIdTextBox = FindControl<KryptonTextBox>("ReceiptUsbProductIdTextBox");
        ReceiptImageCommandModeComboBox = FindControl<KryptonComboBox>("ReceiptImageCommandModeComboBox");
        ReceiptPrinterEnabledCheckBox = FindControl<KryptonCheckBox>("ReceiptPrinterEnabledCheckBox");
        LabelPrinterComboBox = FindControl<KryptonComboBox>("LabelPrinterComboBox");
        LabelTransportModeComboBox = FindControl<KryptonComboBox>("LabelTransportModeComboBox");
        LabelUsbVendorIdTextBox = FindControl<KryptonTextBox>("LabelUsbVendorIdTextBox");
        LabelUsbProductIdTextBox = FindControl<KryptonTextBox>("LabelUsbProductIdTextBox");
        LabelPrinterEnabledCheckBox = FindControl<KryptonCheckBox>("LabelPrinterEnabledCheckBox");
        LabelCharacterEncodingTextBox = FindControl<KryptonTextBox>("LabelCharacterEncodingTextBox");
        LabelCodePageTextBox = FindControl<KryptonTextBox>("LabelCodePageTextBox");
        PosTerminalEnabledCheckBox = FindControl<KryptonCheckBox>("PosTerminalEnabledCheckBox");
        PosTerminalHostTextBox = FindControl<KryptonTextBox>("PosTerminalHostTextBox");
        PosTerminalPortBox = FindControl<KryptonNumericUpDown>("PosTerminalPortBox");
        PosTerminalMerchantIdTextBox = FindControl<KryptonTextBox>("PosTerminalMerchantIdTextBox");
        PosTerminalTimeoutBox = FindControl<KryptonNumericUpDown>("PosTerminalTimeoutBox");
        TsplLabelWidthBox = FindControl<KryptonNumericUpDown>("TsplLabelWidthBox");
        TsplLabelHeightBox = FindControl<KryptonNumericUpDown>("TsplLabelHeightBox");
        TsplLabelGapBox = FindControl<KryptonNumericUpDown>("TsplLabelGapBox");
        TsplDirectionComboBox = FindControl<KryptonComboBox>("TsplDirectionComboBox");
        TsplSpeedBox = FindControl<KryptonNumericUpDown>("TsplSpeedBox");
        TsplDensityBox = FindControl<KryptonNumericUpDown>("TsplDensityBox");
        ApiBaseUrlTextBox = FindControl<KryptonTextBox>("ApiBaseUrlTextBox");
        RegistrationTokenTextBox = FindControl<KryptonTextBox>("RegistrationTokenTextBox");
        AgentNameTextBox = FindControl<KryptonTextBox>("AgentNameTextBox");
        StartWithWindowsCheckBox = FindControl<KryptonCheckBox>("StartWithWindowsCheckBox");
        StatusLabel = FindControl<KryptonLabel>("StatusLabel");

        LoadInitialValues();
        LoadPrinters();
        UpdateTransportFields();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AgentSettings? SavedSettings { get; protected set; }

    private Control BuildFooter()
    {
        var statusLabel = new KryptonLabel
        {
            Name = "StatusLabel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(16, 70, 133),
            Text = IsFirstRun
                ? "Complete registration, then save production settings."
                : "Update settings or re-register this workstation.",
            MaximumSize = new Size(760, 0)
        };

        var saveButton = new KryptonButton
        {
            Text = IsFirstRun ? "Register and Start" : "Save Settings",
            AutoSize = true
        };
        saveButton.Click += async (_, _) => await SaveAsync();

        var cancelButton = new KryptonButton
        {
            Text = IsFirstRun ? "Exit" : "Cancel",
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

    protected static TableLayoutPanel CreateFormTable()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    protected void ToggleEnabled(bool enabled)
    {
        foreach (var control in _toggleRootControls)
        {
            control.Enabled = enabled;
        }

        Enabled = true;
    }

    protected T FindControl<T>(string name) where T : Control
    {
        return Controls.Find(name, true).OfType<T>().Single();
    }

    protected static void OpenPath(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    protected static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
