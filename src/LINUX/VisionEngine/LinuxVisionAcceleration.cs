using System.Diagnostics;
using System.Globalization;

namespace NOVORA.Linux.VisionEngine;

internal sealed record LinuxAccelerationBackend(
    string Name,
    bool Available,
    string Diagnostic,
    string? Device = null)
{
    public override string ToString() => $"{Name}: {(Available ? "verificado" : "no disponible")} · {Diagnostic}";
}

internal static class LinuxVisionAcceleration
{
    public static string[] SupportedBackends { get; } = PlatformRuntime.IsMacOS
        ? ["videotoolbox"]
        : ["vaapi", "cuda", "qsv", "vulkan"];

    public static string DefaultBackend => SupportedBackends[0];

    private static readonly Dictionary<string, LinuxAccelerationBackend> Cache = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // H.264 Baseline 8-bit 4:2:0. The previous sample set constraint_set1 and was reported
    // by FFmpeg as profile 578 (Constrained Baseline), which some VAAPI drivers reject even
    // when they support the ordinary Baseline profile. Keep this probe independent of x264.
    private const string Sample = "AAAAAWdCgBSMaEJMBAeEQjUAAAABaM48gAAAAAFluAAECeTFAAEZ+Tk5OuuuuuuuupK668AAAAABYeAAfkCeEYAAAAABYeAAvkD+EYA=";

    public static async Task<IReadOnlyList<LinuxAccelerationBackend>> ProbeAsync(CancellationToken token)
    {
        var result = new List<LinuxAccelerationBackend>();
        foreach (string backend in SupportedBackends)
            result.Add(await ProbeBackendAsync(backend, token));
        return result;
    }

    public static string Select(LinuxVisionSettings settings) => SelectBackend(settings).Name;

