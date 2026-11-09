using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaControl = Avalonia.Controls.Control;
using NOVORA.ExInEngine;
using NOVORA.Integration;
using NOVORA.Linux.ExIn;
using NOVORA.Service;
using System.Diagnostics;

namespace NOVORA.Linux;

/// <summary>
/// ExInEngine page and runtime. Mirrors Windows NLUIWindowMain (ExIn parts of xaml.cs,
/// ExInSelection, ExInRoute, VisionEngine.EnsureExInStandaloneAsync and AndroidControl
/// CaptureAndroidExIn) on top of the unchanged Windows engine and HID session.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record ExInPaddleTargetOption(ExInButtons Button, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ExInPhoneOption(string? Serial, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ExInPaddleTargetOption[] ExInPaddleTargetsVE =
    [
        new(ExInButtons.None, "Sin asignar"),
        new(ExInButtons.South, "A / Sur"),
        new(ExInButtons.East, "B / Este"),
        new(ExInButtons.West, "X / Oeste"),
        new(ExInButtons.North, "Y / Norte"),
        new(ExInButtons.LeftShoulder, "LB"),
        new(ExInButtons.RightShoulder, "RB"),
        new(ExInButtons.LeftStick, "Clic stick izquierdo"),
        new(ExInButtons.RightStick, "Clic stick derecho"),
        new(ExInButtons.DPadUp, "Cruceta arriba"),
        new(ExInButtons.DPadDown, "Cruceta abajo"),
        new(ExInButtons.DPadLeft, "Cruceta izquierda"),
        new(ExInButtons.DPadRight, "Cruceta derecha"),
        new(ExInButtons.Back, "Vista / Atrás"),
        new(ExInButtons.Start, "Menú / Inicio"),
        new(ExInButtons.Guide, "Guía"),
        new(ExInButtons.Touchpad, "Touchpad")
    ];

    private readonly NLServiceNovoraPaths _exInPaths = new();
    private readonly SemaphoreSlim _exInConnectionGate = new(1, 1);
    private readonly DispatcherTimer _exInTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private NLServiceADB? _exInAdb;
    private ExInCoreEngine? _exInEngine;
    private ExInControlSession? _exInControlSession;
    private Task _exInInitialization = Task.CompletedTask;
    private LinuxExInSettings _exInSettings = LinuxExInSettings.Default;
    private volatile bool _exInUiApplyingVE;
    private volatile bool _exInRemoteApplyingVE;
    private volatile bool _exInClosing;
    private bool _exInPaddleMappingsApplyingVE;
    private bool _exInSelectionApplyingVE;
    private bool _exInRefreshing;
    private string? _exInManualSerialVE;
    private IReadOnlyList<NLServiceADBDevice> _exInPhonesVE = [];
    private ExInLiveSnapshot? _pendingExInLiveSnapshotVE;
    private int _exInLiveUpdateQueuedVE;
    private ExInBatteryAlert? _exInBatteryAlert;
    private long _exInBatteryAlertSequence;
    private Task? _exInShutdown;

    private readonly LinuxExInCalibrationView _exInCalibrationView = new() { Height = 440, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _exInCalibrationGuideText = new();
    private readonly TextBlock _exInCalibrationOverallText = new();
    private readonly ProgressBar _exInCalibrationOverallProgress = ExInProgressBar();
    private readonly TextBlock _exInCalibrationLeftText = new();
    private readonly ProgressBar _exInCalibrationLeftProgress = ExInProgressBar();
    private readonly TextBlock _exInCalibrationRightText = new();
    private readonly ProgressBar _exInCalibrationRightProgress = ExInProgressBar();
    private readonly TextBlock _exInCalibrationLeftTriggerText = new();
    private readonly ProgressBar _exInCalibrationLeftTriggerProgress = ExInProgressBar();
    private readonly TextBlock _exInCalibrationRightTriggerText = new();
    private readonly ProgressBar _exInCalibrationRightTriggerProgress = ExInProgressBar();
    private readonly Button _exInCalibrationStartButton = FlatButton("INICIAR");
    private readonly Button _exInCalibrationFinishButton = FlatButton("GUARDAR");
    private readonly Button _exInCalibrationResetButton = FlatButton("RESTABLECER");
    private readonly Button _exInSystemCalibrationButton = FlatButton("CONTROLES DE LINUX");
    private readonly TextBlock _gamepadStatusText = new();
    private readonly TextBlock _gamepadCountText = new();
    private readonly ComboBox _exInPhoneComboBox = new();
    private readonly TextBlock _exInPhoneText = new();
    private readonly ComboBox _exInControllerComboBox = new();
    private readonly CheckBox _exInForwardSelectedOnlyCheck = new() { Content = "Enviar al teléfono solo el mando seleccionado" };
    private readonly Button _exInGameModeButton = FlatButton("MODO JUEGO");
    private readonly Button _exInUiModeButton = FlatButton("MODO UI");
    private readonly TextBlock _exInIdentityText = new();
    private readonly Slider _exInPointerSensitivitySlider = new() { Minimum = 5, Maximum = 40, Value = 20, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock _exInPointerSensitivityText = new();
    private readonly Slider _exInGameStickSensitivitySlider = new() { Minimum = 25, Maximum = 150, Value = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock _exInGameStickSensitivityText = new();
    private readonly TextBlock _exInPaddleSupportText = new();
    private readonly ComboBox[] _exInPaddleMappingComboBoxes = [new() { Tag = "P1" }, new() { Tag = "P2" }, new() { Tag = "P3" }, new() { Tag = "P4" }];
    private readonly TextBlock _exInAxesText = new();
    private readonly TextBlock _exInHealthText = new();
    private readonly TextBlock _exInBatteryText = new();

    /// <summary>Live ExIn status line (Windows GamepadStatusText14); Inicio can bind to it.</summary>
    internal TextBlock ExInStatusText => _gamepadStatusText;

    private AvaloniaControl BuildExInPage()
    {
        InitializeExInVE();
        var content = PageContent();
        AddPageHeading(content, "EXINENGINE", "ExInEngine · Control",
            "Primero calibra el mando; después elige el teléfono, el modo y las asignaciones.");
        content.Children.Add(BuildExInCalibrationCard());
        content.Children.Add(BuildExInDeviceCards());
        content.Children.Add(BuildExInModeAndPaddles());
        content.Children.Add(BuildExInDiagnostics());
        if (_exInEngine is not null) ApplyGamepadShellVE(_exInEngine.Status);
        return ScrollPage(content);
    }

    private static ProgressBar ExInProgressBar() => new() { Minimum = 0, Maximum = 100, Value = 0, Height = 10 };

    private AvaloniaControl BuildExInCalibrationCard()
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("1.4*,24,*") };
        layout.Children.Add(_exInCalibrationView);

        var controls = new StackPanel();
        controls.Children.Add(SectionTitle("Calibración dinámica"));
        _exInCalibrationGuideText.Text = "Deja el mando quieto y pulsa INICIAR.";
        _exInCalibrationGuideText.Foreground = Brush("#f1f5f9");
        _exInCalibrationGuideText.FontSize = 14;
        _exInCalibrationGuideText.TextWrapping = TextWrapping.Wrap;
        _exInCalibrationGuideText.Margin = new Thickness(0, 10, 0, 0);
        controls.Children.Add(_exInCalibrationGuideText);
        controls.Children.Add(Caption("La imagen muestra el frontal, los dos laterales y los gatillos. Durante la calibración, NOVORA bloquea las entradas hacia las aplicaciones.",
            new Thickness(0, 6, 0, 0)));

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(0, 16, 0, 0)
        };
        PlaceExInButton(buttons, _exInCalibrationStartButton, 0, 0, new Thickness(0, 0, 6, 8));
        PlaceExInButton(buttons, _exInCalibrationFinishButton, 1, 0, new Thickness(6, 0, 0, 8));
        PlaceExInButton(buttons, _exInCalibrationResetButton, 0, 1, new Thickness(0, 0, 6, 0));
        PlaceExInButton(buttons, _exInSystemCalibrationButton, 1, 1, new Thickness(6, 0, 0, 0));
        ToolTip.SetTip(_exInCalibrationStartButton, "Primero deja sticks y gatillos en reposo");
        ToolTip.SetTip(_exInSystemCalibrationButton, "Abrir la herramienta de mandos del sistema");
        _exInCalibrationStartButton.Click += (_, _) => ApplyExInCalibrationUiVE(
            CanApplyExInCalibrationVE() && _exInEngine?.Manager.BeginCalibrationVE() == true,
            "No se pudo iniciar la calibración.");
        _exInCalibrationFinishButton.Click += (_, _) => ApplyExInCalibrationUiVE(
            CanApplyExInCalibrationVE() && _exInEngine?.Manager.FinishCalibrationVE() == true,
            "El recorrido capturado fue insuficiente.");
        _exInCalibrationResetButton.Click += async (_, _) => await ResetExInCalibrationVEAsync();
        _exInSystemCalibrationButton.Click += (_, _) => OpenSystemControllerToolVE();
        controls.Children.Add(buttons);

        var overall = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 4) };
        var overallLabel = Caption("COBERTURA MÍNIMA");
        overallLabel.FontWeight = FontWeight.SemiBold;
        ToolTip.SetTip(overallLabel, "Muestra el menor recorrido entre los dos sticks y los dos gatillos. La calibración se completa al llegar cada uno al 90%.");
        overall.Children.Add(overallLabel);
        StyleExInPercent(_exInCalibrationOverallText, 1);
        overall.Children.Add(_exInCalibrationOverallText);
        controls.Children.Add(overall);
        _exInCalibrationOverallProgress.Margin = new Thickness(0, 0, 0, 14);
        controls.Children.Add(_exInCalibrationOverallProgress);

        var pairs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var left = new StackPanel();
        AddExInProgress(left, "Stick izquierdo", _exInCalibrationLeftText, _exInCalibrationLeftProgress, 12);
        AddExInProgress(left, "Gatillo izquierdo", _exInCalibrationLeftTriggerText, _exInCalibrationLeftTriggerProgress, 0);
        var right = new StackPanel();
        AddExInProgress(right, "Stick derecho", _exInCalibrationRightText, _exInCalibrationRightProgress, 12);
        AddExInProgress(right, "Gatillo derecho", _exInCalibrationRightTriggerText, _exInCalibrationRightTriggerProgress, 0);
        Grid.SetColumn(right, 2);
        pairs.Children.Add(left);
        pairs.Children.Add(right);
        controls.Children.Add(pairs);

        Grid.SetColumn(controls, 2);
        layout.Children.Add(controls);
        var card = Card(layout);
        card.Margin = new Thickness(0, 0, 0, 20);
        return card;
    }

    private static void PlaceExInButton(Grid grid, Button button, int column, int row, Thickness margin)
    {
        button.MinHeight = 44;
        button.Height = double.NaN;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Margin = margin;
        // Long labels wrap at the 1000 px minimum width instead of being clipped.
        if (button.Content is string label)
            button.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(button, column);
        Grid.SetRow(button, row);
        grid.Children.Add(button);
    }

    private static void StyleExInPercent(TextBlock text, int column)
    {
        text.Text = "0%";
        text.FontSize = 13;
        text.FontWeight = FontWeight.Bold;
        text.Foreground = Brush("#a8b7ca");
        Grid.SetColumn(text, column);
    }

    private static void AddExInProgress(StackPanel parent, string label, TextBlock value, ProgressBar bar, double bottom)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(Caption(label));
        StyleExInPercent(value, 1);
        header.Children.Add(value);
        parent.Children.Add(header);
        bar.Margin = new Thickness(0, 0, 0, bottom);
        parent.Children.Add(bar);
    }

    private AvaloniaControl BuildExInDeviceCards()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("0.85*,14,1.15*"), Margin = new Thickness(0, 0, 0, 18) };

        var stateLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var state = new StackPanel();
        state.Children.Add(SectionTitle("Estado de ExInEngine"));
        _gamepadStatusText.Text = "ExInEngine detenido.";
        _gamepadStatusText.Foreground = Brush("#f1f5f9");
        _gamepadStatusText.FontSize = 15;
        _gamepadStatusText.TextWrapping = TextWrapping.Wrap;
        _gamepadStatusText.Margin = new Thickness(0, 8, 0, 4);
        state.Children.Add(_gamepadStatusText);
        StyleExInCaption(_gamepadCountText, "Mandos conectados: 0");
        state.Children.Add(_gamepadCountText);
        stateLayout.Children.Add(state);
        var reconnect = Caption("Reconexión automática", new Thickness(12, 0, 0, 0));
        Grid.SetColumn(reconnect, 1);
        stateLayout.Children.Add(reconnect);

        var devices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        var phone = new StackPanel();
        phone.Children.Add(SectionTitle("Conexión al teléfono"));
        _exInPhoneComboBox.Margin = new Thickness(0, 8, 0, 4);
        _exInPhoneComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        StyleSelector(_exInPhoneComboBox);
        _exInPhoneComboBox.SelectionChanged += ExInPhoneSelectionChangedVE;
        phone.Children.Add(_exInPhoneComboBox);
        StyleExInCaption(_exInPhoneText, "Automático: USB primero, después ADB Wi-Fi.");
        phone.Children.Add(_exInPhoneText);

        var controller = new StackPanel();
        controller.Children.Add(SectionTitle("Mando"));
        _exInControllerComboBox.Margin = new Thickness(0, 8, 0, 4);
        _exInControllerComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        StyleSelector(_exInControllerComboBox);
        _exInControllerComboBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ExInControllerChoice>(
            (choice, _) => new TextBlock { Text = choice?.Label ?? "" });
        _exInControllerComboBox.SelectionChanged += ExInControllerSelectionChangedVE;
        controller.Children.Add(_exInControllerComboBox);
        _exInForwardSelectedOnlyCheck.Foreground = Brush("#dbe5f1");
        _exInForwardSelectedOnlyCheck.Margin = new Thickness(0, 4, 0, 0);
        _exInForwardSelectedOnlyCheck.IsCheckedChanged += ExInForwardSelectedOnlyChangedVE;
        controller.Children.Add(_exInForwardSelectedOnlyCheck);
        controller.Children.Add(Caption("La identidad, la calibración y los paddles siguen al mando seleccionado.", new Thickness(0, 4, 0, 0)));
        Grid.SetColumn(controller, 2);
        devices.Children.Add(phone);
        devices.Children.Add(controller);

        var stateCard = Card(stateLayout);
        var deviceCard = Card(devices);
        stateCard.Margin = deviceCard.Margin = new Thickness(0);
        Grid.SetColumn(deviceCard, 2);
        grid.Children.Add(stateCard);
        grid.Children.Add(deviceCard);
        return grid;
    }

    private static void StyleExInCaption(TextBlock text, string value)
    {
        text.Text = value;
        text.FontSize = 13;
        text.Foreground = Brush("#a8b7ca");
        text.TextWrapping = TextWrapping.Wrap;
    }

    private AvaloniaControl BuildExInModeAndPaddles()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*"), Margin = new Thickness(0, 0, 0, 18) };
        var mode = new StackPanel();
        mode.Children.Add(SectionTitle("Modo de conexión y control"));
        mode.Children.Add(Caption("El teléfono elegido arriba recibe el modo seleccionado.", new Thickness(0, 6, 0, 0)));
        var modeButtons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 12, 0, 0) };
        PlaceExInButton(modeButtons, _exInGameModeButton, 0, 0, new Thickness(0, 0, 6, 0));
        PlaceExInButton(modeButtons, _exInUiModeButton, 1, 0, new Thickness(6, 0, 0, 0));
        _exInGameModeButton.Click += async (_, _) => await SetExInModeFromUiVEAsync(ExInInputMode.Game);
        _exInUiModeButton.Click += async (_, _) => await SetExInModeFromUiVEAsync(ExInInputMode.Ui);
        mode.Children.Add(modeButtons);
        StyleExInCaption(_exInIdentityText, "Sin control físico");
        _exInIdentityText.Margin = new Thickness(0, 10, 0, 0);
        mode.Children.Add(_exInIdentityText);
        mode.Children.Add(Caption("Sensibilidad del joystick para mouse virtual · solo modo UI", new Thickness(0, 12, 0, 0)));
        _exInPointerSensitivitySlider.Margin = new Thickness(0, 4, 0, 0);
        _exInPointerSensitivitySlider.ValueChanged += ExInPointerSensitivityChangedVE;
        mode.Children.Add(_exInPointerSensitivitySlider);
        StyleExInCaption(_exInPointerSensitivityText, "20% · UI");
        mode.Children.Add(_exInPointerSensitivityText);
        mode.Children.Add(Caption("Sensibilidad de los sticks para el mando · solo modo juego", new Thickness(0, 12, 0, 0)));
        _exInGameStickSensitivitySlider.Margin = new Thickness(0, 4, 0, 0);
        _exInGameStickSensitivitySlider.ValueChanged += ExInGameStickSensitivityChangedVE;
        mode.Children.Add(_exInGameStickSensitivitySlider);
        StyleExInCaption(_exInGameStickSensitivityText, "100% · Juego");
        mode.Children.Add(_exInGameStickSensitivityText);

        var paddles = new StackPanel();
        paddles.Children.Add(SectionTitle("Paddles traseros"));
        StyleExInCaption(_exInPaddleSupportText, "Conecta un mando compatible para configurar P1–P4.");
        _exInPaddleSupportText.Margin = new Thickness(0, 6, 0, 12);
        paddles.Children.Add(_exInPaddleSupportText);
        var mappings = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var leftColumn = new StackPanel();
        var rightColumn = new StackPanel();
        Grid.SetColumn(rightColumn, 2);
        for (int index = 0; index < _exInPaddleMappingComboBoxes.Length; index++)
        {
            ComboBox comboBox = _exInPaddleMappingComboBoxes[index];
            comboBox.ItemsSource = ExInPaddleTargetsVE;
            comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            comboBox.Margin = new Thickness(0, 4, 0, index % 2 == 0 ? 10 : 0);
            StyleSelector(comboBox);
            comboBox.SelectionChanged += ExInPaddleMappingSelectionChangedVE;
            StackPanel column = index < 2 ? leftColumn : rightColumn;
            column.Children.Add(Caption((string)comboBox.Tag!));
            column.Children.Add(comboBox);
        }
        mappings.Children.Add(leftColumn);
        mappings.Children.Add(rightColumn);
        paddles.Children.Add(mappings);
        paddles.Children.Add(Caption("Cada paddle puede repetir el mismo botón. La asignación se guarda para este mando.", new Thickness(0, 12, 0, 0)));

        var modeCard = Card(mode);
        var paddleCard = Card(paddles);
        modeCard.Margin = paddleCard.Margin = new Thickness(0);
        Grid.SetColumn(paddleCard, 2);
        grid.Children.Add(modeCard);
        grid.Children.Add(paddleCard);
        return grid;
    }

    private AvaloniaControl BuildExInDiagnostics()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        var reading = new StackPanel();
        reading.Children.Add(SectionTitle("Lectura del mando"));
        StyleExInCaption(_exInAxesText, "Sticks y gatillos sin datos");
        _exInAxesText.FontFamily = new FontFamily("monospace");
        _exInAxesText.Margin = new Thickness(0, 8, 0, 0);
        reading.Children.Add(_exInAxesText);
        var health = new StackPanel();
        health.Children.Add(SectionTitle("Salud y batería"));
        StyleExInCaption(_exInHealthText, "Diagnóstico no ejecutado");
        _exInHealthText.Margin = new Thickness(0, 8, 0, 0);
        health.Children.Add(_exInHealthText);
        StyleExInCaption(_exInBatteryText, "Batería no reportada");
        _exInBatteryText.Margin = new Thickness(0, 6, 0, 0);
        health.Children.Add(_exInBatteryText);
        var readingCard = Card(reading);
        var healthCard = Card(health);
        readingCard.Margin = healthCard.Margin = new Thickness(0);
        Grid.SetColumn(healthCard, 2);
        grid.Children.Add(readingCard);
        grid.Children.Add(healthCard);
        return grid;
    }

    // ── Runtime ────────────────────────────────────────────────────────────────────────

    private void InitializeExInVE()
    {
        if (_exInEngine is not null) return;
        try { _exInSettings = LinuxExInSettings.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _exInSettings = LinuxExInSettings.Default;
            Dispatcher.UIThread.Post(() => SetMessage($"ExInEngine: no se pudo leer su configuración ({ex.Message}); se usan los valores predeterminados.", "#fbbf24"));
        }
        _exInAdb = new NLServiceADB(_exInPaths);
        _exInEngine = new ExInCoreEngine(_exInPaths);
        _exInEngine.Manager.SetPointerSensitivityVE(Math.Clamp(_exInSettings.PointerSensitivity, 5, 40) / 100d);
        _exInEngine.Manager.SetGameStickSensitivityVE(Math.Clamp(_exInSettings.GameStickSensitivity, 25, 150) / 100d);
        _exInEngine.Manager.StatusChangedVE += ExInStatusChangedVE;
        _exInEngine.Manager.LiveSnapshotChangedVE += ExInLiveSnapshotChangedVE;
        _exInEngine.Manager.BatteryAlertVE += ExInBatteryAlertVE;
        _exInControlSession = new ExInControlSession(_exInEngine, _exInPaths, _exInAdb);
        _exInInitialization = InitializeExInEngineAsync(_exInEngine, _exInSettings.Enabled);
        _host.ExIn = new ExInControlBridge(this);
        _exInTimer.Tick += async (_, _) => await RefreshExInTargetsVEAsync();
        _exInTimer.Start();
        Closed += (_, _) => _exInShutdown ??= ShutdownExInAsync();
        _ = RefreshExInTargetsVEAsync();
    }

    private static async Task InitializeExInEngineAsync(ExInCoreEngine engine, bool enabled)
    {
        try
        {
            // A disabled engine stays uninitialized-on-hardware until enabled, like Windows.
            if (!enabled) await engine.SetEnabledAsync(false).ConfigureAwait(false);
            await engine.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The core/session supervisors retry; a faulted first start must not block the target.
            Debug.WriteLine("ExIn initial start will retry: " + ex.Message);
        }
    }

    private async Task ApplyExInRuntimeStateAsync(bool enabled)
    {
        try
        {
            if (_exInEngine is null) return;
            if (!enabled && _exInControlSession is not null)
                await _exInControlSession.StopAsync();
            await _exInEngine.SetEnabledAsync(enabled);
            await EnsureExInStandaloneAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!_exInClosing) SetMessage($"ExInEngine: {ex.Message}", "#f87171");
        }
    }

    private async Task RefreshExInTargetsVEAsync()
    {
        if (_exInClosing || _exInRefreshing || _exInAdb is null || _exInControlSession is null) return;
        _exInRefreshing = true;
        try
        {
            try { _exInPhonesVE = await _exInAdb.GetDevicesAsync(forceRefresh: true); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException)
            {
                _exInPhonesVE = [];
            }
            if (_exInEngine is not null) ApplyGamepadShellVE(_exInEngine.Status);
            string? running = _exInControlSession.IsRunning ? _exInControlSession.Serial : null;
            bool targetChanged = _exInManualSerialVE is not null &&
                !string.Equals(_exInManualSerialVE, running, StringComparison.OrdinalIgnoreCase);
            if (_exInSettings.Enabled && (running is null || targetChanged))
                await EnsureExInStandaloneAsync();
        }
        finally { _exInRefreshing = false; }
    }

    /// <summary>Windows ResolveExInSerialVE: the first connected ADB route, never a LAN marker.</summary>
    internal static string? ResolveExInSerialVE(string? connectedDeviceSerial, string? authorizedUsbSerial,
        string? activeVisionSerial, string? runningExInSerial = null)
        => new[] { connectedDeviceSerial, authorizedUsbSerial, activeVisionSerial, runningExInSerial }
            .Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                !value.StartsWith("LAN:", StringComparison.OrdinalIgnoreCase));

    private async Task EnsureExInStandaloneAsync()
    {
        if (_exInControlSession is null || _exInEngine is null || _exInAdb is null) return;
        await _exInConnectionGate.WaitAsync();
        try
        {
            string? manualSerial = _exInManualSerialVE;
            string? authorizedUsb = _host.IsUsbRunning ? _host.UsbSerial : null;
            // Linux has no single "selected device" view model: the authorized USB session wins,
            // then the first phone ADB sees over physical USB.
            string? connectedUsb = _exInPhonesVE.FirstOrDefault(device =>
                device.Connected && device.IsUsb && string.Equals(device.Serial, authorizedUsb, StringComparison.Ordinal))?.Serial
                ?? _exInPhonesVE.FirstOrDefault(device => device.Connected && device.IsUsb)?.Serial;
            string? serial = manualSerial ?? ResolveExInSerialVE(connectedUsb, authorizedUsb, null,
                _exInControlSession.IsRunning ? _exInControlSession.Serial : null);
            // USB keeps priority; LAN is the fallback through the phone's ADB Wi-Fi endpoint.
            if (manualSerial is null && string.IsNullOrWhiteSpace(serial) && _host.LanPeerAddress is { } lanHost)
            {
                using var lanTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                serial = await _exInAdb.TryConnectLanEndpointAsync(lanHost.ToString(), cancellationToken: lanTimeout.Token);
            }
            if (manualSerial is null && string.IsNullOrWhiteSpace(serial))
                serial = _exInPhonesVE.FirstOrDefault(device => device.Connected)?.Serial;
            bool shouldRun = !_exInClosing && _exInSettings.Enabled &&
                (!string.IsNullOrWhiteSpace(serial) || _exInControlSession.HasRequestedTarget);
            if (!shouldRun)
            {
                await _exInControlSession.StopAsync();
                return;
            }
            try { await _exInInitialization.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (TimeoutException)
            {
                Debug.WriteLine("ExIn initialization is still pending after 20 s.");
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _exInControlSession.StartAsync(
                manualSerial ?? _exInControlSession.RequestedSerial ?? serial!, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_exInClosing) SetMessage("ExIn está intentando recuperar automáticamente la conexión del control.", "#fbbf24");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!_exInClosing) SetMessage("ExIn intentará recuperar la conexión automáticamente: " + ex.Message, "#fbbf24");
        }
        finally
        {
            _exInConnectionGate.Release();
            if (!_exInClosing && _exInEngine is not null) ApplyGamepadShellVE(_exInEngine.Status);
        }
    }

    private void ExInStatusChangedVE(object? sender, ExInStatus status)
        => Dispatcher.UIThread.Post(() => { if (!_exInClosing) ApplyGamepadShellVE(status); });

    private void ExInLiveSnapshotChangedVE(object? sender, ExInLiveSnapshot snapshot)
    {
        if (_exInClosing) return;
        Interlocked.Exchange(ref _pendingExInLiveSnapshotVE, snapshot);
        QueueExInLiveFrameVE();
    }

    private void QueueExInLiveFrameVE()
    {
        if (Interlocked.CompareExchange(ref _exInLiveUpdateQueuedVE, 1, 0) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ExInLiveSnapshot? latest = Interlocked.Exchange(ref _pendingExInLiveSnapshotVE, null);
                if (!_exInClosing && latest is not null) ApplyExInCalibrationProgressVE(latest);
            }
            finally
            {
                Interlocked.Exchange(ref _exInLiveUpdateQueuedVE, 0);
                if (!_exInClosing && Volatile.Read(ref _pendingExInLiveSnapshotVE) is not null)
                    QueueExInLiveFrameVE();
            }
        }, DispatcherPriority.Render);
    }

    private void ExInBatteryAlertVE(object? sender, ExInBatteryAlert alert)
        => Dispatcher.UIThread.Post(() =>
        {
            _exInBatteryAlert = alert;
            Interlocked.Increment(ref _exInBatteryAlertSequence);
        });

    private void ApplyGamepadShellVE(ExInStatus status)
    {
        _gamepadStatusText.Text = !_exInSettings.Enabled
            ? "ExInEngine desactivado en PC."
            : string.IsNullOrWhiteSpace(status.LastError)
                ? status.Message
                : $"{status.Message} - {status.LastError}";
        _gamepadCountText.Text =
            $"Mandos conectados: {status.ConnectedGamepads} - Reportes enviados: {status.ReportsSent:N0}";

        ExInLiveSnapshot? live = _exInEngine?.LiveSnapshot;
        ExInDevice? device = live?.Device;
        ExInState raw = live?.State ?? default;
        ExInState corrected = live?.CorrectedState ?? default;
        string? profileKey = device?.Identity?.ProfileKey;
        ExInDiagnosticStatus? diagnostic = profileKey is null
            ? null : status.Diagnostics.FirstOrDefault(value => value.ProfileKey == profileKey);
        ExInBatteryStatus? battery = profileKey is null
            ? null : status.Batteries.FirstOrDefault(value => value.ProfileKey == profileKey);
        _exInCalibrationView.UpdateBatteryVE(battery);
        ExInInputMode mode = _exInEngine?.Manager.ModeVE ?? ExInInputMode.Game;
        bool available = device is not null && !_exInUiApplyingVE && !_exInRemoteApplyingVE;
        bool calibrating = live?.Calibrating == true;
        bool canCalibrate = available && _exInEngine?.Manager.IsPrivacyProtectedVE != true;

        _exInIdentityText.Text = device is null
            ? "Sin control físico"
            : $"{device.Identity?.Family ?? ExInControllerFamily.Generic} · {device.Name}\n" +
              $"{live?.Calibration?.ConnectionType ?? "Conexión no reportada"} · " +
              (device.Identity?.Family == ExInControllerFamily.DualShock4
                  ? "Juego · puntero · navegación · touchpad"
                  : "Juego · puntero · navegación");
        ApplyExInCalibrationProgressVE(live);
        static int AxisVE(short value) => (int)Math.Round(value / 32767d * 100);
        static int TriggerVE(short value) => (int)Math.Round(Math.Max(0, (int)value) / 32767d * 100);
        _exInAxesText.Text =
            $"ENTRADA   L {AxisVE(raw.LeftX),4}/{AxisVE(raw.LeftY),4}  R {AxisVE(raw.RightX),4}/{AxisVE(raw.RightY),4}  T {TriggerVE(raw.LeftTrigger),3}/{TriggerVE(raw.RightTrigger),3}\n" +
            $"APLICADO  L {AxisVE(corrected.LeftX),4}/{AxisVE(corrected.LeftY),4}  R {AxisVE(corrected.RightX),4}/{AxisVE(corrected.RightY),4}  T {TriggerVE(corrected.LeftTrigger),3}/{TriggerVE(corrected.RightTrigger),3}";
        _exInHealthText.Text = diagnostic is null
            ? "Diagnóstico no ejecutado"
            : $"{diagnostic.Classification} · {diagnostic.Explanation}";
        _exInBatteryText.Text = battery is null
            ? "Batería no reportada"
            : battery.Percent is int percent ? $"{battery.State} · {percent}%" : battery.State.ToString();

        // The active mode stays enabled and is highlighted instead of faded, as on Windows.
        _exInGameModeButton.IsEnabled = available && !calibrating;
        _exInUiModeButton.IsEnabled = available && !calibrating;
        int pointerSensitivity = (int)Math.Round((_exInEngine?.Manager.PointerSensitivityVE ?? .5d) * 100d);
        if (Math.Abs(_exInPointerSensitivitySlider.Value - pointerSensitivity) >= .5)
            _exInPointerSensitivitySlider.Value = pointerSensitivity;
        _exInPointerSensitivityText.Text = $"{pointerSensitivity}% · UI";
        _exInPointerSensitivitySlider.IsEnabled = available && !calibrating;
        int gameStickSensitivity = (int)Math.Round((_exInEngine?.Manager.GameStickSensitivityVE ?? 1d) * 100d);
        if (Math.Abs(_exInGameStickSensitivitySlider.Value - gameStickSensitivity) >= .5)
            _exInGameStickSensitivitySlider.Value = gameStickSensitivity;
        _exInGameStickSensitivityText.Text = $"{gameStickSensitivity}% · Juego";
        _exInGameStickSensitivitySlider.IsEnabled = available && !calibrating;
        _exInSystemCalibrationButton.IsEnabled = available && !calibrating;
        _exInCalibrationStartButton.IsEnabled = canCalibrate && !calibrating;
        _exInCalibrationFinishButton.IsEnabled = canCalibrate && calibrating && live?.CalibrationProgress?.Complete == true;
        _exInCalibrationResetButton.IsEnabled = canCalibrate && (live?.Calibrated == true || calibrating);
        _exInCalibrationResetButton.Content = calibrating ? "CANCELAR" : "RESTABLECER";
        _exInGameModeButton.Background = Brush(mode == ExInInputMode.Game ? "#1d3150" : "#18283d");
        _exInUiModeButton.Background = Brush(mode == ExInInputMode.Ui ? "#1d3150" : "#18283d");
        _exInGameModeButton.BorderBrush = Brush(mode == ExInInputMode.Game ? "#60a5fa" : "#26364d");
        _exInUiModeButton.BorderBrush = Brush(mode == ExInInputMode.Ui ? "#60a5fa" : "#26364d");
        ApplyExInPaddleMappingsUiVE(available && !calibrating);
        ApplyExInSelectionUiVE(!_exInUiApplyingVE && !_exInRemoteApplyingVE, calibrating);
    }

    private void ApplyExInPaddleMappingsUiVE(bool enabled)
    {
        ExInPaddleMappings mappings = _exInEngine?.Manager.PaddleMappingsVE ?? ExInPaddleMappings.DefaultVE;
        _exInPaddleMappingsApplyingVE = true;
        try
        {
            foreach (ComboBox comboBox in _exInPaddleMappingComboBoxes)
            {
                comboBox.IsEnabled = enabled;
                if (comboBox.Tag is not string tag || !Enum.TryParse(tag, out ExInPaddle paddle)) continue;
                comboBox.SelectedItem = ExInPaddleTargetsVE.FirstOrDefault(option => option.Button == mappings.GetVE(paddle))
                    ?? ExInPaddleTargetsVE[0];
            }
        }
        finally
        {
            _exInPaddleMappingsApplyingVE = false;
        }

        _exInPaddleSupportText.Text = enabled
            ? "P1–P4 se repiten como el botón seleccionado. Puedes elegir el mismo botón para varios paddles."
            : "Conecta un mando compatible y termina la calibración para configurar P1–P4.";
    }

    private void ExInPaddleMappingSelectionChangedVE(object? sender, SelectionChangedEventArgs e)
    {
        if (_exInPaddleMappingsApplyingVE || sender is not ComboBox comboBox ||
            comboBox.Tag is not string tag || !Enum.TryParse(tag, out ExInPaddle paddle) ||
            comboBox.SelectedItem is not ExInPaddleTargetOption option)
            return;

        if (_exInEngine?.Manager.TrySetPaddleMappingVE(paddle, option.Button) == true)
        {
            SetMessage($"{paddle} ahora repite {option.Label}.", "#22c55e");
            ApplyGamepadShellVE(_exInEngine.Status);
            return;
        }

        SetMessage("No se pudo guardar la asignación del paddle.", "#f87171");
        if (_exInEngine is not null) ApplyGamepadShellVE(_exInEngine.Status);
    }

    private void ApplyExInCalibrationProgressVE(ExInLiveSnapshot? live)
    {
        ExInCalibrationProgress progress = live?.CalibrationProgress ??
            (live?.Calibrated == true ? new(100, 100, 100, 100) : new(0, 0, 0, 0));
        _exInCalibrationView.UpdateVE(live);
        _exInCalibrationOverallProgress.Value = progress.Overall;
        _exInCalibrationOverallText.Text = $"{progress.Overall}%";
        _exInCalibrationLeftProgress.Value = progress.LeftStick;
        _exInCalibrationRightProgress.Value = progress.RightStick;
        _exInCalibrationLeftTriggerProgress.Value = progress.LeftTrigger;
        _exInCalibrationRightTriggerProgress.Value = progress.RightTrigger;
        _exInCalibrationLeftText.Text = $"{progress.LeftStick}%";
        _exInCalibrationRightText.Text = $"{progress.RightStick}%";
        _exInCalibrationLeftTriggerText.Text = $"{progress.LeftTrigger}%";
        _exInCalibrationRightTriggerText.Text = $"{progress.RightTrigger}%";
        _exInCalibrationGuideText.Text = live?.Calibrating == true
            ? progress.Complete
                ? "Recorrido completo. Las aplicaciones siguen bloqueadas: suelta el mando y pulsa GUARDAR."
                : NextExInCalibrationStepVE(progress) + " Las aplicaciones están bloqueadas."
            : live?.Calibrated == true
                ? "Perfil activo para este mando. Puedes repetirlo o restablecerlo."
                : "Deja sticks y gatillos en reposo y pulsa INICIAR.";
        if (live?.Calibrating == true)
        {
            _exInCalibrationFinishButton.IsEnabled = CanApplyExInCalibrationVE() && progress.Complete;
            _exInCalibrationResetButton.IsEnabled = CanApplyExInCalibrationVE();
        }
    }

    private static string NextExInCalibrationStepVE(ExInCalibrationProgress progress)
        => progress.LeftStick < 90 ? "Paso 1 de 4: gira el stick izquierdo por todo el borde." :
           progress.RightStick < 90 ? "Paso 2 de 4: gira el stick derecho por todo el borde." :
           progress.LeftTrigger < 90 ? "Paso 3 de 4: presiona y suelta el gatillo izquierdo." :
           "Paso 4 de 4: presiona y suelta el gatillo derecho.";

    private void ExInPointerSensitivityChangedVE(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_exInEngine is null) return;
        _exInEngine.Manager.SetPointerSensitivityVE(e.NewValue / 100d);
        SaveExInSettingsVE(_exInSettings with { PointerSensitivity = (int)Math.Round(e.NewValue) });
        _exInPointerSensitivityText.Text = $"{e.NewValue:0}% · UI";
    }

    private void ExInGameStickSensitivityChangedVE(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_exInEngine is null) return;
        _exInEngine.Manager.SetGameStickSensitivityVE(e.NewValue / 100d);
        SaveExInSettingsVE(_exInSettings with { GameStickSensitivity = (int)Math.Round(e.NewValue) });
        _exInGameStickSensitivityText.Text = $"{e.NewValue:0}% · Juego";
    }

    private void SaveExInSettingsVE(LinuxExInSettings settings)
    {
        if (settings == _exInSettings) return;
        _exInSettings = settings;
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage($"ExInEngine: no se pudo guardar su configuración: {ex.Message}", "#f87171");
        }
    }

    private void OpenSystemControllerToolVE()
    {
        // Linux equivalent of Windows "joy.cpl": the desktop's joystick tester when installed.
        foreach (string tool in new[] { "jstest-gtk", "gnome-control-center", "systemsettings" })
        {
            string? path = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, tool))
                .FirstOrDefault(File.Exists);
            if (path is null) continue;
            if (tool != "jstest-gtk")
            {
                // Desktop settings have no calibration page; only mention them as a fallback.
                SetMessage("Instala jstest-gtk para revisar y calibrar el mando con la herramienta del sistema.", "#fbbf24");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = false })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                SetMessage($"No fue posible abrir la herramienta de mandos del sistema: {ex.Message}", "#f87171");
            }
            return;
        }
        SetMessage("No hay una herramienta de mandos del sistema; instala jstest-gtk. La calibración de NOVORA funciona sin ella.", "#fbbf24");
    }

    private async Task SetExInModeFromUiVEAsync(ExInInputMode mode)
    {
        if (_exInEngine is null || _exInUiApplyingVE || _exInRemoteApplyingVE) return;
        if (_exInEngine.Manager.ModeVE == mode) return;
        _exInUiApplyingVE = true;
        ApplyGamepadShellVE(_exInEngine.Status);
        try
        {
            ExInModeResult result = await _exInEngine.Manager.SetModeAsync(mode);
            if (!result.Success) SetMessage(result.Message, "#fbbf24");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetMessage("ExInEngine: " + ex.Message, "#f87171");
        }
        finally
        {
            _exInUiApplyingVE = false;
            ApplyGamepadShellVE(_exInEngine.Status);
        }
    }

    private bool CanApplyExInCalibrationVE()
        => !_exInUiApplyingVE && !_exInRemoteApplyingVE &&
           _exInEngine?.Manager.IsPrivacyProtectedVE == false;

    private void ApplyExInCalibrationUiVE(bool success, string failureMessage)
    {
        if (!success) SetMessage(failureMessage, "#fbbf24");
        if (_exInEngine is not null) ApplyGamepadShellVE(_exInEngine.Status);
    }

    private async Task ResetExInCalibrationVEAsync()
    {
        if (!CanApplyExInCalibrationVE()) return;
        if (!await ConfirmExInVEAsync("Restablecer calibración",
                "Se eliminará la calibración actual del control. ¿Quieres restablecerla?"))
            return;
        _exInEngine?.Manager.ResetCalibrationVE();
        if (_exInEngine is not null) ApplyGamepadShellVE(_exInEngine.Status);
        SetMessage("Calibración restablecida.", "#22c55e");
    }

    private async Task<bool> ConfirmExInVEAsync(string title, string message)
    {
        bool confirmed = false;
        var yes = FlatButton("Sí");
        var no = FlatButton("No");
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#131f31"),
            FontFamily = FontFamily,
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 16,
                Children =
                {
                    SectionTitle(title),
                    Caption(message),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { no, yes }
                    }
                }
            }
        };
        yes.Click += (_, _) => { confirmed = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return confirmed;
    }

    private void ApplyExInSelectionUiVE(bool enabled, bool calibrating)
    {
        _exInSelectionApplyingVE = true;
        try
        {
            ApplyExInPhoneOptionsVE(enabled);
            ApplyExInControllerOptionsVE(enabled && !calibrating);
            _exInForwardSelectedOnlyCheck.IsChecked = _exInEngine?.Manager.ForwardSelectedOnlyVE == true;
            _exInForwardSelectedOnlyCheck.IsEnabled = enabled && _exInEngine is not null;
        }
        finally
        {
            _exInSelectionApplyingVE = false;
        }
    }

    private void ApplyExInPhoneOptionsVE(bool enabled)
    {
        var options = new List<ExInPhoneOption> { new(null, "Automático") };
        foreach (NLServiceADBDevice device in _exInPhonesVE)
        {
            if (!device.Connected || string.IsNullOrWhiteSpace(device.Serial)) continue;
            // An ADB Wi-Fi serial is host:port; the label never shows the serial or IP.
            string route = device.IsUsb ? "USB" : "ADB Wi-Fi";
            options.Add(new(device.Serial, $"{device.FriendlyName} · {route}"));
        }
        if (_exInManualSerialVE is { } manual &&
            !options.Any(option => string.Equals(option.Serial, manual, StringComparison.OrdinalIgnoreCase)))
            options.Add(new(manual, "Teléfono elegido · no conectado"));

        if (_exInPhoneComboBox.ItemsSource is not IEnumerable<ExInPhoneOption> current || !current.SequenceEqual(options))
            _exInPhoneComboBox.ItemsSource = options;
        _exInPhoneComboBox.SelectedItem = options.FirstOrDefault(option =>
            string.Equals(option.Serial, _exInManualSerialVE, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        _exInPhoneComboBox.IsEnabled = enabled && options.Count > 1;

        string? active = _exInControlSession?.Serial;
        _exInPhoneText.Text = _exInManualSerialVE is null
            ? "Automático: USB primero, después ADB Wi-Fi."
            : active is not null && string.Equals(active, _exInManualSerialVE, StringComparison.OrdinalIgnoreCase)
                ? "ExIn envía al teléfono elegido."
                : "ExIn se está conectando al teléfono elegido.";
    }

    private void ApplyExInControllerOptionsVE(bool enabled)
    {
        IReadOnlyList<ExInControllerChoice> controllers =
            _exInEngine?.Manager.ControllersVE ?? Array.Empty<ExInControllerChoice>();
        if (_exInControllerComboBox.ItemsSource is not IEnumerable<ExInControllerChoice> current || !current.SequenceEqual(controllers))
            _exInControllerComboBox.ItemsSource = controllers;
        uint? selected = _exInEngine?.Manager.SelectedControllerVE;
        _exInControllerComboBox.SelectedItem = controllers.FirstOrDefault(choice => choice.InstanceId == selected);
        _exInControllerComboBox.IsEnabled = enabled && controllers.Count > 1;
    }

    private void ExInPhoneSelectionChangedVE(object? sender, SelectionChangedEventArgs e)
    {
        if (_exInSelectionApplyingVE || _exInPhoneComboBox.SelectedItem is not ExInPhoneOption option) return;
        if (string.Equals(option.Serial, _exInManualSerialVE, StringComparison.OrdinalIgnoreCase)) return;
        _exInManualSerialVE = option.Serial;
        SetMessage(option.Serial is null
            ? "ExIn elegirá el teléfono automáticamente."
            : $"ExIn enviará el control a {option.Label}.", "#22c55e");
        _ = EnsureExInStandaloneAsync();
    }

    private void ExInControllerSelectionChangedVE(object? sender, SelectionChangedEventArgs e)
    {
        if (_exInSelectionApplyingVE || _exInEngine is null ||
            _exInControllerComboBox.SelectedItem is not ExInControllerChoice choice) return;
        if (_exInEngine.Manager.SelectControllerVE(choice.InstanceId))
            ApplyGamepadShellVE(_exInEngine.Status);
    }

    private void ExInForwardSelectedOnlyChangedVE(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_exInSelectionApplyingVE || _exInEngine is null) return;
        _exInEngine.Manager.ForwardSelectedOnlyVE = _exInForwardSelectedOnlyCheck.IsChecked == true;
    }

    private async Task ShutdownExInAsync()
    {
        _exInClosing = true;
        _exInTimer.Stop();
        _host.ExIn = null;
        try
        {
            if (_exInControlSession is not null) await _exInControlSession.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Debug.WriteLine("ExIn session shutdown: " + ex.Message); }
        try
        {
            if (_exInEngine is not null)
            {
                _exInEngine.Manager.StatusChangedVE -= ExInStatusChangedVE;
                _exInEngine.Manager.LiveSnapshotChangedVE -= ExInLiveSnapshotChangedVE;
                _exInEngine.Manager.BatteryAlertVE -= ExInBatteryAlertVE;
                await _exInEngine.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Debug.WriteLine("ExIn engine shutdown: " + ex.Message); }
    }

    // ── LINK Control (AppControl) bridge ─────────────────────────────────────────────

    /// <summary>Windows CaptureAndroidExIn + exin.* handling, called from the control servers.</summary>
    private sealed class ExInControlBridge(MainWindow owner) : ILinuxExInControl
    {
        public LinuxExInEngineState EngineState
        {
            get
            {
                ExInStatus? exIn = owner._exInEngine?.Status;
                bool enabled = owner._exInSettings.Enabled;
                string state = !enabled ? "NotDetected" : exIn?.State == ExInStates.Failed ? "Error" :
                    exIn?.ConnectedGamepads > 0 ? "Detected" : exIn?.State == ExInStates.Running ? "Active" : "NotDetected";
                string message = !enabled
                    ? "ExInEngine desactivado en PC."
                    : exIn?.Message ?? "ExInEngine todavía no está inicializado.";
                return new(state, message, !owner._exInClosing && owner._exInEngine is not null && state != "Active");
            }
        }

        public LinuxControlExIn? CaptureSnapshot()
        {
            ExInCoreEngine? engine = owner._exInEngine;
            var live = engine?.LiveSnapshot;
            var device = live?.Device;
            var state = live?.State ?? default;
            var corrected = live?.CorrectedState ?? default;
            var calibration = live?.Calibration;
            string? profileKey = device?.Identity?.ProfileKey;
            var status = engine?.Status;
            var diagnostic = profileKey is null ? null : status?.Diagnostics.FirstOrDefault(value => value.ProfileKey == profileKey);
            var battery = profileKey is null ? null : status?.Batteries.FirstOrDefault(value => value.ProfileKey == profileKey);
            var progress = live?.CalibrationProgress;
            bool calibrating = live?.Calibrating == true;
            bool transitioning = owner._exInRemoteApplyingVE || owner._exInUiApplyingVE || calibrating;
            ExInBatteryAlert? alert = owner._exInBatteryAlert;
            static int Axis(short value) => (int)Math.Round(value / 32767d * 100);
            static int Trigger(short value) => (int)Math.Round(Math.Max(0, (int)value) / 32767d * 100);
            string[] buttons = Enum.GetValues<ExInButtons>()
                .Where(button => button != 0 && state.Buttons.HasFlag(button)).Select(button => button.ToString()).ToArray();
            return new(device is not null, live?.DeviceName ?? "Sin control físico", device is null ? "---- : ----" : $"{device.VendorId:X4} : {device.ProductId:X4}",
                Axis(state.LeftX), Axis(state.LeftY), Axis(state.RightX), Axis(state.RightY), Trigger(state.LeftTrigger), Trigger(state.RightTrigger),
                buttons, calibrating, live?.Calibrated == true, live?.Deadzone ?? 0.05, live?.Message ?? "ExInEngine no está iniciado.",
                device?.Identity?.Family.ToString() ?? "Unknown", engine?.Manager.ModeVE.ToString() ?? "Game", transitioning,
                device is not null && !transitioning,
                false,
                Axis(corrected.LeftX), Axis(corrected.LeftY), Axis(corrected.RightX), Axis(corrected.RightY),
                Trigger(corrected.LeftTrigger), Trigger(corrected.RightTrigger),
                diagnostic?.Classification.ToString() ?? "Unknown", diagnostic?.CalibrationCanCorrect == true,
                diagnostic?.Explanation ?? "", battery?.State.ToString() ?? "Unknown", battery?.Percent ?? -1,
                device?.Identity?.ProfileKey ?? "", calibration?.ConnectionType ?? "Unknown",
                device is not null, device is not null,
                device?.Identity?.Family == ExInControllerFamily.DualShock4,
                device is not null, device is not null,
                calibration is null ? "" : $"Perfil {calibration.ProfileId} · {calibration.ConnectionType}",
                alert?.Kind.ToString() ?? "", Interlocked.Read(ref owner._exInBatteryAlertSequence),
                live?.SampledAtUtc is { } sampledAt ? new LinuxControlExInTelemetry(
                    live.Sequence, sampledAt, live.PollingHz, live.PcProcessingMs, live.JitterMs) : null,
                progress is null ? null : new LinuxControlExInCalibration(
                    progress.LeftStick, progress.RightStick, progress.LeftTrigger, progress.RightTrigger,
                    progress.Overall, progress.Complete,
                    LinuxExInCalibrationView.NextTargetVE(progress)));
        }

        public async Task<(bool Success, string Message)> HandleAsync(string action, string? value,
            CancellationToken cancellationToken)
        {
            // Same validation as Windows NLControlCommands, then the same actions.
            LinuxControlExIn? snapshot = CaptureSnapshot();
            switch (action)
            {
                case "exin.mode":
                    if (snapshot?.Detected != true) return (false, "ExInEngine necesita un control físico detectado.");
                    if (snapshot.Transitioning || !snapshot.CanSetMode) return (false, "ExInEngine está cambiando de estado.");
                    if (value is not ("Game" or "Ui")) return (false, "Modo de control no válido.");
                    break;
                case "exin.start":
                    if (value is not null || !EngineState.CanStart)
                        return (false, "ExInEngine no está disponible para iniciar en este estado.");
                    break;
                case "exin.synchronize":
                    if (value is not null) return (false, "La sincronización no acepta valores adicionales.");
                    if (snapshot?.Detected != true) return (false, "No hay un control físico para sincronizar.");
                    if (snapshot.Transitioning) return (false, "ExInEngine está cambiando de estado.");
                    break;
                default:
                    return (false, "Acción ExIn no reconocida.");
            }

            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                ExInCoreEngine? engine = owner._exInEngine;
                if (engine is null) return (false, "ExInEngine no está iniciado.");
                owner._exInRemoteApplyingVE = true;
                owner.ApplyGamepadShellVE(engine.Status);
                try
                {
                    switch (action)
                    {
                        case "exin.mode":
                            ExInModeResult mode = await engine.Manager.SetModeAsync(
                                Enum.Parse<ExInInputMode>(value!, ignoreCase: false), cancellationToken);
                            return (mode.Success, mode.Message);
                        case "exin.start":
                            owner.SaveExInSettingsVE(owner._exInSettings with { Enabled = true });
                            await owner.ApplyExInRuntimeStateAsync(true);
                            return (engine.Status.State != ExInStates.Failed, engine.Status.Message);
                        default:
                            await engine.Manager.ResyncConnectedDevicesVEAsync(cancellationToken);
                            return (true, "Control sincronizado con la aplicación activa.");
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    return (false, "ExInEngine: " + ex.Message);
                }
                finally
                {
                    owner._exInRemoteApplyingVE = false;
                    if (!owner._exInClosing) owner.ApplyGamepadShellVE(engine.Status);
                }
            });
        }
    }
}
