using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using NOVORA.Linux.VisionEngine;
using AvaloniaControl = Avalonia.Controls.Control;

namespace NOVORA.Linux;

public sealed partial class MainWindow
{
    private bool _veLoading;
    private bool _veBuilt;
    private readonly ComboBox _veAcceleration = new() { ItemsSource = new[]{"Automática", "Software", "Forzada"} };
    private readonly ComboBox _veBackend = new() { ItemsSource = new[]{"vaapi","cuda","qsv","vulkan"} };
    private readonly ComboBox _veMonitor = new();
    private readonly ComboBox _veAudio = new();
    private readonly CheckBox _veFullscreen = new() { Content="Pantalla completa en el monitor seleccionado" };
    private readonly CheckBox _veAudioEnabled = new() { Content="Reproducir y capturar audio Android" };
    private readonly CheckBox _veInput = new() { Content="Teclado, ratón y touchpad de la laptop" };
    private readonly CheckBox _veLatency = new() { Content="Decoder de baja latencia" };
    private readonly CheckBox _veVsync = new() { Content="Sincronizar presentación con el monitor (VSync)" };
    private readonly TextBlock _veDiagnostic = new() { TextWrapping=Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _veRecording = new() { TextWrapping=Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _veFiles = new() { TextWrapping=Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _veDirectories = new() { TextWrapping=Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _veStart = new() { Content="Solicitar pantalla a Android" };
    private readonly Button _veRecord = new() { Content="Iniciar grabación MKV" };
    private readonly Button _veRecordStop = new() { Content="Finalizar grabación" };
    private readonly Button _veSend = new() { Content="Enviar archivos" };
    private readonly Button _veSendFolder = new() { Content="Enviar carpeta" };
    private readonly Button _veCancelSend = new() { Content="Cancelar envío" };
    private CancellationTokenSource? _veTransfer;
    private LinuxVisionSettings? _veDisplayed;

    private AvaloniaControl BuildFunctionalVisionPage()
    {
        _veBuilt=true; _veLoading=true;
        var content=PageContent();
        AddPageHeading(content,"VISIONENGINE","VisionEngine · Pantalla y audio","USB y LAN · autoriza la captura en LINK Control. Los cambios de captura y salida se aplican a la siguiente sesión.");
        var session=new StackPanel { Spacing=8 };
        session.Children.Add(_visionState); session.Children.Add(_visionMessage); session.Children.Add(_visionMetrics);
        var actions=new WrapPanel { Orientation=Orientation.Horizontal };
        foreach (var button in new[]{_veStart,_visionStopButton}) { StyleFlatButton(button); actions.Children.Add(button); }
        _veStart.Click += (_,_) => VeAction(()=> { if (!_host.IsAuthorized) throw new InvalidOperationException("Enlaza el teléfono en Configuración primero."); _host.RequestVision(); });
        _visionStopButton.Click += async (_,_)=>await StopVisionAsync();
        session.Children.Add(actions); content.Children.Add(Card(session));
        _visionProfileSelector.ItemsSource=VisionProfiles;
        _visionResolutionSelector.ItemsSource=CaptureResolutions;
        _visionFpsSelector.ItemsSource=CaptureFrameRates;
        _visionBitrateSelector.ItemsSource=CaptureBitrates;
        content.Children.Add(VeCard("Perfil y calidad",("Perfil",_visionProfileSelector),("Resolución máxima",_visionResolutionSelector),("FPS",_visionFpsSelector),("Bitrate",_visionBitrateSelector)));
        content.Children.Add(VeCard("Aceleración del decoder",("Modo",_veAcceleration),("Backend forzado",_veBackend)));
        var probe=FlatButton("Comprobar aceleración real");
        probe.Click += async (_,_)=>
        {
            probe.IsEnabled=false; _veDiagnostic.Text="Probando decodificación hardware y descarga de frames…";
            try { var backends=await Task.Run(()=>LinuxVisionAcceleration.ProbeAsync(CancellationToken.None)); _veDiagnostic.Text=string.Join("\n",backends); }
            catch (Exception ex) { _veDiagnostic.Text=ex.Message; }
            finally { probe.IsEnabled=true; }
        };
        content.Children.Add(Card(new StackPanel { Spacing=8, Children={probe,_veDiagnostic} }));
        var outputs=new StackPanel { Spacing=8 };
        outputs.Children.Add(VeFields(("Monitor",_veMonitor),("Salida de audio",_veAudio)));
        var refresh=FlatButton("Actualizar monitores y audio"); refresh.Click += (_,_)=>RefreshVeOutputs(); outputs.Children.Add(refresh);
        foreach (var check in new[]{_veFullscreen,_veAudioEnabled,_veInput,_veLatency,_veVsync}) outputs.Children.Add(check);
        outputs.Children.Add(Caption("El touchpad admite clic/tap, arrastre y desplazamiento con dos dedos. Android requiere el servicio de Accesibilidad de LINK Control; la entrada se envía solo con la ventana de video enfocada."));
        content.Children.Add(Card(outputs));
        var recording=new StackPanel { Spacing=8 };
        recording.Children.Add(SectionTitle("Grabación y archivos"));
        var mediaButtons=new WrapPanel();
        foreach (var button in new[]{_veRecord,_veRecordStop,_veSend,_veSendFolder,_veCancelSend}) { StyleFlatButton(button); mediaButtons.Children.Add(button); }
        _veRecord.Click += (_,_)=>VeAction(_host.StartVisionRecording);
        _veRecordStop.Click += async (_,_)=> { try { if (_host.VisionRuntime is { } runtime) await runtime.StopRecordingAsync(); } catch (Exception ex) { _veRecording.Text=ex.Message; } };
        _veSend.Click += async (_,_)=>await SendVisionSelectionAsync(false);
        _veSendFolder.Click += async (_,_)=>await SendVisionSelectionAsync(true);
        _veCancelSend.Click += (_,_)=>_veTransfer?.Cancel();
        recording.Children.Add(mediaButtons); recording.Children.Add(_veRecording); recording.Children.Add(_veFiles);
        var directories=new WrapPanel();
        var captureDir=FlatButton("Carpeta de grabaciones"); var receiveDir=FlatButton("Carpeta de recibidos");
        captureDir.Click += async (_,_)=>await ChooseVisionDirectoryAsync(true);
        receiveDir.Click += async (_,_)=>await ChooseVisionDirectoryAsync(false);
        directories.Children.Add(captureDir); directories.Children.Add(receiveDir);
        recording.Children.Add(directories); recording.Children.Add(_veDirectories);
        recording.Children.Add(Caption("MKV conserva el video H.264 y audio disponible. Al rotar se crea otro segmento. Los archivos usan un canal FILES V2 autenticado durante VE; no requieren ADB por LAN."));
        content.Children.Add(Card(recording));
        content.Children.Add(_visionSettingsStatus);
        foreach (var combo in new[]{_visionResolutionSelector,_visionFpsSelector,_visionBitrateSelector,_veAcceleration,_veBackend,_veMonitor,_veAudio})
        { StyleSelector(combo); combo.SelectionChanged += (_,_)=>SaveVisionFromUi(); }
        _visionProfileSelector.SelectionChanged += (_,_)=>
        {
            if (_veLoading || _visionProfileSelector.SelectedItem is not string profile) return;
            VeAction(()=>
            {
                var old=_host.VisionSettings; var preset=LinuxVisionSettings.ForProfile(profile);
                _host.ConfigureVisionSettings(old with { Profile=preset.Profile,Bitrate=preset.Bitrate,MaxSize=preset.MaxSize,Fps=preset.Fps,
                    Acceleration=preset.Acceleration,LowLatency=preset.LowLatency,VSync=preset.VSync });
                SyncVisionUi();
            });
        };
        StyleSelector(_visionProfileSelector);
        foreach (var check in new[]{_veFullscreen,_veAudioEnabled,_veInput,_veLatency,_veVsync}) check.IsCheckedChanged += (_,_)=>SaveVisionFromUi();
        _host.VisionSettingsChanged += ()=>Dispatcher.UIThread.Post(SyncVisionUi);
        RefreshVeOutputs(); SyncVisionUi(); _veLoading=false;
        RefreshFunctionalVisionState();
        return ScrollPage(content);
    }
    private AvaloniaControl VeCard(string title, params (string Label,ComboBox Control)[] fields) =>
        Card(new StackPanel { Spacing=8, Children={SectionTitle(title),VeFields(fields)} });
    private AvaloniaControl VeFields(params (string Label,ComboBox Control)[] fields)
    {
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("*,*") };
        for (int i=0;i<fields.Length;i++)
        {
            if (i%2==0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (label,control)=fields[i];
            control.HorizontalAlignment=HorizontalAlignment.Stretch;
            var field=new StackPanel { Spacing=6,Margin=new Avalonia.Thickness(0,0,14,10),Children={Caption(label),control} };
            Grid.SetRow(field,i/2);Grid.SetColumn(field,i%2);grid.Children.Add(field);
        }
        return grid;
    }

    private void VeAction(Action action)
    {
        try { action(); _visionSettingsStatus.Text="Operación completada. Los cambios de captura y salida se aplican a la siguiente sesión."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { _visionSettingsStatus.Text=ex.Message; }
        RefreshFunctionalVisionState();
    }
    private void RefreshVeOutputs()
    {
        bool loading=_veLoading; _veLoading=true;
        try
        {
            var settings=_host.VisionSettings;
            var monitors=LinuxSdlDevices.Monitors().ToList();
            var audio=LinuxSdlDevices.AudioOutputs().ToList();
            if (monitors.All(m=>m.Id!=settings.MonitorId)) monitors.Add(new(settings.MonitorId,"Monitor guardado no conectado; elige otra salida"));
            if (audio.All(a=>a.Id!=settings.AudioDeviceId)) audio.Add(new(settings.AudioDeviceId,"Audio guardado no conectado; elige otra salida"));
            _veMonitor.ItemsSource=monitors; _veAudio.ItemsSource=audio;
            _veMonitor.SelectedItem=VeOutputSelection(monitors,settings.MonitorId,settings.MonitorName); _veAudio.SelectedItem=VeOutputSelection(audio,settings.AudioDeviceId,settings.AudioDeviceName);
        }
        catch (Exception ex) { _veDiagnostic.Text="No se pudieron consultar las salidas: "+ex.Message; }
        finally { _veLoading=loading; }
    }
    private static LinuxOutputDevice? VeOutputSelection(IEnumerable<LinuxOutputDevice> devices,uint id,string name)
    {
        if (id==0 && string.IsNullOrEmpty(name)) return devices.FirstOrDefault(d=>d.Id==0);
        var named=devices.Where(d=>d.Name==name && d.Id!=0).ToArray();
        return named.Length==1 ? named[0] : devices.FirstOrDefault(d=>d.Id==id);
    }
    private void SyncVisionUi()
    {
        if (!_veBuilt) return;
        bool loading=_veLoading; _veLoading=true;
        try
        {
            var s=_host.VisionSettings; _veDisplayed=s;
            _visionProfileSelector.SelectedItem=VisionProfiles.FirstOrDefault(p=>LinuxVisionSettings.ProfileId(p)==s.Profile);
            _visionResolutionSelector.SelectedItem=s.MaxSize.ToString(); _visionFpsSelector.SelectedItem=s.Fps.ToString(); _visionBitrateSelector.SelectedItem=s.Bitrate;
            _veAcceleration.SelectedItem=s.Acceleration switch {"Forced"=>"Forzada","Software"=>"Software",_=>"Automática"};
            _veBackend.SelectedItem=s.HardwareBackend; _veBackend.IsEnabled=s.Acceleration=="Forced";
            _veFullscreen.IsChecked=s.Fullscreen; _veAudioEnabled.IsChecked=s.AudioEnabled; _veInput.IsChecked=s.RemoteInput;
            _veLatency.IsChecked=s.LowLatency; _veVsync.IsChecked=s.VSync;
            if (_veMonitor.ItemsSource is IEnumerable<LinuxOutputDevice> monitors) _veMonitor.SelectedItem=VeOutputSelection(monitors,s.MonitorId,s.MonitorName);
            if (_veAudio.ItemsSource is IEnumerable<LinuxOutputDevice> audio) _veAudio.SelectedItem=VeOutputSelection(audio,s.AudioDeviceId,s.AudioDeviceName);
            _veDirectories.Text=$"Grabaciones: {s.RecordingDirectory}\nRecibidos: {s.ReceiveDirectory}";
        }
        finally { _veLoading=loading; }
    }
    private void SaveVisionFromUi()
    {
        if (_veLoading || _visionProfileSelector.SelectedItem is not string profile || _visionBitrateSelector.SelectedItem is not string bitrate ||
            !int.TryParse(_visionResolutionSelector.SelectedItem as string,out int size) || !int.TryParse(_visionFpsSelector.SelectedItem as string,out int fps)) return;
        VeAction(()=>_host.ConfigureVisionSettings(_host.VisionSettings with {
            Profile=LinuxVisionSettings.ProfileId(profile),Bitrate=bitrate,MaxSize=size,Fps=fps,
            Acceleration=(_veAcceleration.SelectedItem as string) switch {"Forzada"=>"Forced","Software"=>"Software",_=>"Automatic"},
            HardwareBackend=_veBackend.SelectedItem as string ?? "vaapi",MonitorId=(_veMonitor.SelectedItem as LinuxOutputDevice)?.Id ?? 0,
            AudioDeviceId=(_veAudio.SelectedItem as LinuxOutputDevice)?.Id ?? 0,
            MonitorName=(_veMonitor.SelectedItem as LinuxOutputDevice) is { Id: not 0 } monitor ? monitor.Name : "",
            AudioDeviceName=(_veAudio.SelectedItem as LinuxOutputDevice) is { Id: not 0 } output ? output.Name : "",Fullscreen=_veFullscreen.IsChecked==true,
            AudioEnabled=_veAudioEnabled.IsChecked==true,RemoteInput=_veInput.IsChecked==true,LowLatency=_veLatency.IsChecked==true,VSync=_veVsync.IsChecked==true }));
    }
    private void RefreshFunctionalVisionState()
    {
        if (!_veBuilt) return;
        if (_veDisplayed!=_host.VisionSettings) SyncVisionUi();
        var runtime=_host.VisionRuntime; bool recording=_host.VisionRecording;
        _veStart.IsEnabled=_host.IsAuthorized && !_host.HasVisionSession;
        _veRecord.IsEnabled=_host.VisionState=="Streaming" && !recording;
        _veRecordStop.IsEnabled=recording;
        _veSend.IsEnabled=_veSendFolder.IsEnabled=runtime?.Files.IsConnected==true && _veTransfer is null;
        _veCancelSend.IsEnabled=_veTransfer is not null;
        _veRecording.Text=runtime?.Recorder.Status ?? "Grabación detenida.";
        _veFiles.Text=runtime?.Files.Status ?? "Archivos: inicia una sesión VE.";
        if (_host.VisionTelemetry is { } t)
            _visionMetrics.Text=$"Decoder: {t.DecoderBackend} · SDL: {t.RendererBackend} · audio: {(t.AudioPlaying ? "activo" : "sin reproducción")}\n"+
                $"{t.FramesRendered} frames · {t.OutputWidth}×{t.OutputHeight}\n{t.InputStatus}";
    }
    private async Task ChooseVisionDirectoryAsync(bool recording)
    {
        var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title=recording ? "Guardar grabaciones en" : "Recibir archivos en",AllowMultiple=false });
        if (folders.Count==0 || folders[0].TryGetLocalPath() is not { } path) return;
        VeAction(()=>_host.ConfigureVisionSettings(recording ? _host.VisionSettings with { RecordingDirectory=path } : _host.VisionSettings with { ReceiveDirectory=path }));
        SyncVisionUi();
    }
    private async Task SendVisionSelectionAsync(bool folder)
    {
        if (_host.VisionRuntime is not { } runtime || _veTransfer is not null) return;
        var paths=new List<string>();
        if (folder)
        {
            var picked=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title="Enviar carpeta a Android",AllowMultiple=false });
            if (picked.Count>0 && picked[0].TryGetLocalPath() is { } path) paths.Add(path);
        }
        else
        {
            var picked=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title="Enviar archivos a Android",AllowMultiple=true });
            paths.AddRange(picked.Select(f=>f.TryGetLocalPath()).OfType<string>());
        }
        if (paths.Count==0) return;
        using var stop=new CancellationTokenSource(); _veTransfer=stop; RefreshFunctionalVisionState();
        try { foreach (string path in paths) await runtime.Files.SendAsync(path,stop.Token); }
        catch (OperationCanceledException) { _veFiles.Text="Envío cancelado."; }
        catch (Exception ex) { _veFiles.Text="No se pudo enviar: "+ex.Message; }
        finally { _veTransfer=null; RefreshFunctionalVisionState(); }
    }
}
