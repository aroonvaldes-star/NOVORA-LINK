using Android.Media;
using Android.Media.Projection;
using Android.Content;
using NOVORA.VisionEngine.Protocol;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace NOVORA.AndroidVideo;

[SupportedOSPlatform("android29.0")]
public sealed class NLAndroidAudioCapture : IAsyncDisposable
{
    private readonly AudioRecord _record;
    private readonly AudioManager _audioManager;
    private readonly bool _muteLocalPlayback;
    private readonly object _volumeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task _run = Task.CompletedTask;
    private int? _originalMediaVolume;

    public NLAndroidAudioCapture(Context context, MediaProjection projection, bool muteLocalPlayback)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        _audioManager = (AudioManager?)context.GetSystemService(Context.AudioService)
            ?? throw new InvalidOperationException("Android no expuso el administrador de audio.");
        _muteLocalPlayback = muteLocalPlayback;
        using var captureBuilder = new AudioPlaybackCaptureConfiguration.Builder(projection);
        captureBuilder.AddMatchingUsage(AudioUsageKind.Media);
        captureBuilder.AddMatchingUsage(AudioUsageKind.Game);
        using AudioPlaybackCaptureConfiguration capture = captureBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó la configuración de audio.");
        using var formatBuilder = new AudioFormat.Builder();
        formatBuilder.SetEncoding(Encoding.Pcm16bit);
        formatBuilder.SetSampleRate(VEProtocolConstants.RawAudioSampleRateVE);
        // The .NET binding types this integer API as ChannelOut; preserve the
        // Android CHANNEL_IN_STEREO value required by AudioRecord.
        formatBuilder.SetChannelMask((ChannelOut)(int)ChannelIn.Stereo);
        using AudioFormat format = formatBuilder.Build()
            ?? throw new InvalidOperationException("Android no creó el formato de audio.");
        int minimumBufferBytes = AudioRecord.GetMinBufferSize(
            VEProtocolConstants.RawAudioSampleRateVE,
            ChannelIn.Stereo,
            Encoding.Pcm16bit);
        if (minimumBufferBytes <= 0)
            throw new InvalidOperationException(
                $"Android no admite PCM16 estéreo a 48 kHz ({minimumBufferBytes}).");
        using var recordBuilder = new AudioRecord.Builder();
        recordBuilder.SetAudioFormat(format);
        recordBuilder.SetBufferSizeInBytes(Math.Max(
            minimumBufferBytes,
            VEProtocolConstants.RawAudioPacketBytesVE * 4));
        recordBuilder.SetAudioPlaybackCaptureConfig(capture);
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
                var clock = Stopwatch.StartNew();
                _record.StartRecording();
                if (_record.RecordingState != RecordState.Recording)
                    throw new InvalidOperationException("Android no inició la captura de audio.");
                MuteLocalPlayback();
                ready.TrySetResult();
                while (!linked.IsCancellationRequested)
                {
                    int count = _record.Read(buffer, 0, buffer.Length, (int)AudioRecordReadOptions.Blocking);
                    if (count < 0) throw new IOException($"AudioRecord falló ({count}).");
                    if (count == 0) continue;
                    long ptsUs = clock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
                    await writer.WriteAudioPacketAsync(buffer.AsMemory(0, count), ptsUs, linked.Token);
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
