using NOVORA.LinkEngine.Egress;
using NOVORA.VisionEngine.Video;
using System.Windows;
using System.Windows.Controls;

namespace NOVORA;

/// <summary>Controles locales de captura, grabación y publicación en vivo de VisionEngine.</summary>
public partial class NLUIWindowMain
{
    private bool _loadingStreamingDestination14;
    private bool _performanceSession14;
    private System.Windows.Threading.DispatcherTimer? _streamingRefreshTimer14;

    // Canal único de la página: los eventos de grabación y Live se agrupan a 1 Hz como máximo.
    private void QueueStreamingRefresh14()
    {
        if (_closing || StreamingPage14?.Visibility != Visibility.Visible) return;
        if (_streamingRefreshTimer14 is null)
        {
            _streamingRefreshTimer14 = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _streamingRefreshTimer14.Tick += (_, _) =>
            {
                _streamingRefreshTimer14!.Stop();
                RefreshStreamingPage14();
            };
        }
        if (!_streamingRefreshTimer14.IsEnabled) _streamingRefreshTimer14.Start();
    }

    private void UpdateHomeGuide14()
    {
        if (HomeNextStep14 is null) return;
        bool phone = _viewModel.Device.Connected || _androidLanControl?.IsAuthorized == true;
        HomeNextStep14.Text =
            !phone ? "Paso 1 de 5 · Conecta el teléfono por USB o Wi-Fi." :
            _pcVideoAuthorizationRequested ? "Paso 2 de 5 · Autoriza la captura de pantalla en el teléfono." :
            !IsVisionEngineRunningVE() ? "Paso 3 de 5 · Abre VisionEngine con VER PANTALLA." :
            _linkEngineRuntimeLE?.IsRunningLE != true && _androidLanDataGateway is null
                ? "Paso 4 de 5 · Abre Streaming para capturar, grabar o transmitir. LinkEngine (paso 5) solo si necesitas Internet o datos."
                : "Sesión lista · VisionEngine y LinkEngine están activos.";
    }

    private async void HomePerformanceSession_Changed14(object sender, RoutedEventArgs e)
    {
        _performanceSession14 = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        if (_shellInitialized14)
        {
            var settings = _settingsService.Load();
            settings.PerformanceSessionEnabled = _performanceSession14;
            _settingsService.Save(settings);
        }
        if (!_performanceSession14) return;
        VisionTelemetryExpander14.IsExpanded = false;
        await DisableExperimentalNetworkForBaselineAsync();
    }

    private void RefreshStreamingPage14(bool loadDestination = false)
    {
        UpdateHomeGuide14();
        if (StreamingPage14 is null || !loadDestination && StreamingPage14.Visibility != Visibility.Visible) return;
        var runtime = _visionEngineVE?.RuntimeVE;
        var recorder = runtime?.RecordingVE;
        var live = runtime?.LiveVE;
        VEMediaStatus? recording = recorder?.StatusVE;
        LEEgressStatus? liveStatus = live?.StatusLE;

        StreamingVisionStatus14.Text = IsVisionEngineRunningVE()
            ? "VisionEngine está activo. Puedes capturar, grabar o transmitir según las capacidades disponibles."
            : "Inicia VisionEngine y autoriza la captura de pantalla en el teléfono para habilitar estas acciones.";
        StreamingRecordingStatus14.Text = (recording?.Error is { } recordingError ? recordingError + " Comprueba que VisionEngine siga activo con H.264 y vuelve a iniciar." : null) ??
            (recording?.Starting == true ? "Preparando grabación: esperando la primera imagen…" :
             recording?.Recording == true ? recording.AudioIncluded ? "Grabación activa · video y audio interno del teléfono." : "Grabación activa · esperando audio interno del teléfono." :
             recording?.LastFile is { } file ? "Último archivo: " + file : "La grabación estará disponible con video H.264 y audio interno.");
        StreamingCaptureButton14.IsEnabled = !_closing && recorder?.CanCapture == true;
        StreamingRecordButton14.IsEnabled = !_closing &&
            (recording is { Recording: true } or { Starting: true } || recorder?.CanRecord == true);
        StreamingRecordButton14.Content = recording?.Starting == true ? "CANCELAR GRABACIÓN" : recording?.Recording == true ? "DETENER Y GUARDAR" : "INICIAR GRABACIÓN";
        StreamingLiveStatus14.Text = liveStatus is null ? "Inicia VisionEngine para habilitar la transmisión." : DescribeStreamingLive14(liveStatus);
        StreamingLiveButton14.IsEnabled = !_closing &&
            (liveStatus?.Active == true || live?.CanStartLE == true && LEEgressTargetStore.Load()?.IsComplete == true);
        StreamingLiveButton14.Content = liveStatus?.Active == true ? "DETENER TRANSMISIÓN" : "INICIAR TRANSMISIÓN";

        if (loadDestination) LoadStreamingDestination14();
    }

