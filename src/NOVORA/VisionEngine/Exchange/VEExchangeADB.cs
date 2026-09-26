using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.Service;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Ejecutor ADB compartido exclusivamente por ExchangeVE.
///
/// No consulta dispositivos.
/// No genera polling.
/// Siempre recibe un serial ya conocido por VisionEngine.
/// </summary>
internal static class VEExchangeADB
{
    public static string ResolveAdbPathVE()
    {
        string baseDirectory =
            AppContext.BaseDirectory;

        string toolsCandidate =
            Path.Combine(
                baseDirectory,
                "Tools",
                "adb.exe");

        if (File.Exists(toolsCandidate))
        {
            return
                toolsCandidate;
        }

        string rootCandidate =
            Path.Combine(
                baseDirectory,
                "adb.exe");

        if (File.Exists(rootCandidate))
        {
            return
                rootCandidate;
        }

        return
            toolsCandidate;
    }

    public static async Task<VEExchangeResultADB> RunAsync(
        string serial,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentNullException.ThrowIfNull(
            arguments);

        string adbPath =
            ResolveAdbPathVE();

        if (!File.Exists(adbPath))
        {
            throw new FileNotFoundException(
                "ExchangeVE no encontró adb.exe.",
                adbPath);
        }

        string[] command = ["-s", serial, .. arguments];
        NLServiceProcessResult result = await new NLServiceProcess()
            .RunAsync(adbPath, command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return
            new VEExchangeResultADB(
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
    }
}

internal sealed class VEExchangeResultADB
{
    public VEExchangeResultADB(
        int exitCode,
        string output,
        string error)
    {
        ExitCodeVE =
            exitCode;

        OutputVE =
            output;

        ErrorVE =
            error;
    }

    public int ExitCodeVE
    {
        get;
    }

    public string OutputVE
    {
        get;
    }

    public string ErrorVE
    {
        get;
    }

    public bool SuccessVE =>
        ExitCodeVE == 0;
}
