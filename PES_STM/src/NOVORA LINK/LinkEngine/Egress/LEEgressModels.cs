namespace NOVORA.LinkEngine.Egress;

public enum LEEgressStates { Idle, Connecting, Live, Reconnecting, Paused, Error }

/// <summary>Destino RTMP(S). La clave nunca se registra ni se muestra.</summary>
public sealed record LEEgressTarget(string ServerUrl, string StreamKey, int AudioBitrate = 128000)
{
    public Uri ServerUri
    {
        get
        {
            if (!Uri.TryCreate(ServerUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("rtmp" or "rtmps") || uri.AbsolutePath.Trim('/').Length == 0)
                throw new InvalidOperationException("El servidor debe ser rtmp:// o rtmps:// con aplicación (por ejemplo rtmp://servidor/app).");
            return uri;
        }
    }
    public bool IsComplete => !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(StreamKey);
    public override string ToString() => ServerUrl;
}

public sealed record LEEgressStatus(
    LEEgressStates State,
    string? Error,
    long BytesSent,
    long DroppedVideo,
    int Reconnects,
    bool AudioIncluded,
    DateTimeOffset? LiveSince)
{
    public bool Active => State is not (LEEgressStates.Idle or LEEgressStates.Error);
}
