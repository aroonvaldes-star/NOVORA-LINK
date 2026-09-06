using NOVORA.Services;
using NOVORA.VisionEngine.Protocol;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Decoder FFmpeg de audio para VisionEngine. Convierte los formatos de
/// muestra comunes de FFmpeg a PCM16LE estéreo sin lanzar procesos externos.
/// El servidor scrcpy usa 48 kHz / 2 canales para el canal de audio.
/// </summary>
public sealed class DecoderAudioVE : IDisposable
{
    private const int ChannelsVE = 2;
    private const int SampleRateVE = 48000;
    private readonly NovoraPaths _paths;
    private IntPtr _avutilVE, _swresampleVE, _avcodecVE;
    private IntPtr _contextVE, _packetVE, _frameVE;
    private CodecProtocolVE? _codecVE;
    private long _sequenceVE;
    private bool _disposedVE;

    private AvcodecFindDecoderByNameDelegate? _findDecoderVE;
    private AvcodecAllocContext3Delegate? _allocContextVE;
    private AvcodecOpen2Delegate? _openCodecVE;
    private AvcodecSendPacketDelegate? _sendPacketVE;
    private AvcodecReceiveFrameDelegate? _receiveFrameVE;
    private AvcodecFreeContextDelegate? _freeContextVE;
    private AvPacketAllocDelegate? _packetAllocVE;
    private AvPacketUnrefDelegate? _packetUnrefVE;
    private AvPacketFreeDelegate? _packetFreeVE;
    private AvPacketFromDataDelegate? _packetFromDataVE;
    private AvFrameAllocDelegate? _frameAllocVE;
    private AvFrameUnrefDelegate? _frameUnrefVE;
    private AvFrameFreeDelegate? _frameFreeVE;
    private AvMallocDelegate? _mallocVE;
    private AvFreeDelegate? _freeVE;
    private AvStrerrorDelegate? _strerrorVE;

    public DecoderAudioVE(NovoraPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public void InitializeVE(CodecProtocolVE codec)
    {
        ThrowIfDisposedVE();
        if (_contextVE != IntPtr.Zero)
        {
            if (_codecVE == codec) return;
            throw new InvalidOperationException("DecoderAudioVE ya fue inicializado con otro codec.");
        }
        if (!codec.IsAudioVE()) throw new NotSupportedException($"Codec de audio VE no soportado: {codec}.");
        LoadLibrariesVE();
        LoadExportsVE();
        IntPtr decoder = _findDecoderVE!(codec.GetFfmpegDecoderNameVE());
        if (decoder == IntPtr.Zero) throw new NotSupportedException($"FFmpeg no contiene decoder para {codec}.");
        _contextVE = _allocContextVE!(decoder);
        if (_contextVE == IntPtr.Zero) throw new OutOfMemoryException("FFmpeg no pudo crear el contexto de audio VisionEngine.");
        int result = _openCodecVE!(_contextVE, decoder, IntPtr.Zero);
        if (result < 0) throw ErrorVE("avcodec_open2", result);
        _packetVE = _packetAllocVE!();
        _frameVE = _frameAllocVE!();
        if (_packetVE == IntPtr.Zero || _frameVE == IntPtr.Zero) throw new OutOfMemoryException("FFmpeg no pudo reservar AVPacket/AVFrame de audio.");
        _codecVE = codec;
    }

    public IReadOnlyList<FrameAudioVE> DecodeVE(PacketAudioVE packet)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(packet);
        if (_contextVE == IntPtr.Zero) throw new InvalidOperationException("DecoderAudioVE no está inicializado.");
        if (packet.IsConfiguration) return Array.Empty<FrameAudioVE>();

        IntPtr buffer = _mallocVE!(checked((nuint)(packet.Data.Length + ConstantsProtocolVE.FfmpegInputPaddingSizeVE)));
        if (buffer == IntPtr.Zero) throw new OutOfMemoryException("FFmpeg no pudo reservar el bitstream de audio.");
        bool owned = false;
        try
        {
            Marshal.Copy(packet.Data, 0, buffer, packet.Data.Length);
            byte[] padding = new byte[ConstantsProtocolVE.FfmpegInputPaddingSizeVE];
            Marshal.Copy(padding, 0, IntPtr.Add(buffer, packet.Data.Length), padding.Length);
            int result = _packetFromDataVE!(_packetVE, buffer, packet.Data.Length);
            if (result < 0) throw ErrorVE("av_packet_from_data", result);
            owned = true;
            result = _sendPacketVE!(_contextVE, _packetVE);
            if (result < 0 && result != ConstantsProtocolVE.FfmpegAgainVE) throw ErrorVE("avcodec_send_packet", result);
            List<FrameAudioVE> frames = [];
            while (true)
            {
                result = _receiveFrameVE!(_contextVE, _frameVE);
                if (result == ConstantsProtocolVE.FfmpegAgainVE || result == ConstantsProtocolVE.FfmpegEofVE) break;
                if (result < 0) throw ErrorVE("avcodec_receive_frame", result);
                try
                {
                    frames.Add(ConvertFrameVE(_frameVE, packet.PresentationTimeUs));
                }
                finally { _frameUnrefVE!(_frameVE); }
            }
            return frames;
        }
        finally
        {
            if (owned) _packetUnrefVE!(_packetVE);
            else if (buffer != IntPtr.Zero) _freeVE!(buffer);
        }
    }

