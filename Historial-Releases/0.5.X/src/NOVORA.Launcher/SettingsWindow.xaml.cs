using System.Windows;
using NOVORA.Launcher.Services;
using NOVORA.Launcher.ViewModels;

namespace NOVORA.Launcher;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly MainViewModel _viewModel;
    private readonly bool _originalAudioEnabled;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _originalAudioEnabled = viewModel.AudioEnabled;
        DataContext = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Save(new NovoraSettings
        {
            AudioEnabled = _viewModel.AudioEnabled,
            SelectedMonitorLabel = _viewModel.SelectedMonitor?.DisplayLabel
        });

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AudioEnabled = _originalAudioEnabled;
        DialogResult = false;
        Close();
    }
}
