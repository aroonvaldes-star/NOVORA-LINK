using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInBattery
{
    [Fact]
    public void Alerts_cross_once_and_rearm()
    {
        ExInBatteryAlerts alerts = new();
        Assert.Null(alerts.UpdateVE(OnBatteryVE(16)));
        Assert.Equal(ExInBatteryAlertKind.Low15, alerts.UpdateVE(OnBatteryVE(15))!.Kind);
        Assert.Null(alerts.UpdateVE(OnBatteryVE(14)));
        Assert.Equal(ExInBatteryAlertKind.Critical5, alerts.UpdateVE(OnBatteryVE(5))!.Kind);
        Assert.Equal(ExInBatteryAlertKind.Full100, alerts.UpdateVE(UpdateVE(100, ExInBatteryState.Charged))!.Kind);
        Assert.Null(alerts.UpdateVE(UpdateVE(100, ExInBatteryState.Charged)));
        Assert.Null(alerts.UpdateVE(UpdateVE(25, ExInBatteryState.OnBattery)));
        Assert.Equal(ExInBatteryAlertKind.Low15, alerts.UpdateVE(OnBatteryVE(15))!.Kind);
    }

    [Fact]
    public void Unknown_percentage_never_invents_an_alert()
    {
        ExInBatteryAlerts alerts = new();
        Assert.Null(alerts.UpdateVE(UpdateVE(-1, ExInBatteryState.Unknown)));
        Assert.Null(alerts.UpdateVE(UpdateVE(-1, ExInBatteryState.Charging)));
    }

    [Fact]
    public void New_charging_cycle_rearms_full_alert()
    {
        ExInBatteryAlerts alerts = new();
        alerts.UpdateVE(UpdateVE(90, ExInBatteryState.Charging));
        Assert.Equal(ExInBatteryAlertKind.Full100, alerts.UpdateVE(UpdateVE(100, ExInBatteryState.Charged))!.Kind);
        alerts.UpdateVE(UpdateVE(99, ExInBatteryState.Charging));
        Assert.Equal(ExInBatteryAlertKind.Full100, alerts.UpdateVE(UpdateVE(100, ExInBatteryState.Charged))!.Kind);
    }

    [Fact]
    public void Initial_or_unknown_threshold_is_not_a_crossing()
    {
        ExInBatteryAlerts initial = new();
        Assert.Null(initial.UpdateVE(OnBatteryVE(5)));

        ExInBatteryAlerts unknown = new();
        Assert.Null(unknown.UpdateVE(UpdateVE(-1, ExInBatteryState.Unknown)));
        Assert.Null(unknown.UpdateVE(OnBatteryVE(5)));
    }

    [Fact]
    public void Charging_regression_does_not_emit_low_alerts()
    {
        ExInBatteryAlerts alerts = new();
        alerts.UpdateVE(UpdateVE(16, ExInBatteryState.Charging));
        Assert.Null(alerts.UpdateVE(UpdateVE(15, ExInBatteryState.Charging)));
        Assert.Null(alerts.UpdateVE(UpdateVE(5, ExInBatteryState.Charging)));
    }

    [Fact]
    public void Profiles_have_independent_threshold_state()
    {
        ExInBatteryAlerts alerts = new();
        alerts.UpdateVE(ExInBatteryStatus.CreateVE("xbox-a", "Xbox", 16, ExInBatteryState.OnBattery));
        alerts.UpdateVE(ExInBatteryStatus.CreateVE("ds4-a", "DS4", 16, ExInBatteryState.OnBattery));
        Assert.Equal(ExInBatteryAlertKind.Low15,
            alerts.UpdateVE(ExInBatteryStatus.CreateVE("xbox-a", "Xbox", 15, ExInBatteryState.OnBattery))!.Kind);
        Assert.Equal(ExInBatteryAlertKind.Low15,
            alerts.UpdateVE(ExInBatteryStatus.CreateVE("ds4-a", "DS4", 15, ExInBatteryState.OnBattery))!.Kind);
    }

    private static ExInBatteryStatus OnBatteryVE(int percent) => UpdateVE(percent, ExInBatteryState.OnBattery);
    private static ExInBatteryStatus UpdateVE(int percent, ExInBatteryState state)
        => ExInBatteryStatus.CreateVE("xbox-a", "Xbox", percent, state);
}
