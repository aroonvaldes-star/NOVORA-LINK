using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using NOVORA.VisionEngine.Protocol;

namespace NOVORA.LinkEngine.Egress;

/// <summary>
/// PCM16LE 48 kHz estéreo → AAC-LC crudo con el encoder nativo de avcodec.
/// Los campos de AVFrame (nb_samples, format, sample_rate, ch_layout) se
/// localizan calibrando con un frame real del decoder pcm_s16le, sin
/// depender de offsets fijos de una versión de FFmpeg.
/// </summary>
internal sealed class LEEgressAacEncoder : IDisposable
{
    public const int FrameSamplesLE = 1024;
    /// <summary>AudioSpecificConfig AAC-LC, 48 kHz, 2 canales.</summary>
    public static readonly byte[] AudioSpecificConfigLE = [0x11, 0x90];

    private const int SampleFmtS16LE = 1, SampleFmtFltpLE = 8, ProbeSamplesLE = 1021;
    private readonly IntPtr _avutil, _avcodec;
    private IntPtr _context, _frame, _packet;
    private int _contextSampleFmtOffset = -1, _nbSamplesOffset, _formatOffset, _sampleRateOffset, _layoutOffset;
    private readonly short[] _pending = new short[FrameSamplesLE * 2];
    private readonly float[] _left = new float[FrameSamplesLE], _right = new float[FrameSamplesLE];
    private int _pendingSamples;

