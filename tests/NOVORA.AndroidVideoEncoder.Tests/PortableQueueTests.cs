using System.Buffers.Binary;
using NOVORA.AndroidVideo;
using NOVORA.Control;

internal static class PortableQueueTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("Fresh IDR also replaces a stale config-only queue", () =>
        {
            var clock = new ManualClock(); var queue = Queue(clock);
            queue.Enqueue(Config()); clock.Advance(60); queue.Enqueue(Frame(700_000, 5));
            Require(queue.Reader.TryRead(out var config) && config.Configuration &&
                queue.Reader.TryRead(out var idr) && idr.KeyFrame && !queue.AwaitingKeyFrame,
                "Stale config prevented immediate IDR recovery.");
            return Task.CompletedTask;
        });
        yield return ("A newer codec configuration invalidates pending old references", () =>
        {
            var queue = Queue(); queue.Enqueue(Config(1)); queue.Enqueue(Frame(30_000));
            queue.Enqueue(Config(2));
            Require(queue.AwaitingKeyFrame && !queue.Reader.TryRead(out _), "Old references survived new configuration.");
            queue.Enqueue(Frame(700_000, 5));
            Require(queue.Reader.TryRead(out var config) && config.Configuration && config.Payload[^1] == 2 &&
                queue.Reader.TryRead(out var idr) && idr.KeyFrame, "Latest configuration was not sent before IDR.");
            return Task.CompletedTask;
        });
        yield return ("Queue age expires at 60ms and only IDR resumes the stream", () =>
        {
            var clock = new ManualClock(); var queue = Queue(clock);
            queue.Enqueue(Frame(30_000)); clock.Advance(59); queue.Enqueue(Frame(30_000));
            Require(queue.Reader.Count == 2, "Queue expired before its deadline.");
            clock.Advance(1); Require(queue.Enqueue(Frame(30_000)).RequestKeyFrame && !queue.Reader.TryRead(out _),
                "Old references accumulated beyond the queue-age budget.");
            Require(!queue.Enqueue(Frame(30_000)).Accepted, "P frame resumed an invalid reference chain.");
            Require(queue.Enqueue(Frame(700_000, 5)).Accepted && !queue.AwaitingKeyFrame, "IDR failed to resume.");
            return Task.CompletedTask;
        });
        yield return ("Frames queued behind a keyframe on the wire do not restart recovery", () =>
        {
            // 20 Mbps: 60 ms budget = 256 KiB floor, so a 600 KB IDR takes ~140 ms of airtime.
            var clock = new ManualClock(); var queue = Queue(clock);
            queue.Enqueue(Frame(600_000, 5));
            Require(queue.Reader.TryRead(out var idr) && idr.KeyFrame, "IDR was not dequeued.");
            queue.Enqueue(Frame(30_000)); clock.Advance(70);
            Require(queue.Enqueue(Frame(30_000)).Accepted && !queue.AwaitingKeyFrame,
                "Frames behind an IDR in transit started another recovery.");
            clock.Advance(500);
            Require(queue.Enqueue(Frame(30_000)).RequestKeyFrame, "A real stall after the grace was not detected.");
            return Task.CompletedTask;
        });
        yield return ("Encoder sets an operating rate and a short keyframe period", () =>
        {
            var options = NLAndroidVideoEncoderConfiguration.Options(20_000_000, 60, 36, true, true);
            Require(options["operating-rate"] == 60 && options["i-frame-interval"] <= 5,
                "Operating rate or keyframe period regressed.");
            var basic = NLAndroidVideoEncoderConfiguration.Options(20_000_000, 60, 36, true, true, advanced: false);
            Require(!basic.ContainsKey("operating-rate"), "The base fallback format must stay minimal.");
            return Task.CompletedTask;
        });
        yield return ("One valid large P frame is admitted without a backlog", () =>
        {
            var queue = Queue(); var large = Frame(700_000);
            Require(queue.Enqueue(large).Accepted && queue.Reader.TryRead(out var actual) &&
                ReferenceEquals(actual.Payload, large.Payload), "A valid large P frame was treated as a backlog.");
            return Task.CompletedTask;
        });
        yield return ("Count overflow clears dependent packets and records every discarded packet", () =>
        {
            var queue = Queue(); for (int i = 0; i < 9; i++) queue.Enqueue(Frame(10_000));
            Require(queue.Reader.Count == 0 && queue.DroppedPackets == 9 && queue.AwaitingKeyFrame,
                "The eight-packet cap retained a broken chain or lost drops.");
            queue.Enqueue(Frame(10_000)); Require(queue.DroppedPackets == 10, "Awaiting-IDR drop was lost.");
            return Task.CompletedTask;
        });
        yield return ("Queued bytes are released by reader atomically, before sender work", () =>
        {
            var clock = new ManualClock(); var telemetry = new NLControlCaptureTelemetryWindow(1, clock);
            var queue = new NLAndroidVideoPacketQueue(20_000_000, telemetry, clock);
            queue.Enqueue(Frame(30_000)); Require(queue.Reader.TryRead(out _), "Missing queued frame.");
            queue.Enqueue(Frame(40_000)); clock.Advance(1_000);
            Require(queue.QueueBytes == 40_000 && telemetry.Take()!.QueueBytes == 40_000,
                "Byte accounting stayed attached to a dequeued frame.");
            return Task.CompletedTask;
        });
        yield return ("Payload memory stays bounded with maximum IDR, config and repeated overflow", () =>
        {
            var queue = Queue(); queue.Enqueue(new(new byte[65_536], null, true, false));
            for (int i = 0; i < 25; i++)
            {
                queue.Enqueue(Frame(16 * 1024 * 1024, 5));
                Require(queue.Reader.Count == 2 && queue.QueueBytes == 16 * 1024 * 1024 + 65_536 &&
                    queue.RetainedPayloadBytes <= 16 * 1024 * 1024 + 2 * 65_536,
                    "A large IDR retained more than one frame/config recovery.");
                queue.Enqueue(Frame(10_000));
                Require(queue.Reader.Count == 0 && queue.QueueBytes == 0 && queue.RetainedPayloadBytes == 65_536,
                    "Overflow retained old payload arrays.");
            }
            return Task.CompletedTask;
        });
        yield return ("Configuration memory is bounded and latest replacement is reused", () =>
        {
            var queue = Queue();
            for (int i = 0; i < 100; i++)
            {
                queue.Enqueue(new(new byte[65_536], null, true, false));
                Require(queue.RetainedPayloadBytes <= 2 * 65_536, "Configuration payloads accumulated during replacement.");
            }
            Require(queue.RetainedPayloadBytes <= 2 * 65_536, "Configuration payloads accumulated.");
            queue.Enqueue(new(new byte[65_537], null, true, false));
            Require(queue.RetainedPayloadBytes <= 2 * 65_536 && queue.AwaitingKeyFrame,
                "Oversized configuration escaped its cap.");
            return Task.CompletedTask;
        });
        yield return ("Reader cancellation and completion release payloads without a wait", async () =>
        {
            var queue = Queue(); using var stop = new CancellationTokenSource();
            var wait = queue.Reader.WaitToReadAsync(stop.Token).AsTask(); stop.Cancel();
            try { await wait; throw new Exception("Expected reader cancellation."); }
            catch (OperationCanceledException) { }
            queue.Enqueue(Config()); queue.Enqueue(Frame(700_000, 5)); queue.Complete();
            Require(queue.QueueBytes == 0 && queue.RetainedPayloadBytes == 0 && !queue.Reader.TryRead(out _),
                "Closed queue retained payloads.");
            Require(!await queue.Reader.WaitToReadAsync(), "Closed queue did not complete.");
            await queue.Reader.Completion;
            Require(!queue.Enqueue(Frame(10)).Accepted, "Closed queue accepted another frame.");
        });
        yield return ("Byte budget follows bitrate while one large recovery remains valid", () =>
        {
            var queue = new NLAndroidVideoPacketQueue(100_000_000, new NLControlCaptureTelemetryWindow(1));
            Require(queue.QueueByteBudget == 750_000, "100Mbps should have a 60ms payload budget.");
            queue.Enqueue(Frame(200_000)); queue.Enqueue(Frame(200_000));
            Require(queue.SetTargetBitrate(20_000_000) && queue.AwaitingKeyFrame && queue.QueueBytes == 0,
                "New bitrate budget retained a multi-frame backlog.");
            queue.Enqueue(Frame(700_000, 5));
            Require(!queue.SetTargetBitrate(250_000) && queue.Reader.Count == 1,
                "A single valid recovery was killed by the soft budget.");
            return Task.CompletedTask;
        });
        yield return ("AVC length prefixes cannot masquerade as AnnexB IDR start codes", () =>
        {
            // The length 357 is 00 00 01 65, which resembles an IDR start code.
            byte[] pFrame = new byte[4 + 357]; BinaryPrimitives.WriteInt32BigEndian(pFrame, 357); pFrame[4] = 0x61;
            Require(!NLAndroidVideoPacketQueue.ContainsIdr(pFrame), "An AVC P-frame length was mistaken for IDR.");
            pFrame[4] = 0x65; Require(NLAndroidVideoPacketQueue.ContainsIdr(pFrame), "Length-prefixed IDR was missed.");
            Require(NLAndroidVideoPacketQueue.ContainsIdr(Frame(100, 5).Payload) &&
                !NLAndroidVideoPacketQueue.ContainsIdr(Frame(100).Payload), "AnnexB detection regressed.");
            return Task.CompletedTask;
        });
        yield return ("Encoder latency and no-B-frame options respect their API and capabilities", () =>
        {
            var options = NLAndroidVideoEncoderConfiguration.Options(100_000_000, 120, 36, true, true);
            Require(options["latency"] == 1 && options["max-bframes"] == 0 && options["bitrate-mode"] == 2 &&
                !options.ContainsKey("low-latency") && !options.Keys.Any(k => k.StartsWith("vendor.")),
                "Encoder used decoder/vendor options or ignored verified capabilities.");
            var old = NLAndroidVideoEncoderConfiguration.Options(20_000_000, 60, 26, false, true);
            Require(!old.ContainsKey("max-bframes") && !old.ContainsKey("bitrate-mode") && old["latency"] == 1,
                "Old API or denied CBR received incompatible options.");
            var denied = NLAndroidVideoEncoderConfiguration.Options(20_000_000, 60, 36, true, false);
            Require(!denied.ContainsKey("max-bframes"), "Unverified max-bframes was forced.");
            return Task.CompletedTask;
        });
    }

    private static NLAndroidVideoPacketQueue Queue(ManualClock? clock = null) =>
        new(20_000_000, new NLControlCaptureTelemetryWindow(1, clock), clock);
    private static NLAndroidVideoPacket Config(byte marker = 0) => new([0, 0, 1, 0x67, marker], null, true, false);
    private static NLAndroidVideoPacket Frame(int bytes, byte type = 1)
    {
        byte[] payload = new byte[bytes]; payload[2] = 1; payload[3] = (byte)(0x60 | type);
        return new(payload, 123, false, type == 5);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class ManualClock : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => _now;
        internal void Advance(long ms) => _now += ms;
    }
}
