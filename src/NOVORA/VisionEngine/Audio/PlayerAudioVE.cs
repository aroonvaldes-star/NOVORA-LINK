using NOVORA.Services;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Reproductor SDL3 de VisionEngine.
///
/// Entrada:
///     PCM16LE
///     48 kHz
///     2 canales
///
/// Funciones:
///     - salida de audio independiente;
///     - cambio de endpoint en caliente;
///     - verificación del endpoint ligado;
///     - telemetría de cola SDL3.
///
/// La telemetría permite distinguir la latencia interna de VisionEngine
/// de la latencia posterior del dispositivo/driver.
/// </summary>
public sealed class PlayerAudioVE : IDisposable
{
    private const uint SdlInitAudioVE =
        0x00000010;

    private const int AudioS16LeVE =
        0x8010;

    private const int SampleRateVE =
        48000;

    private const int ChannelsVE =
        2;

    private const int BytesPerSampleVE =
        2;

    private const int BytesPerSecondVE =
        SampleRateVE *
        ChannelsVE *
        BytesPerSampleVE;

    private const int TelemetryPeriodMsVE =
        250;

    private const int TelemetryFlushPeriodMsVE =
        1000;

    private readonly NovoraPaths _paths;

    private readonly object _gateVE =
        new();

    private IntPtr _libraryVE;
    private IntPtr _streamVE;

    private SdlInitSubSystemDelegate? _initVE;
    private SdlQuitSubSystemDelegate? _quitVE;

    private SdlOpenAudioDeviceStreamDelegate? _openVE;
    private SdlPutAudioStreamDataDelegate? _putVE;
    private SdlResumeAudioStreamDeviceDelegate? _resumeVE;
    private SdlDestroyAudioStreamDelegate? _destroyVE;

    private SdlGetAudioStreamDeviceDelegate? _getStreamDeviceVE;
    private SdlGetAudioDeviceNameDelegate? _getDeviceNameVE;

    private SdlGetAudioStreamQueuedDelegate? _getQueuedVE;
    private SdlClearAudioStreamDelegate? _clearVE;

    private SdlGetErrorDelegate? _getErrorVE;

    private bool _audioSubsystemInitializedVE;
    private bool _disposedVE;

    private string _selectedOutputVE =
        OutputAudioVE.DefaultValueVE;

    private uint _resolvedPhysicalDeviceIdVE =
        OutputAudioVE.DefaultPlaybackDeviceIdVE;

    private uint _boundDeviceIdVE;

    private string _boundDeviceNameVE =
        string.Empty;

    private readonly string _telemetryPathVE;

    private StreamWriter? _telemetryWriterVE;

    private long _lastTelemetryTickVE;
    private long _lastTelemetryFlushTickVE;

    [StructLayout(
        LayoutKind.Sequential)]
    private struct SdlAudioSpecVE
    {
        public int Format;
        public int Channels;
        public int Frequency;
    }

    public PlayerAudioVE(
        NovoraPaths paths)
    {
        _paths =
            paths ??
            throw new ArgumentNullException(
                nameof(paths));

        string desktop =
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

        _telemetryPathVE =
            Path.Combine(
                desktop,
                "NOVORA-AUDIO-BT-VS-DIRECT.csv");
    }

    public bool IsOpenVE
    {
        get
        {
            lock (_gateVE)
            {
                return _streamVE !=
                       IntPtr.Zero;
            }
        }
    }

    public string SelectedOutputVE
    {
        get
        {
            lock (_gateVE)
            {
                return _selectedOutputVE;
            }
        }
    }

    public uint ResolvedPhysicalDeviceIdVE
    {
        get
        {
            lock (_gateVE)
            {
                return _resolvedPhysicalDeviceIdVE;
            }
        }
    }

    public uint BoundDeviceIdVE
    {
        get
        {
            lock (_gateVE)
            {
                return _boundDeviceIdVE;
            }
        }
    }

    public string BoundDeviceNameVE
    {
        get
        {
            lock (_gateVE)
            {
                return _boundDeviceNameVE;
            }
        }
    }

    public string TelemetryPathVE =>
        _telemetryPathVE;

    public int QueuedBytesVE
    {
        get
        {
            lock (_gateVE)
            {
                return GetQueuedBytesInternalVE();
            }
        }
    }

