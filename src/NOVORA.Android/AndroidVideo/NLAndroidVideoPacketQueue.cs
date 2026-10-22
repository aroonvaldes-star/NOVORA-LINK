using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using NOVORA.Control;

namespace NOVORA.AndroidVideo;

internal sealed record NLAndroidVideoPacket(byte[] Payload, long? PresentationTimeUs, bool Configuration, bool KeyFrame,
    long EnqueuedAtTicks = 0);

internal readonly record struct NLAndroidVideoQueueResult(bool Accepted, bool RequestKeyFrame, bool EnteredRecovery = false);

/// <summary>
/// Keeps a short H.264 reference chain. Overflow invalidates that chain; only a
/// fresh IDR can replace it, with the latest codec configuration immediately before it.
/// </summary>
internal sealed class NLAndroidVideoPacketQueue
{
    // This local payload ceiling is independent of the protocol's 64MiB limit.
    // A single large frame is allowed, but never eight frames of this size.
    internal const int MaximumIndividualFrameBytes = 16 * 1024 * 1024;
    internal const int MaximumConfigurationBytes = 64 * 1024;
    internal const int MaximumPackets = 8;
    internal static readonly TimeSpan MaximumQueueAge = TimeSpan.FromMilliseconds(60);
    private readonly object _gate = new();
    private readonly Channel<NLAndroidVideoPacket> _channel;
    private readonly NLControlCaptureTelemetryWindow _telemetry;
    private readonly TimeProvider _clock;
    private byte[]? _configuration;
    private long _queueBytes, _droppedPackets;
    private int _videoPackets, _byteBudget;
    private bool _awaitingKeyFrame, _completed;
    // While a keyframe is still on the wire, the frames behind it age by its transmission
    // time. That wait is expected and must not start another recovery (and another keyframe).
    private long _keyFrameGraceUntil;

    internal NLAndroidVideoPacketQueue(int bitrate, NLControlCaptureTelemetryWindow telemetry, TimeProvider? clock = null)
    {
        _telemetry = telemetry;
        _clock = clock ?? TimeProvider.System;
        _byteBudget = ByteBudget(bitrate);
        _channel = Channel.CreateBounded<NLAndroidVideoPacket>(new BoundedChannelOptions(MaximumPackets)
        {
            FullMode = BoundedChannelFullMode.Wait,
            // Producer recovery also drains the channel, under the same gate as consumer reads.
            SingleReader = false, SingleWriter = true, AllowSynchronousContinuations = false
        });
        Reader = new AccountedReader(this);
    }

    internal ChannelReader<NLAndroidVideoPacket> Reader { get; }
    internal long DroppedPackets { get { lock (_gate) return _droppedPackets; } }
    internal long QueueBytes { get { lock (_gate) return _queueBytes; } }
    // Conservative bound: the cached configuration may alias a queued packet.
    internal long RetainedPayloadBytes { get { lock (_gate) return _queueBytes + (_configuration?.Length ?? 0); } }
    internal int QueueByteBudget { get { lock (_gate) return _byteBudget; } }
    internal bool AwaitingKeyFrame { get { lock (_gate) return _awaitingKeyFrame; } }

    internal bool SetTargetBitrate(int bitrate)
    {
        lock (_gate)
        {
            _byteBudget = ByteBudget(bitrate);
            // A lone large frame remains valid; a backlog exceeding the new budget cannot wait.
            if (!_completed && _videoPackets > 1 && _queueBytes > _byteBudget)
            {
                BeginRecovery();
                return true;
            }
            return false;
        }
    }

    internal NLAndroidVideoQueueResult RejectOversized()
    {
        lock (_gate)
        {
            if (_completed) return default;
            bool entered = BeginRecovery();
            RecordDrop();
            return new(false, true, entered);
        }
    }

    internal NLAndroidVideoQueueResult Enqueue(NLAndroidVideoPacket packet)
    {
        lock (_gate)
        {
            if (_completed) return default;
            if (packet.Payload.Length > (packet.Configuration ? MaximumConfigurationBytes : MaximumIndividualFrameBytes))
                return RejectOversized();
            if (packet.Configuration)
            {
                _configuration = packet.Payload;
                if (_awaitingKeyFrame) return new(true, true);
                if (_queueBytes > 0)
                {
                    // New parameter sets cannot be interleaved with an old queued
                    // reference chain. Keep just the latest set for a fresh IDR.
                    bool changed = BeginRecovery();
                    return new(true, true, changed);
                }
            }
            bool idr = !packet.Configuration && packet.KeyFrame && ContainsIdr(packet.Payload);
            if (idr && (_awaitingKeyFrame || _queueBytes > 0))
            {
                // Publish config + IDR while holding the reader gate: no consumer can
                // observe half a recovery or pop a stale reference between these writes.
                Drain(recordDrops: true);
                if (_configuration is not null)
                    Write(new(_configuration, null, true, false));
                Write(packet);
                _awaitingKeyFrame = false;
                return new(true, false);
            }
            if (_awaitingKeyFrame)
            {
                RecordDrop();
                return new(false, true);
            }

            bool stale = _channel.Reader.TryPeek(out var oldest) &&
                _clock.GetElapsedTime(oldest.EnqueuedAtTicks) >= MaximumQueueAge &&
                _clock.GetTimestamp() >= _keyFrameGraceUntil;
            // Config plus one frame can exceed the soft budget, up to the individual
            // ceiling. That exception never admits a second frame behind the large one.
            bool fitsBytes = _queueBytes + packet.Payload.Length <= _byteBudget ||
                (!packet.Configuration && _videoPackets == 0);
            if (!stale && fitsBytes && _channel.Reader.Count < MaximumPackets)
            {
                Write(packet);
                return new(true, false);
            }

            bool recovery = BeginRecovery();
            if (!packet.Configuration) RecordDrop(); // Config is retained for the next IDR.
            return new(packet.Configuration, true, recovery);
        }
    }

