using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace NOVORA.Linux.VisionEngine;

internal sealed record LinuxOutputDevice(uint Id, string Name)
{
    public override string ToString() => Name;
}

internal static class LinuxSdlDevices
{
    // SDL instance IDs are valid only while their subsystem remains initialized.
    // Hold one process-wide reference; individual sessions retain/release their own references.
    private static readonly IntPtr Library = NativeLibrary.Load("libSDL3.so.0");
    private static readonly object InitializationGate = new();
    private static bool _videoInitialized, _audioInitialized;

    public static IReadOnlyList<LinuxOutputDevice> Monitors() => Enumerate(true);
    public static IReadOnlyList<LinuxOutputDevice> AudioOutputs() => Enumerate(false);

    private static IReadOnlyList<LinuxOutputDevice> Enumerate(bool video)
    {
        IReadOnlyList<LinuxOutputDevice>? result = null;
        void Read()
        {
            IntPtr library = Library;
            T Load<T>(string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var init = Load<Init>("SDL_InitSubSystem");
            var error = Load<GetError>("SDL_GetError");
            uint flags = video ? 0x20u : 0x10u;
            lock (InitializationGate)
            {
                if (!(video ? _videoInitialized : _audioInitialized))
                {
                    if (!init(flags)) throw new InvalidOperationException(Marshal.PtrToStringUTF8(error()) ?? "SDL no pudo enumerar salidas.");
                    if (video) _videoInitialized = true;
                    else _audioInitialized = true;
                }
            }
            {
                var get = Load<GetDevices>(video ? "SDL_GetDisplays" : "SDL_GetAudioPlaybackDevices");
                var name = Load<GetName>(video ? "SDL_GetDisplayName" : "SDL_GetAudioDeviceName");
                var free = Load<Free>("SDL_free");
                IntPtr devices = get(out int count);
                if (devices == IntPtr.Zero) throw new InvalidOperationException(Marshal.PtrToStringUTF8(error()) ?? "No se pudieron enumerar dispositivos SDL.");
                var list = new List<LinuxOutputDevice> { new(0, "Predeterminado del sistema") };
                try
                {
                    for (int index = 0; index < count; index++)
                    {
                        uint id = unchecked((uint)Marshal.ReadInt32(devices, checked(index * sizeof(uint))));
                        list.Add(new LinuxOutputDevice(id, Marshal.PtrToStringUTF8(name(id)) ?? $"Dispositivo {id}"));
                    }
                }
                finally { free(devices); }
                result = list;
            }
        }
        if (video && !Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.InvokeAsync(Read).GetAwaiter().GetResult();
        else Read();
        return result!;
    }

    public static uint ResolveMonitorId(uint id, string? name = null) => ResolveId(Monitors(), id, name, "monitor");
    public static uint ResolveAudioDeviceId(uint id, string? name = null) => ResolveId(AudioOutputs(), id, name, "salida de audio");

    internal static uint ResolveId(IReadOnlyList<LinuxOutputDevice> devices, uint id, string? name, string kind)
    {
        if (id == 0 && string.IsNullOrEmpty(name)) return 0;
        if (!string.IsNullOrWhiteSpace(name))
        {
            LinuxOutputDevice[] named = devices.Where(device => device.Id != 0 && device.Name == name).ToArray();
            if (named.Length == 1) return named[0].Id;
            if (named.Length > 1 && named.Any(device=>device.Id==id)) return id;
            if (named.Length > 1)
                throw new InvalidOperationException($"Hay varias salidas con el nombre {name}; vuelve a seleccionar la {kind} para distinguirlas.");
            throw new InvalidOperationException($"La {kind} seleccionada ({name}) ya no está disponible. Selecciona otra salida o el predeterminado.");
        }
        if (devices.Any(device => device.Id == id)) return id;
        throw new InvalidOperationException($"La {kind} seleccionada ya no está disponible. Selecciona otra salida o el predeterminado.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool Init(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetDevices(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetName(uint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetError();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(IntPtr pointer);
}