    public LEEgressAacEncoder(string toolsDirectory, int bitrate)
    {
        _avutil = NativeLibrary.Load(Path.Combine(toolsDirectory, "avutil-60.dll"));
        NativeLibrary.Load(Path.Combine(toolsDirectory, "swresample-6.dll"));
        _avcodec = NativeLibrary.Load(Path.Combine(toolsDirectory, "avcodec-62.dll"));
        try
        {
            Bind();
            Calibrate();
            IntPtr codec = _findEncoder!("aac");
            if (codec == IntPtr.Zero) throw new NotSupportedException("FFmpeg no incluye el encoder AAC.");
            _context = _allocContext!(codec);
            Option(_context, "b", bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Option(_context, "ar", "48000");
            Option(_context, "ch_layout", "stereo");
            // sample_fmt no es opción pública en esta build: offset calibrado en Calibrate().
            Marshal.WriteInt32(_context, _contextSampleFmtOffset, SampleFmtFltpLE);
            Check(_open!(_context, codec, IntPtr.Zero), "avcodec_open2 aac");
            _frame = _frameAlloc!();
            Marshal.WriteInt32(_frame, _nbSamplesOffset, FrameSamplesLE);
            Marshal.WriteInt32(_frame, _formatOffset, SampleFmtFltpLE);
            Marshal.WriteInt32(_frame, _sampleRateOffset, 48000);
            WriteStereoLayout(_frame + _layoutOffset);
            Check(_frameGetBuffer!(_frame, 0), "av_frame_get_buffer");
            _packet = _packetAlloc!();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Acumula PCM y devuelve los paquetes AAC listos (cada uno 1024 muestras).</summary>
    public List<byte[]> Encode(ReadOnlySpan<byte> pcm16le)
    {
        var output = new List<byte[]>();
        int samples = pcm16le.Length / 4, read = 0;
        while (read < samples)
        {
            int take = Math.Min(FrameSamplesLE - _pendingSamples, samples - read);
            for (int i = 0; i < take * 2; i++)
                _pending[_pendingSamples * 2 + i] = BinaryPrimitives.ReadInt16LittleEndian(pcm16le.Slice((read * 2 + i) * 2));
            _pendingSamples += take; read += take;
            if (_pendingSamples < FrameSamplesLE) break;
            _pendingSamples = 0;
            EncodeFrame(output);
        }
        return output;
    }

    private void EncodeFrame(List<byte[]> output)
    {
        Check(_frameMakeWritable!(_frame), "av_frame_make_writable");
        for (int i = 0; i < FrameSamplesLE; i++)
        {
            _left[i] = _pending[i * 2] / 32768f;
            _right[i] = _pending[i * 2 + 1] / 32768f;
        }
        Marshal.Copy(_left, 0, Marshal.ReadIntPtr(_frame, 0), FrameSamplesLE);
        Marshal.Copy(_right, 0, Marshal.ReadIntPtr(_frame, IntPtr.Size), FrameSamplesLE);
        Check(_sendFrame!(_context, _frame), "avcodec_send_frame");
        while (true)
        {
            int result = _receivePacket!(_context, _packet);
            if (result == VEProtocolConstants.FfmpegAgainVE || result == VEProtocolConstants.FfmpegEofVE) return;
            Check(result, "avcodec_receive_packet");
            // AVPacket: buf(0) pts(8) dts(16) data(24) size(32), estable desde FFmpeg 4.
            IntPtr data = Marshal.ReadIntPtr(_packet, 24);
            int size = Marshal.ReadInt32(_packet, 32);
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            _packetUnref!(_packet);
            output.Add(bytes);
        }
    }

    private void Calibrate()
    {
        IntPtr decoder = _findDecoder!("pcm_s16le");
        if (decoder == IntPtr.Zero) throw new NotSupportedException("FFmpeg no incluye pcm_s16le.");
        IntPtr context = _allocContext!(decoder), packet = IntPtr.Zero, frame = IntPtr.Zero;
        try
        {
            Option(context, "ar", "48000");
            Option(context, "ch_layout", "stereo");
            const int contextScan = 768; // sample_fmt está en el primer tercio de AVCodecContext
            var before = new int[contextScan / 4];
            Marshal.Copy(context, before, 0, before.Length);
            Check(_open!(context, decoder, IntPtr.Zero), "avcodec_open2 pcm");
            var after = new int[contextScan / 4];
            Marshal.Copy(context, after, 0, after.Length);
            var formats = Enumerable.Range(0, before.Length).Where(i => before[i] == -1 && after[i] == SampleFmtS16LE).ToArray();
            if (formats.Length != 1) throw new NotSupportedException("No se pudo calibrar AVCodecContext.sample_fmt.");
            _contextSampleFmtOffset = formats[0] * 4;
            int bytes = ProbeSamplesLE * 4;
            IntPtr buffer = _malloc!((nuint)(bytes + 64));
            for (int i = 0; i < bytes + 64; i++) Marshal.WriteByte(buffer, i, 0);
            packet = _packetAlloc!();
            Check(_packetFromData!(packet, buffer, bytes), "av_packet_from_data");
            Check(_sendPacket!(context, packet), "avcodec_send_packet pcm");
            frame = _frameAlloc!();
            Check(_receiveFrame!(context, frame), "avcodec_receive_frame pcm");
            const int limit = 512; // sizeof(AVFrame) ronda 480 B
            _nbSamplesOffset = _formatOffset = _sampleRateOffset = _layoutOffset = -1;
            var rates = new List<int>();
            for (int at = 96; at + 16 <= limit; at += 4)
            {
                int value = Marshal.ReadInt32(frame, at);
                if (_nbSamplesOffset < 0 && value == ProbeSamplesLE && Marshal.ReadInt32(frame, at + 4) == SampleFmtS16LE) { _nbSamplesOffset = at; _formatOffset = at + 4; }
                else if (value == 48000) rates.Add(at);
                else if (_layoutOffset < 0 && at % 8 == 0 && value == 1 && Marshal.ReadInt32(frame, at + 4) == 2 && Marshal.ReadInt64(frame, at + 8) == 3) _layoutOffset = at;
            }
            // sample_rate precede a ch_layout; un time_base {1,48000} quedaría antes.
            _sampleRateOffset = rates.Where(r => r > _formatOffset && r < _layoutOffset).DefaultIfEmpty(-1).Max();
            if (_nbSamplesOffset < 0 || _sampleRateOffset < 0 || _layoutOffset < 0)
                throw new NotSupportedException("No se pudo calibrar AVFrame para audio AAC.");
        }
        finally
        {
            if (frame != IntPtr.Zero) _frameFree!(ref frame);
            if (packet != IntPtr.Zero) _packetFree!(ref packet);
            _freeContext!(ref context);
        }
    }

    private static void WriteStereoLayout(IntPtr layout)
    {
        Marshal.WriteInt32(layout, 0, 1);   // AV_CHANNEL_ORDER_NATIVE
        Marshal.WriteInt32(layout, 4, 2);   // nb_channels
        Marshal.WriteInt64(layout, 8, 3);   // FL | FR
        Marshal.WriteIntPtr(layout, 16, IntPtr.Zero);
    }

    private void Option(IntPtr target, string name, string value) => Check(_optSet!(target, name, value, 0), "av_opt_set " + name);
    private static void Check(int result, string operation) { if (result < 0) throw new IOException($"{operation} falló ({result})."); }

    public void Dispose()
    {
        if (_packet != IntPtr.Zero) _packetFree!(ref _packet);
        if (_frame != IntPtr.Zero) _frameFree!(ref _frame);
        if (_context != IntPtr.Zero) _freeContext!(ref _context);
    }

    private void Bind()
    {
        _optSet = Load<OptSetDelegate>(_avutil, "av_opt_set");
        _malloc = Load<MallocDelegate>(_avutil, "av_malloc");
        _frameAlloc = Load<FrameAllocDelegate>(_avutil, "av_frame_alloc");
        _frameFree = Load<FrameFreeDelegate>(_avutil, "av_frame_free");
        _frameGetBuffer = Load<FrameIntDelegate>(_avutil, "av_frame_get_buffer");
        _frameMakeWritable = Load<FrameDelegate>(_avutil, "av_frame_make_writable");
        _findEncoder = Load<FindCodecDelegate>(_avcodec, "avcodec_find_encoder_by_name");
        _findDecoder = Load<FindCodecDelegate>(_avcodec, "avcodec_find_decoder_by_name");
        _allocContext = Load<AllocContextDelegate>(_avcodec, "avcodec_alloc_context3");
        _open = Load<OpenDelegate>(_avcodec, "avcodec_open2");
        _freeContext = Load<FreeContextDelegate>(_avcodec, "avcodec_free_context");
        _sendFrame = Load<PairDelegate>(_avcodec, "avcodec_send_frame");
        _receivePacket = Load<PairDelegate>(_avcodec, "avcodec_receive_packet");
        _sendPacket = Load<PairDelegate>(_avcodec, "avcodec_send_packet");
        _receiveFrame = Load<PairDelegate>(_avcodec, "avcodec_receive_frame");
        _packetAlloc = Load<FrameAllocDelegate>(_avcodec, "av_packet_alloc");
        _packetFree = Load<FrameFreeDelegate>(_avcodec, "av_packet_free");
        _packetUnref = Load<VoidDelegate>(_avcodec, "av_packet_unref");
        _packetFromData = Load<PacketFromDataDelegate>(_avcodec, "av_packet_from_data");
    }

    private static T Load<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private OptSetDelegate? _optSet; private MallocDelegate? _malloc;
    private FrameAllocDelegate? _frameAlloc, _packetAlloc; private FrameFreeDelegate? _frameFree, _packetFree;
    private FrameIntDelegate? _frameGetBuffer; private FrameDelegate? _frameMakeWritable;
    private FindCodecDelegate? _findEncoder, _findDecoder; private AllocContextDelegate? _allocContext;
    private OpenDelegate? _open; private FreeContextDelegate? _freeContext;
    private PairDelegate? _sendFrame, _receivePacket, _sendPacket, _receiveFrame;
    private VoidDelegate? _packetUnref; private PacketFromDataDelegate? _packetFromData;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OptSetDelegate(IntPtr target, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr MallocDelegate(nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FrameAllocDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FrameFreeDelegate(ref IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FrameIntDelegate(IntPtr frame, int align);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FrameDelegate(IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FindCodecDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AllocContextDelegate(IntPtr codec);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OpenDelegate(IntPtr context, IntPtr codec, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeContextDelegate(ref IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PairDelegate(IntPtr context, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidDelegate(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PacketFromDataDelegate(IntPtr packet, IntPtr data, int size);
}
