using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Printing;
using WorkstationAgent.Services;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;

namespace WorkstationAgent.Forms;

internal sealed partial class SetupWizardForm : Wpf.Window
{
    private sealed record Choice<T>(T Value, string Caption);

    private readonly AgentSettings _initial;
    private readonly AgentSettingsStore _store;
    private readonly AgentPaths _paths;
    private readonly string _footerHint;
    private readonly (Wpf.FrameworkElement Content, string Title, string Description)[] _pages;
    private bool _ready;
    private bool _busy;

    public SetupWizardForm(AgentSettings initialSettings, AgentSettingsStore settingsStore, AgentPaths paths, bool isFirstRun)
    {
        _initial = initialSettings.Clone();
        _store = settingsStore;
        _paths = paths;
        _footerHint = isFirstRun ? "Введите токен для регистрации." : "Настройки применятся после перезапуска агента.";
        InitializeComponent();

        _pages =
        [
            (OverviewPage, "Рабочее место", "Подключение агента и состояние оборудования."),
            (ConnectionPage, "Подключение к CRM", "Адрес сервера и регистрация рабочего места."),
            (ReceiptPage, "Чековый принтер", "Настройте печать чеков и открытие денежного ящика."),
            (LabelPage, "Принтер этикеток", "Выберите принтер TSPL и формат этикетки."),
            (TerminalPage, "Платёжный терминал", "Настройте подключение к терминалу ПриватБанка."),
            (JournalPage, "Журнал агента", "Последние записи о подключении и работе оборудования.")
        ];
        BrandLogo.Source = LoadLogo();
        var version = AgentVersionProvider.CurrentVersion.Split('+', 2);
        VersionText.Text = $"Версия {version[0]}";
        if (version.Length == 2) VersionText.ToolTip = AgentVersionProvider.CurrentVersion;
        if (isFirstRun)
        {
            Title = "AutoF Agent — регистрация";
            SaveSettings.Content = "Зарегистрировать";
            CancelSettings.Content = "Выход";
        }
        TokenHint.Text = isFirstRun ? "Введите токен рабочего места из AutoF CRM."
            : "Введите токен, если нужно повторно зарегистрировать агент.";
        LoadSettings();
        SetConnectionStatus(isFirstRun ? "Unregistered" : "Connecting");
        SetFeedback(_footerHint);
        _ready = true;
        RefreshOverview();
        RefreshTransport();
        Navigation.SelectedIndex = isFirstRun ? 1 : 0;
    }

    public AgentSettings? SavedSettings { get; private set; }

    public void SetConnectionStatus(string status)
    {
        var (text, ink, backdrop) = status switch
        {
            "Connected" => ("Подключён к CRM", "#21644B", "#E1F3E9"),
            "Connecting" => ("Подключение…", "#3F51B5", "#E9EBFA"),
            "Unregistered" => ("Требуется регистрация", "#74571D", "#F6EEDB"),
            _ => ("Нет связи с CRM", "#843E26", "#F9E9E1")
        };
        ConnectionText.Text = text;
        ConnectionText.Foreground = MakeBrush(ink);
        ConnectionDot.Fill = ConnectionText.Foreground;
        ConnectionBadge.Background = MakeBrush(backdrop);
    }

    private void SelectPage(object sender, Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || !ReferenceEquals(e.OriginalSource, Navigation)) return;
        var index = Navigation.SelectedIndex;
        if (index < 0) return;
        for (var i = 0; i < _pages.Length; i++)
            _pages[i].Content.Visibility = i == index ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;

