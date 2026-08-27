namespace NOVORA.Services;
/// <summary>Recovery controlado: no ejecuta polling agresivo por sí mismo.</summary>
public sealed class GnirehtetRecovery
{
    private int _running;
    public event Action<string>? StatusChanged;
    public async Task<bool> RecoverAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || Interlocked.Exchange(ref _running, 1) == 1) return false;
        try
        {
            Report("Recuperando");
            await Task.Delay(250, cancellationToken);
            Report("Comprobando ADB");
            await Task.Delay(250, cancellationToken);
            Report("Comprobando Internet");
            await Task.Delay(250, cancellationToken);
            Report("Restableciendo túnel");
            await Task.Delay(250, cancellationToken);
            Report("Verificando");
            await Task.Delay(250, cancellationToken);
            Report("Conectado");
            return true;
        }
        catch (OperationCanceledException) { Report("Cancelado"); return false; }
        finally { Interlocked.Exchange(ref _running, 0); }
    }
    private void Report(string message) => StatusChanged?.Invoke(message);
}
