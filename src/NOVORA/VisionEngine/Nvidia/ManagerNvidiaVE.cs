using NOVORA.Services;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Nvidia;

/// <summary>
/// Detección real y bajo demanda de CUDA/NVDEC/NVENC.
/// No activa un FastPath si no existe el bridge nativo de NOVORA.
/// No usa timers ni polling.
/// </summary>
public sealed class ManagerNvidiaVE
{
    private readonly NovoraPaths _pathsVE;
    private readonly object _gateVE = new();
    private ProfileNvidiaVE _profileVE = ProfileNvidiaVE.Automatic;
    private StatusNvidiaVE _statusVE = new(
        CapabilitiesNvidiaVE.NoneVE(),
        PipelineNvidiaVE.FallbackVE(),
        DateTimeOffset.UtcNow,
        "NVIDIA aún no evaluado.");

    public ManagerNvidiaVE(NovoraPaths paths)
    {
        _pathsVE = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler<StatusNvidiaVE>? StatusChangedVE;

    public StatusNvidiaVE StatusVE
    {
        get { lock (_gateVE) return _statusVE; }
    }

    public ProfileNvidiaVE ProfileVE => _profileVE;

    public void SetProfileVE(ProfileNvidiaVE profile)
    {
        _profileVE = profile;
        EvaluateVE();
    }

    public void EvaluateVE()
    {
        CapabilitiesNvidiaVE capabilities = DetectCapabilitiesVE();
        PipelineNvidiaVE pipeline = PolicyNvidiaVE.BuildVE(_profileVE, capabilities);

        StatusNvidiaVE status = new(
            capabilities,
            pipeline,
            DateTimeOffset.UtcNow,
            _profileVE == ProfileNvidiaVE.Disabled
                ? "NVIDIA desactivada; VisionEngine usará FFmpeg."
                : pipeline.UseNvdec
                ? "Perfil NVIDIA disponible; decodificador activo sigue en FFmpeg hasta integrar el backend nativo."
                : capabilities.NvdecApiAvailable
                    ? "NVDEC detectado; FFmpeg continúa activo hasta disponer del bridge nativo NOVORA."
                    : "VisionEngine usará FFmpeg.");

        lock (_gateVE)
            _statusVE = status;

        StatusChangedVE?.Invoke(this, status);
    }

    private CapabilitiesNvidiaVE DetectCapabilitiesVE()
    {
        // El perfil desactivado no consulta bibliotecas ni dispositivos NVIDIA.
        if (_profileVE == ProfileNvidiaVE.Disabled)
            return CapabilitiesNvidiaVE.NoneVE();

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

        bool bridge = File.Exists(Path.Combine(_pathsVE.ToolsDirectory, "NOVORA.VisionEngine.Nvidia.Native.dll"));

        return new CapabilitiesNvidiaVE(
            cudaLoaded,
            cudaInitialized,
            Math.Max(0, deviceCount),
            Math.Max(0, driverVersion),
            nvdecApi,
            nvencApi,
            nvencVersion,
            bridge,
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
