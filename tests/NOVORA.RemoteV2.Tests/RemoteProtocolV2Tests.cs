using NOVORA.Remote;
using Xunit;

namespace NOVORA.RemoteV2.Tests;

public sealed class RemoteProtocolV2Tests
{
    [Fact]
    public void Protocol_is_v2_and_supports_settings_commands()
    {
        Assert.Equal(2, ProtocolRemoteNV.ProtocolVersionNV);
        Assert.Equal(CommandRemoteNV.PrepareLink, Enum.Parse<CommandRemoteNV>("PrepareLink"));
        Assert.Equal(CommandRemoteNV.GetSettings, Enum.Parse<CommandRemoteNV>("GetSettings"));
        Assert.Equal(CommandRemoteNV.SaveSettings, Enum.Parse<CommandRemoteNV>("SaveSettings"));
    }

    [Fact]
    public void Command_payload_round_trips_utf8_json()
    {
        const string requestId = "abc123";
        const string json = "{\"Theme\":\"Dark\",\"Monitor\":\"\\\\.\\DISPLAY1\"}";

        string frame = ProtocolRemoteNV.CreateCommandNV(
            requestId,
            CommandRemoteNV.SaveSettings,
            json);

        bool parsed = ProtocolRemoteNV.TryParseCommandNV(
            frame,
            out string parsedRequestId,
            out CommandRemoteNV command,
            out string payload,
            out string error);

        Assert.True(parsed, error);
        Assert.Equal(requestId, parsedRequestId);
        Assert.Equal(CommandRemoteNV.SaveSettings, command);
        Assert.Equal(json, payload);
    }

    [Fact]
    public async Task Frame_supports_payloads_larger_than_legacy_four_kibibytes()
    {
        string payload = new('N', 12 * 1024);

        await using var stream = new MemoryStream();

        await ProtocolRemoteNV.WriteFrameNVAsync(stream, payload);

        stream.Position = 0;

        string read = await ProtocolRemoteNV.ReadFrameNVAsync(stream);

        Assert.Equal(payload, read);
    }
}
