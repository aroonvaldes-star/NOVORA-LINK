using System.IO;
using System.Text.Json;
using NOVORA.Control;
using NOVORA.VisionEngine.Exchange;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VEExchangeLanFileV2? _experimentalFilesV2;
    private NLControlFileLeaseCoordinatorV2? _experimentalFileLeases;
    private Task StartExperimentalFilesV2Async(Stream stream,long generation)
    {
        if(generation<=0 || _experimentalFilesV2 is not null)throw new InvalidOperationException("La sesión FILES binaria no está disponible.");
        _experimentalFileLeases=new(generation);
        _experimentalFilesV2=new(stream,generation,_experimentalFileLeases,PaceExperimentalFilesAsync,
            canReceiveFiles: CanReceiveFilesFromAndroidVE);
        return Task.CompletedTask;
    }
    private async Task StopExperimentalFilesV2Async()
    {
        var files=_experimentalFilesV2; var leases=_experimentalFileLeases;
        _experimentalFilesV2=null; _experimentalFileLeases=null;
        leases?.CloseGeneration(leases.Generation);
        if(files is not null)await files.DisposeAsync();
        leases?.Dispose();
    }
    private Task<NLControlReply> HandleExperimentalFilesControlAsync(NLControlRequest request,string transport)
    {
        NLControlReply Reply(bool success,string text,string?value=null)=>new(NLControlProtocol.Version,request.Id,success,text,Value:value);
        if(transport!="LAN" || _experimentalFileLeases is not { } leases || _experimentalFilesV2 is null ||
            ExperimentalPeer("LAN")?.FilesWireVersions?.Contains(2)!=true)
            return Task.FromResult(Reply(false,"Inicia una sesión VE LAN con FILES binario negociado."));
        var acquire=JsonSerializer.Deserialize<NLFileLeaseRequestV2>(request.Value!);
        if(acquire is null || acquire.Generation!=leases.Generation)
            return Task.FromResult(Reply(false,"La solicitud FILES pertenece a otra sesión."));
        if(request.Action=="files.acquire")
        {
            var grant=leases.PollRemote(acquire);
            return Task.FromResult(Reply(true,grant.Granted ? "Turno FILES concedido." : "Turno FILES en cola.",JsonSerializer.Serialize(grant)));
        }
        return Task.FromResult(Reply(leases.CancelRemote(acquire),"Solicitud de turno FILES cancelada."));
    }
}
