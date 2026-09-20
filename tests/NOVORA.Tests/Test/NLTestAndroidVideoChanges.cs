using NOVORA.Control;
using System.Text.Json;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidVideoChanges
{
    private static NLControlSnapshot State() => new(7, "PC", "1.4", "8M", "Balanced", "default", "default", true,
        [new("8M", "8 Mbps")], [new("Balanced", "Equilibrado")], [new("default", "Predeterminada")],
        new(false, true, false, false, false, "", "", "Phone"),
        new("1080", "60", [new("1080", "1080")], [new("60", "60")], "display1", [new("display1", "Principal")], true),
        new(true, true, false, ""));
    private static NLControlVideoChanges Changes() => new("Balanced", "8M", "1080", "60", "default", "display1");
    private static NLControlRequest Request(NLControlVideoChanges? changes = null) => new(1, 2, "applyVideoSettings", JsonSerializer.Serialize(changes ?? Changes()), 7);
    [Fact] public void CompleteAdvertisedSettingsAreAccepted() => Assert.Null(NLControlCommands.Validate(Request(), State()));
    [Theory]
    [InlineData("monitor")][InlineData("profile")][InlineData("bitrate")][InlineData("resolution")][InlineData("fps")][InlineData("audio")]
    public void OneInvalidValueRejectsEntireRequest(string field) {
        var c = Changes();
        c = field switch { "monitor" => c with { Monitor = "removed" }, "profile" => c with { Profile = "99" },
            "bitrate" => c with { Bitrate = "999M" }, "resolution" => c with { Resolution = "99999" },
            "fps" => c with { Fps = "-1" }, _ => c with { Audio = "removed" } };
        Assert.NotNull(NLControlCommands.Validate(Request(c), State()));
    }
    [Fact] public void StaleRevisionIsRejected() => Assert.NotNull(NLControlCommands.Validate(Request() with { Revision = 6 }, State()));
    [Theory][InlineData(true, false)][InlineData(false, true)]
    public void RecordingOrStartingCannotBeInterrupted(bool recording, bool starting) => Assert.NotNull(NLControlCommands.Validate(Request(), State() with { Media = new(true, true, recording, "", starting) }));
    [Theory][InlineData("null")][InlineData("{")][InlineData("{}")]
    public void MissingOrMalformedSettingsAreRejected(string json) => Assert.NotNull(NLControlCommands.Validate(Request() with { Value = json }, State()));
    [Fact] public void OlderPcDoesNotAcceptBatch() => Assert.NotNull(NLControlCommands.Validate(Request(), State() with { VideoSettings = State().VideoSettings! with { CanApplyTogether = false } }));
    [Fact] public void StoppedEngineCanApplyAndStartWhenAvailable() => Assert.Null(NLControlCommands.Validate(Request(), State() with { VideoRunning = false, Engines = State().Engines! with { VideoCanStart = true, VideoCanStop = false } }));
    [Fact] public void BusyEngineIsRejected() => Assert.NotNull(NLControlCommands.Validate(Request(), State() with { Engines = State().Engines! with { VideoCanStop = false } }));
}
