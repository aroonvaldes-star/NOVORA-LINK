using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidUniversal
{
    private static NLControlSnapshot State => new(12, "PC", "1.4", "4M", "Gaming", "default", "default", true,
        [new("4M", "4 Mbps")], [new("Gaming", "Juegos")], [new("default", "Predeterminado")],
        new(false, true, false, false, false, "Stopped", "", "Android"),
        new("1280", "45", [new("1280", "1280 px"), new("1920", "1920 px")], [new("30", "30 FPS"), new("45", "45 FPS")]),
        new(true, true, false, "Listo"), true);

    [Theory]
    [InlineData("resolution", "1920")]
    [InlineData("fps", "30")]
    public void AdvertisedVideoChoiceAccepted(string action, string value) =>
        Assert.Null(NLControlCommands.Validate(new(1, 1, action, value, 12), State));

    [Theory]
    [InlineData("resolution", "-1")]
    [InlineData("fps", "300")]
    public void UnadvertisedVideoChoiceRejected(string action, string value) =>
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, action, value, 12), State));

    [Theory]
    [InlineData("capture")]
    [InlineData("startRecording")]
    public void RecordingAndCaptureRequireCurrentRevisionAndCapability(string action)
    {
        Assert.Null(NLControlCommands.Validate(new(1, 1, action, Revision: 12), State));
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, action, Revision: 11), State));
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, action, Revision: 12), State with { Media = null }));
    }

    [Fact]
    public void ActiveRecordingCannotStartTwiceAndStoppedRecordingCannotStop()
    {
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, "stopRecording", Revision: 12), State));
        var recording = State with { Media = State.Media! with { Recording = true } };
        Assert.Null(NLControlCommands.Validate(new(1, 1, "stopRecording", Revision: 12), recording));
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, "startRecording", Revision: 12), recording));
    }

    [Fact]
    public void FileChunksAreIndependentOfVideoRevisionButRequireCapability()
    {
        Assert.Null(NLControlCommands.Validate(new(1, 1, "file.chunk", "{}", 9), State));
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, "file.chunk", "{}", 9), State with { FileSharing = false }));
        Assert.NotNull(NLControlCommands.Validate(new(1, 1, "file.erase", "{}", 12), State));
    }

    [Fact]
    public async Task NewCapabilitiesRoundTripOverFramedProtocol()
    {
        using var stream = new MemoryStream();
        await NLControlProtocol.WriteAsync(stream, State, default);
        stream.Position = 0;
        var result = await NLControlProtocol.ReadAsync<NLControlSnapshot>(stream, default);
        Assert.True(result.FileSharing);
        Assert.Equal("45", result.VideoSettings!.Fps);
        Assert.True(result.Media!.CanCapture);
        Assert.False(result.Media.Recording);
    }
}
