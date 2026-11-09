using NOVORA.LinkEngine.Egress;
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

        LoadLiveTarget();
    }

    private void LoadLiveTarget()
    {
        var target = LEEgressTargetStore.Load();
        LiveServerBox.Text = target?.ServerUrl ?? "";
        LiveKeyBox.Password = target?.StreamKey ?? "";
        int index = LivePlatformCombo.Items.Cast<ComboBoxItem>()
            .ToList().FindIndex(i => (string)i.Tag == LiveServerBox.Text);
        LivePlatformCombo.SelectedIndex = index >= 0 ? index : LivePlatformCombo.Items.Count - 1;
    }

    private LEEgressTarget? ReadLiveTarget()
    {
        var target = new LEEgressTarget(LiveServerBox.Text.Trim(), LiveKeyBox.Password.Trim());
        if (target.ServerUrl.Length == 0 && target.StreamKey.Length == 0) return null;
        if (!target.IsComplete) throw new InvalidOperationException("Para transmitir en vivo indica servidor y clave, o deja ambos vacíos.");
        _ = target.ServerUri;
        return target;
    }

    private void LivePlatform_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (LivePlatformCombo.SelectedItem is ComboBoxItem { Tag: string url } && url.Length > 0)
            LiveServerBox.Text = url;
    }

    private async void LiveTest_Click(
        object sender,
        RoutedEventArgs e)
    {
        LiveTestButton.IsEnabled = false;
        LiveTestStatus.Text = "Probando conexión…";
        try
        {
            var target = ReadLiveTarget() ?? throw new InvalidOperationException("Indica servidor y clave.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await LEEgressLiveProbe.ProbeAsync(target, deadline.Token);
            LiveTestStatus.Text = "Conexión correcta: el servidor aceptó la publicación.";
        }
        catch (Exception ex)
        {
            LiveTestStatus.Text = "No se pudo conectar: " + ex.Message;
        }
        finally
        {
            LiveTestButton.IsEnabled = true;
        }
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

            NLServiceSettingsAdvanced.CaptureNV(_viewModel, settings);
            if (ReadLiveTarget() is { } liveTarget) LEEgressTargetStore.Save(liveTarget);
            else LEEgressTargetStore.Clear();
            _settingsService.Save(
                settings);

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
