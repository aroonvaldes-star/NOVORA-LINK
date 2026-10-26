using System;

namespace NOVORA.LinkEngine.Core;

public sealed record LECoreResult(
    bool Success,
    string Message,
    Exception? Exception = null)
{
    public static LECoreResult Ok(string message = "Operación completada.")
        => new(true, message);

    public static LECoreResult Fail(
        string message,
        Exception? exception = null)
        => new(false, message, exception);
}
