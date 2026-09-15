using NOVORA.Service;
using NOVORA.NVIDIA;
using NOVORA.VisionEngine.Protocol;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Decoder FFmpeg de VisionEngine. Block D conserva cada frame recibido con
/// av_frame_clone() para transferir ownership al renderer sin copiar los
/// planos de video en CPU.
/// </summary>
public sealed class VEVideoDecoder : IDisposable
{
    private readonly NLServiceNovoraPaths _paths;

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
    private AvOptSetDelegate? _avOptSetVE;

    private IntPtr _codecContextVE;
    private IntPtr _packetVE;
    private IntPtr _frameVE;

    private VEProtocolCodec? _codecVE;
    private long _frameSequenceVE;
    private long _backendFramesVE;
    private byte[]? _configurationVE;
    private bool _disposed;

    public VEVideoDecoder(NLServiceNovoraPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool IsInitializedVE
        => _codecContextVE != IntPtr.Zero;

    public VEProtocolCodec? CodecVE
        => _codecVE;

    public string DecoderNameVE { get; private set; } = "Sin decoder";
    public string? FallbackReasonVE { get; private set; }
    public bool IsNvidiaVE => DecoderNameVE.EndsWith("_cuvid", StringComparison.Ordinal);
    public bool NvdecActiveVE => IsNvidiaVE && _backendFramesVE > 0;

    public void InitializeVE(VEProtocolCodec codec, bool preferNvidia = false)
    {
        ThrowIfDisposedVE();

        if (IsInitializedVE)
        {
            if (_codecVE == codec)
            {
                return;
            }

            throw new InvalidOperationException(
                "VEVideoDecoder ya fue inicializado con otro codec.");
        }

        if (!codec.IsVideoVE())
        {
            throw new NotSupportedException(
                $"Codec VisionEngine no soportado: {codec}.");
        }

        ValidateLibrariesVE();
        LoadLibrariesVE();
        LoadExportsVE();

        string? hardwareName = preferNvidia ? NLNVIDIADecoder.GetNameVE(codec) : null;
        if (hardwareName is not null)
        {
            try { OpenDecoderVE(hardwareName); }
            catch (Exception ex) when (ex is VEVideoFfmpegException or NotSupportedException)
            {
                FallbackReasonVE = ex.Message;
                OpenDecoderVE(codec.GetFfmpegDecoderNameVE());
            }
        }
        else OpenDecoderVE(codec.GetFfmpegDecoderNameVE());

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

    private void OpenDecoderVE(string name)
    {
        if (_codecContextVE != IntPtr.Zero) _avcodecFreeContextVE!(ref _codecContextVE);
        IntPtr codecPointer = _avcodecFindDecoderByNameVE!(name);
        if (codecPointer == IntPtr.Zero)
            throw new NotSupportedException($"FFmpeg no contiene decoder '{name}'.");
        _codecContextVE = _avcodecAllocContext3VE!(codecPointer);
        if (_codecContextVE == IntPtr.Zero) throw new OutOfMemoryException("No se pudo crear AVCodecContext.");
        if (name.EndsWith("_cuvid", StringComparison.Ordinal))
        {
            // FFmpeg CUVID usa ulMaxDisplayDelay=0 con low_delay; sin offsets de AVCodecContext.
            int optionResult = _avOptSetVE!(_codecContextVE, "flags", "+low_delay", 0);
            if (optionResult < 0) throw CreateVEVideoFfmpegException("av_opt_set low_delay", optionResult);
        }
        int result = _avcodecOpen2VE!(_codecContextVE, codecPointer, IntPtr.Zero);
        if (result < 0)
        {
            _avcodecFreeContextVE!(ref _codecContextVE);
            throw CreateVEVideoFfmpegException($"avcodec_open2 ({name})", result);
        }
        DecoderNameVE = name;
        _backendFramesVE = 0;
    }

    public void ConfigureVE(byte[] configuration)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(configuration);
        if (_configurationVE is not null && configuration.AsSpan().SequenceEqual(_configurationVE)) return;
        _configurationVE = configuration.ToArray();
        if (!IsNvidiaVE || _backendFramesVE == 0) return;
        // CUVID no puede ampliar un pool existente al rotar/cambiar el tamaño.
        // Cada nueva configuración abre un contexto, conservando los clones en CPU.
        try { OpenDecoderVE(DecoderNameVE); }
        catch (Exception ex) when (ex is VEVideoFfmpegException or NotSupportedException)
        {
            FallBackToSoftwareVE(ex);
        }
    }

    // Conserva cargada libavutil: el renderer puede poseer clones del decoder anterior.
    public void FallBackToSoftwareVE(Exception reason)
    {
        ThrowIfDisposedVE();
        if (!IsNvidiaVE || _codecVE is not VEProtocolCodec codec)
            throw new InvalidOperationException("No hay decoder NVIDIA que sustituir.");
        FallbackReasonVE = reason.Message;
        _avFrameUnrefVE!(_frameVE);
        OpenDecoderVE(codec.GetFfmpegDecoderNameVE());
    }

    public sealed class VEVideoFfmpegException(string message) : InvalidOperationException(message);

    public IReadOnlyList<VEVideoFrame> DecodeVE(
        VEVideoPacket packet,
        VEProtocolSession session)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(session);

        if (!IsInitializedVE ||
            _codecVE is not VEProtocolCodec codec)
        {
            throw new InvalidOperationException(
                "VEVideoDecoder no está inicializado.");
        }

        if (packet.IsConfiguration)
        {
            return Array.Empty<VEVideoFrame>();
        }

        session.ValidateVE();

        IntPtr inputBuffer =
            _avMallocVE!(
                checked((nuint)(
                    packet.Data.Length +
                    VEProtocolConstants.FfmpegInputPaddingSizeVE)));

        if (inputBuffer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                "FFmpeg no pudo reservar el bitstream de VisionEngine.");
        }

