using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NOVORA.Streaming;
using AvaloniaControl = Avalonia.Controls.Control;

namespace NOVORA.Linux;

public sealed partial class MainWindow
{
    private readonly NLStreamingService _streaming = new();
    private readonly ComboBox _streamModeSelector = new();
    private readonly StackPanel _streamRtmpPanel = new() { Spacing = 8 };
    private readonly StackPanel _streamObsPanel = new() { Spacing = 8 };
    private readonly TextBox _streamRtmpUrl = StreamField("rtmp://servidor/live", "");
    private readonly TextBox _streamRtmpKey = StreamField("Clave de transmisión", "", password: true);
    private readonly TextBox _streamBitrate = StreamField("4500", "4500");
    private readonly TextBox _streamFps = StreamField("30", "30");
    private readonly TextBox _streamObsHost = StreamField("127.0.0.1", "127.0.0.1");
    private readonly TextBox _streamObsPort = StreamField("4455", "4455");
    private readonly TextBox _streamObsPassword = StreamField("Contraseña de WebSocket", "", password: true);
    private readonly TextBox _streamObsScene = StreamField("Escena de OBS (opcional)", "");
    private readonly TextBlock _streamState = new() { Text = "Detenido", FontSize = 18, FontWeight = FontWeight.Bold };
    private readonly Button _streamStartButton = new() { Content = "INICIAR TRANSMISIÓN" };
    private readonly Button _streamStopButton = new() { Content = "DETENER" };

