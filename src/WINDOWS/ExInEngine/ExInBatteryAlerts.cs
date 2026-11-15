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

    /// <summary>
    /// Actualiza el estado de batería y devuelve una alerta si procede.
    /// ⭐ OPTIMIZADO: Usa switch expressions y evita allocations innecesarias
    /// </summary>
    public ExInBatteryAlert? UpdateVE(ExInBatteryStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        
        if (!_statesVE.TryGetValue(status.ProfileKey, out AlertStateVE previous))
        {
            // Primera observación: inicializar estado
            _statesVE[status.ProfileKey] = new(
                status.Percent, 
                status.State, 
                true,  // lowArmed
                true,  // criticalArmed
                status.Percent is not 100 && status.State != ExInBatteryState.Charged  // fullArmed
            );
            return null;
        }

        int? percent = status.Percent;

        // ⭐ OPTIMIZADO: Usar variables locales antes de muchos accesos
        int? prevPercent = previous.Percent;
        bool lowArmed = previous.LowArmed || percent is > 20;
        bool criticalArmed = previous.CriticalArmed || percent is > 10;
        bool fullArmed = previous.FullArmed || 
            (percent is < 95 || status.State == ExInBatteryState.Charging && previous.State != ExInBatteryState.Charging);

        // ⭐ OPTIMIZADO: Switch expression en lugar de if-else cadena
        ExInBatteryAlertKind? kind = (status.State, percent, prevPercent) switch
        {
            // Critical: 5% o menos en batería
            (ExInBatteryState.OnBattery, <= 5, > 5) when criticalArmed 
                => ExInBatteryAlertKind.Critical5,
            
            // Low: 15% o menos en batería
            (ExInBatteryState.OnBattery, <= 15, > 15) when lowArmed 
                => ExInBatteryAlertKind.Low15,
            
            // Full: Cargado al 100% o estado Charged
            (_, 100, not 100) when fullArmed 
                => ExInBatteryAlertKind.Full100,
            
            (ExInBatteryState.Charged, _, not null) when fullArmed && previous.State != ExInBatteryState.Charged
                => ExInBatteryAlertKind.Full100,
            
            _ => null
        };

        // Actualizar flag según si generó alerta
        if (kind == ExInBatteryAlertKind.Critical5)
        {
            criticalArmed = false;
            lowArmed = false;
        }
        else if (kind == ExInBatteryAlertKind.Low15)
        {
            lowArmed = false;
        }
        else if (kind == ExInBatteryAlertKind.Full100)
        {
            fullArmed = false;
        }

        // Guardar nuevo estado
        _statesVE[status.ProfileKey] = new(percent, status.State, lowArmed, criticalArmed, fullArmed);

        return kind is null 
            ? null 
            : new(status.ProfileKey, status.ControllerName, kind.Value, percent, DateTimeOffset.UtcNow);
    }

    private readonly record struct AlertStateVE(
        int? Percent,
        ExInBatteryState State,
        bool LowArmed,
        bool CriticalArmed,
        bool FullArmed);
}