    private FrameAudioVE ConvertFrameVE(IntPtr frame, long? pts)
    {
        int pointerBytes = IntPtr.Size * 8;
        int linesizeBytes = sizeof(int) * 8;
        int extendedDataOffset = pointerBytes + linesizeBytes;
        int widthOffset = extendedDataOffset + IntPtr.Size;
        int nbSamplesOffset = widthOffset + sizeof(int) * 2;
        int formatOffset = nbSamplesOffset + sizeof(int);
        IntPtr extendedData = Marshal.ReadIntPtr(frame, extendedDataOffset);
        int samples = Marshal.ReadInt32(frame, nbSamplesOffset);
        int format = Marshal.ReadInt32(frame, formatOffset);
        if (samples <= 0 || samples > 1_000_000) throw new InvalidDataException($"AVFrame audio inválido: {samples} muestras.");
        byte[] pcm = new byte[checked(samples * ChannelsVE * 2)];
        bool planar = format is 5 or 6 or 7 or 8 or 9 or 11;
        int baseFormat = planar ? format - 5 : format;
        if (format == 10 || format == 11) baseFormat = 10;

        for (int i = 0; i < samples; i++)
        {
            for (int channel = 0; channel < ChannelsVE; channel++)
            {
                IntPtr plane = planar ? ReadPlaneVE(frame, extendedData, channel) : ReadPlaneVE(frame, extendedData, 0);
                int sampleIndex = planar ? i : i * ChannelsVE + channel;
                short value = ReadSampleVE(plane, sampleIndex, baseFormat);
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan((i * ChannelsVE + channel) * 2, 2), value);
            }
        }