    private readonly ComboBox _streamEncoder = new()
    {
        ItemsSource = new[]
        {
            "Automático (libx264)", "libx264 (CPU)", "NVENC (NVIDIA)", "QSV (Intel)", "AMF (AMD)"
        },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ComboBox _streamPreset = new()
    {
        ItemsSource = new[]
        {
            "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
        },
        SelectedIndex = 2,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ComboBox _streamTune = new()
    {
        ItemsSource = new[]
        {
            "zerolatency", "film", "animation", "grain", "stillimage", "fastdecode", "none"
        },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ComboBox _streamPixelFormat = new()
    {
        ItemsSource = new[] { "yuv420p", "nv12", "yuv422p" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBox _streamKeyframe = StreamField("2", "2");
    private readonly TextBox _streamAudioDevice = StreamField("default", "");
    private readonly TextBox _streamAudioBitrate = StreamField("160", "160");
    private readonly TextBox _streamVideoFilter = StreamField("scale=1280:720,fps=30", "");
    private readonly CheckBox _streamAudioEnabled = new() { Content = "Incluir audio en RTMP" };
    private readonly Expander _streamAdvanced = new() { Header = "Opciones avanzadas", IsExpanded = false };

    private readonly TextBlock _streamMonitorState = new()
    {
        Text = "Sin muestras · Detenido",
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush("#a8b7ca")
    };
    private readonly TextBlock _streamMonitorFps = MonitorValue("—");
    private readonly TextBlock _streamMonitorBitrate = MonitorValue("—");
    private readonly TextBlock _streamMonitorDuration = MonitorValue("—");
    private readonly TextBlock _streamMonitorHealth = MonitorValue("—");
    private readonly TextBlock _streamMonitorHealthLabel = Caption("VELOCIDAD ffmpeg");
    private readonly TextBlock _streamMonitorDetail = new()
    {
        Text = "Inicia la transmisión para ver métricas en vivo.",
        TextWrapping = TextWrapping.Wrap
    };

    private AvaloniaControl BuildStreamingPage()
    {
        var content = PageContent();
        AddPageHeading(content, "", "Streaming",
            "Transmite la pantalla de esta PC por RTMP o controla OBS Studio por WebSocket.");

        _streamModeSelector.ItemsSource = new[] { "RTMP", "OBS WebSocket" };
        _streamModeSelector.SelectedIndex = 0;
        _streamModeSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
        StyleSelector(_streamModeSelector);
        _streamModeSelector.SelectionChanged += (_, _) => UpdateStreamingPanels();

        _streamRtmpPanel.Children.Add(Caption("URL del servidor"));
        _streamRtmpPanel.Children.Add(_streamRtmpUrl);
        _streamRtmpPanel.Children.Add(Caption("Clave de transmisión"));
        _streamRtmpPanel.Children.Add(_streamRtmpKey);
        _streamRtmpPanel.Children.Add(Caption("Bitrate (kbps) y FPS"));
        _streamRtmpPanel.Children.Add(TwoColumns(_streamBitrate, _streamFps));

        _streamObsPanel.Children.Add(Caption("Host y puerto de OBS WebSocket"));
        _streamObsPanel.Children.Add(TwoColumns(_streamObsHost, _streamObsPort));
        _streamObsPanel.Children.Add(Caption("Contraseña de WebSocket"));
        _streamObsPanel.Children.Add(_streamObsPassword);
        _streamObsPanel.Children.Add(Caption("Escena de OBS (opcional)"));
        _streamObsPanel.Children.Add(_streamObsScene);

        var advanced = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        advanced.Children.Add(Caption("Codificador de video (RTMP)"));
        StyleSelector(_streamEncoder);
        advanced.Children.Add(_streamEncoder);
        advanced.Children.Add(TwoColumns(_streamPreset, _streamTune));
        advanced.Children.Add(Caption("Intervalo de keyframe (segundos)"));
        advanced.Children.Add(_streamKeyframe);
        advanced.Children.Add(_streamAudioEnabled);
        advanced.Children.Add(TwoColumns(_streamAudioBitrate, _streamPixelFormat));
        advanced.Children.Add(Caption("Dispositivo de audio (opcional en Linux)"));
        advanced.Children.Add(_streamAudioDevice);
        advanced.Children.Add(Caption("Filtro de video de ffmpeg (opcional)"));
        advanced.Children.Add(_streamVideoFilter);
        _streamAdvanced.Content = advanced;

        content.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                SectionTitle("Destino"),
                Caption("Elige cómo se transmite: RTMP directo con ffmpeg, o control de OBS Studio."),
                _streamModeSelector,
                _streamRtmpPanel,
                _streamObsPanel,
                _streamAdvanced
            }
        }));

        StylePrimaryButton(_streamStartButton);
        _streamStartButton.Click += (_, _) => _ = StartStreamingAsync();
        StyleFlatButton(_streamStopButton);
        _streamStopButton.Click += (_, _) => _ = StopStreamingAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(_streamStartButton);
        buttons.Children.Add(_streamStopButton);

        content.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                SectionTitle("Estado"),
                _streamState,
                buttons
            }
        }));

        var monitor = new StackPanel { Spacing = 10 };
        monitor.Children.Add(SectionTitle("Monitor de transmisión en tiempo real"));
        monitor.Children.Add(Caption("Solo datos reportados por ffmpeg u OBS. «—» = la fuente no lo entrega."));
        monitor.Children.Add(_streamMonitorState);
        monitor.Children.Add(new WrapPanel
        {
            Children =
            {
                StreamMetric("FPS", _streamMonitorFps),
                StreamMetric("BITRATE", _streamMonitorBitrate),
                StreamMetric("DURACIÓN", _streamMonitorDuration),
                StreamMetric(_streamMonitorHealthLabel, _streamMonitorHealth)
            }
        });
        monitor.Children.Add(_streamMonitorDetail);
        var refreshMonitor = new Button { Content = "ACTUALIZAR AHORA" };
        StyleFlatButton(refreshMonitor);
        refreshMonitor.Click += (_, _) => _ = RefreshStreamMonitorAsync();
        monitor.Children.Add(refreshMonitor);
        content.Children.Add(Card(monitor));

        _streaming.StatusChanged += message => Dispatcher.UIThread.Post(() =>
        {
            SetMessage("Streaming: " + message);
            UpdateStreamingState();
        });
        _streaming.MonitorChanged += sample => Dispatcher.UIThread.Post(() =>
            UpdateStreamingMonitor(sample));

        UpdateStreamingPanels();
        UpdateStreamingState();
        UpdateStreamingMonitor(_streaming.LastSample ?? IdleStreamSample(NLStreamMode.Rtmp));
        return ScrollPage(content);
    }

    private static TextBox StreamField(string watermark, string text, bool password = false) => new()
    {
        Watermark = watermark,
        Text = text,
        PasswordChar = password ? '•' : '\0',
        Foreground = Brush("#dbe5f1"),
        Background = Brush("#1b2a3e"),
        BorderBrush = Brush("#34475f"),
        MinHeight = 38
    };

    private static TextBlock MonitorValue(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush("#dbe5f1"),
        Margin = new Thickness(0, 0, 18, 8)
    };

    private static StackPanel StreamMetric(string label, TextBlock value) =>
        StreamMetric(Caption(label), value);

    private static StackPanel StreamMetric(TextBlock label, TextBlock value) => new()
    {
        Children = { label, value }
    };

    private static Grid TwoColumns(AvaloniaControl left, AvaloniaControl right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        grid.Children.Add(left);
        grid.Children.Add(right);
        Grid.SetColumn(right, 2);
        return grid;
    }

    private void UpdateStreamingPanels()
    {
        bool obs = _streamModeSelector.SelectedIndex == 1;
        _streamRtmpPanel.IsVisible = !obs;
        _streamObsPanel.IsVisible = obs;
        _streamAdvanced.IsVisible = !obs;
    }

    private void UpdateStreamingState()
    {
        _streamState.Text = _streaming.IsLive ? "En vivo" : "Detenido";
        _streamState.Foreground = Brush(_streaming.IsLive ? "#22c55e" : "#a8b7ca");
        _streamStartButton.IsEnabled = !_streaming.IsLive;
        _streamStopButton.IsEnabled = _streaming.IsLive;
    }

    private void UpdateStreamingMonitor(NLStreamMonitorSample sample)
    {
        _streamMonitorState.Text = $"{sample.ModeLabel} · {sample.StateLabel}";
        _streamMonitorState.Foreground = Brush(sample.IsActive ? "#22c55e" : "#a8b7ca");
        _streamMonitorFps.Text = sample.FpsLabel;
        _streamMonitorBitrate.Text = sample.BitrateLabel;
        _streamMonitorDuration.Text = sample.DurationLabel;
        _streamMonitorHealthLabel.Text = sample.HealthCaption;
        _streamMonitorHealth.Text = sample.HealthLabel;
        _streamMonitorDetail.Text = sample.Detail;
    }

    private static NLStreamMonitorSample IdleStreamSample(NLStreamMode mode) => new(
        DateTimeOffset.UtcNow, mode, NLStreamMonitorState.Idle,
        null, null, null, null, null, null,
        "Inicia la transmisión para ver respuestas reales.");

    private NLStreamSettings ReadStreamingSettings() => new()
    {
        Mode = _streamModeSelector.SelectedIndex == 1 ? NLStreamMode.ObsWebSocket : NLStreamMode.Rtmp,
        RtmpUrl = _streamRtmpUrl.Text?.Trim() ?? "",
        RtmpKey = _streamRtmpKey.Text?.Trim() ?? "",
        BitrateKbps = ParsePositive(_streamBitrate.Text, 4500),
        Fps = ParsePositive(_streamFps.Text, 30),
        ObsHost = _streamObsHost.Text?.Trim() is { Length: > 0 } host ? host : "127.0.0.1",
        ObsPort = ParsePositive(_streamObsPort.Text, 4455),
        ObsPassword = _streamObsPassword.Text ?? "",
        ObsScene = _streamObsScene.Text?.Trim() ?? "",
        Encoder = (_streamEncoder.SelectedItem as string) switch
        {
            "libx264 (CPU)" => "libx264",
            "NVENC (NVIDIA)" => "h264_nvenc",
            "QSV (Intel)" => "h264_qsv",
            "AMF (AMD)" => "h264_amf",
            _ => "auto"
        },
        Preset = _streamPreset.SelectedItem as string ?? "veryfast",
        Tune = _streamTune.SelectedItem as string ?? "zerolatency",
        KeyframeSeconds = ParsePositive(_streamKeyframe.Text, 2),
        AudioEnabled = _streamAudioEnabled.IsChecked == true,
        AudioBitrateKbps = ParsePositive(_streamAudioBitrate.Text, 160),
        AudioDevice = _streamAudioDevice.Text?.Trim() ?? "",
        VideoFilter = _streamVideoFilter.Text?.Trim() ?? "",
        PixelFormat = _streamPixelFormat.SelectedItem as string ?? "yuv420p",
        LowLatency = !string.Equals(_streamTune.SelectedItem as string, "none", StringComparison.OrdinalIgnoreCase)
    };

    private static int ParsePositive(string? text, int fallback) =>
        int.TryParse(text, out int value) && value > 0 ? value : fallback;

    private async Task StartStreamingAsync()
    {
        try
        {
            await _streaming.StartAsync(ReadStreamingSettings());
        }
        catch (Exception ex)
        {
            SetMessage("Streaming: " + ex.Message, "#fbbf24");
        }
        UpdateStreamingState();
    }

    private async Task StopStreamingAsync()
    {
        try
        {
            await _streaming.StopAsync(ReadStreamingSettings());
        }
        catch (Exception ex)
        {
            SetMessage("Streaming: " + ex.Message, "#fbbf24");
        }
        UpdateStreamingState();
    }

    private async Task RefreshStreamMonitorAsync()
    {
        try
        {
            await _streaming.RefreshMonitorAsync();
        }
        catch (Exception ex)
        {
            SetMessage("Monitor de streaming: " + ex.Message, "#fbbf24");
        }
    }
}
