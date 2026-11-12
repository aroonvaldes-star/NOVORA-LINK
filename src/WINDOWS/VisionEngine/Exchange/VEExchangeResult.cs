namespace NOVORA.VisionEngine.Exchange;

public sealed record VEExchangeResult(
    bool Success,
    string Message,
    string? Source,
    string? Destination,
    string? Detail,
    long ItemCount = 0,
    long Bytes = 0)
{
    public static VEExchangeResult OkVE(
        string message,
        string? source = null,
        string? destination = null,
        string? detail = null,
        long itemCount = 0,
        long bytes = 0)
        => new(true, message, source, destination, detail, itemCount, bytes);

    public static VEExchangeResult FailVE(
        string message,
        string? source = null,
        string? destination = null,
        string? detail = null)
        => new(false, message, source, destination, detail);
}
