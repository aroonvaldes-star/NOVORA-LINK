using System.Diagnostics;

namespace NOVORA.Linux.Control;

internal sealed record AdbDevice(string Serial, string State, string Model, bool IsUsb)
{
    public override string ToString()
    {
        string title = string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} · {Serial}";
        return $"{title} · {(IsUsb ? "USB" : "no USB")} · {State}";
    }
}

internal sealed class AdbBridge
{
    public async Task<IReadOnlyList<AdbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        AdbCommandResult result = await ExecuteAsync(["devices", "-l"], cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ADB no pudo enumerar dispositivos: {Trim(result.Error)}");

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(ParseDevice)
            .Where(device => device is not null)
            .Cast<AdbDevice>()
            .ToArray();
    }

    public async Task<UsbReverseLease> PrepareUsbControlAsync(string serial, ControlInvitation invitation,
        CancellationToken cancellationToken)
    {
        AdbDevice? current = (await GetDevicesAsync(cancellationToken))
            .SingleOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
        if (current is null)
            throw new InvalidOperationException("El teléfono se desconectó de ADB.");
        if (current.State != "device")
            throw new InvalidOperationException(current.State == "unauthorized"
                ? "Autoriza la depuración USB en el teléfono y vuelve a buscar dispositivos."
                : $"ADB informa el estado «{current.State}» para este teléfono.");
        if (!current.IsUsb)
            throw new InvalidOperationException("El dispositivo seleccionado no aparece conectado por USB físico.");

        AdbCommandResult package = await ExecuteForDeviceAsync(serial,
            ["shell", "pm", "path", "com.linkcontrol.novora"], cancellationToken);
        if (package.ExitCode != 0 || !package.Output.Contains("package:", StringComparison.Ordinal))
            throw new InvalidOperationException("LINK Control no está instalado para el usuario activo del teléfono.");

        UsbReverseLease reverse = await PrepareUsbReversePortsAsync(serial, [27214], cancellationToken);

        try
        {
            var bootstrap = new TunnelBootstrap(invitation, "USB");
            string payload = Convert.ToBase64String(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bootstrap));
            AdbCommandResult launch = await ExecuteForDeviceAsync(serial,
                ["shell", "am", "start", "-n", "com.linkcontrol.novora/.UsbBootstrapActivity",
                    "--es", "novora.usb", payload],
                cancellationToken);
            EnsureCommandSucceeded(launch, "ADB no pudo abrir el punto de entrada USB protegido de LINK Control.");
            if (launch.Output.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                launch.Output.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) ||
                launch.Output.Contains("SecurityException", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Android rechazó la entrada USB protegida; actualiza LINK Control 1.4.124.");

            return reverse;
        }
        catch
        {
            await reverse.DisposeAsync();
            throw;
        }
    }

    public async Task<UsbReverseLease> PrepareUsbReversePortsAsync(string serial, int[] ports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return await PrepareUsbReverseMappingsAsync(serial,
            ports.Select(port => (DevicePort: port, HostPort: port)).ToArray(), cancellationToken);
    }

    public async Task<UsbReverseLease> PrepareUsbReverseMappingsAsync(
        string serial, IReadOnlyList<(int DevicePort, int HostPort)> mappings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (mappings.Count == 0 ||
            mappings.Any(mapping => mapping.DevicePort is < 1 or > 65535 ||
                mapping.HostPort is < 1 or > 65535) ||
            mappings.Select(mapping => mapping.DevicePort).Distinct().Count() != mappings.Count)
            throw new ArgumentException("Las rutas ADB reverse no son válidas.", nameof(mappings));

        AdbDevice? current = (await GetDevicesAsync(cancellationToken))
            .SingleOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
        if (current is null)
            throw new InvalidOperationException("El teléfono se desconectó de ADB.");
        if (current.State != "device" || !current.IsUsb)
            throw new InvalidOperationException("El teléfono debe estar autorizado y conectado por USB físico.");

        AdbCommandResult before = await ExecuteForDeviceAsync(serial, ["reverse", "--list"], cancellationToken);
        EnsureCommandSucceeded(before, "No se pudieron consultar las rutas USB existentes.");
        var ownedMappings = new List<(int DevicePort, int HostPort)>();
        foreach ((int devicePort, int hostPort) in mappings)
        {
            bool exact = TryFindMapping(before.Output, devicePort, hostPort,
                out bool matching, out bool conflicting);
            if (conflicting)
                throw new InvalidOperationException(
                    $"La ruta USB tcp:{devicePort} ya apunta a otro destino. No se modificó.");
            if (!exact && !matching)
                ownedMappings.Add((devicePort, hostPort));
        }

        var createdMappings = new List<(int DevicePort, int HostPort)>();
        try
        {
            foreach ((int devicePort, int hostPort) in ownedMappings)
            {
                AdbCommandResult reverse = await ExecuteForDeviceAsync(serial,
                    ["reverse", "--no-rebind", $"tcp:{devicePort}", $"tcp:{hostPort}"], cancellationToken);
                EnsureCommandSucceeded(reverse, $"ADB no pudo crear la ruta USB tcp:{devicePort}.");
                createdMappings.Add((devicePort, hostPort));
            }

            AdbCommandResult after = await ExecuteForDeviceAsync(serial, ["reverse", "--list"], cancellationToken);
            EnsureCommandSucceeded(after, "No se pudieron confirmar las rutas USB.");
            foreach ((int devicePort, int hostPort) in mappings)
            {
                if (!TryFindMapping(after.Output, devicePort, hostPort, out bool matching, out bool conflicting) ||
                    !matching || conflicting)
                    throw new InvalidOperationException($"ADB no confirmó la ruta USB tcp:{devicePort}.");
            }
            return new UsbReverseLease(this, serial, createdMappings.ToArray());
        }
        catch
        {
            await new UsbReverseLease(this, serial, createdMappings.ToArray()).DisposeAsync();
            throw;
        }
    }