    private static string DescribeStreamingLive14(LEEgressStatus status) => status.State switch
    {
        LEEgressStates.Connecting => "Conectando con el servidor RTMP(S)…",
        LEEgressStates.Live => "EN VIVO" + (status.AudioIncluded ? " · con audio interno" : " · sin audio interno") +
            (status.DroppedVideo > 0 ? $" · {status.DroppedVideo} cuadros descartados" : string.Empty),
        LEEgressStates.Reconnecting => $"Reconectando ({status.Reconnects}): {status.Error}",
        LEEgressStates.Paused => "Pausada por privacidad.",
        LEEgressStates.Error => "Transmisión detenida: " + status.Error + " Usa PROBAR CONEXIÓN para revisar servidor, clave y red.",
        _ => "Configura un destino RTMP(S) para transmitir."
    };

    private void LoadStreamingDestination14()
    {
        _loadingStreamingDestination14 = true;
        try
        {
            LEEgressTarget? target = LEEgressTargetStore.Load();
            StreamingServerBox14.Text = target?.ServerUrl ?? string.Empty;
            StreamingKeyBox14.Password = target?.StreamKey ?? string.Empty;
            int index = StreamingPlatformCombo14.Items.Cast<ComboBoxItem>().ToList()
                .FindIndex(item => string.Equals(item.Tag as string, StreamingServerBox14.Text, StringComparison.Ordinal));
            StreamingPlatformCombo14.SelectedIndex = index >= 0 ? index : StreamingPlatformCombo14.Items.Count - 1;
            StreamingDestinationStatus14.Text = target?.IsComplete == true
                ? "Destino configurado y protegido con Windows DPAPI."
                : "Aún no hay un destino configurado.";
        }
        finally { _loadingStreamingDestination14 = false; }
    }

    private LEEgressTarget? ReadStreamingDestination14()
    {
        var target = new LEEgressTarget(StreamingServerBox14.Text.Trim(), StreamingKeyBox14.Password.Trim());
        if (target.ServerUrl.Length == 0 && target.StreamKey.Length == 0) return null;
        if (!target.IsComplete) throw new InvalidOperationException("Indica servidor y clave de transmisión, o deja ambos vacíos.");
        _ = target.ServerUri;
        return target;
    }

    private void StreamingPlatform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingStreamingDestination14 && StreamingPlatformCombo14.SelectedItem is ComboBoxItem { Tag: string url } && url.Length > 0)
            StreamingServerBox14.Text = url;
    }

    private void StreamingSaveDestination_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ReadStreamingDestination14() is { } target)
            {
                LEEgressTargetStore.Save(target);
                StreamingDestinationStatus14.Text = "Destino guardado y protegido con Windows DPAPI.";
            }
            else
            {
                LEEgressTargetStore.Clear();
                StreamingDestinationStatus14.Text = "Destino eliminado de este equipo.";
            }
            RefreshStreamingPage14();
        }
        catch (Exception ex) { StreamingDestinationStatus14.Text = "No se pudo guardar: " + ex.Message; }
    }

    private async void StreamingTestConnection_Click(object sender, RoutedEventArgs e)
    {
        StreamingTestButton14.IsEnabled = false;
        StreamingDestinationStatus14.Text = "Probando conexión…";
        try
        {
            var target = ReadStreamingDestination14() ?? throw new InvalidOperationException("Indica servidor y clave de transmisión.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await LEEgressLiveProbe.ProbeAsync(target, deadline.Token);
            StreamingDestinationStatus14.Text = "Conexión correcta: el servidor aceptó la publicación.";
        }
        catch (Exception ex) { StreamingDestinationStatus14.Text = "No se pudo conectar: " + ex.Message; }
        finally { StreamingTestButton14.IsEnabled = true; }
    }

    private async void StreamingCapture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var recorder = _visionEngineVE?.RuntimeVE.RecordingVE ?? throw new InvalidOperationException("VisionEngine no está disponible.");
            StreamingRecordingStatus14.Text = "Capturando imagen…";
            StreamingRecordingStatus14.Text = "Captura guardada: " + await recorder.CaptureAsync();
        }
        catch (Exception ex) { StreamingRecordingStatus14.Text = "No se pudo capturar: " + ex.Message; }
        finally { RefreshStreamingPage14(); }
    }

    private async void StreamingRecord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var recorder = _visionEngineVE?.RuntimeVE.RecordingVE ?? throw new InvalidOperationException("VisionEngine no está disponible.");
            if (recorder.StatusVE.Recording || recorder.StatusVE.Starting)
                StreamingRecordingStatus14.Text = "Grabación guardada: " + await recorder.StopAsync();
            else
            {
                recorder.Start();
                StreamingRecordingStatus14.Text = "Preparando grabación: esperando la primera imagen…";
            }
        }
        catch (Exception ex) { StreamingRecordingStatus14.Text = "No se pudo cambiar la grabación: " + ex.Message; }
        finally { RefreshStreamingPage14(); }
    }

    private async void StreamingLive_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var live = _visionEngineVE?.RuntimeVE.LiveVE ?? throw new InvalidOperationException("VisionEngine no está disponible.");
            if (live.StatusLE.Active) await live.StopAsync();
            else live.Start(LEEgressTargetStore.Load() ?? throw new InvalidOperationException("Guarda un destino RTMP(S) antes de iniciar."));
        }
        catch (Exception ex) { StreamingLiveStatus14.Text = "No se pudo cambiar la transmisión: " + ex.Message; }
        finally { RefreshStreamingPage14(); }
    }
}
