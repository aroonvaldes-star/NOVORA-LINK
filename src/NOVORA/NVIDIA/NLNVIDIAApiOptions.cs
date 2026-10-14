namespace NOVORA.NVIDIA;

public sealed record NLNVIDIAApiOptions(
    string? ApiKey,
    string? Model,
    Uri Endpoint,
    TimeSpan Timeout)
{
    public const string ApiKeyEnvironmentVariable = "NVIDIA_API_KEY";
    public const string ModelEnvironmentVariable = "NVIDIA_MODEL";
    public const string EndpointEnvironmentVariable = "NVIDIA_API_ENDPOINT";

    public static readonly Uri DefaultEndpoint =
        new("https://integrate.api.nvidia.com/v1/chat/completions", UriKind.Absolute);

    public static NLNVIDIAApiOptions FromEnvironment()
    {
        string? endpointValue = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);
        Uri endpoint = Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? parsed)
            ? parsed
            : DefaultEndpoint;

        return new NLNVIDIAApiOptions(
            Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
            Environment.GetEnvironmentVariable(ModelEnvironmentVariable),
            endpoint,
            TimeSpan.FromSeconds(45));
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                $"Falta {ApiKeyEnvironmentVariable}. Configura la API key de NVIDIA fuera del código.");

        if (string.IsNullOrWhiteSpace(Model))
            throw new InvalidOperationException(
                $"Falta {ModelEnvironmentVariable}. Configura el identificador del modelo NVIDIA NIM.");

        if (!Endpoint.IsAbsoluteUri)
            throw new InvalidOperationException("El endpoint de NVIDIA debe ser una URI absoluta.");

        if (Timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("El timeout de NVIDIA debe ser mayor que cero.");
    }
}