    private async Task RemoveMappingIfOwnedAsync(
        string serial, int devicePort, int hostPort, CancellationToken cancellationToken)
    {
        AdbCommandResult current = await ExecuteForDeviceAsync(serial, ["reverse", "--list"], cancellationToken);
        EnsureCommandSucceeded(current, "No se pudo comprobar la ruta USB antes de quitarla.");
        if (TryFindMapping(current.Output, devicePort, hostPort, out bool matching, out bool conflicting) &&
            matching && !conflicting)
        {
            AdbCommandResult removed = await ExecuteForDeviceAsync(serial,
                ["reverse", "--remove", $"tcp:{devicePort}"], cancellationToken);
            EnsureCommandSucceeded(removed,
                $"No se pudo quitar la ruta USB creada por NOVORA-LINK en tcp:{devicePort}.");
        }
    }

    internal static AdbDevice? ParseDevice(string line)
    {
        string[] columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 2)
            return null;
        string? model = columns.Skip(2).FirstOrDefault(column => column.StartsWith("model:", StringComparison.Ordinal));
        bool isUsb = columns.Skip(2).Any(column => column.StartsWith("usb:", StringComparison.Ordinal));
        return new AdbDevice(columns[0], columns[1],
            model is null ? "" : model["model:".Length..].Replace('_', ' '), isUsb);
    }

    internal static bool TryFindMapping(string output, out bool matching, out bool conflicting)
        => TryFindMapping(output, 27214, out matching, out conflicting);

    internal static bool TryFindMapping(string output, int port, out bool matching, out bool conflicting)
        => TryFindMapping(output, port, port, out matching, out conflicting);

    internal static bool TryFindMapping(
        string output, int devicePort, int hostPort, out bool matching, out bool conflicting)
    {
        matching = false;
        conflicting = false;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 3 || columns[^2] != $"tcp:{devicePort}")
                continue;
            if (columns[^1] != $"tcp:{hostPort}")
            {
                conflicting = true;
                continue;
            }
            matching = true;
        }
        return matching && !conflicting;
    }

    private async Task<AdbCommandResult> ExecuteForDeviceAsync(string serial, string[] arguments,
        CancellationToken cancellationToken)
    {
        string[] all = ["-s", serial, .. arguments];
        return await ExecuteAsync(all, cancellationToken);
    }

    private static async Task<AdbCommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = "adb",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("No se pudo iniciar ADB. Instala Android platform-tools y verifica PATH.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("ADB no está instalado o no está disponible en PATH.", ex);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new(await outputTask, await errorTask, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
    }

    private static void EnsureCommandSucceeded(AdbCommandResult result, string message)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{message} {Trim(result.Error)}");
    }

    private static string Trim(string text)
    {
        string value = text.Trim();
        return value.Length <= 200 ? value : value[..200];
    }

    private sealed record AdbCommandResult(string Output, string Error, int ExitCode);

    internal sealed class UsbReverseLease : IAsyncDisposable
    {
        private readonly AdbBridge _owner;
        private readonly string _serial;
        private readonly (int DevicePort, int HostPort)[] _ownedMappings;
        private int _disposed;

        internal UsbReverseLease(
            AdbBridge owner, string serial, (int DevicePort, int HostPort)[] ownedMappings)
        {
            _owner = owner;
            _serial = serial;
            _ownedMappings = ownedMappings;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            foreach ((int devicePort, int hostPort) in _ownedMappings)
                await _owner.RemoveMappingIfOwnedAsync(
                    _serial, devicePort, hostPort, CancellationToken.None);
        }
    }
}
