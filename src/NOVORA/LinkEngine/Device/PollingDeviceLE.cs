using System;

namespace NOVORA.LinkEngine.Device;

/// <summary>
/// Estado operativo utilizado para decidir cuánto polling ADB
/// necesita LinkEngine.
///
/// Principio:
///
/// HEALTHY:
///     LinkEngine CONTROL es la fuente de verdad.
///     ADB permanece prácticamente dormido.
///
/// DEGRADED:
///     Aumentamos comprobaciones porque el Control Plane
///     empieza a mostrar problemas.
///
/// RECOVERY:
///     ADB vuelve temporalmente a participar para reconstruir
///     transporte, reverse o sesión.
///
/// DISCONNECTED:
///     Se realizan comprobaciones lentas esperando que
///     el dispositivo reaparezca.
/// </summary>
public enum PollingStateLE
{
    Bootstrap = 0,
    Healthy = 1,
    Degraded = 2,
    Recovery = 3,
    Disconnected = 4
}

/// <summary>
/// Política central de polling de NOVORA LinkEngine.
///
/// Ninguna UI debería decidir por su cuenta cada cuánto ejecutar
/// comandos ADB.
/// </summary>
public sealed class PollingDeviceLE
{
    /*
     * ============================================================
     * INTERVALOS
     * ============================================================
     *
     * HEALTHY:
     *     LinkEngine funciona correctamente.
     *     ADB sólo realiza una comprobación de respaldo ocasional.
     *
     * DEGRADED:
     *     El heartbeat/control empieza a mostrar problemas.
     *
     * RECOVERY:
     *     Estamos recuperando activamente la sesión.
     *
     * DISCONNECTED:
     *     El dispositivo desapareció y esperamos su regreso.
     */

    public TimeSpan BootstrapIntervalLE { get; } =
        TimeSpan.FromSeconds(2);

    public TimeSpan HealthyIntervalLE { get; } =
        TimeSpan.FromSeconds(30);

    public TimeSpan DegradedIntervalLE { get; } =
        TimeSpan.FromSeconds(5);

    public TimeSpan RecoveryIntervalLE { get; } =
        TimeSpan.FromSeconds(2);

    public TimeSpan DisconnectedIntervalLE { get; } =
        TimeSpan.FromSeconds(10);

    /*
     * ============================================================
     * HEARTBEAT
     * ============================================================
     *
     * Esto NO es polling ADB.
     *
     * Es un mensaje muy pequeño dentro del socket CONTROL
     * que ya está abierto.
     */

    public TimeSpan ControlHeartbeatIntervalLE { get; } =
        TimeSpan.FromSeconds(2);

    public TimeSpan ControlHeartbeatTimeoutLE { get; } =
        TimeSpan.FromSeconds(8);

    /*
     * ============================================================
     * GET INTERVAL
     * ============================================================
     */

    public TimeSpan GetAdbPollingIntervalLE(
        PollingStateLE state)
    {
        return state switch
        {
            PollingStateLE.Bootstrap =>
                BootstrapIntervalLE,

            PollingStateLE.Healthy =>
                HealthyIntervalLE,

            PollingStateLE.Degraded =>
                DegradedIntervalLE,

            PollingStateLE.Recovery =>
                RecoveryIntervalLE,

            PollingStateLE.Disconnected =>
                DisconnectedIntervalLE,

            _ =>
                HealthyIntervalLE
        };
    }

    /*
     * ============================================================
     * SHOULD POLL
     * ============================================================
     *
     * Permite que ManagerRecoveryLE decida si realmente hace falta
     * ejecutar una comprobación ADB.
     */

    public bool ShouldPollAdbLE(
        PollingStateLE state,
        DateTimeOffset lastAdbPollUtc,
        DateTimeOffset nowUtc)
    {
        if (lastAdbPollUtc == default)
        {
            return true;
        }

        TimeSpan interval =
            GetAdbPollingIntervalLE(
                state);

        TimeSpan elapsed =
            nowUtc -
            lastAdbPollUtc;

        return elapsed >= interval;
    }

    /*
     * ============================================================
     * CONTROL HEALTH
     * ============================================================
     *
     * Mientras recibamos heartbeat dentro del timeout,
     * CONTROL es nuestra fuente de verdad.
     */

    public bool IsControlHealthyLE(
        DateTimeOffset lastHeartbeatUtc,
        DateTimeOffset nowUtc)
    {
        if (lastHeartbeatUtc == default)
        {
            return false;
        }

        TimeSpan elapsed =
            nowUtc -
            lastHeartbeatUtc;

        return elapsed <=
            ControlHeartbeatTimeoutLE;
    }

    /*
     * ============================================================
     * RESOLVE STATE
     * ============================================================
     */

    public PollingStateLE ResolveStateLE(
        bool deviceKnown,
        bool recovering,
        bool controlConnected,
        DateTimeOffset lastHeartbeatUtc,
        DateTimeOffset nowUtc)
    {
        if (!deviceKnown)
        {
            return PollingStateLE.Disconnected;
        }

        if (recovering)
        {
            return PollingStateLE.Recovery;
        }

        if (!controlConnected)
        {
            return PollingStateLE.Degraded;
        }

        bool controlHealthy =
            IsControlHealthyLE(
                lastHeartbeatUtc,
                nowUtc);

        if (!controlHealthy)
        {
            return PollingStateLE.Degraded;
        }

        return PollingStateLE.Healthy;
    }
}