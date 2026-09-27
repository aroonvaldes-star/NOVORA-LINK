using Android.Media;
using Android.Media.Projection;
using NOVORA.VisionEngine.Protocol;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace NOVORA.AndroidVideo;

[SupportedOSPlatform("android29.0")]
public sealed class NLAndroidAudioCapture : IAsyncDisposable
{
    private const int SampleRate = 48000;
    private const int BufferBytes = 19200;
    private readonly AudioRecord _record;
    private readonly CancellationTokenSource _stop = new();
    private Task _run = Task.CompletedTask;

    public NLAndroidAudioCapture(MediaProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        using var captureBuilder = new AudioPlaybackCaptureConfiguration.Builder(projection);
        captureBuilder.AddMatchingUsage(AudioUsageKind.Media);
        captureBuilder.AddMatchingUsage(AudioUsageKind.Game);
        using AudioPlaybackCaptureConfiguration capture = captureBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó la configuración de audio.");
        using var formatBuilder = new AudioFormat.Builder();
        formatBuilder.SetEncoding(Encoding.Pcm16bit);
        formatBuilder.SetSampleRate(SampleRate);
        // The .NET binding types this integer API as ChannelOut; preserve the
        // Android CHANNEL_IN_STEREO value required by AudioRecord.
        formatBuilder.SetChannelMask((ChannelOut)(int)ChannelIn.Stereo);
        using AudioFormat format = formatBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó el formato de audio.");
        using var recordBuilder = new AudioRecord.Builder();
        recordBuilder.SetAudioFormat(format);
        recordBuilder.SetBufferSizeInBytes(BufferBytes * 2);
        recordBuilder.SetAudioPlaybackCaptureConfig(capture);
        _record = recordBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó la captura de audio.");
        if (_record.State != State.Initialized)
            throw new InvalidOperationException("La captura de audio Android no quedó inicializada.");
    }

    public Task StartAsync(System.IO.Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!_run.IsCompleted) throw new InvalidOperationException("La captura de audio ya está activa.");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        _run = Task.Run(() => RunAsync(stream, linked), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunAsync(System.IO.Stream stream, CancellationTokenSource linked)
    {
        using (linked)
        {
            var writer = new VEProtocolWriter(stream);
            await writer.WriteAudioSessionAsync(VEProtocolCodec.Raw, linked.Token);
            byte[] buffer = new byte[BufferBytes];
            var clock = Stopwatch.StartNew();
            _record.StartRecording();
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    int count = _record.Read(buffer, 0, buffer.Length, (int)AudioRecordReadOptions.Blocking);
                    if (count < 0) throw new IOException($"AudioRecord falló ({count}).");
                    if (count == 0) continue;
                    long ptsUs = clock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
                    await writer.WriteAudioPacketAsync(buffer.AsMemory(0, count), ptsUs, linked.Token);
                }
            }
            finally
            {
                try { _record.Stop(); } catch { }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { _record.Stop(); } catch { }
        try { await _run.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        _record.Release();
        _record.Dispose();
        _stop.Dispose();
    }
}
