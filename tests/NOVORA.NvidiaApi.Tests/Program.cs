using System.Net;
using System.Text;
using NOVORA.NVIDIA;

// Pruebas sin red: todas las respuestas salen de un HttpMessageHandler simulado.
int passed = 0;

NLNVIDIAApiOptions Options(string? apiKey = "test-key", string endpoint = "https://integrate.api.nvidia.com/v1/chat/completions",
    TimeSpan? timeout = null) =>
    new(apiKey, "nvidia/test-model", new Uri(endpoint), timeout ?? TimeSpan.FromSeconds(5));

HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
    new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

const string okJson = """{"choices":[{"message":{"role":"assistant","content":"respuesta NVIDIA"}}]}""";

await Check("Envía Bearer, POST y payload, y devuelve el contenido", async () =>
{
    string? scheme = null, parameter = null, payload = null;
    HttpMethod? method = null;
    Uri? uri = null;
    using var http = new HttpClient(new StubHandler(async (request, _) =>
    {
        scheme = request.Headers.Authorization?.Scheme;
        parameter = request.Headers.Authorization?.Parameter;
        method = request.Method;
        uri = request.RequestUri;
        payload = await request.Content!.ReadAsStringAsync();
        return Json(okJson);
    }));
    NLNVIDIAApiOptions options = Options();
    using var client = new NLNVIDIAApiClient(options, http);

    Require(await client.CompleteAsync("hola", "sistema") == "respuesta NVIDIA", "contenido");
    Require(scheme == "Bearer" && parameter == "test-key", "cabecera Authorization");
    Require(method == HttpMethod.Post && uri == options.Endpoint, "método y endpoint");
    Require(payload!.Contains("\"model\":\"nvidia/test-model\""), "modelo en payload");
    Require(payload.Contains("\"role\":\"system\"") && payload.Contains("\"role\":\"user\""), "mensajes");
    Require(payload.Contains("\"stream\":false"), "stream=false");
});

await Check("Sin API key falla antes de la red", async () =>
{
    int calls = 0;
    using var http = new HttpClient(new StubHandler((_, _) => { calls++; return Task.FromResult(Json(okJson)); }));
    using var client = new NLNVIDIAApiClient(Options(apiKey: null), http);

    var error = await Throws<InvalidOperationException>(() => client.CompleteAsync("hola"));
    Require(error.Message.Contains(NLNVIDIAApiOptions.ApiKeyEnvironmentVariable), "mensaje menciona la variable");
    Require(calls == 0 && !client.IsConfigured, "no hubo llamada");
});

await Check("Endpoint http se rechaza sin enviar la clave", async () =>
{
    int calls = 0;
    using var http = new HttpClient(new StubHandler((_, _) => { calls++; return Task.FromResult(Json(okJson)); }));
    // Se arma con UriBuilder para probar el rechazo sin dejar una URL sin TLS en el código.
    var plain = new UriBuilder(NLNVIDIAApiOptions.DefaultEndpoint) { Scheme = Uri.UriSchemeHttp, Port = -1 };
    using var client = new NLNVIDIAApiClient(Options(endpoint: plain.Uri.ToString()), http);

    var error = await Throws<InvalidOperationException>(() => client.CompleteAsync("hola"));
    Require(error.Message.Contains("https"), "mensaje menciona https");
    Require(calls == 0, "no hubo llamada");
});

await Check("Error HTTP incluye código y detalle recortado", async () =>
{
    using var http = new HttpClient(new StubHandler((_, _) =>
        Task.FromResult(Json("{\"detail\":\"" + new string('x', 1000) + "\"}", HttpStatusCode.Unauthorized))));
    using var client = new NLNVIDIAApiClient(Options(), http);

    var error = await Throws<HttpRequestException>(() => client.CompleteAsync("hola"));
    Require(error.StatusCode == HttpStatusCode.Unauthorized && error.Message.Contains("401"), "código 401");
    Require(error.Message.Length < 600 && error.Message.EndsWith("..."), "detalle recortado");
});

await Check("Respuesta 200 que no es JSON da un error claro", async () =>
{
    using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(Json("<html>gateway</html>"))));
    using var client = new NLNVIDIAApiClient(Options(), http);

    var error = await Throws<InvalidOperationException>(() => client.CompleteAsync("hola"));
    Require(error.Message.Contains("JSON"), "mensaje de JSON inválido");
});

await Check("JSON sin choices da un error claro", async () =>
{
    using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(Json("""{"choices":[]}"""))));
    using var client = new NLNVIDIAApiClient(Options(), http);

    var error = await Throws<InvalidOperationException>(() => client.CompleteAsync("hola"));
    Require(error.Message.Contains("choices[0].message.content"), "mensaje de forma inesperada");
});

await Check("El timeout se distingue de la cancelación del usuario", async () =>
{
    using var http = new HttpClient(new StubHandler(async (_, token) =>
    {
        await Task.Delay(Timeout.Infinite, token);
        return Json(okJson);
    }));
    using var client = new NLNVIDIAApiClient(Options(timeout: TimeSpan.FromMilliseconds(100)), http);
    await Throws<TimeoutException>(() => client.CompleteAsync("hola"));

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    using var slow = new NLNVIDIAApiClient(Options(timeout: TimeSpan.FromSeconds(30)), http);
    await Throws<OperationCanceledException>(() => slow.CompleteAsync("hola", cancellationToken: cts.Token));
});

Console.WriteLine($"{passed} NVIDIA API checks passed.");

async Task Check(string name, Func<Task> test)
{
    await test();
    Console.WriteLine($"PASS {name}");
    passed++;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
}

static async Task<T> Throws<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T ex)
    {
        return ex;
    }
    throw new InvalidOperationException($"FAIL: se esperaba {typeof(T).Name}");
}

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request, cancellationToken);
}