        return new FrameAudioVE(
            Interlocked.Increment(ref _sequenceVE),
            pts,
            SampleRateVE,
            ChannelsVE,
            samples,
            pcm,
            DateTimeOffset.UtcNow);
    }

    private static IntPtr ReadPlaneVE(IntPtr frame, IntPtr extendedData, int index)
    {
        if (extendedData != IntPtr.Zero)
            return Marshal.ReadIntPtr(extendedData, index * IntPtr.Size);
        return Marshal.ReadIntPtr(frame, index * IntPtr.Size);
    }

    private static short ReadSampleVE(IntPtr data, int index, int format)
    {
        if (data == IntPtr.Zero) return 0;
        return format switch
        {
            0 => (short)((Marshal.ReadByte(data, index) - 128) << 8),
            1 => Marshal.ReadInt16(data, index * 2),
            2 => (short)(Marshal.ReadInt32(data, index * 4) >> 16),
            3 => FloatToS16VE(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, index * 4))),
            4 => DoubleToS16VE(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, index * 8))),
            10 => (short)(Marshal.ReadInt64(data, index * 8) >> 48),
            _ => throw new NotSupportedException($"FFmpeg sample format {format} no está soportado por AudioVE.")
        };
    }

    private static short FloatToS16VE(float value)
        => value <= -1f ? short.MinValue : checked((short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue));
    private static short DoubleToS16VE(double value)
        => value <= -1d ? short.MinValue : checked((short)Math.Round(Math.Clamp(value, -1d, 1d) * short.MaxValue));

    private void LoadLibrariesVE()
    {
        string avutil = Path.Combine(_paths.ToolsDirectory, "avutil-60.dll");
        string swresample = Path.Combine(_paths.ToolsDirectory, "swresample-6.dll");
        string avcodec = Path.Combine(_paths.ToolsDirectory, "avcodec-62.dll");
        foreach (string path in new[] { avutil, swresample, avcodec })
            if (!File.Exists(path)) throw new FileNotFoundException("Falta una librería FFmpeg requerida por AudioVE.", path);
        _avutilVE = NativeLibrary.Load(avutil);
        _swresampleVE = NativeLibrary.Load(swresample);
        _avcodecVE = NativeLibrary.Load(avcodec);
    }

    private void LoadExportsVE()
    {
        _findDecoderVE = LoadVE<AvcodecFindDecoderByNameDelegate>(_avcodecVE, "avcodec_find_decoder_by_name");
        _allocContextVE = LoadVE<AvcodecAllocContext3Delegate>(_avcodecVE, "avcodec_alloc_context3");
        _openCodecVE = LoadVE<AvcodecOpen2Delegate>(_avcodecVE, "avcodec_open2");
        _sendPacketVE = LoadVE<AvcodecSendPacketDelegate>(_avcodecVE, "avcodec_send_packet");
        _receiveFrameVE = LoadVE<AvcodecReceiveFrameDelegate>(_avcodecVE, "avcodec_receive_frame");
        _freeContextVE = LoadVE<AvcodecFreeContextDelegate>(_avcodecVE, "avcodec_free_context");
        _packetAllocVE = LoadVE<AvPacketAllocDelegate>(_avcodecVE, "av_packet_alloc");
        _packetUnrefVE = LoadVE<AvPacketUnrefDelegate>(_avcodecVE, "av_packet_unref");
        _packetFreeVE = LoadVE<AvPacketFreeDelegate>(_avcodecVE, "av_packet_free");
        _packetFromDataVE = LoadVE<AvPacketFromDataDelegate>(_avcodecVE, "av_packet_from_data");
        _frameAllocVE = LoadVE<AvFrameAllocDelegate>(_avutilVE, "av_frame_alloc");
        _frameUnrefVE = LoadVE<AvFrameUnrefDelegate>(_avutilVE, "av_frame_unref");
        _frameFreeVE = LoadVE<AvFrameFreeDelegate>(_avutilVE, "av_frame_free");
        _mallocVE = LoadVE<AvMallocDelegate>(_avutilVE, "av_malloc");
        _freeVE = LoadVE<AvFreeDelegate>(_avutilVE, "av_free");
        _strerrorVE = LoadVE<AvStrerrorDelegate>(_avutilVE, "av_strerror");
    }

    private static T LoadVE<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private Exception ErrorVE(string op, int code)
    {
        IntPtr buffer = Marshal.AllocHGlobal(256);
        try
        {
            string detail = "error desconocido";
            if (_strerrorVE is not null && _strerrorVE(code, buffer, 256) >= 0)
                detail = Marshal.PtrToStringAnsi(buffer) ?? detail;
            return new InvalidOperationException($"FFmpeg {op} falló ({code}): {detail}");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposedVE, this);

    public void Dispose()
    {
        if (_disposedVE) return;
        _disposedVE = true;
        if (_packetVE != IntPtr.Zero && _packetFreeVE is not null) _packetFreeVE(ref _packetVE);
        if (_frameVE != IntPtr.Zero && _frameFreeVE is not null) _frameFreeVE(ref _frameVE);
        if (_contextVE != IntPtr.Zero && _freeContextVE is not null) _freeContextVE(ref _contextVE);
        if (_avcodecVE != IntPtr.Zero) NativeLibrary.Free(_avcodecVE);
        if (_swresampleVE != IntPtr.Zero) NativeLibrary.Free(_swresampleVE);
        if (_avutilVE != IntPtr.Zero) NativeLibrary.Free(_avutilVE);
        _avcodecVE = _swresampleVE = _avutilVE = IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvcodecFindDecoderByNameDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvcodecAllocContext3Delegate(IntPtr codec);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvcodecOpen2Delegate(IntPtr context, IntPtr codec, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvcodecSendPacketDelegate(IntPtr context, IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvcodecReceiveFrameDelegate(IntPtr context, IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvcodecFreeContextDelegate(ref IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvPacketAllocDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvPacketUnrefDelegate(IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvPacketFreeDelegate(ref IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvPacketFromDataDelegate(IntPtr packet, IntPtr data, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvFrameAllocDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvFrameUnrefDelegate(IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvFrameFreeDelegate(ref IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvMallocDelegate(nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvFreeDelegate(IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvStrerrorDelegate(int code, IntPtr buffer, nuint size);
}