        bool packetOwnsBuffer = false;
        List<VEVideoFrame> frames = [];

        try
        {
            Marshal.Copy(
                packet.Data,
                0,
                inputBuffer,
                packet.Data.Length);

            byte[] padding =
                new byte[VEProtocolConstants.FfmpegInputPaddingSizeVE];

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
                throw CreateVEVideoFfmpegException(
                    "av_packet_from_data",
                    packetResult);
            }

            packetOwnsBuffer = true;

            int sendResult =
                _avcodecSendPacketVE!(
                    _codecContextVE,
                    _packetVE);

            if (sendResult == VEProtocolConstants.FfmpegAgainVE)
            {
                DrainFramesVE(frames, packet.PresentationTimeUs, codec, session);
                sendResult = _avcodecSendPacketVE!(_codecContextVE, _packetVE);
            }
            if (sendResult < 0) throw CreateVEVideoFfmpegException("avcodec_send_packet", sendResult);

            DrainFramesVE(
                frames,
                packet.PresentationTimeUs,
                codec,
                session);

            return frames;
        }
        catch
        {
            foreach (VEVideoFrame frame in frames) frame.Dispose();
            throw;
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
        List<VEVideoFrame> frames,
        long? sourcePts,
        VEProtocolCodec codec,
        VEProtocolSession session)
    {
        while (true)
        {
            int receiveResult =
                _avcodecReceiveFrameVE!(
                    _codecContextVE,
                    _frameVE);

            if (receiveResult == VEProtocolConstants.FfmpegAgainVE ||
                receiveResult == VEProtocolConstants.FfmpegEofVE)
            {
                return;
            }

            if (receiveResult < 0)
            {
                throw CreateVEVideoFfmpegException(
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
                VEVideoLayout layout =
                    VEVideoNativeFrame.ReadVE(clonedFrame);

                frames.Add(
                    new VEVideoFrame(
                        sequence: sequence,
                        sourcePresentationTimeUs: sourcePts,
                        codec: codec,
                        expectedWidth: session.Width,
                        expectedHeight: session.Height,
                        decodedAtUtc: DateTimeOffset.UtcNow,
                        nativeFrame: clonedFrame,
                        layout: layout,
                        release: ReleaseClonedFrameVE));

                _backendFramesVE++;
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

        // El avcodec compartido depende de avutil y
        // swresample. Los precargamos por ruta absoluta desde Tools para
        // que Windows no dependa del PATH global del usuario.
        _avutilLibraryVE = NativeLibrary.Load(avutil);
        _swresampleLibraryVE = NativeLibrary.Load(swresample);
        _avcodecLibraryVE = NativeLibrary.Load(avcodec);
    }

    private void LoadExportsVE()
    {
        _avOptSetVE = LoadDelegateVE<AvOptSetDelegate>(_avutilLibraryVE, "av_opt_set");
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

    private Exception CreateVEVideoFfmpegException(
        string operation,
        int errorCode)
    {
        string detail = GetFfmpegErrorVE(errorCode);

        return new VEVideoFfmpegException(
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
                nameof(VEVideoDecoder),
                "libavutil ya fue liberado antes que un VEVideoFrame.");

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
    private delegate int AvOptSetDelegate(IntPtr target,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int searchFlags);

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
