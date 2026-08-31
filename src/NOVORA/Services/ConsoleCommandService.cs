using System.Diagnostics;
using System.Text;

namespace NOVORA.Services;

public sealed record ConsoleCommandResult(bool Success, string Output);

/// <summary>Consola limitada al ecosistema NOVORA/Android. Nunca abre cmd.exe ni PowerShell.</summary>
public sealed class ConsoleCommandService
{
    private readonly NovoraPaths _paths;
    private readonly AdbService _adb;
    private readonly GnirehtetService _gnirehtet;

    public ConsoleCommandService(NovoraPaths paths, AdbService adb, GnirehtetService gnirehtet)
    { _paths = paths; _adb = adb; _gnirehtet = gnirehtet; }

    public async Task<ConsoleCommandResult> ExecuteAsync(string command, string? serial = null, CancellationToken cancellationToken = default)
    {
        command = command.Trim();
        if (string.IsNullOrWhiteSpace(command)) return new(false, "Escribe un comando.");

        var parts = SplitArguments(command);
        if (parts.Count == 0) return new(false, "Escribe un comando.");
        var tool = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        try
        {
            switch (tool)
            {
                case "novora":
                    return new(true, await ExecuteNovoraAsync(args, serial, cancellationToken));
                case "adb":
                    if (string.IsNullOrWhiteSpace(serial) && !args.Contains("-s"))
                        return new(false, "Selecciona un dispositivo antes de usar ADB.");
                    return new(true, await _adb.ExecuteRawAsync(EnsureSerial(args, serial), cancellationToken));
                case "scrcpy":
                    return await RunToolAsync(_paths.Scrcpy, args, cancellationToken);
                case "gnirehtet":
                    return await RunToolAsync(_paths.Gnirehtet, args, cancellationToken);
                default:
                    return new(false, "Solo se permiten comandos de NOVORA, ADB, scrcpy y Gnirehtet.");
            }
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    private async Task<string> ExecuteNovoraAsync(string[] args, string? serial, CancellationToken ct)
    {
        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            return $"NOVORA 1.1 · ADB={(string.IsNullOrWhiteSpace(serial) ? "sin dispositivo" : serial)} · Gnirehtet={(_gnirehtet.IsActive ? "activo" : "inactivo")}";
        if (args[0].Equals("devices", StringComparison.OrdinalIgnoreCase))
            return string.Join(Environment.NewLine, (await _adb.GetDevicesAsync(ct, true)).Select(d => d.DisplayLabel));
        return "Comandos NOVORA: novora status | novora devices";
    }

    private async Task<ConsoleCommandResult> RunToolAsync(string executable, string[] args, CancellationToken ct)
    {
        _paths.ValidateRequiredTools();
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = _paths.ToolsDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No fue posible iniciar la herramienta.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = (await stdout) + (await stderr);
        return new(process.ExitCode == 0, string.IsNullOrWhiteSpace(output) ? $"Código de salida: {process.ExitCode}" : output.Trim());
    }

    private static string[] EnsureSerial(string[] args, string? serial)
    {
        if (args.Any(a => a.Equals("-s", StringComparison.OrdinalIgnoreCase))) return args;
        return string.IsNullOrWhiteSpace(serial) ? args : new[] { "-s", serial }.Concat(args).ToArray();
    }

    private static IReadOnlyList<string> SplitArguments(string input)
    {
        var result = new List<string>(); var current = new StringBuilder(); var quote = '\0';
        foreach (var ch in input)
        {
            if (quote != '\0') { if (ch == quote) quote = '\0'; else current.Append(ch); continue; }
            if (ch is '\'' or '"') { quote = ch; continue; }
            if (char.IsWhiteSpace(ch)) { if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); } }
            else current.Append(ch);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
