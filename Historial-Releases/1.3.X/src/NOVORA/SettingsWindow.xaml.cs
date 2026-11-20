using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using NOVORA.Services;
using NOVORA.ViewModels;
using NOVORA.Widgets;

namespace NOVORA;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _viewModel;
    private readonly WidgetManager _widgetManager;
    private readonly WidgetRegistry _widgetRegistry;

    private readonly (
        bool Audio,
        string Bitrate,
        int Fps,
        int Size,
        bool Console,
        bool Video,
        bool Stats,
        bool Ref,
        bool Red,
        bool Performance,
        bool Capture,
        bool Files,
        bool Applications,
        string Theme) _original;

    private bool _themeInitialized;

    private readonly List<WidgetTypeOption> _widgetTypes = new();

    public SettingsWindow(
        MainViewModel viewModel,
        WidgetManager widgetManager,
        WidgetRegistry widgetRegistry)
    {
        InitializeComponent();

        _settingsService = new SettingsService();

        _viewModel = viewModel ??
            throw new ArgumentNullException(nameof(viewModel));

        _widgetManager = widgetManager ??
            throw new ArgumentNullException(nameof(widgetManager));

        _widgetRegistry = widgetRegistry ??
            throw new ArgumentNullException(nameof(widgetRegistry));

        _original = (
            _viewModel.AudioEnabled,
            _viewModel.Bitrate,
            _viewModel.TargetFps,
            _viewModel.MaxSize,
            _viewModel.ShowConsoleWidget,
            _viewModel.ShowVideoWidget,
            _viewModel.ShowStatisticsWidget,
            _viewModel.ShowRefWidget,
            _viewModel.ShowRedWidget,
            _viewModel.ShowPerformanceWidget,
            _viewModel.ShowCaptureWidget,
            _viewModel.ShowFilesWidget,
            _viewModel.ShowApplicationsWidget,
            _viewModel.Theme);

        DataContext = _viewModel;

        ThemeService.Apply(
            _viewModel.Theme);

        _themeInitialized = true;

        // ========================================================
        // WIDGETS ACTIVOS
        // ========================================================

        ActiveWidgetsList.ItemsSource =
            _widgetManager.Widgets;

        // ========================================================
        // TIPOS DISPONIBLES
        // ========================================================

        _widgetTypes.Clear();

        foreach (var type in
                 _widgetRegistry.GetAvailableTypes())
        {
            _widgetTypes.Add(
                new WidgetTypeOption(
                    type,
                    GetDisplayName(type)));
        }

        WidgetTypeCombo.ItemsSource =
            _widgetTypes;

        if (_widgetTypes.Count > 0)
        {
            WidgetTypeCombo.SelectedIndex = 0;
        }
    }

    // ============================================================
    // TEMA
    // ============================================================

    private void Theme_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_themeInitialized)
            return;

        ThemeService.Apply(
            _viewModel.Theme);
    }

    // ============================================================
    // AGREGAR WIDGET
    // ============================================================

    private void AddWidget_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WidgetTypeCombo.SelectedItem
            is not WidgetTypeOption option)
        {
            return;
        }

        try
        {
            _widgetManager.AddWidget(
                option.Type);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "NOVORA — Widget",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ============================================================
    // ELIMINAR WIDGET
    // ============================================================

    private void RemoveWidget_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ActiveWidgetsList.SelectedItem
            is not WidgetBase widget)
        {
            return;
        }

        try
        {
            _widgetManager.RemoveWidget(
                widget);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "NOVORA — Widget",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ============================================================
    // NOMBRE DEL WIDGET
    // ============================================================

    private static string GetDisplayName(
        string type)
    {
        return type switch
        {
            "red" =>
                "Red",

            "transmission" =>
                "Transmisión",

            "console" =>
                "Consola",

            "performance" =>
                "Rendimiento",

            "video" =>
                "Vídeo",

            "statistics" =>
                "Estadísticas",

            "capture" =>
                "Captura",

            "files" =>
                "Archivos",

            "applications" =>
                "Aplicaciones",

            "ref" =>
                "Referencia",

            _ =>
                type
        };
    }

    // ============================================================
    // GUARDAR
    // ============================================================

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ThemeService.Apply(
                _viewModel.Theme);

            var settings =
                _settingsService.Load();

            settings.AudioEnabled =
                _viewModel.AudioEnabled;

            settings.SelectedMonitorLabel =
                _viewModel.SelectedMonitor?
                    .DisplayLabel;

            settings.SelectedDeviceSerial =
                _viewModel.Device?
                    .Serial;

            settings.Bitrate =
                _viewModel.Bitrate;

            settings.TargetFps =
                _viewModel.TargetFps;

            settings.MaxSize =
                _viewModel.MaxSize;

            settings.ShowConsoleWidget =
                _viewModel.ShowConsoleWidget;

            settings.ShowVideoWidget =
                _viewModel.ShowVideoWidget;

            settings.ShowStatisticsWidget =
                _viewModel.ShowStatisticsWidget;

            settings.ShowRefWidget =
                _viewModel.ShowRefWidget;

            settings.ShowRedWidget =
                _viewModel.ShowRedWidget;

            settings.ShowPerformanceWidget =
                _viewModel.ShowPerformanceWidget;

            settings.ShowCaptureWidget =
                _viewModel.ShowCaptureWidget;

            settings.ShowFilesWidget =
                _viewModel.ShowFilesWidget;

            settings.ShowApplicationsWidget =
                _viewModel.ShowApplicationsWidget;

            settings.Theme =
                _viewModel.Theme;

            // Guardar widgets modulares.
            _widgetManager.SaveAll();

            // Guardar configuración general.
            _settingsService.Save(
                settings);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "NOVORA — Configuración",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ============================================================
    // CANCELAR
    // ============================================================

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _viewModel.AudioEnabled =
                _original.Audio;

            _viewModel.Bitrate =
                _original.Bitrate;

            _viewModel.TargetFps =
                _original.Fps;

            _viewModel.MaxSize =
                _original.Size;

            _viewModel.ShowConsoleWidget =
                _original.Console;

            _viewModel.ShowVideoWidget =
                _original.Video;

            _viewModel.ShowStatisticsWidget =
                _original.Stats;

            _viewModel.ShowRefWidget =
                _original.Ref;

            _viewModel.ShowRedWidget =
                _original.Red;

            _viewModel.ShowPerformanceWidget =
                _original.Performance;

            _viewModel.ShowCaptureWidget =
                _original.Capture;

            _viewModel.ShowFilesWidget =
                _original.Files;

            _viewModel.ShowApplicationsWidget =
                _original.Applications;

            _viewModel.Theme =
                _original.Theme;

            ThemeService.Apply(
                _original.Theme);

            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "NOVORA — Configuración",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

// ================================================================
// OPCIÓN DEL COMBOBOX
//
// IMPORTANTE:
// Esta clase debe existir UNA SOLA VEZ en todo el proyecto.
// ================================================================

public sealed class WidgetTypeOption
{
    public string Type { get; }

    public string DisplayName { get; }

    public WidgetTypeOption(
        string type,
        string displayName)
    {
        Type = type;
        DisplayName = displayName;
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
