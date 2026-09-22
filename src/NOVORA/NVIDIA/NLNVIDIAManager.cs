using NOVORA.Service;
using NOVORA.VisionEngine.Video;
using System.Runtime.InteropServices;

namespace NOVORA.NVIDIA;

/// <summary>
/// Detección real y bajo demanda de CUDA/NVDEC/NVENC.
/// La actividad NVDEC procede de frames del decoder; las API sólo indican capacidad.
/// No usa timers ni polling.
/// </summary>
public sealed class NLNVIDIAManager
{
    private VEVideoStatus? _videoVE;
    private NLNVIDIAProfile _sessionProfileVE;
    private readonly object _gateVE = new();
    private NLNVIDIAProfile _profileVE = NLNVIDIAProfile.Competitive;
    private NLNVIDIAStatus _statusVE = new(
        NLNVIDIACapabilities.NoneVE(),
        NLNVIDIAPipeline.FallbackVE(),
        DateTimeOffset.UtcNow,
        "NVIDIA aún no evaluado.");

    public NLNVIDIAManager(NLServiceNovoraPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
    }

    public event EventHandler<NLNVIDIAStatus>? StatusChangedVE;

    public NLNVIDIAStatus StatusVE
    {
        get { lock (_gateVE) return _statusVE; }
    }

    public NLNVIDIAProfile ProfileVE => _profileVE;

    public void SetProfileVE(NLNVIDIAProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        _profileVE = profile;
        EvaluateVE();
    }

    public void EvaluateVE()
    {
        NLNVIDIACapabilities capabilities = DetectCapabilitiesVE();
        PublishDecoderStatusVE(capabilities);
    }

    public NLNVIDIAProfile BeginSessionVE()
    {
        lock (_gateVE) return _sessionProfileVE = _profileVE;
    }

    public void UpdateDecoderVE(VEVideoStatus video)
    {
        lock (_gateVE)
        {
            if (_videoVE?.State == video.State && _videoVE.DecoderName == video.DecoderName &&
                _videoVE.NvdecActive == video.NvdecActive &&
                (_videoVE.Stats.FramesDecoded > 0) == (video.Stats.FramesDecoded > 0) &&
                _videoVE.DecoderFallbackReason == video.DecoderFallbackReason) return;
            _videoVE = video;
        }
        PublishDecoderStatusVE(StatusVE.Capabilities);
    }

    private void PublishDecoderStatusVE(NLNVIDIACapabilities capabilities)
    {
        NLNVIDIAStatus status;
        lock (_gateVE)
        {
            bool running = _videoVE?.State == VEVideoStates.Streaming;
            bool active = running && _videoVE!.NvdecActive && _videoVE.Stats.FramesDecoded > 0;
            var pipeline = active
                ? new NLNVIDIAPipeline(_sessionProfileVE, true, false, false, false, false, 2)
                : NLNVIDIAPolicy.BuildVE(_profileVE, capabilities);
            string backend = running ? _videoVE!.DecoderName : _videoVE is null ? capabilities.Backend : "Sin decoder";
            string detail = active ? $"NVDEC activo ({backend}); transferencia a CPU."
                : running ? $"Decoder: {backend}; NVDEC no activo."
                : "Sin decodificación de video activa.";
            if (_videoVE?.DecoderFallbackReason is string reason) detail += $" Fallback: {reason}";
            status = new(capabilities with { Backend = backend }, pipeline, DateTimeOffset.UtcNow,
                $"Perfil solicitado: {_profileVE} (se aplica al iniciar). {detail}");
            _statusVE = status;
        }
        StatusChangedVE?.Invoke(this, status);
    }

    private NLNVIDIACapabilities DetectCapabilitiesVE()
    {
        // El perfil desactivado no consulta bibliotecas ni dispositivos NVIDIA.
        if (_profileVE == NLNVIDIAProfile.Disabled)
            return NLNVIDIACapabilities.NoneVE();

        IntPtr cuda = IntPtr.Zero;
        IntPtr nvdec = IntPtr.Zero;
        IntPtr nvenc = IntPtr.Zero;

        bool cudaLoaded = false;
        bool cudaInitialized = false;
        int deviceCount = 0;
        int driverVersion = 0;
        bool nvdecApi = false;
        bool nvencApi = false;
        uint nvencVersion = 0;

        try
        {
            cudaLoaded = NativeLibrary.TryLoad("nvcuda.dll", out cuda) && cuda != IntPtr.Zero;
            if (cudaLoaded)
            {
                CudaInitDelegate init = LoadDelegateVE<CudaInitDelegate>(cuda, "cuInit");
                CudaDeviceGetCountDelegate count = LoadDelegateVE<CudaDeviceGetCountDelegate>(cuda, "cuDeviceGetCount");
                CudaDriverGetVersionDelegate version = LoadDelegateVE<CudaDriverGetVersionDelegate>(cuda, "cuDriverGetVersion");

                cudaInitialized = init(0) == 0;
                if (cudaInitialized)
                {
                    _ = count(out deviceCount);
                    _ = version(out driverVersion);
                }
            }

            if (NativeLibrary.TryLoad("nvcuvid.dll", out nvdec) && nvdec != IntPtr.Zero)
            {
                nvdecApi =
                    NativeLibrary.TryGetExport(nvdec, "cuvidCreateVideoParser", out _) &&
                    NativeLibrary.TryGetExport(nvdec, "cuvidCreateDecoder", out _) &&
                    NativeLibrary.TryGetExport(nvdec, "cuvidDecodePicture", out _);
            }

            if (NativeLibrary.TryLoad("nvEncodeAPI64.dll", out nvenc) && nvenc != IntPtr.Zero &&
                NativeLibrary.TryGetExport(nvenc, "NvEncodeAPIGetMaxSupportedVersion", out IntPtr address))
            {
                NvEncodeGetMaxVersionDelegate getVersion = Marshal.GetDelegateForFunctionPointer<NvEncodeGetMaxVersionDelegate>(address);
                nvencApi = getVersion(out nvencVersion) == 0;
            }
        }
        catch
        {
            // La detección NVIDIA nunca debe impedir el fallback FFmpeg.
        }
        finally
        {
            if (nvenc != IntPtr.Zero) NativeLibrary.Free(nvenc);
            if (nvdec != IntPtr.Zero) NativeLibrary.Free(nvdec);
            if (cuda != IntPtr.Zero) NativeLibrary.Free(cuda);
        }

        // Campo histórico: esta ruta usa FFmpeg, no NOVORA.NVIDIA.Native.dll.

        return new NLNVIDIACapabilities(
            cudaLoaded,
            cudaInitialized,
            Math.Max(0, deviceCount),
            Math.Max(0, driverVersion),
            nvdecApi,
            nvencApi,
            nvencVersion,
            NativeBridgeAvailable: false,
            RtxVideoAvailable: false,
            FrucAvailable: false,
            Backend: "FFmpeg software");
    }

    private static T LoadDelegateVE<T>(IntPtr library, string export) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CudaInitDelegate(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CudaDeviceGetCountDelegate(out int count);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CudaDriverGetVersionDelegate(out int version);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NvEncodeGetMaxVersionDelegate(out uint version);
}
