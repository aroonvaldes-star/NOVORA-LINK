using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Ejecutor ADB compartido exclusivamente por ExchangeVE.
///
/// No consulta dispositivos.
/// No genera polling.
/// Siempre recibe un serial ya conocido por VisionEngine.
/// </summary>
internal static class AdbExchangeVE
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

    public static async Task<ResultAdbExchangeVE> RunAsync(
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

        ProcessStartInfo startInfo =
            new()
            {
                FileName =
                    adbPath,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true
            };

        startInfo.ArgumentList.Add(
            "-s");

        startInfo.ArgumentList.Add(
            serial);

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using Process process =
            new()
            {
                StartInfo =
                    startInfo,

                EnableRaisingEvents =
                    true
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "ExchangeVE no pudo iniciar ADB.");
        }

        try
        {
            /*
             * La prioridad CPU se baja cuando Windows lo permite.
             *
             * Esto NO limita el bus USB, pero evita que un proceso
             * adb pesado compita innecesariamente por CPU con VE.
             */
            process.PriorityClass =
                ProcessPriorityClass.BelowNormal;
        }
        catch
        {
        }

        Task<string> outputTask =
            process.StandardOutput
                .ReadToEndAsync(
                    cancellationToken);

        Task<string> errorTask =
            process.StandardError
                .ReadToEndAsync(
                    cancellationToken);

        await process
            .WaitForExitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        string output =
            await outputTask
                .ConfigureAwait(false);

        string error =
            await errorTask
                .ConfigureAwait(false);

        return
            new ResultAdbExchangeVE(
                process.ExitCode,
                output,
                error);
    }
}

internal sealed class ResultAdbExchangeVE
{
    public ResultAdbExchangeVE(
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
