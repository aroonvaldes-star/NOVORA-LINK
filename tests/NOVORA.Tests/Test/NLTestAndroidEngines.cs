using NOVORA.Control;
using Xunit;
namespace NOVORA.Tests;
public sealed class NLTestAndroidEngines
{
    private static NLControlSnapshot State() => new(7, "PC", "1.4.0", "4M", "Gaming", "default", "default", false, [], [], [],
        new(true, false, false, false, false, "Unavailable", "Cliente Android pendiente", "Teléfono seleccionado"));
    [Fact] public void ReadyVideoCanBeStarted() => Assert.Null(NLControlCommands.Validate(new(1, 1, "startVideo", Revision:7), State()));
    [Theory]
    [InlineData("startVideo")][InlineData("stopVideo")][InlineData("startLink")][InlineData("stopLink")]
    public void OldPcWithoutEngineCapabilitiesRejectsCommands(string action) => Assert.NotNull(NLControlCommands.Validate(new(1, 1, action, Revision:7), State() with { Engines=null }));
    [Fact] public void UnavailableLinkCannotBeStarted() => Assert.NotNull(NLControlCommands.Validate(new(1, 1, "startLink", Revision:7), State()));
    [Fact] public void RunningVideoCanBeStopped() => Assert.Null(NLControlCommands.Validate(new(1, 1, "stopVideo", Revision:7), State() with { VideoRunning=true, Engines=State().Engines! with { VideoCanStart=false,VideoCanStop=true } }));
    [Fact] public void UsbReadyLinkCanBeStarted() => Assert.Null(NLControlCommands.Validate(new(1, 1, "startLink", Revision:7), State() with { Engines=State().Engines! with { LinkCanStart=true } }));
    [Fact] public void PendingLinkCanBeStopped() => Assert.Null(NLControlCommands.Validate(new(1, 1, "stopLink", Revision:7), State() with { Engines=State().Engines! with { LinkCanStop=true, LinkState="Starting" } }));
    [Fact] public void EngineCommandsRejectUnexpectedArguments() => Assert.NotNull(NLControlCommands.Validate(new(1, 1, "startVideo", "anything", 7), State()));
    [Fact] public void StaleStartIsRejected() => Assert.NotNull(NLControlCommands.Validate(new(1, 1, "startVideo", Revision:6), State()));
    [Fact] public async Task OptionalEngineStateRoundTripsAndLegacySnapshotIsAccepted()
    {
        using var stream=new MemoryStream();
        await NLControlProtocol.WriteAsync(stream, State(), default);stream.Position=0;
        var read=await NLControlProtocol.ReadAsync<NLControlSnapshot>(stream,default);
        Assert.Equal(State().Engines,read.Engines);
        var legacy=System.Text.Json.JsonSerializer.Deserialize<NLControlSnapshot>("""{"Revision":7,"PcName":"PC","PcVersion":"1.4.0","Bitrate":"4M","Profile":"Gaming","AudioOutput":"default","ActiveAudioOutput":"default","VideoRunning":false,"Bitrates":[],"Profiles":[],"AudioOutputs":[]}""");
        Assert.Null(legacy!.Engines);
    }
}
