using System.Diagnostics;
namespace NOVORA.Linux.VisionEngine;
internal sealed record LinuxAccelerationBackend(string Name, bool Available, string Diagnostic)
{
    public override string ToString() => $"{Name}: {(Available ? "verificado" : "no disponible")} · {Diagnostic}";
}
internal static class LinuxVisionAcceleration
{
    private static readonly string[] Backends = ["vaapi", "cuda", "qsv", "vulkan"];
    private static readonly Dictionary<string, LinuxAccelerationBackend> Cache = new();
    private static readonly SemaphoreSlim Gate = new(1,1);
    private const string Sample = "AAAAAWdCwBSMaEJMBAeEQjUAAAABaM48gAAAAAFluAAECeTFAAEZ+Tk5OuuuuuuuupK668AAAAABYeAAfkCeEYAAAAABYeAAvkD+EYA=";
    public static async Task<IReadOnlyList<LinuxAccelerationBackend>> ProbeAsync(CancellationToken token)
    {
        var result = new List<LinuxAccelerationBackend>();
        foreach (string backend in Backends) result.Add(await ProbeBackendAsync(backend, token));
        return result;
    }
    public static string Select(LinuxVisionSettings settings)
    {
        if (settings.Acceleration == "Software") return "software";
        if (settings.Acceleration == "Forced")
        {
            var result = ProbeBackendAsync(settings.HardwareBackend, CancellationToken.None).GetAwaiter().GetResult();
            if (!result.Available) throw new InvalidOperationException($"Aceleración forzada {result.Name} no disponible: {result.Diagnostic}");
            return result.Name;
        }
        foreach (string backend in Backends)
            if (ProbeBackendAsync(backend, CancellationToken.None).GetAwaiter().GetResult().Available) return backend;
        return "software";
    }
    public static async Task<LinuxAccelerationBackend> ProbeBackendAsync(string backend, CancellationToken token)
    {
        if (!Backends.Contains(backend)) return new(backend,false,"Backend desconocido.");
        await Gate.WaitAsync(token);
        try
        {
            if (Cache.TryGetValue(backend,out var found)) return found;
            string path = Path.Combine(Path.GetTempPath(), "novora-ve-probe-" + Guid.NewGuid().ToString("N") + ".h264");
            try
            {
                await File.WriteAllBytesAsync(path,Convert.FromBase64String(Sample),token);
                var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError=true, RedirectStandardOutput=true, UseShellExecute=false };
                foreach (string arg in new[]{"-hide_banner","-loglevel","error","-hwaccel",backend,"-hwaccel_output_format",backend,"-i",path,
                    "-vf","hwdownload,format=nv12,format=rgba","-frames:v","1","-f","rawvideo","pipe:1"}) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar FFmpeg.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var output = new MemoryStream();
                Task copy = process.StandardOutput.BaseStream.CopyToAsync(output,timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync(timeout.Token); await copy; }
                catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
                string diagnostic = (await error).Trim();
                bool available = process.ExitCode == 0 && output.Length == 64*64*4;
                var result = new LinuxAccelerationBackend(backend,available,available ? "Decodificación hardware y descarga RGBA comprobadas." : diagnostic.Length > 500 ? diagnostic[..500] : diagnostic);
                Cache[backend] = result;
                return result;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Cache[backend] = new(backend,false,"La prueba hardware agotó 8 segundos."); }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { return Cache[backend] = new(backend,false,ex.Message); }
            finally { File.Delete(path); }
        }
        finally { Gate.Release(); }
    }
}
