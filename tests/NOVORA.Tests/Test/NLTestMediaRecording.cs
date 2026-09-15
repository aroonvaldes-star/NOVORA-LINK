using System.IO;
using System.Runtime.InteropServices;
using NOVORA.Service;
using NOVORA.VisionEngine.Video;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Protocol;
using System.Reflection;
using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace NOVORA.Tests;

[Collection("Native decoder")]
public sealed class NLTestMediaRecording
{
    [Fact]
    public void Matroska_is_readable_by_bundled_ffmpeg_with_real_h264_and_phone_pcm()
    {
        byte[] source = Convert.FromBase64String(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NLFixturesFrame.txt")));
        string path = Path.Combine(Path.GetTempPath(), "novora-mux-" + Guid.NewGuid().ToString("N") + ".mkv");
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var mux = new VEMediaMatroska(file, VEMediaMatroska.AvcConfiguration(source), 320, 240, true))
            {
                mux.Write(new(1, 0, true, source));
                mux.Write(new(2, 0, true, new byte[480 * 4]));
                mux.Write(new(1, 33333, true, source));
                mux.Write(new(2, 10000, true, new byte[480 * 4]));
            }
            var paths = new NLServiceNovoraPaths();
            IntPtr util = NativeLibrary.Load(Path.Combine(paths.ToolsDirectory, "avutil-60.dll"));
            IntPtr codec = NativeLibrary.Load(Path.Combine(paths.ToolsDirectory, "avcodec-62.dll"));
            IntPtr format = NativeLibrary.Load(Path.Combine(paths.ToolsDirectory, "avformat-62.dll"));
            IntPtr context = IntPtr.Zero, packet = IntPtr.Zero;
            T Load<T>(IntPtr lib, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
            var close = Load<Close>(format, "avformat_close_input");
            var free = Load<Free>(codec, "av_packet_free");
            try
            {
                Assert.Equal(0, Load<Open>(format, "avformat_open_input")(ref context, path, IntPtr.Zero, IntPtr.Zero));
                Assert.True(Load<Info>(format, "avformat_find_stream_info")(context, IntPtr.Zero) >= 0);
                int video = Load<Best>(format, "av_find_best_stream")(context, 0, -1, -1, IntPtr.Zero, 0);
                int audio = Load<Best>(format, "av_find_best_stream")(context, 1, -1, -1, IntPtr.Zero, 0);
                Assert.True(video >= 0); Assert.True(audio >= 0); Assert.NotEqual(video, audio);
                packet = Load<Alloc>(codec, "av_packet_alloc")(); Assert.NotEqual(IntPtr.Zero, packet);
                var timestamps = new List<long>(); int pcmPackets = 0;
                var read = Load<Read>(format, "av_read_frame"); var unref = Load<Unref>(codec, "av_packet_unref");
                while (read(context, packet) >= 0)
                {
                    int index = Marshal.ReadInt32(packet, 36); int size = Marshal.ReadInt32(packet, 32);
                    if (index == video) { timestamps.Add(Marshal.ReadInt64(packet, 8)); Assert.True(size > 100); }
                    if (index == audio) { pcmPackets++; Assert.Equal(480 * 4, size); }
                    unref(packet);
                }
                Assert.Equal(new long[] { 0, 33333 }, timestamps); Assert.Equal(2, pcmPackets);
            }
            finally
            {
                if (packet != IntPtr.Zero) free(ref packet);
                if (context != IntPtr.Zero) close(ref context);
                NativeLibrary.Free(format); NativeLibrary.Free(codec); NativeLibrary.Free(util);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_configuration_is_rejected_instead_of_publishing_corrupt_video()
    {
        Assert.Throws<InvalidDataException>(() => VEMediaMatroska.AvcConfiguration([0, 0, 1, 0x65, 1]));
        Assert.Throws<InvalidDataException>(() => VEMediaMatroska.AvcSample([1, 2, 3]));
    }
    [Fact]
    public void Packet_budget_rejects_oversized_packets_and_bounds_total_retained_bytes()
    {
        var budget = new VEMediaPacketBudget();
        Assert.False(budget.TryReserve(VEMediaPacketBudget.MaximumPacketBytes + 1));
        for (int i = 0; i < 4; i++) Assert.True(budget.TryReserve(VEMediaPacketBudget.MaximumPacketBytes));
        Assert.False(budget.TryReserve(1));
        budget.Release(VEMediaPacketBudget.MaximumPacketBytes);
        Assert.True(budget.TryReserve(VEMediaPacketBudget.MaximumPacketBytes));
        Assert.False(budget.TryReserve(1));
    }
    [Fact]
    public void Split_sps_pps_config_is_combined_without_growing_on_repeated_config()
    {
        byte[] sps = [0, 0, 1, 0x67, 0x42, 0, 0x14];
        byte[] pps = [0, 0, 0, 1, 0x68, 0xCE];
        var first = VEMediaMatroska.MergeConfiguration(null, sps);
        Assert.Throws<InvalidDataException>(() => VEMediaMatroska.AvcConfiguration(first));
        var complete = VEMediaMatroska.MergeConfiguration(first, pps);
        Assert.Equal(1, VEMediaMatroska.AvcConfiguration(complete)[0]);
        var repeated = VEMediaMatroska.MergeConfiguration(complete, pps);
        Assert.Equal(complete, repeated);
    }
    [Fact]
    public async Task Capabilities_publish_when_configuration_audio_or_privacy_changes()
    {
        var paths = new NLServiceNovoraPaths();
        await using var video = new VEVideoManager(paths);
        await using var audio = new VEAudioManager(paths);
        await using var recorder = new VEMediaRecorder(video, audio) { PhoneAudioAllowed = true };
        int changes = 0;
        recorder.StatusChangedVE += (_, _) => changes++;
        SetState(video, VEVideoStatus.CreateInitialVE() with { State = VEVideoStates.Streaming, Codec = VEProtocolCodec.H264, Session = new VEProtocolSession(320, 240, false) });
        Assert.True(recorder.CanCapture); Assert.False(recorder.CanRecord); Assert.Equal(1, changes);
        var packet = new VEVideoPacket(null, true, false, [0, 0, 1, 0x67, 0x42, 0, 0x14, 0, 0, 1, 0x68, 0xCE]);
        Raise(video, "PacketReceivedVE", packet);
        Assert.False(recorder.CanRecord);
        SetState(audio, VEAudioStatus.CreateInitialVE() with { State = VEAudioStates.Streaming });
        Assert.True(recorder.CanRecord); Assert.Equal(2, changes);
        recorder.SetProtected(true);
        Assert.False(recorder.CanCapture); Assert.False(recorder.CanRecord); Assert.Equal(3, changes);
        recorder.SetProtected(false);
        Assert.True(recorder.CanRecord); Assert.Equal(4, changes);
        recorder.PhoneAudioAllowed = false;
        Assert.False(recorder.CanRecord);
    }
    private static void SetState<T>(object owner, T state)
    {
        owner.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == typeof(T)).SetValue(owner, state);
        Raise(owner, "StatusChangedVE", state);
    }
    [Fact]
    public void Final_video_duration_uses_observed_frame_interval_in_the_file()
    {
        byte[] source = Convert.FromBase64String(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NLFixturesFrame.txt")));
        using var stream = new MemoryStream();
        using (var mux = new VEMediaMatroska(stream, VEMediaMatroska.AvcConfiguration(source), 320, 240, false))
        {
            mux.Write(new(1, 0, true, source));
            mux.Write(new(1, 40000, true, source));
            Assert.Equal(80000, mux.DurationUs);
        }
        byte[] bytes = stream.ToArray();
        int marker = bytes.AsSpan().IndexOf(new byte[] { 0x44, 0x89, 0x88 });
        Assert.True(marker > 0);
        double duration = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(marker + 3, 8)));
        Assert.Equal(80000, duration);
    }
    [Fact]
    public async Task Capture_saves_actual_black_white_yuv_frame_as_png_without_desktop_capture()
    {
        var paths = new NLServiceNovoraPaths();
        await using var video = new VEVideoManager(paths);
        await using var audio = new VEAudioManager(paths);
        string path = Path.Combine(Path.GetTempPath(), "novora-capture-" + Guid.NewGuid().ToString("N") + ".png");
        await using var recorder = new VEMediaRecorder(video, audio, (_, _) => path);
        SetState(video, VEVideoStatus.CreateInitialVE() with { State = VEVideoStates.Streaming });
        IntPtr native = Marshal.AllocHGlobal(6);
        Marshal.Copy(new byte[] { 16, 235, 16, 235, 128, 128 }, 0, native, 6);
        using var frame = new VEVideoFrame(1, 0, VEProtocolCodec.H264, 2, 2, DateTimeOffset.UtcNow, native,
            new VEVideoLayout(2, 2, (int)VEVideoPixelFormat.Yuv420P, native, IntPtr.Add(native, 4), IntPtr.Add(native, 5), 2, 1, 1), Marshal.FreeHGlobal);
        try
        {
            Task<string> capture = recorder.CaptureAsync();
            Raise(video, "FrameDecodedVE", frame);
            Assert.Equal(path, await capture);
            using var input = File.OpenRead(path);
            var decoded = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var image = new FormatConvertedBitmap(decoded, PixelFormats.Bgr24, null, 0);
            byte[] rgb = new byte[12]; image.CopyPixels(rgb, 6, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 0, 0, 0, 255, 255, 255 }, rgb);
            Assert.False(File.Exists(path + ".partial"));
        }
        finally { File.Delete(path); }
    }
    private static void Raise<T>(object owner, string eventName, T value)
    {
        var callback = owner.GetType().GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner) as EventHandler<T>;
        callback?.Invoke(owner, value!);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Open(ref IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr format, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Info(IntPtr context, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Best(IntPtr context, int type, int wanted, int related, IntPtr decoder, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(IntPtr context, IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Close(ref IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Alloc();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(ref IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Unref(IntPtr packet);
}
