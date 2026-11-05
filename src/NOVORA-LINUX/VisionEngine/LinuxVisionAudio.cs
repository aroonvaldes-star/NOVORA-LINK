using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace NOVORA.Linux.VisionEngine;

internal enum AudioCodec : uint
{
    Disabled = 0,
    ConfigurationError = 1,
    Opus = 0x6f707573,
    Aac = 0x00616163,
    Flac = 0x666c6163,
    Raw = 0x00726177
}

internal sealed record AudioPacket(byte[] Data, bool IsConfiguration);

internal sealed class LinuxAudioProtocolReader(Stream stream)
{
    private const int MaxPacketLength = 16 * 1024 * 1024;
    private const ulong ConfigFlag = 1UL << 62;
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly byte[] _header = new byte[12];

    public async Task<AudioCodec?> OpenAsync(CancellationToken cancellationToken)
    {
        byte[] codecBytes = new byte[4];
        await _stream.ReadExactlyAsync(codecBytes, cancellationToken).ConfigureAwait(false);
        uint raw = BinaryPrimitives.ReadUInt32BigEndian(codecBytes);
        if (raw == (uint)AudioCodec.Disabled)
            return null;
        if (raw == (uint)AudioCodec.ConfigurationError)
            throw new InvalidDataException("LINK Control informó un error de configuración de audio.");
        if (!Enum.IsDefined(typeof(AudioCodec), raw))
            throw new NotSupportedException($"Codec de audio VE no soportado: 0x{raw:x8}.");
        return (AudioCodec)raw;
    }

    public async Task<AudioPacket> ReadPacketAsync(CancellationToken cancellationToken)
    {
        await _stream.ReadExactlyAsync(_header, cancellationToken).ConfigureAwait(false);
        ulong flags = BinaryPrimitives.ReadUInt64BigEndian(_header.AsSpan(0, 8));
        if ((flags & (1UL << 63)) != 0)
            throw new InvalidDataException("El canal de audio VE recibió un encabezado de sesión de video.");
        uint rawLength = BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(8, 4));
        if (rawLength is 0 or > MaxPacketLength)
            throw new InvalidDataException($"Longitud de paquete de audio VE inválida: {rawLength}.");
        byte[] payload = new byte[checked((int)rawLength)];
        await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new AudioPacket(payload, (flags & ConfigFlag) != 0);
    }
}

internal sealed class LinuxFfmpegAudioDecoder : IDisposable
{
    private const int FfmpegAgain = -11;
    private const int FfmpegEof = -541478725;
    private const int Padding = 64;
    private readonly AudioCodec _codec;
    private IntPtr _avutil;
    private IntPtr _avcodec;
    private IntPtr _context;
    private IntPtr _packet;
    private IntPtr _frame;
    private bool _disposed;

    private AvcodecFindDecoderByName? _findDecoder;
    private AvcodecAllocContext3? _allocContext;
    private AvcodecOpen2? _openCodec;
    private AvcodecSendPacket? _sendPacket;
    private AvcodecReceiveFrame? _receiveFrame;
    private AvcodecFreeContext? _freeContext;
    private AvPacketAlloc? _packetAlloc;
    private AvPacketUnref? _packetUnref;
    private AvPacketFree? _packetFree;
    private AvPacketFromData? _packetFromData;
    private AvFrameAlloc? _frameAlloc;
    private AvFrameUnref? _frameUnref;
    private AvFrameFree? _frameFree;
    private AvMalloc? _malloc;
    private AvFree? _free;
    private AvStrerror? _strerror;

