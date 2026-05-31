using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Forms;

internal partial class SetupWizardFormCore : Form
{
    protected readonly AgentSettingsStore SettingsStore;
    protected readonly AgentPaths Paths;
    protected readonly AgentSettings InitialSettings;
    protected readonly bool IsFirstRun;

    protected readonly ComboBox ReceiptPrinterComboBox;
    protected readonly ComboBox ReceiptTransportModeComboBox;
    protected readonly TextBox ReceiptUsbVendorIdTextBox;
    protected readonly TextBox ReceiptUsbProductIdTextBox;
    protected readonly ComboBox ReceiptImageCommandModeComboBox;
    protected readonly CheckBox ReceiptPrinterEnabledCheckBox;
    protected readonly ComboBox LabelPrinterComboBox;
    protected readonly ComboBox LabelTransportModeComboBox;
    protected readonly TextBox LabelUsbVendorIdTextBox;
    protected readonly TextBox LabelUsbProductIdTextBox;
    protected readonly CheckBox LabelPrinterEnabledCheckBox;
    protected readonly TextBox LabelCharacterEncodingTextBox;
    protected readonly TextBox LabelCodePageTextBox;
    protected readonly TextBox ApiBaseUrlTextBox;
    protected readonly TextBox RegistrationTokenTextBox;
    protected readonly TextBox AgentNameTextBox;
    protected readonly CheckBox StartWithWindowsCheckBox;
    protected readonly Label StatusLabel;
    protected readonly NumericUpDown TsplLabelWidthBox;
    protected readonly NumericUpDown TsplLabelHeightBox;
    protected readonly NumericUpDown TsplLabelGapBox;
    protected readonly ComboBox TsplDirectionComboBox;
    protected readonly NumericUpDown TsplSpeedBox;
    protected readonly NumericUpDown TsplDensityBox;

    protected SetupWizardFormCore(AgentSettings initialSettings, AgentSettingsStore settingsStore, AgentPaths paths, bool isFirstRun)
    {
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

        Controls.Add(content);
        content.Controls.Add(BuildHeader(), 0, 0);
        content.Controls.Add(BuildSettingsTabs(), 0, 1);

        ReceiptPrinterComboBox = FindControl<ComboBox>("ReceiptPrinterComboBox");
        ReceiptTransportModeComboBox = FindControl<ComboBox>("ReceiptTransportModeComboBox");
        ReceiptUsbVendorIdTextBox = FindControl<TextBox>("ReceiptUsbVendorIdTextBox");
        ReceiptUsbProductIdTextBox = FindControl<TextBox>("ReceiptUsbProductIdTextBox");
        ReceiptImageCommandModeComboBox = FindControl<ComboBox>("ReceiptImageCommandModeComboBox");
        ReceiptPrinterEnabledCheckBox = FindControl<CheckBox>("ReceiptPrinterEnabledCheckBox");
        LabelPrinterComboBox = FindControl<ComboBox>("LabelPrinterComboBox");
        LabelTransportModeComboBox = FindControl<ComboBox>("LabelTransportModeComboBox");
        LabelUsbVendorIdTextBox = FindControl<TextBox>("LabelUsbVendorIdTextBox");
        LabelUsbProductIdTextBox = FindControl<TextBox>("LabelUsbProductIdTextBox");
        LabelPrinterEnabledCheckBox = FindControl<CheckBox>("LabelPrinterEnabledCheckBox");
        LabelCharacterEncodingTextBox = FindControl<TextBox>("LabelCharacterEncodingTextBox");
        LabelCodePageTextBox = FindControl<TextBox>("LabelCodePageTextBox");
        TsplLabelWidthBox = FindControl<NumericUpDown>("TsplLabelWidthBox");
        TsplLabelHeightBox = FindControl<NumericUpDown>("TsplLabelHeightBox");
        TsplLabelGapBox = FindControl<NumericUpDown>("TsplLabelGapBox");
        TsplDirectionComboBox = FindControl<ComboBox>("TsplDirectionComboBox");
        TsplSpeedBox = FindControl<NumericUpDown>("TsplSpeedBox");
        TsplDensityBox = FindControl<NumericUpDown>("TsplDensityBox");
        ApiBaseUrlTextBox = FindControl<TextBox>("ApiBaseUrlTextBox");
        RegistrationTokenTextBox = FindControl<TextBox>("RegistrationTokenTextBox");
        AgentNameTextBox = FindControl<TextBox>("AgentNameTextBox");
        StartWithWindowsCheckBox = FindControl<CheckBox>("StartWithWindowsCheckBox");
        StatusLabel = FindControl<Label>("StatusLabel");

        LoadInitialValues();
        LoadPrinters();
        UpdateTransportFields();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AgentSettings? SavedSettings { get; protected set; }

    private Control BuildFooter()
    {
        var statusLabel = new Label
        {
            Name = "StatusLabel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(16, 70, 133),
            Text = IsFirstRun
                ? "Complete registration and save the production settings for this workstation."
                : "Update local settings or re-register this workstation.",
            MaximumSize = new Size(680, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var saveButton = new Button
        {
            Text = IsFirstRun ? "Register and Start" : "Save Settings",
            AutoSize = true
        };
        saveButton.Click += async (_, _) => await SaveAsync();

        var cancelButton = new Button
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
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    protected void ToggleEnabled(bool enabled)
    {
        foreach (Control control in Controls)
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