    public double QueuedMillisecondsVE
    {
        get
        {
            lock (_gateVE)
            {
                int queued =
                    GetQueuedBytesInternalVE();

                return BytesToMillisecondsVE(
                    queued);
            }
        }
    }

    public void OpenVE(
        string? selectedOutput = null)
    {
        ThrowIfDisposedVE();

        lock (_gateVE)
        {
            if (
                _streamVE !=
                IntPtr.Zero
            )
            {
                return;
            }

            EnsureRuntimeVE();
            EnsureTelemetryVE();

            string normalized =
                NormalizeOutputVE(
                    selectedOutput);

            OpenAndSetStreamVE(
                normalized);

            WriteTelemetryVE(
                "OPEN",
                force: true);
        }
    }

    /// <summary>
    /// Cambia la salida física en caliente.
    ///
    /// Se abre primero el stream nuevo.
    /// Si el nuevo stream funciona, se sustituye el anterior.
    /// Video y resto de VisionEngine permanecen activos.
    /// </summary>
    public void SwitchOutputVE(
        string? selectedOutput)
    {
        ThrowIfDisposedVE();

        string normalized =
            NormalizeOutputVE(
                selectedOutput);

        if (
            string.Equals(
                normalized,
                OutputAudioVE.DisabledValueVE,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                "Para desactivar AudioVE completamente reinicia la sesión con Audio desactivado.");
        }

        lock (_gateVE)
        {
            EnsureRuntimeVE();
            EnsureTelemetryVE();

            if (
                _streamVE ==
                IntPtr.Zero
            )
            {
                OpenAndSetStreamVE(
                    normalized);

                WriteTelemetryVE(
                    "OPEN",
                    force: true);

                return;
            }

            if (
                string.Equals(
                    normalized,
                    _selectedOutputVE,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            WriteTelemetryVE(
                "SWITCH_BEFORE",
                force: true);

            uint requestedDeviceId =
                OutputAudioVE
                    .ResolvePlaybackDeviceIdVE(
                        _libraryVE,
                        normalized);

            IntPtr newStream =
                OpenStreamVE(
                    requestedDeviceId,
                    normalized,
                    out uint boundDeviceId,
                    out string boundDeviceName);

            IntPtr oldStream =
                _streamVE;

            _streamVE =
                newStream;

            _selectedOutputVE =
                normalized;

            _resolvedPhysicalDeviceIdVE =
                requestedDeviceId;

            _boundDeviceIdVE =
                boundDeviceId;

            _boundDeviceNameVE =
                boundDeviceName;

            /*
             * IMPORTANTE:
             *
             * El stream anterior se destruye después de haber abierto
             * y validado el nuevo dispositivo.
             *
             * No copiamos la cola vieja al endpoint nuevo.
             */
            if (
                oldStream !=
                IntPtr.Zero
            )
            {
                _destroyVE?.Invoke(
                    oldStream);
            }

            WriteTelemetryVE(
                "SWITCH_AFTER",
                force: true);
        }
    }

    public void QueueVE(
        ReadOnlySpan<byte> pcm)
    {
        ThrowIfDisposedVE();

        if (pcm.IsEmpty)
        {
            return;
        }

        byte[] buffer =
            pcm.ToArray();

        GCHandle handle =
            GCHandle.Alloc(
                buffer,
                GCHandleType.Pinned);

        try
        {
            lock (_gateVE)
            {
                if (
                    _streamVE ==
                    IntPtr.Zero
                )
                {
                    throw new InvalidOperationException(
                        "PlayerAudioVE no está abierto.");
                }

                if (
                    !_putVE!(
                        _streamVE,
                        handle.AddrOfPinnedObject(),
                        buffer.Length)
                )
                {
                    throw new InvalidOperationException(
                        "SDL_PutAudioStreamData falló: " +
                        GetSdlErrorVE());
                }

                WriteTelemetryVE(
                    "QUEUE",
                    force: false);
            }
        }
        finally
        {
            handle.Free();
        }
    }


    public void ClearQueuedAudioVE()
    {
        ThrowIfDisposedVE();

        lock (_gateVE)
        {
            if (_streamVE == IntPtr.Zero || _clearVE is null)
                return;

            if (!_clearVE(_streamVE))
                throw new InvalidOperationException(
                    "SDL_ClearAudioStream falló: " +
                    GetSdlErrorVE());

            WriteTelemetryVE(
                "PRIVACY_CLEAR",
                force: true);
        }
    }

    private void OpenAndSetStreamVE(
        string normalized)
    {
        if (
            string.Equals(
                normalized,
                OutputAudioVE.DisabledValueVE,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                "AudioVE está desactivado.");
        }

        uint requestedDeviceId =
            OutputAudioVE
                .ResolvePlaybackDeviceIdVE(
                    _libraryVE,
                    normalized);

        IntPtr stream =
            OpenStreamVE(
                requestedDeviceId,
                normalized,
                out uint boundDeviceId,
                out string boundDeviceName);

        _streamVE =
            stream;

        _selectedOutputVE =
            normalized;

        _resolvedPhysicalDeviceIdVE =
            requestedDeviceId;

        _boundDeviceIdVE =
            boundDeviceId;

        _boundDeviceNameVE =
            boundDeviceName;
    }

    private IntPtr OpenStreamVE(
        uint requestedDeviceId,
        string requestedOutput,
        out uint boundDeviceId,
        out string boundDeviceName)
    {
        SdlAudioSpecVE spec =
            new()
            {
                Format =
                    AudioS16LeVE,

                Channels =
                    ChannelsVE,

                Frequency =
                    SampleRateVE
            };

        IntPtr stream =
            _openVE!(
                requestedDeviceId,
                ref spec,
                IntPtr.Zero,
                IntPtr.Zero);

        if (
            stream ==
            IntPtr.Zero
        )
        {
            throw new InvalidOperationException(
                $"SDL3 no pudo abrir '{requestedOutput}': {GetSdlErrorVE()}");
        }

        try
        {
            if (
                !_resumeVE!(
                    stream)
            )
            {
                throw new InvalidOperationException(
                    $"SDL3 no pudo reanudar '{requestedOutput}': {GetSdlErrorVE()}");
            }

            boundDeviceId =
                _getStreamDeviceVE!(
                    stream);

            if (
                boundDeviceId ==
                0
            )
            {
                throw new InvalidOperationException(
                    $"SDL3 abrió '{requestedOutput}', pero el stream no quedó ligado a un dispositivo.");
            }

            IntPtr namePointer =
                _getDeviceNameVE!(
                    boundDeviceId);

            boundDeviceName =
                namePointer ==
                    IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUTF8(
                        namePointer) ??
                      string.Empty;

            boundDeviceName =
                boundDeviceName.Trim();

            if (
                !string.Equals(
                    requestedOutput,
                    OutputAudioVE.DefaultValueVE,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    requestedOutput,
                    boundDeviceName,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException(
                    $"AudioVE solicitó '{requestedOutput}', pero SDL ligó el stream a '{boundDeviceName}'.");
            }

            return stream;
        }
        catch
        {
            _destroyVE?.Invoke(
                stream);

            throw;
        }
    }

    private void EnsureRuntimeVE()
    {
        if (
            _libraryVE !=
            IntPtr.Zero
        )
        {
            return;
        }

        string path =
            Path.Combine(
                _paths.ToolsDirectory,
                "SDL3.dll");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "VisionEngine no encuentra SDL3.dll para AudioVE.",
                path);
        }

        try
        {
            _libraryVE =
                NativeLibrary.Load(
                    path);

            _initVE =
                LoadVE<SdlInitSubSystemDelegate>(
                    "SDL_InitSubSystem");

            _quitVE =
                LoadVE<SdlQuitSubSystemDelegate>(
                    "SDL_QuitSubSystem");

            _openVE =
                LoadVE<SdlOpenAudioDeviceStreamDelegate>(
                    "SDL_OpenAudioDeviceStream");

            _putVE =
                LoadVE<SdlPutAudioStreamDataDelegate>(
                    "SDL_PutAudioStreamData");

            _resumeVE =
                LoadVE<SdlResumeAudioStreamDeviceDelegate>(
                    "SDL_ResumeAudioStreamDevice");

            _destroyVE =
                LoadVE<SdlDestroyAudioStreamDelegate>(
                    "SDL_DestroyAudioStream");

            _getStreamDeviceVE =
                LoadVE<SdlGetAudioStreamDeviceDelegate>(
                    "SDL_GetAudioStreamDevice");

            _getDeviceNameVE =
                LoadVE<SdlGetAudioDeviceNameDelegate>(
                    "SDL_GetAudioDeviceName");

            _getQueuedVE =
                LoadVE<SdlGetAudioStreamQueuedDelegate>(
                    "SDL_GetAudioStreamQueued");

            if (NativeLibrary.TryGetExport(
                    _libraryVE,
                    "SDL_ClearAudioStream",
                    out IntPtr clearAddress))
            {
                _clearVE =
                    Marshal.GetDelegateForFunctionPointer<SdlClearAudioStreamDelegate>(
                        clearAddress);
            }

            _getErrorVE =
                LoadVE<SdlGetErrorDelegate>(
                    "SDL_GetError");

            if (
                !_initVE(
                    SdlInitAudioVE)
            )
            {
                throw new InvalidOperationException(
                    "SDL_InitSubSystem(Audio) falló: " +
                    GetSdlErrorVE());
            }

            _audioSubsystemInitializedVE =
                true;
        }
        catch
        {
            CleanupRuntimeVE();
            throw;
        }
    }

    private int GetQueuedBytesInternalVE()
    {
        if (
            _streamVE ==
                IntPtr.Zero ||
            _getQueuedVE is null
        )
        {
            return 0;
        }

        int queued =
            _getQueuedVE(
                _streamVE);

        return queued;
    }

    private static double BytesToMillisecondsVE(
        int bytes)
    {
        if (bytes <= 0)
        {
            return bytes < 0
                ? -1d
                : 0d;
        }

        return (
            bytes /
            (double)BytesPerSecondVE
        ) * 1000d;
    }

    private void EnsureTelemetryVE()
    {
        if (
            _telemetryWriterVE is not null
        )
        {
            return;
        }

        try
        {
            FileStream stream =
                new(
                    _telemetryPathVE,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);

            _telemetryWriterVE =
                new StreamWriter(
                    stream,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: true));

            _telemetryWriterVE.WriteLine(
                "TimeUtc,Event,SelectedOutput,BoundDevice,BoundDeviceId,QueuedBytes,QueuedMs");

            _telemetryWriterVE.Flush();

            _lastTelemetryTickVE =
                0;

            _lastTelemetryFlushTickVE =
                Environment.TickCount64;
        }
        catch
        {
            /*
             * La telemetría nunca debe impedir que AudioVE reproduzca.
             */
            _telemetryWriterVE =
                null;
        }
    }

