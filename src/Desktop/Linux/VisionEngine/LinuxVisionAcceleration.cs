using System.Diagnostics;
using System.Globalization;
using NOVORA.Control;

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

    /// <summary>Backend sugerido para el proveedor detectado; software si no hay hardware conocido.</summary>
    public static string DefaultBackend
    {
        get
        {
            string backend = NLAccelerationProfile.HardwareBackend(DetectVendor(), PlatformName);
            return string.IsNullOrEmpty(backend) ? "software" : backend;
        }
    }

    public static string Platform => PlatformRuntime.IsMacOS ? "mac" : "linux";
    public static NLAccelerationVendor Vendor => DetectVendor();
    private static string PlatformName => Platform;

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

    /// <summary>Devuelve una o dos opciones: hardware del proveedor y software.</summary>
    public static IReadOnlyList<NLAccelerationChoice> Options(LinuxVisionSettings settings) =>
        NLAccelerationProfile.OptionsFor(PlatformName, VendorName(), settings.Profile, settings.CustomAcceleration);

    public static string Select(LinuxVisionSettings settings) => SelectBackend(settings).Name;

    public static LinuxAccelerationBackend SelectBackend(LinuxVisionSettings settings)
    {
        IReadOnlyList<NLAccelerationChoice> options = Options(settings);
        NLAccelerationMode mode = settings.Acceleration switch
        {
            "Software" => NLAccelerationMode.Software,
            "Custom" => NLAccelerationMode.Custom,
            _ => NLAccelerationMode.Automatic
        };
        NLAccelerationChoice choice = mode == NLAccelerationMode.Software
            ? options[^1]
            : NLAccelerationProfile.Select(PlatformName, VendorName(), settings.Profile, mode, settings.CustomAcceleration);

        if (!choice.UsesHardware)
            return new("software", true, choice.Reason);

        LinuxAccelerationBackend result = ProbeBackendAsync(choice.Backend, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (result.Available) return result;

        // Automático sí degrada; la antigua opción Forzada desaparece de la interfaz.
        return new("software", true,
            $"Hardware {choice.BackendLabel} no disponible; se usa software. {result.Diagnostic}");
    }

    private static NLAccelerationVendor DetectVendor()
    {
        if (PlatformRuntime.IsMacOS) return NLAccelerationVendor.Apple;
        return RenderNodeVendor();
    }

    private static string VendorName() => NLAccelerationProfile.VendorName(DetectVendor());

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

    private static NLAccelerationVendor RenderNodeVendor()
    {
        foreach (string node in GetRenderNodes())
        {
            string vendorPath = Path.Combine("/sys/class/drm", Path.GetFileName(node), "device", "vendor");
            try
            {
                string vendor = File.ReadAllText(vendorPath).Trim().ToLowerInvariant();
                if (vendor is "0x8086") return NLAccelerationVendor.Intel;
                if (vendor is "0x1002" or "0x1022") return NLAccelerationVendor.Amd;
                if (vendor is "0x10de") return NLAccelerationVendor.Nvidia;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        try
        {
            if (Directory.GetFiles("/dev", "nvidia*", SearchOption.TopDirectoryOnly).Length > 0)
                return NLAccelerationVendor.Nvidia;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return NLAccelerationVendor.Unknown;
    }

    private static bool IsIntelRenderNode(string path) =>
        RenderNodeVendorOf(path) == NLAccelerationVendor.Intel;

    private static NLAccelerationVendor RenderNodeVendorOf(string path)
    {
        string vendorPath = Path.Combine("/sys/class/drm", Path.GetFileName(path), "device", "vendor");
        try
        {
            return File.ReadAllText(vendorPath).Trim().ToLowerInvariant() switch
            {
                "0x8086" => NLAccelerationVendor.Intel,
                "0x1002" or "0x1022" => NLAccelerationVendor.Amd,
                "0x10de" => NLAccelerationVendor.Nvidia,
                _ => NLAccelerationVendor.Unknown
            };
        }
        catch (IOException)
        {
            return NLAccelerationVendor.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return NLAccelerationVendor.Unknown;
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
