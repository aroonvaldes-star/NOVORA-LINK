using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NOVORA.NVIDIA;

public sealed class NLNVIDIAApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly NLNVIDIAApiOptions _options;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public NLNVIDIAApiClient(
        NLNVIDIAApiOptions? options = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? NLNVIDIAApiOptions.FromEnvironment();
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.Model);

    public async Task<string> CompleteAsync(
        string prompt,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        _options.Validate();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new
            {
                role = "system",
                content = systemPrompt.Trim()
            });
        }

        messages.Add(new
        {
            role = "user",
            content = prompt.Trim()
        });

        var payload = new
        {
            model = _options.Model,
            messages,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response =
            await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"NVIDIA API respondió {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                $"Detalle: {TrimError(body)}",
                null,
                response.StatusCode);
        }

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("choices", out JsonElement choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("La respuesta de NVIDIA no contiene choices[0].message.content.");
    }

    private static string TrimError(string value)
    {
        const int maxLength = 400;
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "sin cuerpo de respuesta"
            : value.ReplaceLineEndings(" ").Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
