using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;

namespace NOVORA.AndroidVpn;

/// <summary>
/// Mantiene el Wi-Fi en modo de baja latencia y la CPU activa mientras el túnel LE LAN está arriba.
/// Solo afecta al teléfono: no genera tráfico ni cambia nada en el router.
/// </summary>
internal sealed class NLAndroidLowLatencyLock
{
    private readonly WifiManager.WifiLock? _wifi;
    private readonly PowerManager.WakeLock? _cpu;

    private NLAndroidLowLatencyLock(WifiManager.WifiLock? wifi, PowerManager.WakeLock? cpu)
    {
        _wifi = wifi;
        _cpu = cpu;
    }

    public static NLAndroidLowLatencyLock Acquire(Context context)
    {
        WifiManager.WifiLock? wifi = null;
        PowerManager.WakeLock? cpu = null;
        try
        {
            if (context.GetSystemService(Context.WifiService) is WifiManager manager &&
                OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                wifi = manager.CreateWifiLock(WifiMode.FullLowLatency, "NOVORA-LE-LowLatency");
                wifi?.SetReferenceCounted(false);
                wifi?.Acquire();
            }
        }
        catch
        {
            // Sin el lock el túnel sigue funcionando; solo pierde la mejora de latencia.
        }

        try
        {
            if (context.GetSystemService(Context.PowerService) is PowerManager power)
            {
                cpu = power.NewWakeLock(WakeLockFlags.Partial, "NOVORA-LE-Tunnel");
                cpu?.SetReferenceCounted(false);
                cpu?.Acquire();
            }
        }
        catch
        {
            // Igual que el Wi-Fi: si falla, el túnel no se detiene.
        }

        return new NLAndroidLowLatencyLock(wifi, cpu);
    }

    public void Release()
    {
        try { if (_wifi is { IsHeld: true }) _wifi.Release(); } catch { }
        try { if (_cpu is { IsHeld: true }) _cpu.Release(); } catch { }
        try { _wifi?.Dispose(); } catch { }
        try { _cpu?.Dispose(); } catch { }
    }
}
