using NOVORA.Services;
using NOVORA.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace NOVORA;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _viewModel;
    private readonly (bool Audio, string Bitrate, int Fps, int Size, string Theme) _original;
    private bool _themeInitialized;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _original = (_viewModel.AudioEnabled, _viewModel.Bitrate, _viewModel.TargetFps, _viewModel.MaxSize, _viewModel.Theme);
        DataContext = _viewModel;
        ThemeService.Apply(_viewModel.Theme);
        _themeInitialized = true;
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_themeInitialized) ThemeService.Apply(_viewModel.Theme);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        settings.AudioEnabled = _viewModel.AudioEnabled;
        settings.SelectedMonitorLabel = _viewModel.SelectedMonitor?.DisplayLabel;
        settings.SelectedMonitorDeviceName = _viewModel.SelectedMonitor?.DeviceName;
        settings.SelectedDeviceSerial = _viewModel.Device?.Serial;
        settings.Bitrate = _viewModel.Bitrate;
        settings.TargetFps = _viewModel.TargetFps;
        settings.MaxSize = _viewModel.MaxSize;
        settings.Theme = _viewModel.Theme;
        _settingsService.Save(settings);
        ThemeService.Apply(_viewModel.Theme);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AudioEnabled = _original.Audio;
        _viewModel.Bitrate = _original.Bitrate;
        _viewModel.TargetFps = _original.Fps;
        _viewModel.MaxSize = _original.Size;
        _viewModel.Theme = _original.Theme;
        ThemeService.Apply(_original.Theme);
        DialogResult = false;
        Close();
    }
}
