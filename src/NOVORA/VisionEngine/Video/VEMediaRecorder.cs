using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Protocol;
using NOVORA.Control;

namespace NOVORA.VisionEngine.Video;

public sealed record VEMediaStatus(bool Recording, string? LastFile, string? Error, bool AudioIncluded = false, bool Starting = false);

/// <summary>Remuxes actual H264 VE packets and decoded phone PCM into Matroska; no microphone capture.</summary>
public sealed class VEMediaRecorder : IAsyncDisposable
{
    private readonly VEVideoManager _video;
    private readonly VEAudioManager _audio;
    private readonly Func<string, string, string> _newPath;
    private readonly object _gate = new();
    private Channel<VEMediaMuxPacket>? _queue;
    private Task<string>? _writer;
    private TaskCompletionSource<Sample>? _capture;
    private byte[]? _configuration;
    private byte[]? _avcConfiguration;
    private VEMediaPacketBudget _budget = new();
    private bool _lastCanCapture, _lastCanRecord;
    private bool _protected, _audioIncluded;
    public bool PhoneAudioAllowed { get; set; }
    private VEMediaStatus _status = new(false, null, null);
    public event EventHandler<VEMediaStatus>? StatusChangedVE;
    public VEMediaStatus StatusVE { get { lock (_gate) return _status; } }
    public bool CanCapture { get { lock (_gate) return !_protected && _video.StatusVE.State == VEVideoStates.Streaming; } }
    public bool CanRecord { get { lock (_gate) return CanCapture && _video.StatusVE.Codec == VEProtocolCodec.H264 && _avcConfiguration is not null && PhoneAudioAllowed && _audio.StatusVE.State == VEAudioStates.Streaming; } }
    private sealed record Sample(byte[] Pixels, int Width, int Height, long TimeUs);