    private void WriteTelemetryVE(
        string eventName,
        bool force)
    {
        StreamWriter? writer =
            _telemetryWriterVE;

        if (writer is null)
        {
            return;
        }

        long now =
            Environment.TickCount64;

        if (
            !force &&
            _lastTelemetryTickVE != 0 &&
            now -
                _lastTelemetryTickVE <
                TelemetryPeriodMsVE
        )
        {
            return;
        }

        _lastTelemetryTickVE =
            now;

        int queuedBytes =
            GetQueuedBytesInternalVE();

        double queuedMs =
            BytesToMillisecondsVE(
                queuedBytes);

        string line =
            string.Join(
                ",",
                DateTimeOffset.UtcNow
                    .ToString(
                        "O",
                        CultureInfo.InvariantCulture),

                EscapeCsvVE(
                    eventName),

                EscapeCsvVE(
                    _selectedOutputVE),

                EscapeCsvVE(
                    _boundDeviceNameVE),

                _boundDeviceIdVE
                    .ToString(
                        CultureInfo.InvariantCulture),

                queuedBytes
                    .ToString(
                        CultureInfo.InvariantCulture),

                queuedMs
                    .ToString(
                        "0.000",
                        CultureInfo.InvariantCulture));

        try
        {
            writer.WriteLine(
                line);

            if (
                force ||
                now -
                    _lastTelemetryFlushTickVE >=
                    TelemetryFlushPeriodMsVE
            )
            {
                writer.Flush();

                _lastTelemetryFlushTickVE =
                    now;
            }
        }
        catch
        {
            /*
             * Fallo de diagnóstico:
             * AudioVE continúa reproduciendo.
             */
        }
    }