    public LinuxFfmpegAudioDecoder(AudioCodec codec)
    {
        _codec = codec;
        if (codec == AudioCodec.Raw)
            return;

        try
        {
            _avutil = NativeLibrary.Load("libavutil.so.60");
            _avcodec = NativeLibrary.Load("libavcodec.so.62");
            LoadExports();
            string name = codec switch
            {
                AudioCodec.Opus => "opus",
                AudioCodec.Aac => "aac",
                AudioCodec.Flac => "flac",
                _ => throw new NotSupportedException($"Codec de audio VE no soportado: {codec}.")
            };
            IntPtr decoder = _findDecoder!(name);
            if (decoder == IntPtr.Zero)
                throw new NotSupportedException($"FFmpeg no contiene decoder de audio '{name}'.");
            _context = _allocContext!(decoder);
            if (_context == IntPtr.Zero)
                throw new OutOfMemoryException("FFmpeg no pudo crear el contexto de audio.");
            int result = _openCodec!(_context, decoder, IntPtr.Zero);
            if (result < 0)
                throw CreateException("avcodec_open2", result);
            _packet = _packetAlloc!();
            _frame = _frameAlloc!();
            if (_packet == IntPtr.Zero || _frame == IntPtr.Zero)
                throw new OutOfMemoryException("FFmpeg no pudo reservar el frame/paquete de audio.");
        }
        catch
        {
            DisposeNative();
            throw;
        }
    }

    public IReadOnlyList<byte[]> Decode(AudioPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (packet.IsConfiguration)
            return [];
        if (_codec == AudioCodec.Raw)
            return [packet.Data];

        IntPtr buffer = _malloc!(checked((nuint)(packet.Data.Length + Padding)));
        if (buffer == IntPtr.Zero)
            throw new OutOfMemoryException("FFmpeg no pudo reservar el bitstream de audio.");
        bool packetOwnsBuffer = false;
        try
        {
            Marshal.Copy(packet.Data, 0, buffer, packet.Data.Length);
            Span<byte> padding = stackalloc byte[Padding];
            padding.Clear();
            Marshal.Copy(padding.ToArray(), 0, IntPtr.Add(buffer, packet.Data.Length), Padding);
            int result = _packetFromData!(_packet, buffer, packet.Data.Length);
            if (result < 0)
                throw CreateException("av_packet_from_data", result);
            packetOwnsBuffer = true;
            result = _sendPacket!(_context, _packet);
            if (result < 0 && result != FfmpegAgain)
                throw CreateException("avcodec_send_packet", result);

            var decoded = new List<byte[]>();
            while (true)
            {
                result = _receiveFrame!(_context, _frame);
                if (result is FfmpegAgain or FfmpegEof)
                    break;
                if (result < 0)
                    throw CreateException("avcodec_receive_frame", result);
                try
                {
                    decoded.Add(ConvertFrame(_frame));
                }
                finally
                {
                    _frameUnref!(_frame);
                }
            }
            return decoded;
        }
        finally
        {
            if (packetOwnsBuffer)
                _packetUnref!(_packet);
            else
                _free!(buffer);
        }
    }

    private static byte[] ConvertFrame(IntPtr frame)
    {
        int pointerBytes = IntPtr.Size * 8;
        int extendedDataOffset = pointerBytes + sizeof(int) * 8;
        int samplesOffset = extendedDataOffset + IntPtr.Size + sizeof(int) * 2;
        int formatOffset = samplesOffset + sizeof(int);
        IntPtr extendedData = Marshal.ReadIntPtr(frame, extendedDataOffset);
        int samples = Marshal.ReadInt32(frame, samplesOffset);
        int format = Marshal.ReadInt32(frame, formatOffset);
        if (samples is <= 0 or > 1_000_000)
            throw new InvalidDataException($"AVFrame audio inválido: {samples} muestras.");

        bool planar = format is 5 or 6 or 7 or 8 or 9 or 11;
        int baseFormat = format is 10 or 11 ? 10 : planar ? format - 5 : format;
        byte[] pcm = new byte[checked(samples * 2 * sizeof(short))];
        for (int sample = 0; sample < samples; sample++)
        {
            for (int channel = 0; channel < 2; channel++)
            {
                IntPtr plane = ReadPlane(frame, extendedData, planar ? channel : 0);
                int sampleIndex = planar ? sample : sample * 2 + channel;
                short value = ReadSample(plane, sampleIndex, baseFormat);
                BinaryPrimitives.WriteInt16LittleEndian(
                    pcm.AsSpan((sample * 2 + channel) * sizeof(short), sizeof(short)), value);
            }
        }
        return pcm;
    }

