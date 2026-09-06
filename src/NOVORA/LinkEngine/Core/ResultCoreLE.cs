using System;

namespace NOVORA.LinkEngine.Core;

public sealed record ResultCoreLE(
    bool Success,
    string Message,
    Exception? Exception = null)
{
    public static ResultCoreLE Ok(string message = "Operación completada.")
        => new(true, message);

    public static ResultCoreLE Fail(
        string message,
        Exception? exception = null)
        => new(false, message, exception);
}
