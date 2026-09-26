using System.Diagnostics;
using System.IO;

namespace NOVORA.Service;

public sealed record NLServiceProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class NLServiceProcess
{
    public Process Start(string executable, IEnumerable<string>? arguments = null, string? workingDirectory = null)
    {
        ProcessStartInfo startInfo = CreateStartInfo(executable, arguments, workingDirectory, redirectOutput: false);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"No fue posible iniciar: {Path.GetFileName(executable)}");
    }

    public async Task<NLServiceProcessResult> RunAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = CreateStartInfo(executable, arguments, workingDirectory, redirectOutput: true);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"No fue posible iniciar: {Path.GetFileName(executable)}");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IEnumerable<string>? arguments,
        string? workingDirectory,
        bool redirectOutput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (!Path.IsPathFullyQualified(executable))
            throw new ArgumentException("La ruta del ejecutable debe ser absoluta.", nameof(executable));
        if (!File.Exists(executable))
            throw new FileNotFoundException("No se encontró el ejecutable.", executable);

        string resolvedWorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
        if (!Path.IsPathFullyQualified(resolvedWorkingDirectory) || !Directory.Exists(resolvedWorkingDirectory))
            throw new DirectoryNotFoundException("El directorio de trabajo debe existir y usar una ruta absoluta.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = resolvedWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };

        if (arguments is not null)
        {
            foreach (string argument in arguments)
            {
                ArgumentNullException.ThrowIfNull(argument);
                if (argument.Any(char.IsControl))
                    throw new ArgumentException("Los argumentos no pueden contener caracteres de control.", nameof(arguments));
                startInfo.ArgumentList.Add(argument);
            }
        }

        return startInfo;
    }
}
