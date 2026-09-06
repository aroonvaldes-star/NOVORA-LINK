using NOVORA.Services;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Salida física de reproducción seleccionable por VisionEngine.
///
/// SDL3 identifica los dispositivos mediante SDL_AudioDeviceID.
/// NOVORA muestra nombres legibles en la UI, pero al abrir el dispositivo
/// resuelve ese nombre contra la enumeración actual de SDL.
///
/// IMPORTANTE:
/// Si una salida específica no existe, NO se utiliza silenciosamente
/// el dispositivo predeterminado.
/// </summary>
public sealed record OutputAudioVE(
    string Value,
    string Label)
{
    public const string DisabledValueVE =
        "__disabled__";

    public const string DefaultValueVE =
        "__default__";

    private const uint SdlInitAudioVE =
        0x00000010;

    public const uint DefaultPlaybackDeviceIdVE =
        0xFFFFFFFF;

    public static IReadOnlyList<OutputAudioVE> GetAvailableVE(
        NovoraPaths paths)
    {
        ArgumentNullException.ThrowIfNull(
            paths);

        List<OutputAudioVE> outputs =
        [
            new(
                DisabledValueVE,
                "Desactivado"),

            new(
                DefaultValueVE,
                "Predeterminado de Windows")
        ];

        string sdlPath =
            Path.Combine(
                paths.ToolsDirectory,
                "SDL3.dll");

        if (!File.Exists(sdlPath))
        {
            return outputs;
        }

        IntPtr library =
            IntPtr.Zero;

        IntPtr devicesPtr =
            IntPtr.Zero;

        SdlQuitSubSystemDelegate? quit =
            null;

        bool initialized =
            false;

        try
        {
            library =
                NativeLibrary.Load(
                    sdlPath);

            SdlInitSubSystemDelegate init =
                LoadVE<SdlInitSubSystemDelegate>(
                    library,
                    "SDL_InitSubSystem");

            SdlGetAudioPlaybackDevicesDelegate getDevices =
                LoadVE<SdlGetAudioPlaybackDevicesDelegate>(
                    library,
                    "SDL_GetAudioPlaybackDevices");

            SdlGetAudioDeviceNameDelegate getName =
                LoadVE<SdlGetAudioDeviceNameDelegate>(
                    library,
                    "SDL_GetAudioDeviceName");

            SdlFreeDelegate free =
                LoadVE<SdlFreeDelegate>(
                    library,
                    "SDL_free");

            quit =
                LoadVE<SdlQuitSubSystemDelegate>(
                    library,
                    "SDL_QuitSubSystem");

            if (!init(SdlInitAudioVE))
            {
                return outputs;
            }

            initialized =
                true;

            devicesPtr =
                getDevices(
                    out int count);

            if (
                devicesPtr ==
                    IntPtr.Zero ||
                count <= 0
            )
            {
                return outputs;
            }

            HashSet<string> names =
                new(
                    StringComparer.OrdinalIgnoreCase);

            for (
                int index = 0;
                index < count;
                index++
            )
            {
                uint deviceId =
                    unchecked(
                        (uint)Marshal.ReadInt32(
                            devicesPtr,
                            index * sizeof(uint)));

                IntPtr namePointer =
                    getName(
                        deviceId);

                string? name =
                    namePointer ==
                        IntPtr.Zero
                        ? null
                        : Marshal.PtrToStringUTF8(
                            namePointer);

                if (
                    string.IsNullOrWhiteSpace(
                        name)
                )
                {
                    continue;
                }

                name =
                    name.Trim();

                if (!names.Add(name))
                {
                    continue;
                }

                outputs.Add(
                    new OutputAudioVE(
                        name,
                        name));
            }

            free(
                devicesPtr);

            devicesPtr =
                IntPtr.Zero;
        }
        catch
        {
            // El selector debe mantener como mínimo Default/Disabled.
        }
        finally
        {
            if (
                devicesPtr !=
                IntPtr.Zero &&
                library !=
                IntPtr.Zero
            )
            {
                try
                {
                    SdlFreeDelegate free =
                        LoadVE<SdlFreeDelegate>(
                            library,
                            "SDL_free");

                    free(
                        devicesPtr);
                }
                catch
                {
                }
            }

            if (
                initialized &&
                quit is not null
            )
            {
                try
                {
                    quit(
                        SdlInitAudioVE);
                }
                catch
                {
                }
            }

            if (
                library !=
                IntPtr.Zero
            )
            {
                try
                {
                    NativeLibrary.Free(
                        library);
                }
                catch
                {
                }
            }
        }

        return outputs;
    }

    /// <summary>
    /// Resuelve una selección de NOVORA a un SDL_AudioDeviceID físico.
    ///
    /// Para "__default__" devuelve el identificador especial de SDL.
    ///
    /// Para una salida específica, si el nombre no existe actualmente,
    /// lanza excepción. No existe fallback silencioso.
    /// </summary>
    public static uint ResolvePlaybackDeviceIdVE(
        IntPtr sdlLibrary,
        string? selectedValue)
    {
        if (
            sdlLibrary ==
            IntPtr.Zero
        )
        {
            throw new ArgumentException(
                "SDL3 no está cargado.",
                nameof(sdlLibrary));
        }

        string normalized =
            string.IsNullOrWhiteSpace(
                selectedValue)
                ? DefaultValueVE
                : selectedValue.Trim();

        if (
            string.Equals(
                normalized,
                DefaultValueVE,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return DefaultPlaybackDeviceIdVE;
        }

        if (
            string.Equals(
                normalized,
                DisabledValueVE,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                "La salida de audio está desactivada.");
        }

        SdlGetAudioPlaybackDevicesDelegate getDevices =
            LoadVE<SdlGetAudioPlaybackDevicesDelegate>(
                sdlLibrary,
                "SDL_GetAudioPlaybackDevices");

        SdlGetAudioDeviceNameDelegate getName =
            LoadVE<SdlGetAudioDeviceNameDelegate>(
                sdlLibrary,
                "SDL_GetAudioDeviceName");

        SdlFreeDelegate free =
            LoadVE<SdlFreeDelegate>(
                sdlLibrary,
                "SDL_free");

        IntPtr devicesPtr =
            getDevices(
                out int count);

        if (
            devicesPtr ==
                IntPtr.Zero ||
            count <= 0
        )
        {
            throw new InvalidOperationException(
                "SDL3 no publicó dispositivos físicos de reproducción.");
        }

        List<string> availableNames =
            [];

        try
        {
            for (
                int index = 0;
                index < count;
                index++
            )
            {
                uint deviceId =
                    unchecked(
                        (uint)Marshal.ReadInt32(
                            devicesPtr,
                            index * sizeof(uint)));

                IntPtr namePointer =
                    getName(
                        deviceId);

                string? name =
                    namePointer ==
                        IntPtr.Zero
                        ? null
                        : Marshal.PtrToStringUTF8(
                            namePointer);

                if (
                    string.IsNullOrWhiteSpace(
                        name)
                )
                {
                    continue;
                }

                name =
                    name.Trim();

                availableNames.Add(
                    name);

                if (
                    string.Equals(
                        name,
                        normalized,
                        StringComparison.OrdinalIgnoreCase)
                )
                {
                    return deviceId;
                }
            }
        }
        finally
        {
            free(
                devicesPtr);
        }

        string available =
            availableNames.Count == 0
                ? "ninguna"
                : string.Join(
                    " | ",
                    availableNames);

        throw new InvalidOperationException(
            $"SDL3 no encontró la salida '{normalized}'. Disponibles: {available}.");
    }

    private static T LoadVE<T>(
        IntPtr library,
        string export)
        where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(
            NativeLibrary.GetExport(
                library,
                export));

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
        SdlGetAudioPlaybackDevicesDelegate(
            out int count);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate IntPtr
        SdlGetAudioDeviceNameDelegate(
            uint deviceId);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate void
        SdlFreeDelegate(
            IntPtr memory);
}
