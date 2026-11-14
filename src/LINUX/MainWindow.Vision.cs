using Avalonia;
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
    private readonly ComboBox _vePresentation = new() { ItemsSource=new[]{"Ventana", "Pantalla completa"} };
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
        AddPageHeading(content,"","VisionEngine · Pantalla y audio","Elige cómo ver y escuchar la pantalla del teléfono.");
        var session=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto") };
        var status=new StackPanel { Margin=new Thickness(0,0,20,0) };
        status.Children.Add(SectionTitle("Estado de la pantalla"));
        _visionMessage.Margin=new Thickness(0,6,0,0); _visionMessage.TextWrapping=Avalonia.Media.TextWrapping.Wrap;
        status.Children.Add(_visionMessage); session.Children.Add(status);
        _veStart.Content="VER PANTALLA"; _veStart.MinWidth=150; _veStart.Height=42;
        _veStart.VerticalAlignment=VerticalAlignment.Center; StylePrimaryButton(_veStart);
        _veStart.Click += async (_,_)=>await ToggleHomeVisionAsync();
        _visionStopButton.Click += async (_,_)=>await StopVisionAsync();
        Grid.SetColumn(_veStart,1); session.Children.Add(_veStart); content.Children.Add(Card(session));

        _visionProfileSelector.ItemsSource=VisionProfiles;
        _visionResolutionSelector.ItemsSource=CaptureResolutions;
        _visionFpsSelector.ItemsSource=CaptureFrameRates;
        _visionBitrateSelector.ItemsSource=CaptureBitrates;
        var profile=new StackPanel();
        profile.Children.Add(SectionTitle("Perfil de transmisión"));
        profile.Children.Add(Caption("El perfil propone los valores de imagen y aceleración. Después puedes ajustar la calidad manualmente.",new Thickness(0,5,0,10)));
        _visionProfileSelector.Margin=new Thickness(0,8,0,0); profile.Children.Add(_visionProfileSelector);
        profile.Children.Add(Caption("El perfil se aplica al iniciar o reconectar la transmisión.",new Thickness(0,8,0,0)));
        content.Children.Add(Card(profile));

        var outputs=new StackPanel();
        outputs.Children.Add(SectionTitle("Dónde ver y escuchar"));
        outputs.Children.Add(Caption("Selecciona el monitor de la PC, la forma de abrir la imagen y la salida de audio.",new Thickness(0,5,0,12)));
        var outputFields=new Grid { ColumnDefinitions=new ColumnDefinitions("*,16,*") };
        var display=new StackPanel();
        display.Children.Add(SectionTitle("Monitor")); _veMonitor.Margin=new Thickness(0,8,0,14); display.Children.Add(_veMonitor);
        display.Children.Add(SectionTitle("Presentación de la ventana")); _vePresentation.Margin=new Thickness(0,8,0,14); display.Children.Add(_vePresentation);
        outputFields.Children.Add(display);
        var sound=new StackPanel(); sound.Children.Add(SectionTitle("Salida de audio")); _veAudio.Margin=new Thickness(0,8,0,0); sound.Children.Add(_veAudio);
        Grid.SetColumn(sound,2); outputFields.Children.Add(sound); outputs.Children.Add(outputFields); content.Children.Add(Card(outputs));

        var quality=new StackPanel();
        quality.Children.Add(Caption("Cambiar estos valores sustituye los sugeridos por el perfil. Se guardan para la siguiente transmisión.",new Thickness(0,0,0,12)));
        quality.Children.Add(VeFields(("Resolución",_visionResolutionSelector),("FPS",_visionFpsSelector),("Calidad / bitrate",_visionBitrateSelector)));
        content.Children.Add(VeExpander("Ajustar calidad manualmente",quality));
        var options=new StackPanel();
        _veVsync.Content="Presentación dinámica"; _veVsync.Margin=new Thickness(0,0,0,6); options.Children.Add(_veVsync);
        options.Children.Add(Caption("Ajusta la presentación al refresco de la pantalla. Puede cambiarse durante la sesión.",new Thickness(0,0,0,14)));
        _veLatency.Content="Optimización de latencia"; _veLatency.Margin=new Thickness(0,0,0,6); options.Children.Add(_veLatency);
        options.Children.Add(Caption("Solicita decodificación de baja latencia al iniciar el video, sin cambiar resolución ni bitrate."));
        options.Children.Add(_veAudioEnabled); options.Children.Add(_veInput);
        options.Children.Add(Caption("La entrada requiere Accesibilidad de LINK Control y se envía con la ventana de video enfocada."));
        content.Children.Add(VeExpander("Opciones experimentales",options));

        var diagnostics=new StackPanel { Spacing=8 };
        diagnostics.Children.Add(SectionTitle("Aceleración disponible en esta PC"));
        diagnostics.Children.Add(_veDiagnostic);
        diagnostics.Children.Add(VeFields(("Modo",_veAcceleration),("Backend forzado",_veBackend)));
        var probe=FlatButton("Comprobar aceleración real");
        probe.Click += async (_,_)=>
        {
            probe.IsEnabled=false; _veDiagnostic.Text="Probando decodificación hardware y descarga de frames…";
            try { var backends=await Task.Run(()=>LinuxVisionAcceleration.ProbeAsync(CancellationToken.None)); _veDiagnostic.Text=string.Join("\n",backends); }
            catch (Exception ex) { _veDiagnostic.Text=ex.Message; }
            finally { probe.IsEnabled=true; }
        };
        diagnostics.Children.Add(probe);
        var refresh=FlatButton("Actualizar monitores y audio"); refresh.Click += (_,_)=>RefreshVeOutputs(); diagnostics.Children.Add(refresh);
        content.Children.Add(VeExpander("Diagnóstico técnico de VisionEngine",diagnostics));

        var recording=new StackPanel { Spacing=8 };
        var mediaButtons=new WrapPanel {  };
        foreach (var button in new[]{_veRecord,_veRecordStop,_veSend,_veSendFolder,_veCancelSend}) { StyleFlatButton(button); button.Margin=new Thickness(0,0,12,12); mediaButtons.Children.Add(button); }
        _veRecord.Click += (_,_)=>VeAction(_host.StartVisionRecording);
        _veRecordStop.Click += async (_,_)=> { try { if (_host.VisionRuntime is { } runtime) await runtime.StopRecordingAsync(); } catch (Exception ex) { _veRecording.Text=ex.Message; } };
        _veSend.Click += async (_,_)=>await SendVisionSelectionAsync(false);
        _veSendFolder.Click += async (_,_)=>await SendVisionSelectionAsync(true);
        _veCancelSend.Click += (_,_)=>_veTransfer?.Cancel();
        recording.Children.Add(mediaButtons); recording.Children.Add(_veRecording); recording.Children.Add(_veFiles);
        var directories=new WrapPanel {  };
        var captureDir=FlatButton("Carpeta de grabaciones"); var receiveDir=FlatButton("Carpeta de recibidos");
        captureDir.Click += async (_,_)=>await ChooseVisionDirectoryAsync(true);
        receiveDir.Click += async (_,_)=>await ChooseVisionDirectoryAsync(false);
        captureDir.Margin=new Thickness(0,0,12,12); receiveDir.Margin=new Thickness(0,0,12,12);
        directories.Children.Add(captureDir); directories.Children.Add(receiveDir);
        recording.Children.Add(directories); recording.Children.Add(_veDirectories);
        recording.Children.Add(Caption("MKV conserva el video H.264 y audio disponible. Al rotar se crea otro segmento. Los archivos usan FILES V2 autenticado durante VE."));
        content.Children.Add(VeExpander("Grabación y archivos",recording));
        content.Children.Add(_visionSettingsStatus);
        foreach (var combo in new[]{_visionResolutionSelector,_visionFpsSelector,_visionBitrateSelector,_veAcceleration,_veBackend,_veMonitor,_veAudio,_vePresentation})
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
    private static Expander VeExpander(string title,AvaloniaControl content) => new()
    {
        Header=title, IsExpanded=false, Margin=new Thickness(0,0,0,14),Content=Card(content)
    };
    private AvaloniaControl VeFields(params (string Label,ComboBox Control)[] fields)
    {
        var grid=new Grid();
        for(int i=0;i<fields.Length;i++)
        {
            if(i>0) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1,GridUnitType.Star)));
            var (label,control)=fields[i];
            control.HorizontalAlignment=HorizontalAlignment.Stretch;
            control.Margin=new Thickness(0,8,0,0);
            var field=new StackPanel { Children={SectionTitle(label),control} };
            Grid.SetColumn(field,i*2);grid.Children.Add(field);
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
            _veFullscreen.IsChecked=s.Fullscreen; _vePresentation.SelectedIndex=s.Fullscreen?1:0; _veAudioEnabled.IsChecked=s.AudioEnabled; _veInput.IsChecked=s.RemoteInput;
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
        string profileId=LinuxVisionSettings.ProfileId(profile);
        // Choosing a mobile-data profile applies its fixed bitrate, size, FPS and muted audio.
        if (LinuxVisionSettings.IsMobileProfile(profileId) && profileId!=_host.VisionSettings.Profile)
        {
            var preset=LinuxVisionSettings.ForProfile(profileId);
            bitrate=preset.Bitrate; size=preset.MaxSize; fps=preset.Fps; _veAudioEnabled.IsChecked=false;
        }
        VeAction(()=>_host.ConfigureVisionSettings(_host.VisionSettings with {
            Profile=profileId,Bitrate=bitrate,MaxSize=size,Fps=fps,
            Acceleration=(_veAcceleration.SelectedItem as string) switch {"Forzada"=>"Forced","Software"=>"Software",_=>"Automatic"},
            HardwareBackend=_veBackend.SelectedItem as string ?? "vaapi",MonitorId=(_veMonitor.SelectedItem as LinuxOutputDevice)?.Id ?? 0,
            AudioDeviceId=(_veAudio.SelectedItem as LinuxOutputDevice)?.Id ?? 0,
            MonitorName=(_veMonitor.SelectedItem as LinuxOutputDevice) is { Id: not 0 } monitor ? monitor.Name : "",
            AudioDeviceName=(_veAudio.SelectedItem as LinuxOutputDevice) is { Id: not 0 } output ? output.Name : "",Fullscreen=_vePresentation.SelectedIndex==1,
            AudioEnabled=_veAudioEnabled.IsChecked==true,RemoteInput=_veInput.IsChecked==true,LowLatency=_veLatency.IsChecked==true,VSync=_veVsync.IsChecked==true }));
    }
    private void RefreshFunctionalVisionState()
    {
        if (!_veBuilt) return;
        if (_veDisplayed!=_host.VisionSettings) SyncVisionUi();
        var runtime=_host.VisionRuntime; bool recording=_host.VisionRecording;
        _veStart.IsEnabled=true;
        _veStart.Content=_host.HasVisionSession || _host.VisionRequested ? "DETENER PANTALLA" : "VER PANTALLA";
        _veRecord.IsEnabled=_host.VisionState=="Streaming" && !recording;
        _veRecordStop.IsEnabled=recording;
        _veSend.IsEnabled=_veSendFolder.IsEnabled=runtime?.Files.IsConnected==true && _veTransfer is null;
        _veCancelSend.IsEnabled=_veTransfer is not null;
        _veRecording.Text=runtime?.Recorder.Status ?? "Grabación detenida.";
        _veFiles.Text=runtime?.Files.Status ?? "Archivos: inicia una sesión VE.";
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
