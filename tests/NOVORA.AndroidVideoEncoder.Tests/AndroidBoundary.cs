// Android/JNI is the only fake boundary. Tests execute the source-linked production
// encoder callbacks, queue, configuration selection, keyframe gate and telemetry.
namespace Java.Lang
{
    public class IllegalArgumentException(string message = "Unsupported format") : Exception(message);
    public class UnsupportedOperationException(string message = "Unsupported option") : Exception(message);
}
namespace Android.Views
{
    public sealed class Surface
    {
        public bool Released { get; private set; }
        public void Release() => Released = true;
    }
}
namespace Android.OS
{
    public sealed class Bundle : IDisposable
    {
        public Dictionary<string, int> Values { get; } = [];
        public void PutInt(string key, int value) => Values[key] = value;
        public void Dispose() { }
    }
    public enum ThreadPriority { Display = -4 }
    public sealed class Looper;
    public sealed class Handler(Looper looper);
    public sealed class HandlerThread(string name, int priority)
    {
        public Looper? Looper { get; private set; }
        public void Start() => Looper = new();
        public bool QuitSafely() => true;
    }
    public static class Build
    {
        public static class VERSION { public static int SdkInt => 36; }
    }
}
namespace Android.Util
{
    public static class Log
    {
        public static int Warn(string tag, string message) => 0;
        public static int Info(string tag, string message) => 0;
    }
}
namespace Android.Media
{
    [Flags] public enum MediaCodecBufferFlags { KeyFrame = 1, CodecConfig = 2 }
    public enum MediaCodecConfigFlags { Encode = 1 }
    public enum BitrateMode { Cq = 0, Vbr = 1, Cbr = 2 }
    public enum MediaCodecErrorCode { InsufficientResource = 1100, Reclaimed = 1101 }
    public sealed class MediaFormat : IDisposable
    {
        public const string MimetypeVideoAvc = "video/avc";
        public const string KeyColorFormat = "color-format", KeyBitRate = "bitrate",
            KeyFrameRate = "frame-rate", KeyIFrameInterval = "i-frame-interval";
        public Dictionary<string, int> Values { get; } = [];
        public static MediaFormat CreateVideoFormat(string mime, int width, int height)
        {
            var result = new MediaFormat(); result.SetInteger("width", width); result.SetInteger("height", height);
            return result;
        }
        public void SetInteger(string key, int value) => Values[key] = value;
        public bool ContainsKey(string key) => Values.ContainsKey(key);
        public int GetInteger(string key) => Values[key];
        public void Dispose() { }
    }
    public sealed class MediaCodecInfo
    {
        public string Name => "test-h264";
        public CodecCapabilities GetCapabilitiesForType(string mime) => new();
        public sealed class CodecCapabilities : IDisposable
        {
            public EncoderCapabilities EncoderCapabilities => new();
            public bool IsFormatSupported(MediaFormat format) => MediaCodec.FormatSupported(format);
            public void Dispose() { }
        }
        public sealed class EncoderCapabilities
        {
            public bool IsBitrateModeSupported(BitrateMode mode) => MediaCodec.SupportsCbr;
        }
    }
    public sealed class ByteBuffer(byte[] bytes)
    {
        private int _position;
        public void Position(int position) => _position = position;
        public void Limit(int limit) { }
        public void Get(byte[] destination) => Array.Copy(bytes, _position, destination, 0, destination.Length);
    }
    public sealed class MediaCodec : IDisposable
    {
        public abstract class Callback
        {
            public abstract void OnInputBufferAvailable(MediaCodec codec, int index);
            public abstract void OnOutputBufferAvailable(MediaCodec codec, int index, BufferInfo info);
            public abstract void OnOutputFormatChanged(MediaCodec codec, MediaFormat format);
            public abstract void OnError(MediaCodec codec, CodecException e);
        }
        public sealed class BufferInfo
        {
            public int Size { get; init; }
            public int Offset { get; init; }
            public long PresentationTimeUs { get; init; }
            public MediaCodecBufferFlags Flags { get; init; }
        }
        public sealed class CodecException(string message, bool recoverable = false,
            bool transient = false, int errorCode = 0) : Exception(message)
        {
            public bool IsRecoverable => recoverable;
            public bool IsTransient => transient;
            public MediaCodecErrorCode ErrorCode => (MediaCodecErrorCode)errorCode;
            public string DiagnosticInfo => Message;
        }
        public static bool SupportsCbr { get; set; } = true;
        public static Func<MediaFormat, bool> FormatSupported { get; set; } = _ => true;
        public static Func<MediaFormat, Exception?> ConfigureFailure { get; set; } = _ => null;
        public static Exception? SurfaceFailure { get; set; }
        public static Exception? DisposeFailure { get; set; }
        public static MediaCodec LastCreated { get; private set; } = null!;
        public List<Dictionary<string, int>> ConfiguredFormats { get; } = [];
        public int ResetCount { get; private set; }
        public int ReleasedBuffers { get; private set; }
        public int SyncRequests { get; private set; }
        public bool Released { get; private set; }
        public bool Disposed { get; private set; }
        public MediaCodecInfo CodecInfo => new();
        private Callback? _callback;
        private byte[] _output = [];
        public static MediaCodec CreateEncoderByType(string mime) => LastCreated = new();
        public void SetCallback(Callback callback, Android.OS.Handler? handler = null) => _callback = callback;
        public void Configure(MediaFormat format, object? surface, object? crypto, MediaCodecConfigFlags flags)
        {
            ConfiguredFormats.Add(new(format.Values));
            if (ConfigureFailure(format) is Exception failure) throw failure;
        }
        public Android.Views.Surface CreateInputSurface() => SurfaceFailure is Exception failure
            ? throw failure : new();
        public void Reset() => ResetCount++;
        public void Start() { }
        public void Stop() { }
        public void SetParameters(Android.OS.Bundle parameters)
        {
            if (parameters.Values.ContainsKey("request-sync")) SyncRequests++;
        }
        public ByteBuffer GetOutputBuffer(int index) => new(_output);
        public void ReleaseOutputBuffer(int index, bool render) => ReleasedBuffers++;
        public void Emit(byte[] output, bool config = false, bool key = false)
        {
            _output = output;
            _callback!.OnOutputBufferAvailable(this, 0, new BufferInfo
            {
                Size = output.Length, PresentationTimeUs = 123,
                Flags = (config ? MediaCodecBufferFlags.CodecConfig : 0) |
                    (key ? MediaCodecBufferFlags.KeyFrame : 0)
            });
        }
        public void Release() => Released = true;
        public void Dispose() { Disposed = true; if (DisposeFailure is Exception failure) throw failure; }
        public static void ResetBoundary()
        {
            SupportsCbr = true; FormatSupported = _ => true;
            ConfigureFailure = _ => null; SurfaceFailure = null; DisposeFailure = null;
        }
    }
}
