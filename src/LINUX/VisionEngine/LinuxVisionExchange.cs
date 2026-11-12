using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace NOVORA.Linux.VisionEngine;

/// <summary>
/// Windows RecordSentFilesVE/RecordReceivedFilesVE: per-session counters and the last
/// 20 names, shown in Integración. Never stores file contents or clipboard text.
/// </summary>
internal sealed class LinuxVisionTransferSummary
{
    private readonly object _gate = new();
    private readonly List<string> _sentNames = new();
    private readonly List<string> _receivedNames = new();
    private long _sentCount, _sentBytes, _receivedCount, _receivedBytes;
    public event Action? Changed;

    public string SentText { get { lock (_gate) return Format(_sentCount, _sentBytes); } }
    public string ReceivedText { get { lock (_gate) return Format(_receivedCount, _receivedBytes); } }
    public string SentNames { get { lock (_gate) return _sentNames.Count == 0 ? "Aún no hay archivos enviados." : string.Join("\n", _sentNames); } }
    public string ReceivedNames { get { lock (_gate) return _receivedNames.Count == 0 ? "Aún no hay archivos recibidos." : string.Join("\n", _receivedNames); } }

    public void RecordSent(string path)
    {
        (long count, long bytes) = Stats(path);
        if (count <= 0) return;
        lock (_gate) { _sentCount += count; _sentBytes += bytes; AddName(_sentNames, path); }
        Changed?.Invoke();
    }

    public void RecordReceived(long count, long bytes, string? destination = null)
    {
        if (count <= 0) return;
        lock (_gate)
        {
            _receivedCount += count; _receivedBytes += Math.Max(0, bytes);
            if (!string.IsNullOrWhiteSpace(destination)) AddName(_receivedNames, destination);
        }
        Changed?.Invoke();
    }

    private static void AddName(List<string> names, string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        names.Insert(0, string.IsNullOrWhiteSpace(name) ? path : name);
        if (names.Count > 20) names.RemoveAt(names.Count - 1);
    }

    internal static (long Count, long Bytes) Stats(string path)
    {
        try
        {
            if (File.Exists(path)) return (1, new FileInfo(path).Length);
            if (!Directory.Exists(path)) return (0, 0);
            long count = 0, bytes = 0;
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            { count++; bytes += new FileInfo(file).Length; }
            return (count, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (0, 0); }
    }

    internal static string Format(long count, long bytes)
    {
        string files = count == 1 ? "1 archivo" : $"{count:N0} archivos";
        string size = bytes switch
        {
            < 1024 => $"{bytes:N0} B",
            < 1024L * 1024L => $"{bytes / 1024d:0.0} KB",
            < 1024L * 1024L * 1024L => $"{bytes / (1024d * 1024d):0.0} MB",
            _ => $"{bytes / (1024d * 1024d * 1024d):0.0} GB"
        };
        return $"{files} · {size}";
    }
}

/// <summary>Minimal RGBA → PNG (RFC 2083) writer for VE still captures; no native image codec required.</summary>
internal static class LinuxPngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new InvalidDataException("La imagen capturada tiene dimensiones inválidas.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6; // 8-bit RGBA, no interlace
        WriteChunk(file, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter: none
                    zlib.Write(rgba, y * stride, stride);
                }
            }
            WriteChunk(file, "IDAT", compressed.ToArray());
        }
        WriteChunk(file, "IEND", []);
        file.Flush(true);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        byte[] name = Encoding.ASCII.GetBytes(type);
        stream.Write(name);
        stream.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in name) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFF);
        stream.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}

/// <summary>
/// Windows VEExchangeUsbTransport.PullNovoraFolderAsync: adb pull of /sdcard/NOVORA into a
/// private staging folder, then the shared NLControlFileStorage import organizes it by type.
/// LAN keeps the Windows rule: the phone starts the transfer.
/// </summary>
internal static class LinuxNovoraFolder
{
    public const string AndroidRoot = "/sdcard/NOVORA";
    public const string LanMessage = "Por LAN el teléfono inicia la recepción. Envía la carpeta desde Android.";

    public sealed record Result(bool Success, string Message, long Files, long Bytes, string Destination);

    internal static Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>> RunAdb { get; set; } = RunAdbAsync;

    public static async Task<Result> PullAsync(string serial, string destinationRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (!Path.IsPathFullyQualified(destinationRoot)) throw new ArgumentException("La carpeta de recibidos debe ser absoluta.", nameof(destinationRoot));
        Directory.CreateDirectory(destinationRoot);
        string staging = Path.Combine(destinationRoot, ".novora-recepcion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await RunAdb(serial, ["shell", "mkdir", "-p", AndroidRoot], cancellationToken).ConfigureAwait(false);
            (int exit, string output) = await RunAdb(serial, ["pull", AndroidRoot, staging], cancellationToken).ConfigureAwait(false);
            if (exit != 0)
                return new(false, "No fue posible recibir la carpeta NOVORA desde Android. " + output.Trim(), 0, 0, destinationRoot);
            string received = Directory.Exists(Path.Combine(staging, "NOVORA")) ? Path.Combine(staging, "NOVORA") : staging;
            (long files, long bytes) = LinuxVisionTransferSummary.Stats(received);
            int count = global::NOVORA.Control.NLControlFileStorage.ImportDirectory(received, destinationRoot);
            return count > 0
                ? new(true, $"{count} archivo(s)/carpeta(s) recibidos y organizados por tipo.", files, bytes, destinationRoot)
                : new(false, "ADB terminó pero no aparecieron archivos locales.", 0, 0, destinationRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            return new(false, "No fue posible recibir la carpeta NOVORA desde Android. " + ex.Message, 0, 0, destinationRoot);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAdbAsync(string serial, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("adb") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-s"); start.ArgumentList.Add(serial);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar adb.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        return (process.ExitCode, (await output.ConfigureAwait(false)) + (await error.ConfigureAwait(false)));
    }
}
