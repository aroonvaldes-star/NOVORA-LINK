using NOVORA.Services;
using NOVORA.VisionEngine.Protocol;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Decoder FFmpeg de VisionEngine. Block D conserva cada frame recibido con
/// av_frame_clone() para transferir ownership al renderer sin copiar los
/// planos de video en CPU.
/// </summary>
public sealed class DecoderVideoVE : IDisposable
{
    private readonly NovoraPaths _paths;

    private IntPtr _avutilLibraryVE;
    private IntPtr _swresampleLibraryVE;
    private IntPtr _avcodecLibraryVE;

    private AvcodecFindDecoderByNameDelegate? _avcodecFindDecoderByNameVE;
    private AvcodecAllocContext3Delegate? _avcodecAllocContext3VE;
    private AvcodecOpen2Delegate? _avcodecOpen2VE;
    private AvcodecSendPacketDelegate? _avcodecSendPacketVE;
    private AvcodecReceiveFrameDelegate? _avcodecReceiveFrameVE;
    private AvcodecFreeContextDelegate? _avcodecFreeContextVE;
    private AvPacketAllocDelegate? _avPacketAllocVE;
    private AvPacketUnrefDelegate? _avPacketUnrefVE;
    private AvPacketFreeDelegate? _avPacketFreeVE;
    private AvPacketFromDataDelegate? _avPacketFromDataVE;
    private AvFrameAllocDelegate? _avFrameAllocVE;
    private AvFrameCloneDelegate? _avFrameCloneVE;
    private AvFrameUnrefDelegate? _avFrameUnrefVE;
    private AvFrameFreeDelegate? _avFrameFreeVE;
    private AvMallocDelegate? _avMallocVE;
    private AvFreeDelegate? _avFreeVE;
    private AvStrerrorDelegate? _avStrerrorVE;

    private IntPtr _codecContextVE;
    private IntPtr _packetVE;
    private IntPtr _frameVE;

    private CodecProtocolVE? _codecVE;
    private long _frameSequenceVE;
    private bool _disposed;

    public DecoderVideoVE(NovoraPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool IsInitializedVE
        => _codecContextVE != IntPtr.Zero;

    public CodecProtocolVE? CodecVE
        => _codecVE;

    public void InitializeVE(CodecProtocolVE codec)
    {
        ThrowIfDisposedVE();

        if (IsInitializedVE)
        {
            if (_codecVE == codec)
            {
                return;
            }

            throw new InvalidOperationException(
                "DecoderVideoVE ya fue inicializado con otro codec.");
        }

        if (!codec.IsVideoVE())
        {
            throw new NotSupportedException(
                $"Codec VisionEngine no soportado: {codec}.");
        }

        ValidateLibrariesVE();
        LoadLibrariesVE();
        LoadExportsVE();

        string decoderName = codec.GetFfmpegDecoderNameVE();

        IntPtr codecPointer =
            _avcodecFindDecoderByNameVE!(decoderName);

        if (codecPointer == IntPtr.Zero)
        {
            throw new NotSupportedException(
                $"FFmpeg no contiene decoder para '{decoderName}'.");
        }

        _codecContextVE =
            _avcodecAllocContext3VE!(codecPointer);

        if (_codecContextVE == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                "FFmpeg no pudo crear AVCodecContext para VisionEngine.");
        }

        int openResult =
            _avcodecOpen2VE!(
                _codecContextVE,
                codecPointer,
                IntPtr.Zero);

        if (openResult < 0)
        {
            throw CreateFfmpegExceptionVE(
                "avcodec_open2",
                openResult);
        }

        _packetVE = _avPacketAllocVE!();
        _frameVE = _avFrameAllocVE!();

        if (_packetVE == IntPtr.Zero ||
            _frameVE == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                "FFmpeg no pudo reservar AVPacket/AVFrame para VisionEngine.");
        }