    private static string EscapeCsvVE(
        string? value)
    {
        string text =
            value ??
            string.Empty;

        return "\"" +
               text.Replace(
                   "\"",
                   "\"\"") +
               "\"";
    }

    private string GetSdlErrorVE()
    {
        try
        {
            if (_getErrorVE is null)
            {
                return "error SDL desconocido";
            }

            IntPtr pointer =
                _getErrorVE();

            if (
                pointer ==
                IntPtr.Zero
            )
            {
                return "error SDL desconocido";
            }

            return Marshal.PtrToStringUTF8(
                       pointer) ??
                   "error SDL desconocido";
        }
        catch
        {
            return "error SDL desconocido";
        }
    }

    private T LoadVE<T>(
        string export)
        where T : Delegate
    {
        if (
            _libraryVE ==
            IntPtr.Zero
        )
        {
            throw new InvalidOperationException(
                "SDL3 no está cargado.");
        }

        IntPtr address =
            NativeLibrary.GetExport(
                _libraryVE,
                export);

        return Marshal
            .GetDelegateForFunctionPointer<T>(
                address);
    }

    private static string NormalizeOutputVE(
        string? value)
        => string.IsNullOrWhiteSpace(
                value)
            ? OutputAudioVE.DefaultValueVE
            : value.Trim();

