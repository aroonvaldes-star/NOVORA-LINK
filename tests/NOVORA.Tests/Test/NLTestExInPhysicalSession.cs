using NOVORA.ExInEngine;
using NOVORA.Integration;
using NOVORA.Service;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Server;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInPhysicalSession
{
    [Fact]
    public async Task ExIn_control_session_creates_android_output_when_physical_serial_is_provided()
    {
        string? serial = Environment.GetEnvironmentVariable("NOVORA_PHYSICAL_ANDROID_SERIAL");
        if (string.IsNullOrWhiteSpace(serial)) return;

        NLServiceNovoraPaths paths = new(AppContext.BaseDirectory);
        NLServiceADB adb = new(paths);
        await using ExInCoreEngine engine = new(paths);
        await engine.InitializeAsync();
        await using ExInControlSession session = new(engine, paths, adb);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await session.StartAsync(serial, timeout.Token);

        Assert.True(session.IsRunning);
        Assert.Equal(serial, session.Serial);
        string androidInput = await adb.ExecuteRawAsync(serial, "dumpsys input", timeout.Token);
        Assert.Contains("Identifier: vendor 1118, product 654", androidInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_vision_engine_preserves_the_exin_control_session()
    {
        string? serial = Environment.GetEnvironmentVariable("NOVORA_PHYSICAL_ANDROID_SERIAL");
        if (string.IsNullOrWhiteSpace(serial)) return;

        NLServiceNovoraPaths paths = new(AppContext.BaseDirectory);
        NLServiceADB adb = new(paths);
        await using ExInCoreEngine exIn = new(paths);
        await exIn.InitializeAsync();
        await using ExInControlSession exInSession = new(exIn, paths, adb);
        await using VECoreEngine vision = new(paths, adb);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));

        await exInSession.StartAsync(serial, timeout.Token);
        Assert.True(exInSession.IsRunning);

        VECoreResult initialized = await vision.InitializeAsync(timeout.Token);
        Assert.True(initialized.Success, initialized.Message);
        VEServerOptions options = VEServerOptions.CreateDefaultVE() with
        {
            AudioEnabled = false,
            AudioPlaybackEnabled = false,
            MaxFps = 30
        };
        VECoreResult started = await vision.StartAsync(serial, options, timeout.Token);
        Assert.True(started.Success, started.Message);

        await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
        Assert.True(exInSession.IsRunning);
        Assert.Equal(serial, exInSession.Serial);

        string processes = await adb.ExecuteRawAsync(
            serial,
            "ps -A -o PID,NAME,ARGS",
            timeout.Token);
        Assert.Contains("video=false", processes, StringComparison.Ordinal);
        Assert.Contains("video_bit_rate=", processes, StringComparison.Ordinal);
        Assert.True(processes.Split("novora-vision-server-", StringSplitOptions.None).Length >= 3);
    }
}
