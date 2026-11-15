namespace NOVORA.ExInEngine;

public sealed record ExInModeResult(
    bool Success,
    ExInInputMode ActiveMode,
    long Generation,
    string Message,
    string? Error = null);
