using System.Diagnostics;

namespace NOVORA.VisionEngine.Streaming;

/// <summary>
/// Gestiona proveedores cuyo contenido multimedia puede estar protegido
/// mediante DRM/FLAG_SECURE.
///
/// Este componente NO intenta capturar, descifrar ni modificar contenido
/// protegido. Cuando detecta un proveedor conocido, ofrece una ruta de
/// reproducción compatible en Windows.
/// </summary>
public sealed class ProtectedStreamingVE
{
    private static readonly IReadOnlyDictionary<string, StreamingProviderVE>
        ProvidersVE =
            new Dictionary<string, StreamingProviderVE>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["com.amazon.avod.thirdpartyclient"] =
                    new(
                        Name: "Prime Video",
                        AndroidPackage:
                            "com.amazon.avod.thirdpartyclient",
                        WindowsUri:
                            "https://www.primevideo.com/"),

                ["com.netflix.mediaclient"] =
                    new(
                        Name: "Netflix",
                        AndroidPackage:
                            "com.netflix.mediaclient",
                        WindowsUri:
                            "https://www.netflix.com/"),

                ["com.disney.disneyplus"] =
                    new(
                        Name: "Disney+",
                        AndroidPackage:
                            "com.disney.disneyplus",
                        WindowsUri:
                            "https://www.disneyplus.com/"),

                ["com.hbo.hbonow"] =
                    new(
                        Name: "Max",
                        AndroidPackage:
                            "com.hbo.hbonow",
                        WindowsUri:
                            "https://www.max.com/")
            };

    public bool IsProtectedProviderVE(
        string? androidPackage)
    {
        if (string.IsNullOrWhiteSpace(
                androidPackage))
        {
            return false;
        }

        return ProvidersVE.ContainsKey(
            androidPackage.Trim());
    }

    public StreamingProviderVE?
        GetProviderVE(
            string? androidPackage)
    {
        if (string.IsNullOrWhiteSpace(
                androidPackage))
        {
            return null;
        }

        ProvidersVE.TryGetValue(
            androidPackage.Trim(),
            out StreamingProviderVE? provider);

        return provider;
    }

    public ResultProtectedStreamingVE
        OpenProviderVE(
            string androidPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            androidPackage);

        StreamingProviderVE? provider =
            GetProviderVE(androidPackage);

        if (provider is null)
        {
            return new(
                Success: false,
                Provider: null,
                Message:
                    $"El paquete '{androidPackage}' no está registrado como proveedor protegido.",
                Exception: null);
        }

        try
        {
            OpenEdgeAppModeVE(
                provider.WindowsUri);

            return new(
                Success: true,
                Provider: provider,
                Message:
                    $"{provider.Name} se abrió mediante reproducción protegida de Windows.",
                Exception: null);
        }
        catch (Exception ex)
        {
            try
            {
                OpenDefaultBrowserVE(
                    provider.WindowsUri);

                return new(
                    Success: true,
                    Provider: provider,
                    Message:
                        $"{provider.Name} se abrió mediante el navegador predeterminado.",
                    Exception: null);
            }
            catch (Exception fallbackException)
            {
                return new(
                    Success: false,
                    Provider: provider,
                    Message:
                        $"No fue posible abrir {provider.Name}.",
                    Exception:
                        new AggregateException(
                            ex,
                            fallbackException));
            }
        }
    }

    private static void OpenEdgeAppModeVE(
        string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            uri);

        string? edgePath =
            FindEdgeVE();

        if (string.IsNullOrWhiteSpace(
                edgePath))
        {
            throw new FileNotFoundException(
                "Microsoft Edge no fue encontrado.");
        }

        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    edgePath,

                UseShellExecute =
                    true,

                Arguments =
                    $"--app=\"{uri}\""
            };

        Process.Start(
            startInfo);
    }

    private static void OpenDefaultBrowserVE(
        string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            uri);

        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    uri,

                UseShellExecute =
                    true
            };

        Process.Start(
            startInfo);
    }

    private static string?
        FindEdgeVE()
    {
        string[] candidates =
        [
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe"),

            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe")
        ];

        return candidates.FirstOrDefault(
            File.Exists);
    }
}

public sealed record StreamingProviderVE(
    string Name,
    string AndroidPackage,
    string WindowsUri);

public sealed record ResultProtectedStreamingVE(
    bool Success,
    StreamingProviderVE? Provider,
    string Message,
    Exception? Exception);