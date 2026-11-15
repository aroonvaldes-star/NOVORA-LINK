using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NOVORA.Streaming;

namespace NOVORA;

// Streaming page: RTMP (ffmpeg) or OBS WebSocket. Logic lives in NLStreamingService, shared with Linux.
public partial class NLUIWindowMain
{
    private readonly NLStreamingService _streaming14 = new();

    private void InitializeStreaming14()
    {
        _streaming14.StatusChanged += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowTopMessage14("Streaming: " + message);
            UpdateStreamingState14();
        }));
        UpdateStreamingPanels14();
        UpdateStreamingState14();
    }

    private void StreamModeSelector14_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateStreamingPanels14();

    private void UpdateStreamingPanels14()
    {
        bool obs = StreamModeSelector14.SelectedIndex == 1;
        StreamRtmpPanel14.Visibility = obs ? Visibility.Collapsed : Visibility.Visible;
        StreamObsPanel14.Visibility = obs ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStreamingState14()
    {
        bool live = _streaming14.IsLive;
        StreamStateText14.Text = live ? "En vivo" : "Detenido";
        StreamStateText14.Foreground = live
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xc5, 0x5e))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xa8, 0xb7, 0xca));
        StreamStartButton14.IsEnabled = !live;
        StreamStopButton14.IsEnabled = live;
    }

    private NLStreamSettings ReadStreamingSettings14() => new()
    {
        Mode = StreamModeSelector14.SelectedIndex == 1 ? NLStreamMode.ObsWebSocket : NLStreamMode.Rtmp,
        RtmpUrl = StreamRtmpUrl14.Text.Trim(),
        RtmpKey = StreamRtmpKey14.Password.Trim(),
        BitrateKbps = ParsePositive14(StreamBitrate14.Text, 4500),
        Fps = ParsePositive14(StreamFps14.Text, 30),
        ObsHost = string.IsNullOrWhiteSpace(StreamObsHost14.Text) ? "127.0.0.1" : StreamObsHost14.Text.Trim(),
        ObsPort = ParsePositive14(StreamObsPort14.Text, 4455),
        ObsPassword = StreamObsPassword14.Password
    };

    private static int ParsePositive14(string? text, int fallback) =>
        int.TryParse(text, out int value) && value > 0 ? value : fallback;

    private async void StreamStartButton14_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _streaming14.StartAsync(ReadStreamingSettings14());
        }
        catch (Exception ex)
        {
            ShowTopMessage14("Streaming: " + ex.Message, NLUIMessageKind14.Warning);
        }
        UpdateStreamingState14();
    }

    private async void StreamStopButton14_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _streaming14.StopAsync(ReadStreamingSettings14());
        }
        catch (Exception ex)
        {
            ShowTopMessage14("Streaming: " + ex.Message, NLUIMessageKind14.Warning);
        }
        UpdateStreamingState14();
    }
}