    private void CleanupTelemetryVE()
    {
        StreamWriter? writer =
            _telemetryWriterVE;

        _telemetryWriterVE =
            null;

        if (writer is null)
        {
            return;
        }

        try
        {
            writer.Flush();
        }
        catch
        {
        }

        try
        {
            writer.Dispose();
        }
        catch
        {
        }
    }

    private void CleanupRuntimeVE()
    {
        if (
            _streamVE !=
            IntPtr.Zero
        )
        {
            try
            {
                WriteTelemetryVE(
                    "CLOSE",
                    force: true);
            }
            catch
            {
            }

            try
            {
                _destroyVE?.Invoke(
                    _streamVE);
            }
            catch
            {
            }

            _streamVE =
                IntPtr.Zero;
        }

        if (
            _audioSubsystemInitializedVE
        )
        {
            try
            {
                _quitVE?.Invoke(
                    SdlInitAudioVE);
            }
            catch
            {
            }

            _audioSubsystemInitializedVE =
                false;
        }

        if (
            _libraryVE !=
            IntPtr.Zero
        )
        {
            try
            {
                NativeLibrary.Free(
                    _libraryVE);
            }
            catch
            {
            }

            _libraryVE =
                IntPtr.Zero;
        }

        CleanupTelemetryVE();
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);

    public void Dispose()
    {
        if (_disposedVE)
        {
            return;
        }

        lock (_gateVE)
        {
            CleanupRuntimeVE();

            _selectedOutputVE =
                OutputAudioVE.DefaultValueVE;

            _resolvedPhysicalDeviceIdVE =
                OutputAudioVE.DefaultPlaybackDeviceIdVE;

            _boundDeviceIdVE =
                0;

            _boundDeviceNameVE =
                string.Empty;

            _disposedVE =
                true;
        }
    }

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    [return: MarshalAs(
        UnmanagedType.I1)]
    private delegate bool
        SdlInitSubSystemDelegate(
            uint flags);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate void
        SdlQuitSubSystemDelegate(
            uint flags);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate IntPtr
        SdlOpenAudioDeviceStreamDelegate(
            uint device,
            ref SdlAudioSpecVE spec,
            IntPtr callback,
            IntPtr userdata);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    [return: MarshalAs(
        UnmanagedType.I1)]
    private delegate bool
        SdlPutAudioStreamDataDelegate(
            IntPtr stream,
            IntPtr buffer,
            int length);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    [return: MarshalAs(
        UnmanagedType.I1)]
    private delegate bool
        SdlResumeAudioStreamDeviceDelegate(
            IntPtr stream);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate void
        SdlDestroyAudioStreamDelegate(
            IntPtr stream);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate uint
        SdlGetAudioStreamDeviceDelegate(
            IntPtr stream);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate IntPtr
        SdlGetAudioDeviceNameDelegate(
            uint deviceId);

    /*
     * SDL3:
     *
     * int SDL_GetAudioStreamQueued(SDL_AudioStream *stream)
     *
     * Devuelve los bytes actualmente encolados.
     * -1 indica error.
     */
    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        SdlGetAudioStreamQueuedDelegate(
            IntPtr stream);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    [return: MarshalAs(
        UnmanagedType.I1)]
    private delegate bool
        SdlClearAudioStreamDelegate(
            IntPtr stream);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate IntPtr
        SdlGetErrorDelegate();
}
