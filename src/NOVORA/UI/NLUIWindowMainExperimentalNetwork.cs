using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using NOVORA.Control;
using NOVORA.LinkEngine.Network;
using NOVORA.Network;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private readonly Dictionary<string,(long Generation,NLControlNetworkCapabilities Caps)> _experimentalPeers=new();
    private NLExperimentalNetworkSession? _experimentalWanSession, _experimentalDeviceSession;
    private LENetworkRelay? _experimentalWanRelay;
    private DispatcherTimer? _experimentalNetworkTimer;
    private CancellationTokenSource? _experimentalNetworkCancellation;
    private bool _experimentalNetworkBusy;
    private long _experimentalGeneration=DateTime.UtcNow.Ticks, _experimentalRevision, _experimentalCaptureSequence, _experimentalTelemetrySequence;
    private long _experimentalVeGeneration;
    private int _experimentalVeInitialBitrate, _experimentalVeInitialFps, _experimentalVeAppliedBitrate, _experimentalVeAppliedFps;
    private string? _experimentalVeKey;
    private NLExperimentalInternetProbe? _experimentalInternetProbe;
    private NLNetworkSharedLinkAllocation? _experimentalSharedAllocation;
    private bool ExperimentalFeaturesEnabled => ExperimentalNetworkFeatures?.IsChecked==true;
    private NLControlNetworkCapabilities? ExperimentalPeer(string transport) => _experimentalPeers.TryGetValue(transport,out var peer) && peer.Generation==_androidControlGeneration ? peer.Caps : null;
    private long NewExperimentalCaptureGeneration(string transport) => ExperimentalFeaturesEnabled && ExperimentalPeer(transport) is {LiveVideoBitrate:true,CaptureFramePacing:true} ? Interlocked.Increment(ref _experimentalGeneration) : 0;
    private void SetExperimentalVisionSession(long generation,string key,int bitrate,int fps)
    {
        _visionEngineVE?.RuntimeVE.ControlVE.StopNetworkProbeVE();
        var previous=_experimentalDeviceSession; _experimentalDeviceSession=null; previous?.Dispose();
        _experimentalVeGeneration=generation; _experimentalVeKey=key; _experimentalCaptureSequence=0; _experimentalTelemetrySequence=0;
        _experimentalVeInitialBitrate=_experimentalVeAppliedBitrate=bitrate; _experimentalVeInitialFps=_experimentalVeAppliedFps=fps;
    }
    private void ExperimentalNetworkFeatures_Changed(object sender,RoutedEventArgs e)
    {
        if(!_shellInitialized14)return;
        AndroidEngineStateChanged();
        if(!ExperimentalFeaturesEnabled) _=StopExperimentalNetworkAsync();
    }
    private async Task<NLControlReply> HandleExperimentalNetworkControlAsync(NLControlRequest request,string transport)
    {
        string? validation=NLControlCommands.Validate(request,CaptureAndroidControlSnapshot());
        if(validation is not null) return new(NLControlProtocol.Version,request.Id,false,validation);
        try
        {
            if(request.Action=="network.capabilities")
            {
                var capabilities=JsonSerializer.Deserialize<NLControlNetworkCapabilities>(request.Value!);
                if(capabilities is null || capabilities.Version!=1 || capabilities.FilesWireVersions is {Length:>2} ||
                    capabilities.FilesWireVersions?.Any(v=>v is not (1 or 2))==true)
                    return new(NLControlProtocol.Version,request.Id,false,"Capacidades no válidas.");
                _experimentalPeers[transport]=(_androidControlGeneration,capabilities);
                return new(NLControlProtocol.Version,request.Id,true,"Capacidades experimentales negociadas.",CaptureAndroidControlSnapshot());
            }
            if(request.Action=="network.policy")
            {
                using var json=JsonDocument.Parse(request.Value!); var data=json.RootElement;
                long generation=data.GetProperty("Generation").GetInt64();
                if(!Enum.TryParse<NLNetworkDirection>(data.GetProperty("Direction").GetString(),out var direction) || !Enum.IsDefined(direction) || generation!=_experimentalVeGeneration)
                    return new(NLControlProtocol.Version,request.Id,false,"El presupuesto pertenece a otra sesión.");
                var grant=_experimentalDeviceSession?.GetBulkGrant(direction);
                var allocation=new NLControlNetworkAllocation(grant?.Revision??0,generation,_experimentalVeKey??"",direction,
                    grant?.BytesPerSecond??0,grant?.BurstBytes??1500,2000,
                    _experimentalDeviceSession?.IsLimited(direction)==true ? _experimentalDeviceSession.Evaluate().AppliedPolicy.Mode : NLNetworkPolicyMode.Observe);
                return new(NLControlProtocol.Version,request.Id,true,"Presupuesto de archivos.",Value:JsonSerializer.Serialize(allocation));
            }
            return await HandleExperimentalFilesControlAsync(request,transport);
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or IOException)
        { return new(NLControlProtocol.Version,request.Id,false,"Solicitud experimental no válida."); }
    }
    private static long? ParseExperimentalCapacity(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(!double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double mbps) || !double.IsFinite(mbps) || mbps<=0 || mbps>10_000)
            throw new ArgumentException("Indica una capacidad medida entre 0 y 10 000 Mbps.");
        return Math.Max(1,(long)(mbps*125_000));
    }
    private async void ExperimentalNetworkApply_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var domain=ExperimentalNetworkDomain.SelectedIndex==0 ? NLNetworkDomain.Internet : NLNetworkDomain.DeviceLink;
            var mode=ExperimentalNetworkMode.SelectedIndex switch {1=>NLNetworkPolicyMode.Manual,2=>NLNetworkPolicyMode.Adaptive,_=>NLNetworkPolicyMode.Observe};
            long? up=ParseExperimentalCapacity(ExperimentalNetworkUpload.Text),down=ParseExperimentalCapacity(ExperimentalNetworkDownload.Text);
            if(mode!=NLNetworkPolicyMode.Observe && up is null && down is null) throw new ArgumentException("Mide la capacidad antes de activar límites. Puedes observar sin límites.");
            if(domain==NLNetworkDomain.Internet)
            {
                if(_experimentalSharedAllocation is not null && _experimentalDeviceSession?.Mode!=mode)throw new ArgumentException("El presupuesto compartido requiere el mismo modo en LE y VE.");
                var runtime=_linkEngineRuntimeLE;
                var relay=runtime?.IsRunningLE==true ? runtime.EngineLE?.Network.PolicyRelayLE : _androidLanDataGateway?.IsConnected==true ? _androidLanRelay : null;
                if(relay?.IsRunningLE!=true)throw new InvalidOperationException("Inicia LinkEngine para aplicar el presupuesto de Internet.");
                string key=runtime?.IsRunningLE==true ? "LE:"+runtime.SessionLE?.ConnectionType+":"+runtime.SessionLE?.Serial : "LE:LAN:"+_androidLanControl?.Invitation.Host;
                var previous=_experimentalWanSession;
                var observedPolicy=relay.GetQosSnapshotLE();
                long policyGeneration=previous?.LinkKey==key && ReferenceEquals(_experimentalWanRelay,relay) ? previous.Generation :
                    observedPolicy?.LinkKey==key ? observedPolicy.Generation : Interlocked.Increment(ref _experimentalGeneration);
                long revision=Math.Max(Interlocked.Increment(ref _experimentalRevision),relay.GetQosSnapshotLE()?.Revision+1??1);
                _experimentalInternetProbe?.Dispose(); _experimentalInternetProbe=null;
                _experimentalWanSession?.Dispose(); _experimentalWanRelay=relay;
                _experimentalWanSession=new(new(revision,policyGeneration,key,domain,mode,up,down));
            }
            else
            {
                if(!ExperimentalFeaturesEnabled || _experimentalVeGeneration<=0 || !IsVisionEngineRunningVE())throw new InvalidOperationException("Habilita las funciones experimentales antes de iniciar una sesión VE nueva.");
                if(mode!=NLNetworkPolicyMode.Observe && up is { } capacity && capacity<(_viewModel.SelectedAudioOutput==VisionEngine.Audio.VEAudioOutput.DisabledValueVE ? 95_250 : 287_250))
                    throw new ArgumentException("La capacidad de subida no alcanza para audio y video mínimo. Desactiva audio o usa un enlace con más capacidad.");
                NLNetworkSharedLinkAllocation? shared=null;
                if(ExperimentalSharedPhysicalLink.IsChecked==true)
                {
                    if(_experimentalWanSession is not { } wan || wan.Mode!=mode || _experimentalWanRelay?.SupportsPolicyLE!=true || !ExperimentalEnginesShareConfirmedRoute())
                        throw new ArgumentException("Aplica LE con el mismo modo y confirma que ambas sesiones actuales usan USB físico o LAN juntos.");
                    shared=NLNetworkSharedLinkBudget.Split(up,down,_experimentalVeInitialBitrate/8,_viewModel.SelectedAudioOutput==VisionEngine.Audio.VEAudioOutput.DisabledValueVE ? 0 : 192_000);
                    up=shared.VeFromAndroid; down=shared.VeToAndroid;
                }
                var oldDevice=_experimentalDeviceSession;
                _experimentalDeviceSession=new(new(Interlocked.Increment(ref _experimentalRevision),_experimentalVeGeneration,_experimentalVeKey!,domain,mode,up,down),_experimentalVeInitialBitrate,
                    _viewModel.SelectedAudioOutput!=VisionEngine.Audio.VEAudioOutput.DisabledValueVE);
                oldDevice?.Dispose();
                _experimentalSharedAllocation=shared; _experimentalWanSession?.AdvanceRevision();
            }
            EnsureExperimentalNetworkTimer(); await ExperimentalNetworkTickAsync();
        }
        catch(Exception ex) { ExperimentalNetworkStatus.Text=ex.Message; }
    }
    private void EnsureExperimentalNetworkTimer()
    {
        if(_experimentalNetworkTimer is not null)return;
        _experimentalNetworkCancellation=new();
        _experimentalNetworkTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
        _experimentalNetworkTimer.Tick+=async(_,_)=>await ExperimentalNetworkTickAsync(); _experimentalNetworkTimer.Start();
        Closed+=(_,_)=> { _experimentalNetworkTimer?.Stop(); _experimentalNetworkCancellation?.Cancel(); _experimentalInternetProbe?.Dispose(); _experimentalWanSession?.Dispose(); _experimentalDeviceSession?.Dispose(); };
    }
    private async Task ExperimentalNetworkTickAsync()
    {
        if(_experimentalNetworkBusy || _closing)return; _experimentalNetworkBusy=true;
        try
        {
            var ct=_experimentalNetworkCancellation?.Token??CancellationToken.None;
            var status=new List<string>();
            if(_experimentalWanSession is { } wan && _experimentalWanRelay is { } relay)
            {
                if(!relay.IsRunningLE) { wan.Dispose(); _experimentalWanSession=null; status.Add("LE desconectado: límites retirados."); }
                else
                {
                    string? currentKey=_linkEngineRuntimeLE?.IsRunningLE==true ? "LE:"+_linkEngineRuntimeLE.SessionLE?.ConnectionType+":"+_linkEngineRuntimeLE.SessionLE?.Serial :
                        _androidLanDataGateway?.IsConnected==true ? "LE:LAN:"+_androidLanControl?.Invitation.Host : null;
                    if(wan.LinkKey!=currentKey)
                    {
                        await relay.ApplyPolicyAsync([wan.DisabledPolicy],[],ct);
                        wan.Dispose(); _experimentalWanSession=null; _experimentalInternetProbe?.Dispose(); _experimentalInternetProbe=null;
                        ExperimentalNetworkStatus.Text="LE cambió de ruta: vuelve a aplicar el presupuesto.";
                        return;
                    }
                    var pipeline=relay.GetQosSnapshotLE()?.Samples??Array.Empty<NLNetworkSample>();
                    wan.Observe(pipeline.Concat(_experimentalInternetProbe?.Samples??Array.Empty<NLNetworkSample>()).ToArray());
                    if(_experimentalSharedAllocation is not null && (_experimentalDeviceSession is null || !ExperimentalEnginesShareConfirmedRoute()))
                    {
                        _experimentalSharedAllocation=null; wan.AdvanceRevision();
                        status.Add("Presupuesto compartido retirado; vuelve a aplicar VE para usar toda la capacidad disponible.");
                    }
                    var decision=wan.Evaluate();
                    var policies=new List<NLNetworkPolicy>{decision.AppliedPolicy}; var grants=decision.Grants.ToList();
                    if(_experimentalSharedAllocation is { } shared && _experimentalDeviceSession is not null && ExperimentalEnginesShareConfirmedRoute())
                    {
                        var physical=decision.AppliedPolicy with {Domain=NLNetworkDomain.DeviceLink,FromAndroidBytesPerSecond=shared.RelayFromAndroid,ToAndroidBytesPerSecond=shared.RelayToAndroid};
                        policies.Add(physical);
                        foreach(var direction in Enum.GetValues<NLNetworkDirection>())
                        {
                            long rate=direction==NLNetworkDirection.FromAndroid ? shared.RelayFromAndroid : shared.RelayToAndroid;
                            grants.Add(new(physical.Revision,physical.Generation,physical.LinkKey,physical.Domain,direction,NLNetworkTrafficClass.BestEffort,rate,(int)Math.Clamp(rate/200,1500,65536),0));
                        }
                    }
                    bool applied=await relay.ApplyPolicyAsync(policies,grants,ct);
                    status.Add(applied ? "LE: "+decision.Reason+" Tráfico desconocido regulado en conjunto." : "LE: el relay no confirmó los límites; adaptación inactiva.");
                }
            }
            if(_experimentalDeviceSession is { } device)
            {
                if(!IsVisionEngineRunningVE() || device.Generation!=_experimentalVeGeneration)
                { _visionEngineVE?.RuntimeVE.ControlVE.StopNetworkProbeVE(); device.Dispose(); _experimentalDeviceSession=null; status.Add("VE desconectado: presupuesto retirado."); }
                else
                {
                    var control=_visionEngineVE!.RuntimeVE.ControlVE;
                    bool lan=device.LinkKey.StartsWith("VE:LAN:",StringComparison.Ordinal);
                    control.ConfigureNetworkProbeVE(device.Generation,device.LinkKey,ExperimentalFeaturesEnabled ? ExperimentalPeer(lan ? "LAN" : "USB") : null);
                    var samples=control.NetworkProbeSamplesVE.ToList();
                    if(control.CaptureTelemetrySnapshotVE is { } received && received.Telemetry is { } t && t.Generation==_experimentalVeGeneration && t.Sequence>=_experimentalTelemetrySequence)
                    {
                        _experimentalTelemetrySequence=t.Sequence;
                        samples.Add(new(t.Generation,device.LinkKey,NLNetworkDomain.DeviceLink,NLNetworkDirection.FromAndroid,NLNetworkSampleSource.Pipeline,
                            received.ReceivedAtTicks,t.WindowSeconds,null,t.WriteWaitP95Ms,t.QueueAgeP95Ms,t.QueueBytes,t.UsefulBytes,t.ProtocolBytes,t.Dropped,true));
                    }
                    device.Observe(samples);
                    var rtt=samples.LastOrDefault(s=>s.Valid && s.RttMs.HasValue && (s.Source is NLNetworkSampleSource.UsbControl or NLNetworkSampleSource.LanControl) &&
                        TimeProvider.System.GetElapsedTime(s.ObservedAtTicks).TotalSeconds<=3);
                    status.Add(rtt is null ? "RTT del control VE: no disponible." : $"RTT del control VE {(lan ? "LAN" : "USB/ADB")}: {rtt.RttMs:F1} ms (ida y vuelta). Fuente separada de Internet.");
                    var decision=device.Evaluate();
                    int bitrate=(int)Math.Clamp(_experimentalVeInitialBitrate*decision.VideoRateFactor,250_000,_experimentalVeInitialBitrate);
                    int pressureFps=decision.VideoRateFactor<.15 ? 10 : decision.VideoRateFactor<.3 ? 15 : 30;
                    int fps=decision.Congested && decision.VideoRateFactor<.6 ? Math.Min(_experimentalVeInitialFps,pressureFps) : _experimentalVeInitialFps;
                    if(bitrate!=_experimentalVeAppliedBitrate || fps!=_experimentalVeAppliedFps)
                    {
                        var ack=await control.ApplyCapturePolicyAsync(new(_experimentalVeGeneration,++_experimentalCaptureSequence,bitrate,fps),ct);
                        if(ack?.Accepted==true) { _experimentalVeAppliedBitrate=bitrate; _experimentalVeAppliedFps=fps; }
                        status.Add(ack?.Accepted==true ? "VE: Android aceptó el ajuste; tasa efectiva pendiente de medición." : "VE: Android no confirmó el ajuste; se conserva la solicitud anterior.");
                    }
                    else status.Add("VE: "+decision.Reason);
                }
            }
            ExperimentalNetworkStatus.Text=string.Join(" ",status);
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { ExperimentalNetworkStatus.Text="Experimental: "+ex.Message; }
        finally { _experimentalNetworkBusy=false; }
    }
    private async void ExperimentalNetworkStop_Click(object sender,RoutedEventArgs e)=>await StopExperimentalNetworkAsync();
    private async void ExperimentalNetworkCalibrate_Click(object sender,RoutedEventArgs e)
    {
        if(_experimentalWanSession is not { } wan || ExperimentalQuietCalibration.IsChecked!=true)
        { ExperimentalNetworkStatus.Text="Aplica una sesión de Internet y confirma que no hay descargas durante los 30 segundos."; return; }
        _experimentalInternetProbe?.Dispose();
        var probe=new NLExperimentalInternetProbe(wan.Generation,wan.LinkKey); _experimentalInternetProbe=probe;
        probe.BeginCalibration(true); probe.Start();
        ExperimentalNetworkStatus.Text="Calibrando respuestas HTTPS de Internet desde PC durante 30 segundos…";
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30),_experimentalNetworkCancellation?.Token??CancellationToken.None);
            if(!ReferenceEquals(wan,_experimentalWanSession) || !ReferenceEquals(probe,_experimentalInternetProbe))return;
            if(probe.TryGetBaseline() is not { } baseline) {ExperimentalNetworkStatus.Text="Calibración sin evidencia suficiente. Se conservan los límites manuales."; return;}
            wan.SetBaseline(baseline);
            ExperimentalNetworkStatus.Text=$"Baseline HTTP PC-Internet confirmado: p95 {baseline.RttP95Ms:F1} ms. Observando variación durante la descarga.";
        }
        catch(OperationCanceledException) { }
    }
    private async Task StopExperimentalNetworkAsync()
    {
        string result="Experimental: desactivado.";
        _experimentalInternetProbe?.Dispose(); _experimentalInternetProbe=null;
        _visionEngineVE?.RuntimeVE.ControlVE.StopNetworkProbeVE();
        _experimentalNetworkTimer?.Stop(); _experimentalNetworkTimer=null;
        _experimentalNetworkCancellation?.Cancel();
        try
        {
            if(_experimentalWanSession is { } wan && _experimentalWanRelay?.IsRunningLE==true && !await _experimentalWanRelay.ApplyPolicyAsync([wan.DisabledPolicy],[]))
                result="Adaptación detenida. El relay no confirmó retirar su límite; reinicia LE para retirarlo.";
            if(_experimentalVeGeneration>0 && _visionEngineVE?.RuntimeVE.ControlVE.IsReadyVE==true && (_experimentalVeAppliedBitrate!=_experimentalVeInitialBitrate || _experimentalVeAppliedFps!=_experimentalVeInitialFps))
            {
                var restore=await _visionEngineVE.RuntimeVE.ControlVE.ApplyCapturePolicyAsync(new(_experimentalVeGeneration,++_experimentalCaptureSequence,_experimentalVeInitialBitrate,_experimentalVeInitialFps));
                if(restore?.Accepted!=true)result+=" Android no confirmó restaurar la calidad; usa una sesión VE nueva.";
                else { _experimentalVeAppliedBitrate=_experimentalVeInitialBitrate; _experimentalVeAppliedFps=_experimentalVeInitialFps; }
            }
        }
        catch(Exception ex) { result="No se confirmó la retirada: "+ex.Message; }
        finally
        {
            var oldWan=_experimentalWanSession; var oldDevice=_experimentalDeviceSession;
            _experimentalWanSession=_experimentalDeviceSession=null; _experimentalWanRelay=null;
            _experimentalSharedAllocation=null;
            oldWan?.Dispose(); oldDevice?.Dispose();
            _experimentalNetworkCancellation?.Dispose(); _experimentalNetworkCancellation=null;
            ExperimentalNetworkStatus.Text=result;
        }
    }
    private bool ExperimentalEnginesShareConfirmedRoute()
    {
        if(ExperimentalSharedPhysicalLink.IsChecked!=true || _experimentalWanSession is not { } wan || _experimentalVeKey is null)return false;
        if(_experimentalVeKey.StartsWith("VE:LAN:",StringComparison.Ordinal))
            return _androidLanDataGateway?.IsConnected==true && _androidLanControl?.IsAuthorized==true && wan.LinkKey==_experimentalVeKey.Replace("VE:LAN:","LE:LAN:");
        var connection=_linkEngineRuntimeLE?.SessionLE?.ConnectionType.ToString();
        return string.Equals(connection,"Usb",StringComparison.OrdinalIgnoreCase) &&
            string.Equals(wan.LinkKey,_experimentalVeKey.Replace("VE:USB:","LE:Usb:"),StringComparison.OrdinalIgnoreCase);
    }
    private async ValueTask PaceExperimentalFilesAsync(int bytes,CancellationToken ct)
    {
        while(_experimentalDeviceSession is { } session)
        {
            try { await session.PaceBulkAsync(NLNetworkDirection.ToAndroid,bytes,ct); return; }
            catch(ObjectDisposedException) when(!ReferenceEquals(session,_experimentalDeviceSession)) { }
        }
    }
}