        _codecVE = codec;
    }

    public IReadOnlyList<FrameVideoVE> DecodeVE(
        PacketVideoVE packet,
        SessionProtocolVE session)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(session);

        if (!IsInitializedVE ||
            _codecVE is not CodecProtocolVE codec)
        {
            throw new InvalidOperationException(
                "DecoderVideoVE no está inicializado.");
        }

        if (packet.IsConfiguration)
        {
            return Array.Empty<FrameVideoVE>();
        }

        session.ValidateVE();

        IntPtr inputBuffer =
            _avMallocVE!(
                checked((nuint)(
                    packet.Data.Length +
                    ConstantsProtocolVE.FfmpegInputPaddingSizeVE)));

        if (inputBuffer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                "FFmpeg no pudo reservar el bitstream de VisionEngine.");
        }

        bool packetOwnsBuffer = false;

        try
        {
            Marshal.Copy(
                packet.Data,
                0,
                inputBuffer,
                packet.Data.Length);

            byte[] padding =
                new byte[ConstantsProtocolVE.FfmpegInputPaddingSizeVE];

            Marshal.Copy(
                padding,
                0,
                IntPtr.Add(inputBuffer, packet.Data.Length),
                padding.Length);

            int packetResult =
                _avPacketFromDataVE!(
                    _packetVE,
                    inputBuffer,
                    packet.Data.Length);

            if (packetResult < 0)
            {
                throw CreateFfmpegExceptionVE(
                    "av_packet_from_data",
                    packetResult);
            }

            packetOwnsBuffer = true;

            int sendResult =
                _avcodecSendPacketVE!(
                    _codecContextVE,
                    _packetVE);

            if (sendResult < 0 &&
                sendResult != ConstantsProtocolVE.FfmpegAgainVE)
            {
                throw CreateFfmpegExceptionVE(
                    "avcodec_send_packet",
                    sendResult);
            }

            List<FrameVideoVE> frames = [];

            DrainFramesVE(
                frames,
                packet.PresentationTimeUs,
                codec,
                session);

            return frames;
        }
        finally
        {
            if (packetOwnsBuffer)
            {
                _avPacketUnrefVE!(_packetVE);
            }
            else if (inputBuffer != IntPtr.Zero)
            {
                _avFreeVE!(inputBuffer);
            }
        }
    }

    private void DrainFramesVE(
        List<FrameVideoVE> frames,
        long? sourcePts,
        CodecProtocolVE codec,
        SessionProtocolVE session)
    {
        while (true)
        {
            int receiveResult =
                _avcodecReceiveFrameVE!(
                    _codecContextVE,
                    _frameVE);

            if (receiveResult == ConstantsProtocolVE.FfmpegAgainVE ||
                receiveResult == ConstantsProtocolVE.FfmpegEofVE)
            {
                return;
            }

            if (receiveResult < 0)
            {
                throw CreateFfmpegExceptionVE(
                    "avcodec_receive_frame",
                    receiveResult);
            }

            long sequence =
                Interlocked.Increment(
                    ref _frameSequenceVE);

            IntPtr clonedFrame =
                _avFrameCloneVE!(_frameVE);

            if (clonedFrame == IntPtr.Zero)
            {
                _avFrameUnrefVE!(_frameVE);
                throw new OutOfMemoryException(
                    "FFmpeg no pudo clonar AVFrame para RendererVE.");
            }

            try
            {
                LayoutVideoVE layout =
                    NativeFrameVideoVE.ReadVE(clonedFrame);

                frames.Add(
                    new FrameVideoVE(
                        sequence: sequence,
                        sourcePresentationTimeUs: sourcePts,
                        codec: codec,
                        expectedWidth: session.Width,
                        expectedHeight: session.Height,
                        decodedAtUtc: DateTimeOffset.UtcNow,
                        nativeFrame: clonedFrame,
                        layout: layout,
                        release: ReleaseClonedFrameVE));

                clonedFrame = IntPtr.Zero;
            }
            finally
            {
                if (clonedFrame != IntPtr.Zero)
                    ReleaseClonedFrameVE(clonedFrame);

                _avFrameUnrefVE!(_frameVE);
            }
        }
    }

    private void ValidateLibrariesVE()
    {
        string avcodec = Path.Combine(
            _paths.ToolsDirectory,
            "avcodec-62.dll");

        string avutil = Path.Combine(
            _paths.ToolsDirectory,
            "avutil-60.dll");

        string swresample = Path.Combine(
            _paths.ToolsDirectory,
            "swresample-6.dll");

        if (!File.Exists(avcodec))
        {
            throw new FileNotFoundException(
                "VisionEngine no encuentra FFmpeg avcodec-62.dll.",
                avcodec);
        }

        if (!File.Exists(avutil))
        {
            throw new FileNotFoundException(
                "VisionEngine no encuentra FFmpeg avutil-60.dll.",
                avutil);
        }

        if (!File.Exists(swresample))
        {
            throw new FileNotFoundException(
                "VisionEngine no encuentra FFmpeg swresample-6.dll.",
                swresample);
        }
    }

    private void LoadLibrariesVE()
    {
        string avutil = Path.Combine(
            _paths.ToolsDirectory,
            "avutil-60.dll");

        string swresample = Path.Combine(
            _paths.ToolsDirectory,
            "swresample-6.dll");

        string avcodec = Path.Combine(
            _paths.ToolsDirectory,
            "avcodec-62.dll");

        // El avcodec distribuido con scrcpy 4.1 depende de avutil y
        // swresample. Los precargamos por ruta absoluta desde Tools para
        // que Windows no dependa del PATH global del usuario.
        _avutilLibraryVE = NativeLibrary.Load(avutil);
        _swresampleLibraryVE = NativeLibrary.Load(swresample);
        _avcodecLibraryVE = NativeLibrary.Load(avcodec);
    }

    private void LoadExportsVE()
    {
        _avcodecFindDecoderByNameVE = LoadDelegateVE<AvcodecFindDecoderByNameDelegate>(
            _avcodecLibraryVE,
            "avcodec_find_decoder_by_name");

        _avcodecAllocContext3VE = LoadDelegateVE<AvcodecAllocContext3Delegate>(
            _avcodecLibraryVE,
            "avcodec_alloc_context3");

        _avcodecOpen2VE = LoadDelegateVE<AvcodecOpen2Delegate>(
            _avcodecLibraryVE,
            "avcodec_open2");

        _avcodecSendPacketVE = LoadDelegateVE<AvcodecSendPacketDelegate>(
            _avcodecLibraryVE,
            "avcodec_send_packet");

        _avcodecReceiveFrameVE = LoadDelegateVE<AvcodecReceiveFrameDelegate>(
            _avcodecLibraryVE,
            "avcodec_receive_frame");

        _avcodecFreeContextVE = LoadDelegateVE<AvcodecFreeContextDelegate>(
            _avcodecLibraryVE,
            "avcodec_free_context");

        _avPacketAllocVE = LoadDelegateVE<AvPacketAllocDelegate>(
            _avcodecLibraryVE,
            "av_packet_alloc");

        _avPacketUnrefVE = LoadDelegateVE<AvPacketUnrefDelegate>(
            _avcodecLibraryVE,
            "av_packet_unref");

        _avPacketFreeVE = LoadDelegateVE<AvPacketFreeDelegate>(
            _avcodecLibraryVE,
            "av_packet_free");

        _avPacketFromDataVE = LoadDelegateVE<AvPacketFromDataDelegate>(
            _avcodecLibraryVE,
            "av_packet_from_data");

        _avFrameAllocVE = LoadDelegateVE<AvFrameAllocDelegate>(
            _avutilLibraryVE,
            "av_frame_alloc");

        _avFrameCloneVE = LoadDelegateVE<AvFrameCloneDelegate>(
            _avutilLibraryVE,
            "av_frame_clone");

        _avFrameUnrefVE = LoadDelegateVE<AvFrameUnrefDelegate>(
            _avutilLibraryVE,
            "av_frame_unref");

        _avFrameFreeVE = LoadDelegateVE<AvFrameFreeDelegate>(
            _avutilLibraryVE,
            "av_frame_free");

        _avMallocVE = LoadDelegateVE<AvMallocDelegate>(
            _avutilLibraryVE,
            "av_malloc");

        _avFreeVE = LoadDelegateVE<AvFreeDelegate>(
            _avutilLibraryVE,
            "av_free");

        _avStrerrorVE = LoadDelegateVE<AvStrerrorDelegate>(
            _avutilLibraryVE,
            "av_strerror");
    }

    private static TDelegate LoadDelegateVE<TDelegate>(
        IntPtr library,
        string exportName)
        where TDelegate : Delegate
    {
        IntPtr export =
            NativeLibrary.GetExport(
                library,
                exportName);

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(
            export);
    }

    private Exception CreateFfmpegExceptionVE(
        string operation,
        int errorCode)
    {
        string detail = GetFfmpegErrorVE(errorCode);

        return new InvalidOperationException(
            $"FFmpeg {operation} falló ({errorCode}): {detail}");
    }

    private string GetFfmpegErrorVE(int errorCode)
    {
        if (_avStrerrorVE is null)
        {
            return "error desconocido";
        }

        const int bufferSize = 256;
        IntPtr buffer =
            Marshal.AllocHGlobal(bufferSize);

        try
        {
            int result =
                _avStrerrorVE(
                    errorCode,
                    buffer,
                    bufferSize);

            if (result < 0)
            {
                return "error desconocido";
            }

            return Marshal.PtrToStringAnsi(buffer)
                ?? "error desconocido";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }


    private void ReleaseClonedFrameVE(IntPtr frame)
    {
        if (frame == IntPtr.Zero)
            return;

        AvFrameFreeDelegate free = _avFrameFreeVE
            ?? throw new ObjectDisposedException(
                nameof(DecoderVideoVE),
                "libavutil ya fue liberado antes que un FrameVideoVE.");

        IntPtr local = frame;
        free(ref local);
    }

    private void ThrowIfDisposedVE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_packetVE != IntPtr.Zero &&
            _avPacketFreeVE is not null)
        {
            _avPacketFreeVE(ref _packetVE);
        }

        if (_frameVE != IntPtr.Zero &&
            _avFrameFreeVE is not null)
        {
            _avFrameFreeVE(ref _frameVE);
        }

        if (_codecContextVE != IntPtr.Zero &&
            _avcodecFreeContextVE is not null)
        {
            _avcodecFreeContextVE(ref _codecContextVE);
        }

        if (_avcodecLibraryVE != IntPtr.Zero)
        {
            NativeLibrary.Free(_avcodecLibraryVE);
            _avcodecLibraryVE = IntPtr.Zero;
        }

        if (_swresampleLibraryVE != IntPtr.Zero)
        {
            NativeLibrary.Free(_swresampleLibraryVE);
            _swresampleLibraryVE = IntPtr.Zero;
        }

        if (_avutilLibraryVE != IntPtr.Zero)
        {
            NativeLibrary.Free(_avutilLibraryVE);
            _avutilLibraryVE = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AvcodecFindDecoderByNameDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AvcodecAllocContext3Delegate(IntPtr codec);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AvcodecOpen2Delegate(
        IntPtr codecContext,
        IntPtr codec,
        IntPtr options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AvcodecSendPacketDelegate(
        IntPtr codecContext,
        IntPtr packet);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AvcodecReceiveFrameDelegate(
        IntPtr codecContext,
        IntPtr frame);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AvcodecFreeContextDelegate(
        ref IntPtr codecContext);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AvPacketAllocDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AvPacketUnrefDelegate(IntPtr packet);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AvPacketFreeDelegate(ref IntPtr packet);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AvPacketFromDataDelegate(
        IntPtr packet,
        IntPtr data,
        int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AvFrameAllocDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AvFrameCloneDelegate(IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AvFrameUnrefDelegate(IntPtr frame);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AvFrameFreeDelegate(ref IntPtr frame);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AvMallocDelegate(nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AvFreeDelegate(IntPtr pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AvStrerrorDelegate(
        int errorCode,
        IntPtr errorBuffer,
        nuint errorBufferSize);
}
