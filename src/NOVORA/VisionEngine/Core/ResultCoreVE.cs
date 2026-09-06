using System;

namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Resultado uniforme de las operaciones públicas de VisionEngine.
/// </summary>
public sealed record ResultCoreVE(
    bool Success,
    string Message,
    Exception? Exception = null)
{
    public static ResultCoreVE Ok(
        string message = "Operación completada.")
        => new(
            Success: true,
            Message: message,
            Exception: null);

    public static ResultCoreVE Fail(
        string message,
        Exception? exception = null)
        => new(
            Success: false,
            Message: message,
            Exception: exception);
}
