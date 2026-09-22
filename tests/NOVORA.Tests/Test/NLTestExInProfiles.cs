using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInProfiles : IDisposable
{
    private readonly string _directoryVE = Path.Combine(Path.GetTempPath(), "NOVORA-ExInProfiles-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Profiles_are_isolated_by_controller()
    {
        ExInProfileStore store = new(_directoryVE);
        Assert.True(store.SaveVE(ValidVE("xbox-a", 900)));
        Assert.True(store.SaveVE(ValidVE("ds4-a", -700)));

        Assert.Equal(900, store.LoadVE("xbox-a")!.Center.LeftX);
        Assert.Equal(-700, store.LoadVE("ds4-a")!.Center.LeftX);
    }

    [Fact]
    public void Corrupt_profile_is_ignored()
    {
        Directory.CreateDirectory(_directoryVE);
        File.WriteAllText(Path.Combine(_directoryVE, "xbox-a.json"), "{ not-json");

        Assert.Null(new ExInProfileStore(_directoryVE).LoadVE("xbox-a"));
    }

    [Fact]
    public void Failed_calibration_preserves_previous_profile()
    {
        ExInProfileStore store = new(_directoryVE);
        ExInCalibrationProfile valid = ValidVE("xbox-a", 900);
        Assert.True(store.SaveVE(valid));
        ExInCalibrationProfile incomplete = valid with
        {
            Minimum = valid.Center,
            Maximum = valid.Center,
            CalibratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        };

        Assert.False(store.SaveVE(incomplete));
        Assert.Equal(valid.CalibratedAtUtc, store.LoadVE("xbox-a")!.CalibratedAtUtc);
    }

    [Fact]
    public void One_sided_stick_travel_is_rejected()
    {
        ExInCalibrationProfile profile = ValidVE("xbox-a", short.MinValue);

        Assert.False(profile.IsValidVE("xbox-a"));
    }

    [Fact]
    public void Trigger_rest_offset_is_removed()
    {
        ExInCalibrationProfile profile = ExInCalibrationProfile.CreateVE(
            "xbox-a",
            new(0, 0, 0, 0, 1200, 900, ExInButtons.None),
            new(short.MinValue, short.MinValue, short.MinValue, short.MinValue, 1200, 900, ExInButtons.None),
            new(short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, ExInButtons.None));

        ExInState corrected = profile.CorrectVE(profile.Center);
        Assert.Equal(0, corrected.LeftTrigger);
        Assert.Equal(0, corrected.RightTrigger);
    }

    [Fact]
    public void Negative_trigger_domain_is_rejected()
    {
        ExInCalibrationProfile profile = ExInCalibrationProfile.CreateVE(
            "xbox-a",
            new(0, 0, 0, 0, 0, 0, ExInButtons.None),
            new(short.MinValue, short.MinValue, short.MinValue, short.MinValue, -10000, -10000, ExInButtons.None),
            new(short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, 0, 0, ExInButtons.None));

        Assert.False(profile.IsValidVE("xbox-a"));
    }

    [Fact]
    public void Correction_recenters_and_scales_axes()
    {
        ExInCalibrationProfile profile = ValidVE("xbox-a", 1200);

        Assert.Equal(0, profile.CorrectVE(profile.Center).LeftX);
        Assert.Equal(short.MaxValue, profile.CorrectVE(profile.Maximum).LeftX);
        Assert.Equal(short.MinValue, profile.CorrectVE(profile.Minimum).LeftX);
    }

    private static ExInCalibrationProfile ValidVE(string key, short centerX)
        => ExInCalibrationProfile.CreateVE(
            key,
            new(centerX, 0, 0, 0, 0, 0, ExInButtons.None),
            new(short.MinValue, short.MinValue, short.MinValue, short.MinValue, 0, 0, ExInButtons.None),
            new(short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, short.MaxValue, ExInButtons.None));

    public void Dispose()
    {
        try { Directory.Delete(_directoryVE, recursive: true); } catch { }
    }
}