    public static LinuxAccelerationBackend SelectBackend(LinuxVisionSettings settings)
    {
        if (settings.Acceleration == "Software")
            return new("software", true, "Decodificación por software.");

        if (settings.Acceleration == "Forced")
        {
            LinuxAccelerationBackend result = ProbeBackendAsync(settings.HardwareBackend, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (!result.Available)
                throw new InvalidOperationException(
                    $"Aceleración forzada {result.Name} no disponible: {result.Diagnostic}");
            return result;
        }

        // Automática prefiere software: en medidas previas de Linux, NVDEC tardó el doble.
        return new("software", true, "Modo automático usa decodificación por software.");
    }

    public static async Task<LinuxAccelerationBackend> ProbeBackendAsync(string backend, CancellationToken token)
    {
        if (!SupportedBackends.Contains(backend))
            return new(backend, false, "Backend desconocido.");

        await Gate.WaitAsync(token);
        try
        {
            if (Cache.TryGetValue(backend, out LinuxAccelerationBackend? found))
                return found;

            string path = Path.Combine(Path.GetTempPath(), "novora-ve-probe-" + Guid.NewGuid().ToString("N") + ".h264");
            try
            {
                await File.WriteAllBytesAsync(path, Convert.FromBase64String(Sample), token);

                var failures = new List<string>();
                foreach (string? device in GetDeviceCandidates(backend))
                {
                    LinuxAccelerationBackend result;
                    try
                    {
                        result = await ProbeDeviceAsync(backend, device, path, token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        failures.Add($"{DescribeDevice(device)}: la prueba agotó 8 segundos.");
                        continue;
                    }
                    catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
                    {
                        failures.Add($"{DescribeDevice(device)}: {ex.Message}");
                        continue;
                    }

                    if (result.Available)
                    {
                        Cache[backend] = result;
                        return result;
                    }

                    if (!string.IsNullOrWhiteSpace(result.Diagnostic))
                        failures.Add($"{DescribeDevice(device)}: {result.Diagnostic}");
                }

                string diagnostic = string.Join(" | ", failures.TakeLast(3));
                if (diagnostic.Length > 500)
                    diagnostic = diagnostic[..500];

                return Cache[backend] = new(backend, false,
                    string.IsNullOrWhiteSpace(diagnostic) ? "No se encontraron dispositivos compatibles." : diagnostic);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return Cache[backend] = new(backend, false, "La prueba hardware agotó 8 segundos.");
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                return Cache[backend] = new(backend, false, ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<LinuxAccelerationBackend> ProbeDeviceAsync(
        string backend, string? device, string input, CancellationToken token)
    {
        var start = new ProcessStartInfo(PlatformRuntime.ResolveTool("ffmpeg"))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        AddHardwareDeviceArguments(start.ArgumentList, backend, device);
        if (backend == "videotoolbox")
        {
            AddArguments(start.ArgumentList,
            [
                "-hide_banner", "-loglevel", "error", "-hwaccel", backend,
                "-i", input, "-vf", "format=nv12,format=rgba",
                "-frames:v", "1", "-f", "rawvideo", "pipe:1"
            ]);
        }
        else
        {
            AddArguments(start.ArgumentList,
            [
                "-hide_banner", "-loglevel", "error", "-hwaccel", backend,
            ]);
            if (device is not null)
                AddArguments(start.ArgumentList, ["-hwaccel_device", DeviceName(backend)]);
            AddArguments(start.ArgumentList,
            [
                "-hwaccel_output_format", backend,
                "-i", input,
                "-vf", "hwdownload,format=nv12,format=rgba",
                "-frames:v", "1", "-f", "rawvideo", "pipe:1"
            ]);
        }

        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar FFmpeg.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var output = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await copy;
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync();
            throw;
        }

        string diagnostic = (await error).Trim();
        bool available = process.ExitCode == 0 && output.Length == 64 * 64 * 4;
        string detail = available
            ? $"Decodificación H.264 Baseline y descarga RGBA comprobadas{(device is null ? "" : $" ({DescribeDevice(device)})")}."
            : diagnostic.Length > 500 ? diagnostic[..500] : diagnostic;
        return new(backend, available, detail, device);
    }

    internal static void AddHardwareDeviceArguments(IList<string> arguments, string backend, string? device)
    {
        if (device is null)
            return;

        switch (backend)
        {
            case "vaapi":
                AddArguments(arguments, ["-init_hw_device", $"vaapi={DeviceName(backend)}:{device}",
                    "-filter_hw_device", DeviceName(backend)]);
                break;
            case "qsv":
                AddArguments(arguments, ["-init_hw_device", $"vaapi=novora_va:{device}",
                    "-init_hw_device", "qsv=novora_qsv@novora_va"]);
                break;
            case "vulkan":
                AddArguments(arguments, ["-init_hw_device", $"vulkan={DeviceName(backend)}:{device}"]);
                break;
        }
    }

    private static IReadOnlyList<string?> GetDeviceCandidates(string backend)
    {
        if (backend == "vulkan")
            return ["0", "1", "2", "3"];

        string[] renderNodes = GetRenderNodes();
        if (backend == "qsv")
        {
            string[] intelNodes = renderNodes.Where(IsIntelRenderNode).ToArray();
            if (intelNodes.Length > 0)
                return intelNodes;
            return [null];
        }

        if (backend == "vaapi")
            renderNodes = renderNodes.OrderByDescending(IsIntelRenderNode).ToArray();

        return renderNodes.Length == 0 ? [null] : renderNodes;
    }

    private static string[] GetRenderNodes()
    {
        try
        {
            return Directory.Exists("/dev/dri")
                ? Directory.GetFiles("/dev/dri", "renderD*").OrderBy(path => path, StringComparer.Ordinal).ToArray()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsIntelRenderNode(string path)
    {
        string vendorPath = Path.Combine("/sys/class/drm", Path.GetFileName(path), "device", "vendor");
        try
        {
            return File.ReadAllText(vendorPath).Trim().Equals("0x8086", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string DeviceName(string backend) => backend switch
    {
        "vaapi" => "novora_vaapi",
        "qsv" => "novora_qsv",
        "vulkan" => "novora_vk",
        _ => "novora_hw"
    };

    private static void AddArguments(IList<string> target, IEnumerable<string> values)
    {
        foreach (string value in values)
            target.Add(value);
    }

    private static string DescribeDevice(string? device)
    {
        if (device is null)
            return "dispositivo predeterminado";
        if (device.StartsWith("/dev/dri/", StringComparison.Ordinal))
            return device;
        if (int.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            return $"dispositivo Vulkan {index}";
        return device;
    }
}
