using Android.Media;
using Android.Media.Projection;
using Android.Content;
using NOVORA.VisionEngine.Protocol;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace NOVORA.AndroidVideo;

/// <summary>
/// Sends phone audio to the PC as raw PCM 48 kHz stereo. Two sources:
/// device playback (AudioPlaybackCapture, needs MediaProjection) or the phone microphone
/// (AudioSource.Mic, needs RECORD_AUDIO). Microphone audio is captured mono because many
/// phones refuse stereo microphone input, and each sample is duplicated to stereo so the PC
/// receives the same format in both modes.
/// </summary>
[SupportedOSPlatform("android29.0")]
public sealed class NLAndroidAudioCapture : IAsyncDisposable
{
    private readonly AudioRecord _record;
    private readonly AudioManager _audioManager;
    private readonly bool _muteLocalPlayback;
    private readonly bool _microphone;
    private readonly object _volumeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task _run = Task.CompletedTask;
    private int? _originalMediaVolume;

    public NLAndroidAudioCapture(Context context, MediaProjection? projection, bool muteLocalPlayback, bool microphone = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        _audioManager = (AudioManager?)context.GetSystemService(Context.AudioService)
            ?? throw new InvalidOperationException("Android no expuso el administrador de audio.");
        _microphone = microphone;
        // The microphone is the sound source itself: muting media playback would silence the room.
        _muteLocalPlayback = muteLocalPlayback && !microphone;
        ChannelIn channel = microphone ? ChannelIn.Mono : ChannelIn.Stereo;
        using var formatBuilder = new AudioFormat.Builder();
        formatBuilder.SetEncoding(Encoding.Pcm16bit);
        formatBuilder.SetSampleRate(VEProtocolConstants.RawAudioSampleRateVE);
        // The .NET binding types this integer API as ChannelOut; preserve the
        // Android CHANNEL_IN_* value required by AudioRecord.
        formatBuilder.SetChannelMask((ChannelOut)(int)channel);
        using AudioFormat format = formatBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó el formato de audio.");
        int minimumBufferBytes = AudioRecord.GetMinBufferSize(
            VEProtocolConstants.RawAudioSampleRateVE,
            channel,
            Encoding.Pcm16bit);
        if (minimumBufferBytes <= 0)
            throw new InvalidOperationException(
                $"Android no admite PCM16 {(microphone ? "mono" : "estéreo")} a 48 kHz ({minimumBufferBytes}).");
        using var recordBuilder = new AudioRecord.Builder();
        recordBuilder.SetAudioFormat(format);
        recordBuilder.SetBufferSizeInBytes(Math.Max(
            minimumBufferBytes,
            VEProtocolConstants.RawAudioPacketBytesVE * 4));
        if (microphone)
        {
            recordBuilder.SetAudioSource(AudioSource.Mic);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(projection);
            using var captureBuilder = new AudioPlaybackCaptureConfiguration.Builder(projection);
            captureBuilder.AddMatchingUsage(AudioUsageKind.Media);
            captureBuilder.AddMatchingUsage(AudioUsageKind.Game);
            captureBuilder.AddMatchingUsage(AudioUsageKind.Unknown);
            using AudioPlaybackCaptureConfiguration capture = captureBuilder.Build()
                ?? throw new InvalidOperationException("Android no creó la configuración de audio.");
            recordBuilder.SetAudioPlaybackCaptureConfig(capture);
        }
        _record = recordBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó la captura de audio.");
        if (_record.State != State.Initialized)
            throw new InvalidOperationException("La captura de audio Android no quedó inicializada.");
    }

    public Task Completion => _run;

    public async Task StartAsync(System.IO.Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!_run.IsCompleted) throw new InvalidOperationException("La captura de audio ya está activa.");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _run = Task.Run(() => RunAsync(stream, linked, ready), CancellationToken.None);
        await ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(
        System.IO.Stream stream,
        CancellationTokenSource linked,
        TaskCompletionSource ready)
    {
        using (linked)
        {
            try
            {
                var writer = new VEProtocolWriter(stream);
                await writer.WriteAudioSessionAsync(VEProtocolCodec.Raw, linked.Token);
                byte[] buffer = new byte[VEProtocolConstants.RawAudioPacketBytesVE];
                // Mono microphone reads half a packet; it is expanded to stereo into buffer.
                byte[] readBuffer = _microphone ? new byte[buffer.Length / 2] : buffer;
                var clock = Stopwatch.StartNew();
                _record.StartRecording();
                if (_record.RecordingState != RecordState.Recording)
                    throw new InvalidOperationException("Android no inició la captura de audio.");
                MuteLocalPlayback();
                ready.TrySetResult();
                while (!linked.IsCancellationRequested)
                {
                    int count = _record.Read(readBuffer, 0, readBuffer.Length, (int)AudioRecordReadOptions.Blocking);
                    if (count < 0) throw new IOException($"AudioRecord falló ({count}).");
                    if (count == 0) continue;
                    ReadOnlyMemory<byte> payload = _microphone
                        ? MonoToStereo(readBuffer.AsSpan(0, count), buffer)
                        : buffer.AsMemory(0, count);
                    long ptsUs = clock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
                    await writer.WriteAudioPacketAsync(payload, ptsUs, linked.Token);
                }
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
                throw;
            }
            finally
            {
                try { _record.Stop(); } catch { }
                RestoreLocalPlayback();
            }
        }
    }

    /// <summary>Duplicates each 16-bit little-endian mono sample into a left/right stereo frame.</summary>
    private static ReadOnlyMemory<byte> MonoToStereo(ReadOnlySpan<byte> mono, byte[] stereo)
    {
        int samples = mono.Length / 2;
        for (int i = 0; i < samples; i++)
        {
            byte lo = mono[2 * i];
            byte hi = mono[2 * i + 1];
            stereo[4 * i] = lo;
            stereo[4 * i + 1] = hi;
            stereo[4 * i + 2] = lo;
            stereo[4 * i + 3] = hi;
        }
        return stereo.AsMemory(0, samples * 4);
    }

    private void MuteLocalPlayback()
    {
        if (!_muteLocalPlayback || _audioManager.IsVolumeFixed) return;
        lock (_volumeGate)
        {
            if (_originalMediaVolume is not null) return;
            try
            {
                int original = _audioManager.GetStreamVolume(Android.Media.Stream.Music);
                _audioManager.SetStreamVolume(Android.Media.Stream.Music, 0, (VolumeNotificationFlags)0);
                _originalMediaVolume = original;
            }
            catch (Exception ex) when (ex is Java.Lang.SecurityException or InvalidOperationException)
            {
                Android.Util.Log.Warn("NOVORA-AUDIO", "No se pudo silenciar el audio multimedia local: " + ex.Message);
            }
        }
    }

    private void RestoreLocalPlayback()
    {
        lock (_volumeGate)
        {
            if (_originalMediaVolume is not int original) return;
            _originalMediaVolume = null;
            try { _audioManager.SetStreamVolume(Android.Media.Stream.Music, original, (VolumeNotificationFlags)0); }
            catch (Exception ex) when (ex is Java.Lang.SecurityException or InvalidOperationException)
            {
                Android.Util.Log.Warn("NOVORA-AUDIO", "No se pudo restaurar el volumen multimedia: " + ex.Message);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { _record.Stop(); } catch { }
        try { await _run.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        RestoreLocalPlayback();
        _record.Release();
        _record.Dispose();
        _stop.Dispose();
    }
}