    internal void RecordOldestAge()
    {
        lock (_gate)
            if (_channel.Reader.TryPeek(out var oldest))
                _telemetry.RecordQueueAge(_clock.GetElapsedTime(oldest.EnqueuedAtTicks));
    }

    internal void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            _channel.Writer.TryComplete();
            Drain(recordDrops: false);
            _configuration = null;
        }
    }

    private void GrantKeyFrameGrace(int bytes)
    {
        // The byte budget is 60 ms of bitrate, so bytes / budget * 60 ms estimates airtime.
        double milliseconds = Math.Min(500, bytes * MaximumQueueAge.TotalMilliseconds / Math.Max(1, _byteBudget));
        _keyFrameGraceUntil = _clock.GetTimestamp() + (long)(milliseconds * _clock.TimestampFrequency / 1000);
    }

    private bool BeginRecovery()
    {
        bool entered = !_awaitingKeyFrame;
        _awaitingKeyFrame = true;
        Drain(recordDrops: true);
        return entered;
    }
    private void Write(NLAndroidVideoPacket packet)
    {
        packet = packet with { EnqueuedAtTicks = _clock.GetTimestamp() };
        if (!_channel.Writer.TryWrite(packet))
            throw new InvalidOperationException("La cola H.264 perdió su reserva de recuperación.");
        _queueBytes += packet.Payload.Length;
        if (!packet.Configuration) _videoPackets++;
        _telemetry.SetQueueBytes(_queueBytes);
    }
    private void Drain(bool recordDrops)
    {
        while (_channel.Reader.TryRead(out var packet))
            if (recordDrops) RecordDrop();
        _queueBytes = 0;
        _videoPackets = 0;
        _telemetry.SetQueueBytes(0);
    }
    private void RecordDrop()
    {
        _droppedPackets++;
        _telemetry.RecordDrop();
    }
    private static int ByteBudget(int bitrate) => (int)Math.Clamp((long)bitrate * 60 / 8_000, 256 * 1024, 1024 * 1024);

    internal static bool ContainsIdr(ReadOnlySpan<byte> payload)
    {
        // Parse a complete AVC length-prefixed packet first. Its length itself can
        // resemble an Annex B start code (e.g. 357 = 00 00 01 65).
        int offset = 0;
        bool lengthPrefixedIdr = false, validLengths = true;
        while (offset + 4 < payload.Length)
        {
            uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, 4));
            if (length == 0 || length > (uint)(payload.Length - offset - 4)) { validLengths = false; break; }
            if ((payload[offset + 4] & 0x9f) == 5) lengthPrefixedIdr = true;
            offset += 4 + (int)length;
        }
        if (validLengths && offset == payload.Length && offset > 0) return lengthPrefixedIdr;

        // Android AVC output normally uses Annex B. Require a leading start code,
        // rather than scanning arbitrary/malformed payloads for a random type5 byte.
        bool annexB = payload.Length >= 4 && payload[0] == 0 && payload[1] == 0 &&
            (payload[2] == 1 || payload.Length >= 5 && payload[2] == 0 && payload[3] == 1);
        if (!annexB) return false;
        for (int i = 0; i + 3 < payload.Length; i++)
        {
            if (payload[i] != 0 || payload[i + 1] != 0) continue;
            int header = payload[i + 2] == 1 ? i + 3 :
                i + 4 < payload.Length && payload[i + 2] == 0 && payload[i + 3] == 1 ? i + 4 : -1;
            if (header >= 0 && (payload[header] & 0x9f) == 5) return true;
        }
        return false;
    }

    private sealed class AccountedReader(NLAndroidVideoPacketQueue owner) : ChannelReader<NLAndroidVideoPacket>
    {
        public override Task Completion => owner._channel.Reader.Completion;
        public override bool CanCount => true;
        public override int Count { get { lock (owner._gate) return owner._channel.Reader.Count; } }
        public override bool CanPeek => true;
        public override bool TryPeek([MaybeNullWhen(false)] out NLAndroidVideoPacket item)
        {
            lock (owner._gate) return owner._channel.Reader.TryPeek(out item);
        }
        public override bool TryRead([MaybeNullWhen(false)] out NLAndroidVideoPacket item)
        {
            lock (owner._gate)
            {
                if (!owner._channel.Reader.TryRead(out item)) return false;
                owner._queueBytes -= item.Payload.Length;
                if (!item.Configuration) owner._videoPackets--;
                if (item.KeyFrame) owner.GrantKeyFrameGrace(item.Payload.Length);
                owner._telemetry.SetQueueBytes(owner._queueBytes);
                return true;
            }
        }
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            owner._channel.Reader.WaitToReadAsync(cancellationToken);
    }
}
