namespace NOVORA.ExInEngine;

public enum ExInBatteryAlertKind
{
    Low15,
    Critical5,
    Full100
}

public sealed record ExInBatteryAlert(
    string ProfileKey,
    string ControllerName,
    ExInBatteryAlertKind Kind,
    int? Percent,
    DateTimeOffset CreatedAtUtc);

public sealed class ExInBatteryAlerts
{
    private readonly Dictionary<string, AlertStateVE> _statesVE = [];

    public ExInBatteryAlert? UpdateVE(ExInBatteryStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!_statesVE.TryGetValue(status.ProfileKey, out AlertStateVE previous))
        {
            _statesVE[status.ProfileKey] = new(status.Percent, status.State, true, true, status.Percent is not 100 && status.State != ExInBatteryState.Charged);
            return null;
        }
        int? percent = status.Percent;

        bool lowArmed = previous.LowArmed || percent is > 20;
        bool criticalArmed = previous.CriticalArmed || percent is > 10;
        bool fullArmed = previous.FullArmed || percent is < 95 ||
            status.State == ExInBatteryState.Charging && previous.State != ExInBatteryState.Charging;

        ExInBatteryAlertKind? kind = null;
        if (status.State == ExInBatteryState.OnBattery && percent is <= 5 && criticalArmed && previous.Percent is > 5)
        {
            kind = ExInBatteryAlertKind.Critical5;
            criticalArmed = false;
            lowArmed = false;
        }
        else if (status.State == ExInBatteryState.OnBattery && percent is <= 15 && lowArmed && previous.Percent is > 15)
        {
            kind = ExInBatteryAlertKind.Low15;
            lowArmed = false;
        }
        else if ((status.State == ExInBatteryState.Charged || percent == 100) && fullArmed &&
                 previous.State != ExInBatteryState.Charged && previous.Percent != 100)
        {
            kind = ExInBatteryAlertKind.Full100;
            fullArmed = false;
        }

        _statesVE[status.ProfileKey] = new(percent, status.State, lowArmed, criticalArmed, fullArmed);
        return kind is null ? null : new(status.ProfileKey, status.ControllerName, kind.Value, percent, DateTimeOffset.UtcNow);
    }

    private readonly record struct AlertStateVE(
        int? Percent,
        ExInBatteryState State,
        bool LowArmed,
        bool CriticalArmed,
        bool FullArmed);
}
