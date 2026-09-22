using NOVORA.ExInEngine;
using NOVORA.Service;
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
}
