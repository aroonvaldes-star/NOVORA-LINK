using System.Diagnostics;
using System.Text;

namespace NOVORA.Service;

public sealed record NLServiceConsoleCommandResult(
    bool Success,
    string Output);

/// <summary>
/// Consola limitada al ecosistema NOVORA/Android.
/// Nunca abre cmd.exe ni PowerShell.
/// </summary>
public sealed class NLServiceConsoleCommand
{
    private readonly NLServiceNovoraPaths _paths;

    private readonly NLServiceADB _adb;

    public NLServiceConsoleCommand(
        NLServiceNovoraPaths paths,
        NLServiceADB adb)
    {
        _paths =
            paths ??
            throw new ArgumentNullException(
                nameof(paths));

        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));
    }

    public async Task<NLServiceConsoleCommandResult> ExecuteAsync(
        string command,
        string? serial = null,
        CancellationToken cancellationToken = default)
    {
        command =
            command?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(command))
        {
            return new(
                false,
                "Escribe un comando.");
        }

        IReadOnlyList<string> parts =
            SplitArguments(command);

        if (parts.Count == 0)
        {
            return new(
                false,
                "Escribe un comando.");
        }

        string tool =
            parts[0]
                .ToLowerInvariant();

        string[] args =
            parts
                .Skip(1)
                .ToArray();

        try
        {
            switch (tool)
            {
                case "novora":
                    return new(
                        true,
                        await ExecuteNovoraAsync(
                            args,
                            serial,
                            cancellationToken));

                case "adb":
                    if (
                        string.IsNullOrWhiteSpace(serial) &&
                        !args.Contains("-s"))
                    {
                        return new(
                            false,
                            "Selecciona un dispositivo antes de usar ADB.");
                    }

                    return new(
                        true,
                        await _adb.ExecuteRawAsync(
                            EnsureSerial(
                                args,
                                serial),
                            cancellationToken));

                case "scrcpy":
                    return await RunToolAsync(
                        _paths.Scrcpy,
                        args,
                        cancellationToken);

                default:
                    return new(
                        false,
                        "Solo se permiten comandos de NOVORA, ADB y scrcpy.");
            }
        }
        catch (Exception ex)
        {
            return new(
                false,
                ex.Message);
        }
    }

    private async Task<string> ExecuteNovoraAsync(
        string[] args,
        string? serial,
        CancellationToken cancellationToken)
    {
        if (
            args.Length == 0 ||
            args[0].Equals(
                "status",
                StringComparison.OrdinalIgnoreCase))
        {
            string device =
                string.IsNullOrWhiteSpace(serial)
                    ? "sin dispositivo"
                    : serial;

            return
                $"NOVORA · ADB={device} · LinkEngine=integrado";
        }

        if (
            args[0].Equals(
                "devices",
                StringComparison.OrdinalIgnoreCase))
        {
            var devices =
                await _adb.GetDevicesAsync(
                    cancellationToken,
                    true);

            return string.Join(
                Environment.NewLine,
                devices.Select(
                    device =>
                        device.DisplayLabel));
        }

        return
            "Comandos NOVORA: novora status | novora devices";
    }

    private async Task<NLServiceConsoleCommandResult> RunToolAsync(
        string executable,
        string[] args,
        CancellationToken cancellationToken)
    {
        _paths.ValidateRequiredTools();

        ProcessStartInfo info =
            new()
            {
                FileName =
                    executable,

                WorkingDirectory =
                    _paths.ToolsDirectory,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8
            };

        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process =
            Process.Start(info) ??
            throw new InvalidOperationException(
                "No fue posible iniciar la herramienta.");

        Task<string> stdout =
            process.StandardOutput
                .ReadToEndAsync(
                    cancellationToken);

        Task<string> stderr =
            process.StandardError
                .ReadToEndAsync(
                    cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        string output =
            (await stdout) +
            (await stderr);

        return new(
            process.ExitCode == 0,
            string.IsNullOrWhiteSpace(output)
                ? $"Codigo de salida: {process.ExitCode}"
                : output.Trim());
    }

    private static string[] EnsureSerial(
        string[] args,
        string? serial)
    {
        if (
            args.Any(
                arg =>
                    arg.Equals(
                        "-s",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return args;
        }

        return string.IsNullOrWhiteSpace(serial)
            ? args
            : new[]
            {
                "-s",
                serial
            }
            .Concat(args)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitArguments(
        string input)
    {
        List<string> result =
            new();

        StringBuilder current =
            new();

        char quote =
            '\0';

        foreach (char character in input)
        {
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote =
                        '\0';
                }
                else
                {
                    current.Append(
                        character);
                }

                continue;
            }

            if (
                character is '\'' or '"')
            {
                quote =
                    character;

                continue;
            }

            if (
                char.IsWhiteSpace(
                    character))
            {
                if (current.Length > 0)
                {
                    result.Add(
                        current.ToString());

                    current.Clear();
                }
            }
            else
            {
                current.Append(
                    character);
            }
        }

        if (current.Length > 0)
        {
            result.Add(
                current.ToString());
        }

        return result;
    }
}