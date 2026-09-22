using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInDiagnostics
{
    [Theory]
    [InlineData(0, 120, ExInHealth.Healthy)]
    [InlineData(2400, 120, ExInHealth.CorrectableByCalibration)]
    [InlineData(9000, 4000, ExInHealth.ProbableHardwareFault)]
    public void Guided_session_classifies_rest_offset(short center, short noise, ExInHealth expected)
    {
        ExInDiagnosticSession session = CreateSessionVE(center, noise);
        Assert.Equal(expected, session.CompleteVE(TimeSpan.FromSeconds(3)).Classification);
    }

    [Fact]
    public void Active_gameplay_is_not_observed_without_a_session()
    {
        Assert.Null(typeof(ExInManager).GetMethod("ObserveDiagnosticsContinuouslyVE"));
    }

    [Fact]
    public void Window_is_bounded()
    {
        ExInDiagnosticSession session = new("xbox-a");
        for (int index = 0; index < 1000; index++) session.ObserveVE(default, default, TimeSpan.FromMilliseconds(index));
        Assert.Equal(256, session.RestSamplesVE);
    }

    [Fact]
    public void Quiet_rest_needs_elapsed_time_not_fake_events()
    {
        ExInDiagnosticSession session = new("xbox-a");
        session.ObserveVE(default, default, TimeSpan.Zero);
        AddFullTravelVE(session, TimeSpan.FromSeconds(2));

        Assert.Equal(ExInHealth.Healthy, session.CompleteVE(TimeSpan.FromSeconds(4)).Classification);
    }

    [Fact]
    public void Session_expires_and_cannot_consume_later_gameplay()
    {
        ExInDiagnosticSession session = new("xbox-a");
        session.ObserveVE(default, default, TimeSpan.Zero);

        Assert.True(session.IsExpiredVE(TimeSpan.FromSeconds(46)));
        session.ObserveVE(new(short.MaxValue, 0, 0, 0, 0, 0, 0), default, TimeSpan.FromSeconds(46));
        Assert.Equal(1, session.RestSamplesVE);
    }

    [Fact]
    public void Single_spike_does_not_create_hardware_fault()
    {
        ExInDiagnosticSession session = new("xbox-a");
        for (int index = 0; index < 64; index++)
            session.ObserveVE(new(index == 30 ? short.MaxValue : (short)0, 0, 0, 0, 0, 0, 0), default, TimeSpan.FromMilliseconds(index));
        AddFullTravelVE(session);
        Assert.NotEqual(ExInHealth.ProbableHardwareFault, session.CompleteVE(TimeSpan.FromSeconds(2)).Classification);
    }

    [Fact]
    public void Reduced_axis_travel_requests_review()
    {
        ExInDiagnosticSession session = new("xbox-a");
        for (int index = 0; index < 64; index++) session.ObserveVE(default, default, TimeSpan.FromMilliseconds(index));
        session.BeginTravelVE(TimeSpan.FromSeconds(1));
        for (int index = 0; index < 16; index++)
            session.ObserveVE(new((short)(index % 2 == 0 ? -4000 : 4000), short.MinValue, short.MaxValue, short.MinValue,
                index % 2 == 0 ? (short)0 : short.MaxValue, index % 2 == 0 ? (short)0 : short.MaxValue, 0), default, TimeSpan.FromMilliseconds(1000 + index * 100));
        Assert.Equal(ExInHealth.ReviewRecommended, session.CompleteVE(TimeSpan.FromSeconds(3)).Classification);
    }

    [Fact]
    public void Privacy_protection_removes_raw_and_corrected_values()
    {
        ExInDiagnosticStatus status = CreateSessionVE(0, 0).CompleteVE(TimeSpan.FromSeconds(3));
        Assert.Equal(default, status.ProtectVE().RawState);
        Assert.Equal(default, status.ProtectVE().CorrectedState);
    }

    [Fact]
    public void Button_pressed_after_start_is_not_called_stuck()
    {
        ExInDiagnosticSession session = new("xbox-a");
        session.ObserveVE(default, default, TimeSpan.Zero);
        session.ObserveVE(new(0, 0, 0, 0, 0, 0, ExInButtons.South), default, TimeSpan.FromMilliseconds(500));
        AddFullTravelVE(session, TimeSpan.FromSeconds(2), ExInButtons.South);

        Assert.NotEqual(ExInHealth.ProbableHardwareFault, session.CompleteVE(TimeSpan.FromSeconds(4)).Classification);
    }

    [Fact]
    public void Button_held_from_start_and_never_released_is_probable_fault()
    {
        ExInDiagnosticSession session = new("xbox-a");
        session.ObserveVE(new(0, 0, 0, 0, 0, 0, ExInButtons.South), default, TimeSpan.Zero);
        AddFullTravelVE(session, TimeSpan.FromSeconds(2), ExInButtons.South);

        Assert.Equal(ExInHealth.ProbableHardwareFault, session.CompleteVE(TimeSpan.FromSeconds(4)).Classification);
    }

    private static ExInDiagnosticSession CreateSessionVE(short center, short noise)
    {
        ExInDiagnosticSession session = new("xbox-a");
        for (int index = 0; index < 64; index++)
        {
            short value = checked((short)(center + (index % 2 == 0 ? noise : -noise)));
            session.ObserveVE(new(value, 0, 0, 0, 0, 0, 0), default, TimeSpan.FromMilliseconds(index * 8));
        }
        AddFullTravelVE(session);
        return session;
    }

    private static void AddFullTravelVE(
        ExInDiagnosticSession session,
        TimeSpan? start = null,
        ExInButtons buttons = ExInButtons.None)
    {
        TimeSpan phaseStart = start ?? TimeSpan.FromSeconds(1);
        session.BeginTravelVE(phaseStart);
        for (int index = 0; index < 16; index++)
        {
            bool minimum = index % 2 == 0;
            session.ObserveVE(new(
                minimum ? short.MinValue : short.MaxValue,
                minimum ? short.MinValue : short.MaxValue,
                minimum ? short.MinValue : short.MaxValue,
                minimum ? short.MinValue : short.MaxValue,
                minimum ? (short)0 : short.MaxValue,
                minimum ? (short)0 : short.MaxValue,
                buttons), default, phaseStart + TimeSpan.FromMilliseconds(index * 100));
        }
    }
}
