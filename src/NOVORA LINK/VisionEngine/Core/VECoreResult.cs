using System;

namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Resultado uniforme de las operaciones públicas de VisionEngine.
/// </summary>
public sealed record VECoreResult(
    bool Success,
    string Message,
    Exception? Exception = null)
{
    public static VECoreResult Ok(
        string message = "Operación completada.")
        => new(
            Success: true,
            Message: message,
            Exception: null);

    public static VECoreResult Fail(
        string message,
        Exception? exception = null)
        => new(
            Success: false,
            Message: message,
            Exception: exception);
}
