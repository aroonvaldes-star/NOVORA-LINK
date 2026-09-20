using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using System.Diagnostics;

namespace NOVORA.VisionEngine.Metrics;

public sealed class VEMetricsCollector
{
    private readonly object _gateVE = new();
    private DateTimeOffset? _lastCaptureUtcVE;
    private TimeSpan _lastCpuVE;
    private long _lastVideoPacketsVE;
    private long _lastVideoFramesVE;
    private long _lastVideoBytesVE;
    private long _lastAudioPacketsVE;
    private long _lastAudioFramesVE;
    private long _lastControlMessagesVE;

    public VEMetricsSnapshot CaptureVE(
        VEVideoStatus video,
        VEAudioStatus audio,
        VEControlStatus control,
        VETransportStates transportState,
        VETransportSession? transportSession)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(control);

        lock (_gateVE)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool hasPrevious = _lastCaptureUtcVE is not null;
            double seconds = hasPrevious
                ? Math.Max(0.001, (now - _lastCaptureUtcVE!.Value).TotalSeconds)
                : 1.0;

            long videoPacketsDelta = hasPrevious ? Math.Max(0, video.Stats.PacketsReceived - _lastVideoPacketsVE) : 0;
            long videoFramesDelta = hasPrevious ? Math.Max(0, video.Stats.FramesDecoded - _lastVideoFramesVE) : 0;
            long videoBytesDelta = hasPrevious ? Math.Max(0, video.Stats.BytesReceived - _lastVideoBytesVE) : 0;
            long audioPacketsDelta = hasPrevious ? Math.Max(0, audio.Stats.PacketsReceived - _lastAudioPacketsVE) : 0;
            long audioFramesDelta = hasPrevious ? Math.Max(0, audio.Stats.FramesDecoded - _lastAudioFramesVE) : 0;
            long controlMessages = control.Stats.MessagesSent + control.Stats.MessagesReceived;
            long controlMessagesDelta = hasPrevious ? Math.Max(0, controlMessages - _lastControlMessagesVE) : 0;

            using Process process = Process.GetCurrentProcess();
            TimeSpan cpuNow = process.TotalProcessorTime;
            double cpu = !hasPrevious
                ? 0
                : Math.Clamp((cpuNow - _lastCpuVE).TotalSeconds / (seconds * Math.Max(1, Environment.ProcessorCount)) * 100.0, 0, 100);

            VEMetricsSnapshot snapshot = new(
                CapturedAtUtc: now,
                Video: new VEMetricsVideo(
                    video.Stats.PacketsReceived,
                    video.Stats.BytesReceived,
                    video.Stats.FramesDecoded,
                    video.Stats.DecodeErrors,
                    videoPacketsDelta / seconds,
                    videoFramesDelta / seconds,
                    (videoBytesDelta * 8.0) / seconds / 1_000_000.0),
                Audio: new VEMetricsAudio(
                    audio.Stats.PacketsReceived,
                    audio.Stats.BytesReceived,
                    audio.Stats.FramesDecoded,
                    audio.Stats.PcmBytesProduced,
                    audio.Stats.DecodeErrors,
                    audio.Stats.PlaybackErrors,
                    audioPacketsDelta / seconds,
                    audioFramesDelta / seconds),
                Control: new VEMetricsControl(
                    control.Stats.MessagesSent,
                    control.Stats.MessagesReceived,
                    control.Stats.BytesSent,
                    control.Stats.BytesReceived,
                    control.Stats.Errors,
                    controlMessagesDelta / seconds),
                Transport: new VEMetricsTransport(
                    transportState,
                    transportSession?.Tunnel.Mode,
                    transportSession?.HasVideoVE ?? false,
                    transportSession?.HasAudioVE ?? false,
                    transportSession?.HasControlVE ?? false,
                    transportSession is null ? TimeSpan.Zero : now - transportSession.ConnectedAtUtc),
                WorkingSetBytes: process.WorkingSet64,
                ProcessCpuPercent: cpu,
                RendererEnabled: video.RendererEnabled);

            _lastCaptureUtcVE = now;
            _lastCpuVE = cpuNow;
            _lastVideoPacketsVE = video.Stats.PacketsReceived;
            _lastVideoFramesVE = video.Stats.FramesDecoded;
            _lastVideoBytesVE = video.Stats.BytesReceived;
            _lastAudioPacketsVE = audio.Stats.PacketsReceived;
            _lastAudioFramesVE = audio.Stats.FramesDecoded;
            _lastControlMessagesVE = controlMessages;

            return snapshot;
        }
    }
}
