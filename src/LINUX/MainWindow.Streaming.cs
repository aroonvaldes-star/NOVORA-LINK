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
    private readonly TextBlock _streamState = new() { Text = "Detenido", FontSize = 18, FontWeight = FontWeight.Bold };
    private readonly Button _streamStartButton = new() { Content = "INICIAR TRANSMISIÓN" };
    private readonly Button _streamStopButton = new() { Content = "DETENER" };

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
        _streamRtmpPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            Children = { _streamBitrate, _streamFps }
        });
        Grid.SetColumn(_streamFps, 2);

        _streamObsPanel.Children.Add(Caption("Host y puerto de OBS WebSocket"));
        _streamObsPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            Children = { _streamObsHost, _streamObsPort }
        });
        Grid.SetColumn(_streamObsPort, 2);
        _streamObsPanel.Children.Add(Caption("Contraseña de WebSocket"));
        _streamObsPanel.Children.Add(_streamObsPassword);

        content.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                SectionTitle("Destino"),
                Caption("Elige cómo se transmite: RTMP directo con ffmpeg, o control de OBS Studio."),
                _streamModeSelector,
                _streamRtmpPanel,
                _streamObsPanel
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

        _streaming.StatusChanged += message => Dispatcher.UIThread.Post(() =>
        {
            SetMessage("Streaming: " + message);
            UpdateStreamingState();
        });

        UpdateStreamingPanels();
        UpdateStreamingState();
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

    private void UpdateStreamingPanels()
    {
        bool obs = _streamModeSelector.SelectedIndex == 1;
        _streamRtmpPanel.IsVisible = !obs;
        _streamObsPanel.IsVisible = obs;
    }

    private void UpdateStreamingState()
    {
        _streamState.Text = _streaming.IsLive ? "En vivo" : "Detenido";
        _streamState.Foreground = Brush(_streaming.IsLive ? "#22c55e" : "#a8b7ca");
        _streamStartButton.IsEnabled = !_streaming.IsLive;
        _streamStopButton.IsEnabled = _streaming.IsLive;
    }

    private NLStreamSettings ReadStreamingSettings() => new()
    {
        Mode = _streamModeSelector.SelectedIndex == 1 ? NLStreamMode.ObsWebSocket : NLStreamMode.Rtmp,
        RtmpUrl = _streamRtmpUrl.Text?.Trim() ?? "",
        RtmpKey = _streamRtmpKey.Text?.Trim() ?? "",
        BitrateKbps = ParsePositive(_streamBitrate.Text, 4500),
        Fps = ParsePositive(_streamFps.Text, 30),
        ObsHost = _streamObsHost.Text?.Trim() is { Length: > 0 } host ? host : "127.0.0.1",
        ObsPort = ParsePositive(_streamObsPort.Text, 4455),
        ObsPassword = _streamObsPassword.Text ?? ""
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
}
