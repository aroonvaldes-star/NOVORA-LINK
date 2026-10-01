using System.Net;
using System.Text;
using NOVORA.NVIDIA;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestNvidiaApiTests
{
    [Fact]
    public async Task CompleteAsync_sends_bearer_request_and_returns_content()
    {
        string? authScheme = null;
        string? authParameter = null;
        HttpMethod? method = null;
        Uri? requestUri = null;
        string? requestPayload = null;
        var handler = new StubHandler(async request =>
        {
            authScheme = request.Headers.Authorization?.Scheme;
            authParameter = request.Headers.Authorization?.Parameter;
            method = request.Method;
            requestUri = request.RequestUri;
            requestPayload = await request.Content!.ReadAsStringAsync();
            string json = """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "respuesta NVIDIA"
                  }
                }
              ]
            }
            """;

            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var http = new HttpClient(handler);
        var options = new NLNVIDIAApiOptions(
            "test-key",
            "nvidia/test-model",
            new Uri("https://integrate.api.nvidia.com/v1/chat/completions"),
            TimeSpan.FromSeconds(5));

        using var client = new NLNVIDIAApiClient(options, http);
        string result = await client.CompleteAsync("hola", "sistema");

        Assert.Equal("respuesta NVIDIA", result);
        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", captured.Headers.Authorization?.Parameter);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(options.Endpoint, captured.RequestUri);

        string payload = await captured.Content!.ReadAsStringAsync();
        Assert.Contains(""model":"nvidia/test-model"", payload);
        Assert.Contains(""role":"system"", payload);
        Assert.Contains(""role":"user"", payload);
        Assert.Contains(""stream":false", payload);
    }

    [Fact]
    public async Task CompleteAsync_rejects_missing_credentials_before_network()
    {
        int calls = 0;
        var handler = new StubHandler(request =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var http = new HttpClient(handler);
        var options = new NLNVIDIAApiOptions(
            null,
            "nvidia/test-model",
            NLNVIDIAApiOptions.DefaultEndpoint,
            TimeSpan.FromSeconds(5));

        using var client = new NLNVIDIAApiClient(options, http);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("hola"));

        Assert.Contains(NLNVIDIAApiOptions.ApiKeyEnvironmentVariable, error.Message);
        Assert.Equal(0, calls);
        Assert.False(client.IsConfigured);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
