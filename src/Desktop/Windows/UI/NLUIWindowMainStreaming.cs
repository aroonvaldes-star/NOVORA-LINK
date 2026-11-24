using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NOVORA.Streaming;

namespace NOVORA;

// Streaming page: RTMP (ffmpeg) or OBS WebSocket. Logic lives in NLStreamingService.
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
        _streaming14.MonitorChanged += sample => Dispatcher.BeginInvoke(new Action(() =>
            UpdateStreamingMonitor14(sample)));
        UpdateStreamingPanels14();
        UpdateStreamingState14();
        UpdateStreamingMonitor14(_streaming14.LastSample ?? IdleStreamSample(NLStreamMode.Rtmp));
    }

    private void StreamModeSelector14_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateStreamingPanels14();

    private void UpdateStreamingPanels14()
    {
        bool obs = StreamModeSelector14.SelectedIndex == 1;
        StreamRtmpPanel14.Visibility = obs ? Visibility.Collapsed : Visibility.Visible;
        StreamObsPanel14.Visibility = obs ? Visibility.Visible : Visibility.Collapsed;
        StreamAdvancedExpander14.Visibility = obs ? Visibility.Collapsed : Visibility.Visible;
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

    private void UpdateStreamingMonitor14(NLStreamMonitorSample sample)
    {
        StreamMonitorState14.Text = $"{sample.ModeLabel} · {sample.StateLabel}";
        StreamMonitorState14.Foreground = sample.IsActive
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xc5, 0x5e))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xa8, 0xb7, 0xca));
        StreamMonitorFps14.Text = sample.FpsLabel;
        StreamMonitorBitrate14.Text = sample.BitrateLabel;
        StreamMonitorDuration14.Text = sample.DurationLabel;
        StreamMonitorHealthLabel14.Text = sample.HealthCaption;
        StreamMonitorHealth14.Text = sample.HealthLabel;
        StreamMonitorDetail14.Text = sample.Detail;
    }

    private static NLStreamMonitorSample IdleStreamSample(NLStreamMode mode) => new(
        DateTimeOffset.UtcNow, mode, NLStreamMonitorState.Idle,
        null, null, null, null, null, null,
        "Inicia la transmisión para ver respuestas reales.");

    private NLStreamSettings ReadStreamingSettings14()
    {
        NLStreamMode mode = StreamModeSelector14.SelectedIndex == 1
            ? NLStreamMode.ObsWebSocket
            : NLStreamMode.Rtmp;
        string encoder = SelectedComboText14(StreamEncoder14) switch
        {
            "libx264 (CPU)" => "libx264",
            "NVENC (NVIDIA)" => "h264_nvenc",
            "QSV (Intel)" => "h264_qsv",
            "AMF (AMD)" => "h264_amf",
            _ => "auto"
        };
        string tune = SelectedComboText14(StreamTune14, "zerolatency");
        return new NLStreamSettings
        {
            Mode = mode,
            RtmpUrl = StreamRtmpUrl14.Text.Trim(),
            RtmpKey = StreamRtmpKey14.Password.Trim(),
            BitrateKbps = ParsePositive14(StreamBitrate14.Text, 4500),
            Fps = ParsePositive14(StreamFps14.Text, 30),
            ObsHost = string.IsNullOrWhiteSpace(StreamObsHost14.Text) ? "127.0.0.1" : StreamObsHost14.Text.Trim(),
            ObsPort = ParsePositive14(StreamObsPort14.Text, 4455),
            ObsPassword = StreamObsPassword14.Password,
            ObsScene = StreamObsScene14.Text.Trim(),
            Encoder = encoder,
            Preset = SelectedComboText14(StreamPreset14, "veryfast"),
            Tune = tune,
            KeyframeSeconds = ParsePositive14(StreamKeyframe14.Text, 2),
            AudioEnabled = StreamAudioEnabled14.IsChecked == true,
            AudioBitrateKbps = ParsePositive14(StreamAudioBitrate14.Text, 160),
            AudioDevice = StreamAudioDevice14.Text.Trim(),
            VideoFilter = StreamVideoFilter14.Text.Trim(),
            PixelFormat = SelectedComboText14(StreamPixelFormat14, "yuv420p"),
            LowLatency = !string.Equals(tune, "none", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string SelectedComboText14(ComboBox combo, string fallback = "")
    {
        if (combo.SelectedIndex < 0 || combo.SelectedIndex >= combo.Items.Count) return fallback;
        string? content = (combo.Items[combo.SelectedIndex] as ComboBoxItem)?.Content as string;
        return string.IsNullOrWhiteSpace(content) ? fallback : content.Trim();
    }

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

    private async void StreamMonitorRefresh14_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _streaming14.RefreshMonitorAsync();
        }
        catch (Exception ex)
        {
            ShowTopMessage14("Monitor de streaming: " + ex.Message, NLUIMessageKind14.Warning);
        }
    }
}