    public VEMediaRecorder(VEVideoManager video, VEAudioManager audio) : this(video, audio, NewPath) { }
    internal VEMediaRecorder(VEVideoManager video, VEAudioManager audio, Func<string, string, string> newPath)
    {
        _video = video; _audio = audio;
        _newPath = newPath;
        video.FrameDecodedVE += OnFrame; video.PacketReceivedVE += OnPacket;
        video.StatusChangedVE += OnVideoState; audio.FrameDecodedVE += OnAudio;
        audio.StatusChangedVE += OnAudioState;
    }
    public void Start()
    {
        lock (_gate)
        {
            EnsureAvailable();
            if (_writer is { IsCompleted: false }) throw new InvalidOperationException("La grabación ya está activa o finalizándose.");
            if (!CanRecord) throw new InvalidOperationException("La grabación requiere H264 y audio interno del teléfono activo; el micrófono no se graba.");
            byte[] config = _avcConfiguration!.ToArray();
            var session = _video.StatusVE.Session ?? throw new InvalidOperationException("No hay sesión de video.");
            _audioIncluded = PhoneAudioAllowed && _audio.StatusVE.State == VEAudioStates.Streaming;
            _budget = new();
            _queue = Channel.CreateBounded<VEMediaMuxPacket>(new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            var queue = _queue;
            _status = new(true, _status.LastFile, null, false, true);
            _writer = Task.Run(() => WriteRecordingAsync(queue, config, session.Width, session.Height, _audioIncluded));
            _ = _writer.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        Publish();
        if (_video.RequestKeyFrameVE is { } request) _ = RequestKeyFrameAsync(request);
    }
    private static async Task RequestKeyFrameAsync(Func<CancellationToken, Task> request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await request(deadline.Token).ConfigureAwait(false); } catch { }
    }
    public async Task<string> StopAsync()
    {
        Task<string>? task;
        lock (_gate) { _queue?.Writer.TryComplete(); _queue = null; task = _writer; }
        if (task is null) throw new InvalidOperationException("No hay una grabación para finalizar.");
        return await task.ConfigureAwait(false);
    }
    public void SetProtected(bool value)
    {
        lock (_gate)
        {
            _protected = value;
            if (value)
            {
                _capture?.TrySetException(new InvalidOperationException("Captura cancelada por privacidad."));
                _queue?.Writer.TryComplete(); _queue = null;
            }
        }
        PublishCapabilities();
    }
    private void EnsureAvailable()
    {
        if (!CanCapture) throw new InvalidOperationException("Se necesita video VE activo y sin protección de privacidad.");
    }
    private void OnVideoState(object? sender, VEVideoStatus state)
    {
        if (state.State == VEVideoStates.Streaming) { PublishCapabilities(); return; }
        lock (_gate)
        {
            _configuration = null;
            _avcConfiguration = null;
            _queue?.Writer.TryComplete(); _queue = null;
            _capture?.TrySetException(new IOException("La sesión de video terminó antes de la captura."));
        }
        PublishCapabilities();
    }
    private void OnPacket(object? sender, VEVideoPacket packet)
    {
        try
        {
        lock (_gate)
        {
            if (packet.IsConfiguration)
            {
                if (_video.StatusVE.Codec != VEProtocolCodec.H264) return;
                _configuration = VEMediaMatroska.MergeConfiguration(_configuration, packet.Data);
                byte[]? prepared;
                try { prepared = VEMediaMatroska.AvcConfiguration(_configuration); }
                catch (InvalidDataException) { prepared = null; }
                if (_avcConfiguration is not null && prepared is not null && !_avcConfiguration.AsSpan().SequenceEqual(prepared) && _queue is not null)
                {
                    _status = _status with { Error = "Grabación finalizada porque cambió la configuración de video." };
                    _queue.Writer.TryComplete(); _queue = null;
                }
                _avcConfiguration = prepared; return;
            }
            if (_protected || _queue is null || packet.PresentationTimeUs is null) return;
            Enqueue(new(1, packet.PresentationTimeUs.Value, packet.IsKeyFrame, packet.Data));
        }
        }
        finally { if (packet.IsConfiguration) PublishCapabilities(); }
    }
    private void OnAudio(object? sender, VEAudioFrame frame)
    {
        lock (_gate)
        {
            if (_protected || !PhoneAudioAllowed || !_audioIncluded || _queue is null || frame.SourcePresentationTimeUs is null) return;
            if (frame.SampleRate != 48000 || frame.Channels != 2) { _queue.Writer.TryComplete(new InvalidDataException("Formato de audio cambiado.")); _queue = null; return; }
            Enqueue(new(2, frame.SourcePresentationTimeUs.Value, true, frame.Pcm16Le));
        }
    }
    private void Enqueue(VEMediaMuxPacket packet)
    {
        bool reserved = _budget.TryReserve(packet.Data.Length);
        if (!reserved || !_queue!.Writer.TryWrite(packet))
        {
            if (reserved) _budget.Release(packet.Data.Length);
            _status = _status with { Error = reserved ? "Grabación finalizada: el almacenamiento no mantiene el ritmo." : "Grabación finalizada: se alcanzó el límite de memoria o tamaño de paquete." };
            _queue!.Writer.TryComplete(); _queue = null;
        }
    }
    private void OnFrame(object? sender, VEVideoFrame frame)
    {
        lock (_gate)
        {
            if (_protected || _capture is null) return;
            try { _capture.TrySetResult(CopyFrame(frame, frame.SourcePresentationTimeUs ?? 0)); }
            catch (Exception ex) { _capture.TrySetException(ex); }
            _capture = null;
        }
    }
    private async Task<string> WriteRecordingAsync(Channel<VEMediaMuxPacket> queue, byte[] config, int width, int height, bool audio)
    {
        string? temporary = null;
        try
        {
            string path = _newPath("Videos", ".mkv"); temporary = path + ".partial";
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536))
            using (var mux = new VEMediaMatroska(output, config, width, height, audio))
            {
                long? start = null;
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while (await queue.Reader.WaitToReadAsync(start is null ? deadline.Token : CancellationToken.None).ConfigureAwait(false))
                {
                    while (queue.Reader.TryRead(out var packet))
                    {
                        _budget.Release(packet.Data.Length);
                        if (start is null) deadline.Token.ThrowIfCancellationRequested();
                        if (start is null)
                        {
                            if (packet.Track != 1 || !packet.KeyFrame) continue;
                            start = packet.TimeUs;
                            lock (_gate) _status = _status with { Starting = false };
                            Publish();
                        }
                        if (packet.TimeUs < start.Value) continue;
                        mux.Write(packet with { TimeUs = packet.TimeUs - start.Value });
                        if (packet.Track == 2)
                        {
                            bool changed;
                            lock (_gate) { changed = !_status.AudioIncluded; _status = _status with { AudioIncluded = true }; }
                            if (changed) Publish();
                        }
                    }
                }
                if (start is null) throw new IOException("No llegó un keyframe para iniciar la grabación.");
            }
            File.Move(temporary, path);
            lock (_gate) _status = _status with { Recording = false, LastFile = path, Starting = false };
            return path;
        }
        catch (Exception ex)
        {
            lock (_gate) _status = _status with { Recording = false, Starting = false, Error = ex is OperationCanceledException ? "No llegó un keyframe en 8 segundos." : ex.Message };
            throw;
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_queue, queue)) _queue = null; }
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); } catch { }
            Publish();
        }
    }
    private static string NewPath(string category, string extension)
    {
        string root = NLControlFileStorage.PcRoot;
        string folder = Path.Combine(root, NLControlFileStorage.Category("NOVORA" + extension));
        Directory.CreateDirectory(folder);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("La carpeta NOVORA-Files no puede ser un enlace.");
        return Path.Combine(folder, $"NOVORA_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{extension}");
    }
    private void Publish() { try { StatusChangedVE?.Invoke(this, StatusVE); } catch { } }
    private void OnAudioState(object? sender, VEAudioStatus status) => PublishCapabilities();
    private void PublishCapabilities()
    {
        bool changed;
        lock (_gate)
        {
            bool capture = CanCapture, record = CanRecord;
            changed = capture != _lastCanCapture || record != _lastCanRecord;
            _lastCanCapture = capture; _lastCanRecord = record;
        }
        if (changed) Publish();
    }
    public async ValueTask DisposeAsync()
    {
        _video.FrameDecodedVE -= OnFrame; _video.PacketReceivedVE -= OnPacket; _video.StatusChangedVE -= OnVideoState; _audio.FrameDecodedVE -= OnAudio;
        _audio.StatusChangedVE -= OnAudioState;
        SetProtected(true);
        Task<string>? writer; lock (_gate) writer = _writer;
        if (writer is not null) { try { await writer.ConfigureAwait(false); } catch { } }
    }
    public async Task<string> CaptureAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<Sample> pending;
        lock (_gate)
        {
            EnsureAvailable();
            if (_capture is not null) throw new InvalidOperationException("Ya hay una captura pendiente.");
            pending = _capture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            var sample = await pending.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(sample.Width, sample.Height, 96, 96, PixelFormats.Bgr24, null, sample.Pixels, sample.Width * 3)));
                string path = _newPath("Imagenes", ".png");
                string temporary = path + ".partial";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) encoder.Save(stream);
                    lock (_gate)
                    {
                        if (_protected) throw new InvalidOperationException("Captura cancelada por privacidad.");
                        File.Move(temporary, path);
                        _status = _status with { LastFile = path, Error = null };
                    }
                    Publish();
                    return path;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { lock (_gate) { if (ReferenceEquals(_capture, pending)) _capture = null; } }
    }

    private static Sample CopyFrame(VEVideoFrame frame, long time)
    {
        frame.ValidateForRendererVE();
        int width = frame.Width, height = frame.Height;
        if (width > 8192 || height > 8192) throw new NotSupportedException("Resolución demasiado grande para grabación.");
        var pixels = new byte[checked(width * height * 3)];
        var y = new byte[width];
        bool planar = frame.PixelFormat == VEVideoPixelFormat.Yuv420P;
        // VEVideoFrame currently omits source color metadata. SDR fallback: BT.709 for HD, BT.601 for SD.
        bool hd = height > 576 || width > 1024;
        var u = new byte[planar ? (width + 1) / 2 : ((width + 1) / 2) * 2];
        var v = planar ? new byte[(width + 1) / 2] : Array.Empty<byte>();
        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(IntPtr.Add(frame.Plane0, row * frame.Pitch0), y, 0, y.Length);
            if ((row & 1) == 0)
            {
                Marshal.Copy(IntPtr.Add(frame.Plane1, row / 2 * frame.Pitch1), u, 0, u.Length);
                if (planar) Marshal.Copy(IntPtr.Add(frame.Plane2, row / 2 * frame.Pitch2), v, 0, v.Length);
            }
            for (int x = 0; x < width; x++)
            {
                int chroma = x / 2;
                int uu = planar ? u[chroma] : u[chroma * 2 + (frame.PixelFormat == VEVideoPixelFormat.Nv21 ? 1 : 0)];
                int vv = planar ? v[chroma] : u[chroma * 2 + (frame.PixelFormat == VEVideoPixelFormat.Nv21 ? 0 : 1)];
                int c = y[x] - 16, d = uu - 128, e = vv - 128, offset = (row * width + x) * 3;
                pixels[offset] = (byte)Math.Clamp((298 * c + (hd ? 541 : 516) * d + 128) >> 8, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp((298 * c - (hd ? 55 : 100) * d - (hd ? 136 : 208) * e + 128) >> 8, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp((298 * c + (hd ? 459 : 409) * e + 128) >> 8, 0, 255);
            }
        }
        return new Sample(pixels, width, height, time);
    }

}