    private static IntPtr ReadPlane(IntPtr frame, IntPtr extendedData, int index) =>
        extendedData != IntPtr.Zero
            ? Marshal.ReadIntPtr(extendedData, index * IntPtr.Size)
            : Marshal.ReadIntPtr(frame, index * IntPtr.Size);

    private static short ReadSample(IntPtr data, int index, int format)
    {
        if (data == IntPtr.Zero)
            return 0;
        return format switch
        {
            0 => (short)((Marshal.ReadByte(data, index) - 128) << 8),
            1 => Marshal.ReadInt16(data, index * 2),
            2 => (short)(Marshal.ReadInt32(data, index * 4) >> 16),
            3 => FloatToS16(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, index * 4))),
            4 => DoubleToS16(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, index * 8))),
            10 => (short)(Marshal.ReadInt64(data, index * 8) >> 48),
            _ => throw new NotSupportedException($"Formato de muestra FFmpeg {format} no compatible.")
        };
    }

    private void LoadExports()
    {
        _findDecoder = Load<AvcodecFindDecoderByName>(_avcodec, "avcodec_find_decoder_by_name");
        _allocContext = Load<AvcodecAllocContext3>(_avcodec, "avcodec_alloc_context3");
        _openCodec = Load<AvcodecOpen2>(_avcodec, "avcodec_open2");
        _sendPacket = Load<AvcodecSendPacket>(_avcodec, "avcodec_send_packet");
        _receiveFrame = Load<AvcodecReceiveFrame>(_avcodec, "avcodec_receive_frame");
        _freeContext = Load<AvcodecFreeContext>(_avcodec, "avcodec_free_context");
        _packetAlloc = Load<AvPacketAlloc>(_avcodec, "av_packet_alloc");
        _packetUnref = Load<AvPacketUnref>(_avcodec, "av_packet_unref");
        _packetFree = Load<AvPacketFree>(_avcodec, "av_packet_free");
        _packetFromData = Load<AvPacketFromData>(_avcodec, "av_packet_from_data");
        _frameAlloc = Load<AvFrameAlloc>(_avutil, "av_frame_alloc");
        _frameUnref = Load<AvFrameUnref>(_avutil, "av_frame_unref");
        _frameFree = Load<AvFrameFree>(_avutil, "av_frame_free");
        _malloc = Load<AvMalloc>(_avutil, "av_malloc");
        _free = Load<AvFree>(_avutil, "av_free");
        _strerror = Load<AvStrerror>(_avutil, "av_strerror");
    }

    private static T Load<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private Exception CreateException(string operation, int code)
    {
        IntPtr buffer = Marshal.AllocHGlobal(256);
        try
        {
            string detail = "error desconocido";
            if (_strerror is not null && _strerror(code, buffer, 256) >= 0)
                detail = Marshal.PtrToStringAnsi(buffer) ?? detail;
            return new InvalidOperationException($"FFmpeg {operation} falló ({code}): {detail}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposeNative();
    }

    private void DisposeNative()
    {
        if (_packet != IntPtr.Zero && _packetFree is not null) _packetFree(ref _packet);
        if (_frame != IntPtr.Zero && _frameFree is not null) _frameFree(ref _frame);
        if (_context != IntPtr.Zero && _freeContext is not null) _freeContext(ref _context);
        if (_avcodec != IntPtr.Zero) NativeLibrary.Free(_avcodec);
        if (_avutil != IntPtr.Zero) NativeLibrary.Free(_avutil);
        _avcodec = _avutil = IntPtr.Zero;
    }

    private static short FloatToS16(float value) =>
        value <= -1f ? short.MinValue : (short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue);
    private static short DoubleToS16(double value) =>
        value <= -1d ? short.MinValue : (short)Math.Round(Math.Clamp(value, -1d, 1d) * short.MaxValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvcodecFindDecoderByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvcodecAllocContext3(IntPtr codec);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvcodecOpen2(IntPtr context, IntPtr codec, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvcodecSendPacket(IntPtr context, IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvcodecReceiveFrame(IntPtr context, IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvcodecFreeContext(ref IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvPacketAlloc();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvPacketUnref(IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvPacketFree(ref IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvPacketFromData(IntPtr packet, IntPtr data, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvFrameAlloc();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvFrameUnref(IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvFrameFree(ref IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AvMalloc(nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AvFree(IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AvStrerror(int code, IntPtr buffer, nuint size);
}

internal sealed class LinuxSdlAudioPlayer : IDisposable
{
    private const uint SdlInitAudio = 0x00000010;
    private const uint DefaultPlaybackDevice = 0xffffffff;
    private const uint AudioS16Le = 0x8010;
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int TargetQueuedBytes = SampleRate * Channels * sizeof(short) * 80 / 1000;
    private readonly IntPtr _library;
    private readonly SdlQuitSubSystem _quit;
    private readonly SdlPutAudioStreamData _put;
    private readonly SdlResumeAudioStreamDevice _resume;
    private readonly SdlDestroyAudioStream _destroy;
    private readonly SdlGetAudioStreamQueued _getQueued;
    private readonly SdlClearAudioStream _clear;
    private readonly SdlGetError _getError;
    private IntPtr _stream;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioSpec
    {
        public uint Format;
        public int Channels;
        public int Frequency;
    }

    public LinuxSdlAudioPlayer()
    {
        _library = NativeLibrary.Load("libSDL3.so.0");
        try
        {
            var init = Load<SdlInitSubSystem>("SDL_InitSubSystem");
            _quit = Load<SdlQuitSubSystem>("SDL_QuitSubSystem");
            _put = Load<SdlPutAudioStreamData>("SDL_PutAudioStreamData");
            _resume = Load<SdlResumeAudioStreamDevice>("SDL_ResumeAudioStreamDevice");
            _destroy = Load<SdlDestroyAudioStream>("SDL_DestroyAudioStream");
            _getQueued = Load<SdlGetAudioStreamQueued>("SDL_GetAudioStreamQueued");
            _clear = Load<SdlClearAudioStream>("SDL_ClearAudioStream");
            _getError = Load<SdlGetError>("SDL_GetError");
            var open = Load<SdlOpenAudioDeviceStream>("SDL_OpenAudioDeviceStream");
            if (!init(SdlInitAudio))
                throw CreateException("SDL_InitSubSystem(SDL_INIT_AUDIO)");
            var spec = new AudioSpec { Format = AudioS16Le, Channels = Channels, Frequency = SampleRate };
            _stream = open(DefaultPlaybackDevice, ref spec, IntPtr.Zero, IntPtr.Zero);
            if (_stream == IntPtr.Zero)
                throw CreateException("SDL_OpenAudioDeviceStream");
            if (!_resume(_stream))
                throw CreateException("SDL_ResumeAudioStreamDevice");
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    public void Play(ReadOnlySpan<byte> pcm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pcm.IsEmpty || pcm.Length % (Channels * sizeof(short)) != 0)
            throw new InvalidDataException("El bloque PCM de VisionEngine no tiene muestras estéreo S16 completas.");
        if (_getQueued(_stream) > TargetQueuedBytes && !_clear(_stream))
            throw CreateException("SDL_ClearAudioStream");

        byte[] buffer = pcm.ToArray();
        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (!_put(_stream, pinned.AddrOfPinnedObject(), buffer.Length))
                throw CreateException("SDL_PutAudioStreamData");
        }
        finally
        {
            pinned.Free();
        }
    }

    private T Load<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private InvalidOperationException CreateException(string operation)
    {
        IntPtr error = _getError();
        return new InvalidOperationException(
            $"{operation} falló: {(error == IntPtr.Zero ? "error SDL desconocido" : Marshal.PtrToStringUTF8(error))}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_stream != IntPtr.Zero)
            _destroy(_stream);
        _quit(SdlInitAudio);
        NativeLibrary.Free(_library);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlInitSubSystem(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlQuitSubSystem(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlOpenAudioDeviceStream(uint device, ref AudioSpec spec, IntPtr callback, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlPutAudioStreamData(IntPtr stream, IntPtr buffer, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlResumeAudioStreamDevice(IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlDestroyAudioStream(IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SdlGetAudioStreamQueued(IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlClearAudioStream(IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SdlGetError();
}