        PageTitle.Text = _pages[index].Title;
        PageDescription.Text = _pages[index].Description;
        if (Navigation.SelectedItem is Controls.ListBoxItem item && item.Tag is string icon &&
            Enum.TryParse<PackIconKind>(icon, out var kind)) PageIcon.Kind = kind;
        SettingsScroll.ScrollToTop();
        if (ReferenceEquals(_pages[index].Content, JournalPage)) ReadJournal();
    }

    private void OpenPage(object sender, Wpf.RoutedEventArgs e)
    {
        if (sender is Controls.Button { Tag: string tag } && int.TryParse(tag, out var index))
            Navigation.SelectedIndex = index;
    }

    private void Cancel(object sender, Wpf.RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_busy) e.Cancel = true;
    }

    private void LoadSettings()
    {
        var printers = new PrinterDiscoveryService().GetInstalledPrinters();
        var transports = new Choice<string>[]
        {
            new(PrinterTransportMode.WindowsSpooler, "Служба печати Windows"), new(PrinterTransportMode.DirectUsb, "Прямой USB")
        };
        SetChoices(ReceiptTransport, transports, NormalizeTransport(_initial.ReceiptPrinter.TransportMode));
        SetChoices(LabelTransport, transports, NormalizeTransport(_initial.LabelPrinter.TransportMode));
        SetPrinterChoices(ReceiptWindowsPrinter, printers, _initial.ReceiptPrinter.PrinterName);
        SetPrinterChoices(LabelWindowsPrinter, printers, _initial.LabelPrinter.PrinterName);
        SetChoices(ReceiptImageMode, [new("gs-v-0", "Растровая (GS v 0)"), new("esc-star", "Точечная (ESC *)")],
            string.Equals(_initial.ReceiptPrinter.ImageCommandMode, "esc-star", StringComparison.OrdinalIgnoreCase)
                ? "esc-star" : "gs-v-0");
        SetChoices(LabelDirection, [new(0, "Сверху вниз"), new(1, "Снизу вверх")], Math.Clamp(_initial.LabelPrinter.TsplDirection, 0, 1));

        ApiBaseUrl.Text = _initial.ApiBaseUrl;
        AgentNameField.Text = string.IsNullOrWhiteSpace(_initial.AgentName) ? Environment.MachineName : _initial.AgentName;
        DeviceIdText.Text = string.IsNullOrWhiteSpace(_initial.DeviceId) ? "ID будет присвоен при регистрации." : $"ID устройства: {_initial.DeviceId}";
        StartWithWindows.IsChecked = _initial.StartWithWindows;
        ReceiptEnabled.IsChecked = _initial.ReceiptPrinter.Enabled;
        DrawerHotkey.IsChecked = _initial.CashDrawerHotkeyEnabled;
        ReceiptVid.Text = _initial.ReceiptPrinter.UsbVendorId ?? "";
        ReceiptPid.Text = _initial.ReceiptPrinter.UsbProductId ?? "";
        LabelEnabled.IsChecked = _initial.LabelPrinter.Enabled;
        LabelVid.Text = _initial.LabelPrinter.UsbVendorId ?? "";
        LabelPid.Text = _initial.LabelPrinter.UsbProductId ?? "";
        LabelEncoding.Text = _initial.LabelPrinter.CharacterEncoding;
        LabelCodePage.Text = _initial.LabelPrinter.CodePage ?? "";
        LabelWidth.Text = Math.Clamp(_initial.LabelPrinter.TsplLabelWidthMm, 10, 300).ToString(CultureInfo.CurrentCulture);
        LabelHeight.Text = Math.Clamp(_initial.LabelPrinter.TsplLabelHeightMm, 10, 300).ToString(CultureInfo.CurrentCulture);
        LabelGap.Text = Math.Clamp(_initial.LabelPrinter.TsplLabelGapMm, 0, 20).ToString(CultureInfo.CurrentCulture);
        LabelSpeed.Text = Math.Clamp(_initial.LabelPrinter.TsplSpeed, 1, 5).ToString(CultureInfo.CurrentCulture);
        LabelDensity.Text = Math.Clamp(_initial.LabelPrinter.TsplDensity, 1, 15).ToString(CultureInfo.CurrentCulture);
        TerminalEnabled.IsChecked = _initial.PosTerminal.Enabled;
        TerminalHost.Text = _initial.PosTerminal.Host;
        TerminalPort.Text = Math.Clamp(_initial.PosTerminal.Port, 1, 65535).ToString(CultureInfo.CurrentCulture);
        TerminalMerchant.Text = _initial.PosTerminal.MerchantId;
        TerminalTimeout.Text = Math.Clamp(_initial.PosTerminal.TimeoutSeconds, 10, 600).ToString(CultureInfo.CurrentCulture);
    }

    private static void SetChoices<T>(Controls.ComboBox box, IEnumerable<Choice<T>> choices, T selected)
    {
        box.DisplayMemberPath = nameof(Choice<T>.Caption);
        box.SelectedValuePath = nameof(Choice<T>.Value);
        box.ItemsSource = choices;
        box.SelectedValue = selected;
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
    }

    private static void SetPrinterChoices(Controls.ComboBox box, IEnumerable<string> installed, string? configured)
    {
        var printers = installed.Select(name => new Choice<string>(name, name)).ToList();
        var selected = printers.Find(item => string.Equals(configured, item.Value, StringComparison.OrdinalIgnoreCase));
        if (selected is null && !string.IsNullOrWhiteSpace(configured))
        {
            selected = new Choice<string>(configured, $"{configured} (отсутствует)");
            printers.Add(selected);
        }
        printers.Insert(0, new Choice<string>("", "Не выбран"));
        SetChoices(box, printers, selected?.Value ?? "");
    }

    private void RefreshTransport(object? sender = null, Controls.SelectionChangedEventArgs? e = null)
    {
        if (!_ready) return;
        UpdatePrinterVisibility(ReceiptTransport, ReceiptWindowsPrinter, ReceiptUsbFields);
        UpdatePrinterVisibility(LabelTransport, LabelWindowsPrinter, LabelUsbFields);
    }

    private static void UpdatePrinterVisibility(Controls.ComboBox transport, Controls.ComboBox printer, Wpf.UIElement usbFields)
    {
        var directUsb = PrinterTransportMode.IsDirectUsb(transport.SelectedValue as string);
        printer.Visibility = directUsb ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
        usbFields.Visibility = directUsb ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }

    private void RefreshOverview(object? sender = null, Wpf.RoutedEventArgs? e = null)
    {
        if (!_ready) return;
        SidebarName.Text = AgentNameField.Text.Trim();
        OverviewEndpoint.Text = Uri.TryCreate(_initial.ApiBaseUrl, UriKind.Absolute, out var address)
            ? address.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped)
            : "Адрес сервера не указан";
        OverviewReceipt.Text = $"Чековый принтер · {State(ReceiptEnabled.IsChecked)}";
        OverviewLabel.Text = $"Принтер этикеток · {State(LabelEnabled.IsChecked)}";
        OverviewTerminal.Text = $"Платёжный терминал · {State(TerminalEnabled.IsChecked)}";
        ReceiptStatus.Text = ReceiptEnabled.IsChecked == true ? "Чековый принтер включён" : "Чековый принтер выключен";
        ReceiptTestHint.Text = ReceiptEnabled.IsChecked == true ? "Проверьте чек или логотип перед работой." : "Доступна после включения чекового принтера.";
    }

    private static string NormalizeTransport(string? value) => PrinterTransportMode.IsDirectUsb(value)
        ? PrinterTransportMode.DirectUsb : PrinterTransportMode.WindowsSpooler;

    private static string State(bool? enabled) => enabled == true ? "Включён" : "Выключен";

    private AgentSettings BuildSettings()
    {
        var settings = _initial.Clone();
        if (string.IsNullOrWhiteSpace(settings.DeviceId))
            settings.DeviceId = Guid.NewGuid().ToString("N");
        settings.AgentName = AgentNameField.Text.Trim();
        settings.ApiBaseUrl = ApiBaseUrl.Text.Trim();
        settings.StartWithWindows = StartWithWindows.IsChecked == true;
        settings.CashDrawerHotkeyEnabled = DrawerHotkey.IsChecked == true;
        settings.LogFilePath ??= _paths.LogFilePath;
        ReadPrinter(settings.ReceiptPrinter, ReceiptEnabled.IsChecked, ReceiptTransport, ReceiptWindowsPrinter, ReceiptVid, ReceiptPid);
        settings.ReceiptPrinter.ImageCommandMode = ReceiptImageMode.SelectedValue as string ?? "gs-v-0";
        ReadPrinter(settings.LabelPrinter, LabelEnabled.IsChecked, LabelTransport, LabelWindowsPrinter, LabelVid, LabelPid);
        settings.LabelPrinter.CharacterEncoding = NullIfEmpty(LabelEncoding.Text) ?? "ascii";
        settings.LabelPrinter.CodePage = NullIfEmpty(LabelCodePage.Text);
        settings.LabelPrinter.TsplLabelWidthMm = ReadNumber(LabelWidth.Text, "Ширина этикетки", 10, 300);
        settings.LabelPrinter.TsplLabelHeightMm = ReadNumber(LabelHeight.Text, "Высота этикетки", 10, 300);
        settings.LabelPrinter.TsplLabelGapMm = ReadNumber(LabelGap.Text, "Зазор этикетки", 0, 20);
        settings.LabelPrinter.TsplDirection = LabelDirection.SelectedValue is int direction ? direction : 0;
        settings.LabelPrinter.TsplSpeed = ReadInteger(LabelSpeed.Text, "Скорость печати", 1, 5);
        settings.LabelPrinter.TsplDensity = ReadInteger(LabelDensity.Text, "Плотность печати", 1, 15);
        settings.PosTerminal.Enabled = TerminalEnabled.IsChecked == true;
        settings.PosTerminal.Host = NullIfEmpty(TerminalHost.Text) ?? new PosTerminalSettings().Host;
        settings.PosTerminal.Port = ReadInteger(TerminalPort.Text, "Порт терминала", 1, 65535);
        settings.PosTerminal.MerchantId = NullIfEmpty(TerminalMerchant.Text) ?? "1";
        settings.PosTerminal.TimeoutSeconds = ReadInteger(TerminalTimeout.Text, "Тайм-аут терминала", 10, 600);
        return settings;
    }

    private static void ReadPrinter(PrinterEndpointSettings settings, bool? enabled, Controls.ComboBox transport,
        Controls.ComboBox printer, Controls.TextBox vid, Controls.TextBox pid)
    {
        settings.Enabled = enabled == true;
        settings.TransportMode = NormalizeTransport(transport.SelectedValue as string);
        settings.PrinterName = printer.SelectedValue as string ?? "";
        settings.UsbVendorId = NullIfEmpty(vid.Text);
        settings.UsbProductId = NullIfEmpty(pid.Text);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ReadInteger(string text, string label, int min, int max)
    {
        if (int.TryParse(text, out var result) && result >= min && result <= max) return result;
        throw new InvalidOperationException($"{label}: введите целое число от {min} до {max}.");
    }

    private static double ReadNumber(string text, string label, double min, double max)
    {
        const NumberStyles format = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;
        if ((double.TryParse(text, format, CultureInfo.CurrentCulture, out var result) ||
            double.TryParse(text, format, CultureInfo.InvariantCulture, out result)) && result >= min && result <= max) return result;
        throw new InvalidOperationException($"{label}: введите число от {min} до {max}.");
    }

    private async void Save(object sender, Wpf.RoutedEventArgs e)
    {
        await RunOperationAsync("Сохраняем настройки…", async () =>
        {
            var settings = BuildSettings();
            if (!string.IsNullOrWhiteSpace(RegistrationToken.Password))
            {
                SetFeedback("Регистрируем рабочее место…");
                var registration = await new WorkstationRegistrationClient().RegisterAsync(settings, RegistrationToken.Password.Trim(), CancellationToken.None);
                settings.DeviceId = registration.DeviceId;
                settings.AgentName = registration.AgentName;
                settings.ApiKey = registration.ApiKey;
                settings.SocketIoUrl = registration.SocketIoUrl;
            }
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SocketIoUrl))
                throw new InvalidOperationException("Для запуска агента введите токен регистрации.");

            _store.Save(settings);
            SavedSettings = settings;
        });
        if (SavedSettings is not null) DialogResult = true;
    }

    private async void TestReceipt(object sender, Wpf.RoutedEventArgs e) => await TestPrinterAsync(
        service => service.PrintTestReceipt(Guid.NewGuid().ToString("N")),
        result => (result.Success, result.Success ? "Пробный чек отправлен." : result.Error));

    private async void TestReceiptLogo(object sender, Wpf.RoutedEventArgs e) => await TestPrinterAsync(
        service => service.PrintLogoTest(Guid.NewGuid().ToString("N")),
        result => (result.Success, result.Success ? "Логотип отправлен на печать." : result.Error));

    private async void TestLabel(object sender, Wpf.RoutedEventArgs e) => await TestPrinterAsync(
        service => service.PrintTsplTestLabel(Guid.NewGuid().ToString("N")),
        result => (result.Success, result.Success ? "Пробная этикетка отправлена." : result.Error));

    private Task TestPrinterAsync<T>(Func<ThermalPrinterService, T> action, Func<T, (bool Success, string? Message)> describe)
        => RunOperationAsync("Отправляем на печать…", async () =>
        {
            var settings = BuildSettings();
            var result = await Task.Run(() => action(ThermalPrinterService.Create(settings, _paths, new FileLogger(settings.LogFilePath))));
            var (success, message) = describe(result);
            SetFeedback(message ?? "Ошибка пробной печати.", !success);
        });

    private async void TestTerminal(object sender, Wpf.RoutedEventArgs e)
        => await RunOperationAsync("Проверяем связь с терминалом…", async () =>
        {
            var settings = BuildSettings();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var service = new PosTerminalService(settings, new FileLogger(settings.LogFilePath));
            var result = await service.TestConnectionAsync(timeout.Token);
            var success = string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase);
            SetFeedback(success ? "Терминал ответил." : result.Message ?? result.ResponseCode ?? "Терминал не ответил.", !success);
        });

    private async Task RunOperationAsync(string message, Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        Workspace.IsEnabled = false;
        SetFeedback(message);
        try { await action(); }
        catch (Exception error) { SetFeedback(error.Message, error: true); }
        finally
        {
            Workspace.IsEnabled = true;
            _busy = false;
        }
    }

    private void SetFeedback(string message, bool error = false)
    {
        FooterStatus.Text = message;
        FooterStatus.ToolTip = message;
        FooterStatus.Foreground = error ? MakeBrush("#9C331D") : (Media.Brush)FindResource("MutedInk");
    }

    private void RefreshJournal(object sender, Wpf.RoutedEventArgs e) => ReadJournal();

    private void ReadJournal()
    {
        var file = _initial.LogFilePath ?? _paths.LogFilePath;
        try
        {
            if (!File.Exists(file))
            {
                JournalOutput.Text = "В журнале пока нет записей.";
                return;
            }
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = Math.Max(0, stream.Length - 32768);
            var start = stream.Position;
            using var reader = new StreamReader(stream);
            if (start > 0) reader.ReadLine();
            JournalOutput.Text = reader.ReadToEnd();
            JournalOutput.ScrollToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            JournalOutput.Text = "Не удалось прочитать журнал. Попробуйте обновить записи.";
        }
    }

    private void OpenJournalFolder(object sender, Wpf.RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(_initial.LogFilePath ?? _paths.LogFilePath) ?? _paths.LogsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception error) { SetFeedback(error.Message, error: true); }
    }

    private static Media.ImageSource LoadLogo()
    {
        using var bitmap = AvtoforwardBranding.CreateHeaderLogoBitmap(72, 66);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static Media.Brush MakeBrush(string value) => (Media.Brush)new Media.BrushConverter().ConvertFromString(value)!;
}
