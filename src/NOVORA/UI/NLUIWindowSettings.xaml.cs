using NOVORA.Model;
using NOVORA.Service;
using NOVORA.ViewModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace NOVORA;

public partial class NLUIWindowSettings : Window
{
    private readonly NLServiceSettings _settingsService =
        new();

    private readonly NLViewModelMain _viewModel;

    private readonly NLServiceNovoraPaths _paths =
        new();

    private readonly (
        bool Audio,
        string AudioOutput,
        string Bitrate,
        int Fps,
        int Size,
        string Theme,
        string PresentationMode,
        NLModelMonitorInfo? Monitor
    ) _original;

    private bool _themeInitialized;
    private bool _committed;
    private bool _rollingBack;
    private readonly NLServiceNovoraSettings _originalAdvancedNV = new();

    public NLUIWindowSettings(
        NLViewModelMain viewModel)
    {
        InitializeComponent();

        _viewModel =
            viewModel
            ?? throw new ArgumentNullException(
                nameof(viewModel));

        _original =
        (
            _viewModel.AudioEnabled,
            _viewModel.SelectedAudioOutput,
            _viewModel.Bitrate,
            _viewModel.TargetFps,
            _viewModel.MaxSize,
            _viewModel.Theme,
            _viewModel.VideoPresentationMode,
            _viewModel.SelectedMonitor
        );

        NLServiceSettingsAdvanced.CaptureNV(_viewModel, _originalAdvancedNV);

        DataContext =
            _viewModel;

        _viewModel.RefreshOutputCapabilityOptions();

        _viewModel.RefreshAudioOutputOptions(
            _paths);

        NLServiceTheme.Apply(
            _viewModel.Theme);

        _themeInitialized =
            true;

        NLServiceNovoraSettings startupSettingsNV =
            _settingsService.Load();

        StartWithWindowsCheckBox.IsChecked =
            startupSettingsNV.StartWithWindows;
    }

    private void Theme_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_themeInitialized &&
            !_rollingBack)
        {
            NLServiceTheme.Apply(
                _viewModel.Theme);
        }
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings =
                _settingsService.Load();

            settings.AudioEnabled =
                _viewModel.AudioEnabled;

            settings.SelectedAudioOutput =
                _viewModel.SelectedAudioOutput;

            settings.VideoPresentationMode =
                _viewModel.VideoPresentationMode;

            settings.SelectedMonitorLabel =
                _viewModel.SelectedMonitor?.DisplayLabel;

            settings.SelectedMonitorDeviceName =
                _viewModel.SelectedMonitor?.DeviceName;

            settings.SelectedDeviceSerial =
                _viewModel.Device?.Serial;

            settings.Bitrate =
                _viewModel.Bitrate;

            settings.TargetFps =
                _viewModel.TargetFps;

            settings.MaxSize =
                _viewModel.MaxSize;

            settings.Theme =
                _viewModel.Theme;

            settings.StartWithWindows =
                StartWithWindowsCheckBox.IsChecked != false;

            if (!settings.StartWithWindows)
            {
                settings.AutoStartSuppressed =
                    false;
            }

            NLServiceSettingsAdvanced.CaptureNV(_viewModel, settings);
            _settingsService.Save(
                settings);

            NLServiceAutoStart.ApplyRegistrationNV(
                settings.StartWithWindows);

            NLServiceTheme.Apply(
                _viewModel.Theme);

            _committed =
                true;

            DialogResult =
                true;

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA — Configuración",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        RollbackLiveSettings();

        _committed =
            true;

        DialogResult =
            false;

        Close();
    }

    private void SettingsWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_committed)
        {
            return;
        }

        RollbackLiveSettings();

        _committed =
            true;
    }

    private void RollbackLiveSettings()
    {
        if (_rollingBack)
        {
            return;
        }

        _rollingBack =
            true;

        try
        {
            NLServiceSettingsAdvanced.ApplyNV(_originalAdvancedNV, _viewModel);
            _viewModel.AudioEnabled =
                _original.Audio;

            _viewModel.SelectedAudioOutput =
                _original.AudioOutput;

            _viewModel.Bitrate =
                _original.Bitrate;

            _viewModel.TargetFps =
                _original.Fps;

            _viewModel.MaxSize =
                _original.Size;

            _viewModel.Theme =
                _original.Theme;

            _viewModel.VideoPresentationMode =
                _original.PresentationMode;

            _viewModel.SelectedMonitor =
                _original.Monitor;

            NLServiceTheme.Apply(
                _original.Theme);
        }
        finally
        {
            _rollingBack =
                false;
        }
    }
}
