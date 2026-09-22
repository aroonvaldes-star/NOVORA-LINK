namespace NOVORA.ExInEngine;

public enum ExInBatteryState
{
    Unknown,
    OnBattery,
    Charging,
    Charged,
    NoBattery
}

public sealed record ExInBatteryStatus(
    string ProfileKey,
    string ControllerName,
    int? Percent,
    ExInBatteryState State,
    DateTimeOffset UpdatedAtUtc)
{
    public static ExInBatteryStatus CreateVE(
        string profileKey,
        string controllerName,
        int percent,
        ExInBatteryState state)
        => new(profileKey, controllerName, percent is >= 0 and <= 100 ? percent : null, state, DateTimeOffset.UtcNow);
}
