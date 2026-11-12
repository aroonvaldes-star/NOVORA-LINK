using System.Net;
using System.Text.Json;
using NOVORA.Control;
using NOVORA.Linux.Control;
using NOVORA.Linux.VisionEngine;

/// <summary>Mobile-data profiles: settings, shared contract parity, offers and «mobileDataV1» negotiation.</summary>
internal static class MobileConnectionChecks
{
    public static async Task RunAsync()
    {
        CheckSettingsMatchSharedContract();
        CheckCaptureValidation();
        CheckOfferCapabilities();
        await CheckNegotiationAsync();
        Console.WriteLine("Mobile-data profiles, shared contract parity, capture floors, offer capabilities and mobileDataV1 negotiation passed.");
    }

    private static void CheckSettingsMatchSharedContract()
    {
        Assert(LinuxVisionSettings.ParseBitrate("120K") == 120_000 && LinuxVisionSettings.ParseBitrate("8M") == 8_000_000,
            "Bitrate parsing must accept K and M suffixes.");
        foreach ((string id, NLControlMobileProfile profile) in new[]
                 { ("MobileExtreme", NLControlMobileProfile.Extreme), ("MobileEconomy", NLControlMobileProfile.Economy) })
        {
            LinuxVisionSettings settings = LinuxVisionSettings.ForProfile(id);
            NLControlMobileLimits limits = NLControlMobileData.Limits(profile);
            Assert(settings.IsMobile && settings.BitrateBits == limits.Bitrate && settings.Fps == limits.Fps &&
                settings.MaxSize == limits.MaxSize && settings.AudioEnabled == limits.Audio,
                $"Linux profile {id} diverged from the shared mobile contract.");
        }
        Assert(LinuxVisionSettings.ForProfile("MobileEconomy").Fps == 30, "Economy runs at 30 fps.");
        Assert(LinuxVisionSettings.ProfileId("Móvil extremo") == "MobileExtreme", "Spanish profile label must map.");
        AssertInvalid(() => (LinuxVisionSettings.Default with { Bitrate = "120K" }).Validate(),
            "A legacy profile must not accept a mobile bitrate.");
        AssertInvalid(() => (LinuxVisionSettings.ForProfile("MobileExtreme") with { MaxSize = 1920 }).Validate(),
            "A mobile profile must not accept a large capture.");
        AssertInvalid(() => (LinuxVisionSettings.Default with { Fps = 5 }).Validate(),
            "A legacy profile must keep its frame-rate floor.");
    }

    private static void CheckCaptureValidation()
    {
        LinuxCaptureCapabilities.ValidateCapture(120_000, 480, 5, mobileData: true);
        LinuxCaptureCapabilities.ValidateCapture(250_000, 720, 30, mobileData: true);
        LinuxCaptureCapabilities.ValidateCapture(8_000_000, 1920, 60, mobileData: false);
        AssertInvalid(() => LinuxCaptureCapabilities.ValidateCapture(120_000, 480, 5, mobileData: false),
            "Without mobileDataV1 the legacy floors must remain.");
        AssertInvalid(() => LinuxCaptureCapabilities.ValidateCapture(99_999, 480, 5, mobileData: true),
            "The mobile floor is 100 kbit/s.");
        AssertInvalid(() => LinuxCaptureCapabilities.ValidateCapture(120_000, 1080, 5, mobileData: true),
            "Mobile captures are limited to 480/720 px.");
    }

    private static void CheckOfferCapabilities()
    {
        var legacy = new LinuxVideoSourceOffer(1, "t", 8_000_000, 1920, 60, 2, "c", 3, "a", true, true);
        using JsonDocument legacyJson = JsonDocument.Parse(JsonSerializer.Serialize(legacy));
        Assert(legacyJson.RootElement.GetProperty("CaptureCapabilities").ValueKind == JsonValueKind.Null,
            "A legacy offer must not announce capture capabilities.");
        var mobile = legacy with { Bitrate = 120_000, MaxSize = 480, Fps = 5, AudioEnabled = false,
            CaptureGeneration = 4, CaptureCapabilities = LinuxCaptureCapabilities.Mobile };
        // Android deserializes the same JSON into NLControlNetworkCapabilities.
        var android = JsonSerializer.Deserialize<NLControlNetworkCapabilities>(
            JsonSerializer.Serialize(mobile.CaptureCapabilities))!;
        Assert(android.Version == 1 && android.MobileData && android.CaptureFramePacing && !android.LiveVideoBitrate,
            "Android must read mobileDataV1 and frame pacing from the Linux offer.");
    }

    private static async Task CheckNegotiationAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "novora-mobile-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new LinuxTrustStore(directory);
            await using var server = new LinuxControlServer(IPAddress.Loopback, store, port: 0, allowLoopbackForTests: true);
            server.Start();
            using var client = await ConnectionSelectionChecks.ConnectAsync(server.Invitation.Host, server.Invitation.Port, store.Fingerprint);
            await ConnectionSelectionChecks.PairAsync(client, "pair.lan", server.Invitation.Code);
            long id = 1;
            async Task<ControlReply> Send(string action, string? value = null, long revision = 0)
            {
                await ControlProtocol.WriteAsync(client, new ControlRequest(1, ++id, action, value, revision), CancellationToken.None);
                return await ControlProtocol.ReadAsync<ControlReply>(client, CancellationToken.None);
            }
            string mobile = JsonSerializer.Serialize(new ControlVideoChanges("MobileExtreme", "8M", "1920", "60", "Pc", "0"));
            ControlReply state = await Send("get");
            Assert(state.Snapshot!.Profiles.Any(option => option.Value == "MobileExtreme") &&
                state.Snapshot.Bitrates.Any(option => option.Value == "120K"),
                "The PC must advertise mobile profiles and bitrates to LINK Control.");
            ControlReply refused = await Send("applyVideoSettings", mobile, state.Snapshot.Revision);
            Assert(!refused.Success && refused.Message.Contains("1.4.125", StringComparison.Ordinal) &&
                refused.Snapshot?.Bitrate == "8M", "An older LINK Control must not receive a mobile profile.");
            ControlReply negotiated = await Send("network.capabilities",
                "{\"Version\":1,\"FilesWireVersions\":[1,2],\"MobileData\":true}");
            Assert(negotiated.Success && server.PeerSupportsMobileData, "mobileDataV1 was not negotiated.");
            ControlReply applied = await Send("applyVideoSettings", mobile, negotiated.Snapshot!.Revision);
            Assert(applied.Success && applied.Snapshot?.Bitrate == "120K" && applied.Snapshot.VideoSettings?.Resolution == "480" &&
                applied.Snapshot.AudioOutput == "Disabled",
                "A mobile profile must apply its fixed bitrate, size and muted audio: " + applied.Message);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void AssertInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
