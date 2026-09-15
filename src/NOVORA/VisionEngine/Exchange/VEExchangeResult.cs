namespace NOVORA.VisionEngine.Exchange;

public sealed record VEExchangeResult(
    bool Success,
    string Message,
    string? Source,
    string? Destination,
    string? Detail)
{
    public static VEExchangeResult OkVE(string message, string? source = null, string? destination = null, string? detail = null)
        => new(true, message, source, destination, detail);
    public static VEExchangeResult FailVE(string message, string? source = null, string? destination = null, string? detail = null)
        => new(false, message, source, destination, detail);
}
