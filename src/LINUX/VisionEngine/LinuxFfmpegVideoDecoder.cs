using System.Buffers.Binary;
using System.Diagnostics;

namespace NOVORA.Linux.VisionEngine;

internal sealed class LinuxFfmpegVideoDecoder : IAsyncDisposable
{
    private readonly Process _process;
    private readonly VideoCodec _codec;
    private readonly VideoSession _session;
    private readonly Task<string> _stderrTask;
    private readonly Task _frameTask;
    private readonly ILinuxVideoRenderer _renderer;
    private int _disposed;

    public string ActiveBackend { get; }
    public VideoSession Session => _session;
    private int OutputWidth => _session.RotationDegrees is 90 or 270 ? _session.Height : _session.Width;
    private int OutputHeight => _session.RotationDegrees is 90 or 270 ? _session.Width : _session.Height;
    internal Task FrameTask => _frameTask;
    internal Task<string> ErrorTask => _stderrTask;

    public LinuxFfmpegVideoDecoder(
        VideoCodec codec, VideoSession session, ILinuxVideoRenderer renderer, LinuxVisionSettings? settings = null)
    {
        ActiveBackend = settings is null ? "software" : LinuxVisionAcceleration.Select(settings);
        _codec = codec;
        _session = session;
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        if (codec is VideoCodec.Vp8 or VideoCodec.Vp9)
            ValidateIvfGeometry(session);

        var start = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in BuildArguments(codec, session, ActiveBackend, settings?.LowLatency ?? true))
            start.ArgumentList.Add(argument);

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("No se pudo iniciar FFmpeg para VisionEngine.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("FFmpeg no está instalado o no está disponible en PATH.", ex);
        }
        _stderrTask = _process.StandardError.ReadToEndAsync();
        _frameTask = ReadFramesAsync();
        if (codec is VideoCodec.Vp8 or VideoCodec.Vp9)
            WriteIvfHeader(session);
    }

    public async Task WritePacketAsync(
        ReadOnlyMemory<byte> packet, long? presentationTimeUs, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_process.HasExited)
            throw new InvalidOperationException($"FFmpeg terminó: {await GetErrorAsync().ConfigureAwait(false)}");

        Stream input = _process.StandardInput.BaseStream;
        if (_codec is VideoCodec.Vp8 or VideoCodec.Vp9)
        {
            byte[] frameHeader = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(frameHeader, checked((uint)packet.Length));
            ulong timestampMs = presentationTimeUs is > 0
                ? checked((ulong)(presentationTimeUs.Value / 1000))
                : 0;
            BinaryPrimitives.WriteUInt64LittleEndian(frameHeader.AsSpan(4), timestampMs);
            await input.WriteAsync(frameHeader, cancellationToken).ConfigureAwait(false);
        }
        await input.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool SessionGeometryChanged(VideoSession session) =>
        session.Width != _session.Width || session.Height != _session.Height ||
        session.RotationDegrees != _session.RotationDegrees;

    private async Task ReadFramesAsync()
    {
        int frameLength = checked(OutputWidth * OutputHeight * 4);
        byte[] frame = new byte[frameLength];
        Stream output = _process.StandardOutput.BaseStream;
        while (true)
        {
            int offset = 0;
            while (offset < frame.Length)
            {
                int read = await output.ReadAsync(frame.AsMemory(offset), CancellationToken.None).ConfigureAwait(false);
                if (read == 0)
                {
                    if (offset == 0)
                        return;
                    throw new EndOfStreamException("FFmpeg terminó a mitad de un frame RGBA.");
                }
                offset += read;
            }

            _renderer.Present(frame, OutputWidth, OutputHeight);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(VideoCodec codec, VideoSession session, string backend = "software", bool lowLatency = true)
    {
        string demuxer = codec switch
        {
            VideoCodec.H264 => "h264",
            VideoCodec.H265 => "hevc",
            VideoCodec.Av1 => "obu",
            VideoCodec.Vp8 or VideoCodec.Vp9 => "ivf",
            _ => throw new NotSupportedException($"Codec de video VE no soportado: {codec}.")
        };
        string rotation = session.RotationDegrees switch
        {
            90 => ",transpose=clock",
            180 => ",hflip,vflip",
            270 => ",transpose=cclock",
            _ => ""
        };
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error" };
        if (lowLatency) arguments.AddRange(["-flags", "low_delay"]);
        if (backend != "software") arguments.AddRange(["-hwaccel", backend, "-hwaccel_output_format", backend]);
        arguments.AddRange(["-probesize", "32", "-analyzeduration", "0", "-f", demuxer, "-i", "pipe:0"]);
        string download = backend == "software" ? "" : "hwdownload,format=nv12,";
        arguments.AddRange(["-an", "-vf", $"{download}scale={session.Width}:{session.Height}:flags=fast_bilinear{rotation}",
            "-flush_packets", "1", "-fps_mode", "passthrough", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"]);
        return arguments;
    }

    private void WriteIvfHeader(VideoSession session)
    {
        byte[] header = new byte[32];
        "DKIF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 32);
        (_codec == VideoCodec.Vp8 ? "VP80"u8 : "VP90"u8).CopyTo(header.AsSpan(8, 4));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12, 2), checked((ushort)session.Width));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14, 2), checked((ushort)session.Height));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), 1000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20, 4), 1);
        _process.StandardInput.BaseStream.Write(header);
    }

    private static void ValidateIvfGeometry(VideoSession session)
    {
        if (session.Width > ushort.MaxValue || session.Height > ushort.MaxValue)
            throw new InvalidDataException("La geometría supera el límite del contenedor IVF.");
    }

    private async Task<string> GetErrorAsync()
    {
        string error = await _stderrTask.ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(error) ? $"código de salida {_process.ExitCode}" : error.Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            _process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            try
            {
                await _frameTask.ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            await _stderrTask.ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
        }
    }
}

internal static class LinuxVisionDecoderInput
{
    public static (byte[] Data, long? PresentationTimeUs)[] ForPacket(
        VideoPacket packet, byte[]? configuration, bool decoderRecreated)
    {
        if (decoderRecreated && !packet.IsConfiguration && configuration is not null)
            return [(configuration, null), (packet.Data, packet.PresentationTimeUs)];
        return [(packet.Data, packet.PresentationTimeUs)];
    }
}

internal static class LinuxVisionDecoderMonitor
{
    public static async Task<T> AwaitAsync<T>(
        Task<T> operation,
        Task frameTask,
        CancellationTokenSource sessionLifetime)
    {
        Task completed = await Task.WhenAny(operation, frameTask).ConfigureAwait(false);
        if (completed == frameTask)
        {
            sessionLifetime.Cancel();
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
            await frameTask.ConfigureAwait(false);
            throw new EndOfStreamException("La presentación de VisionEngine terminó antes que el flujo.");
        }

        T result = await operation.ConfigureAwait(false);
        if (frameTask.IsCompleted)
        {
            await frameTask.ConfigureAwait(false);
            throw new EndOfStreamException("La presentación de VisionEngine terminó antes que el flujo.");
        }
        return result;
    }

    public static async Task AwaitAsync(
        Task operation,
        Task frameTask,
        CancellationTokenSource sessionLifetime)
    {
        await AwaitAsync(ObserveAsync(operation), frameTask, sessionLifetime).ConfigureAwait(false);
    }

    private static async Task<bool> ObserveAsync(Task operation)
    {
        await operation.ConfigureAwait(false);
        return true;
    }
}
