namespace NOVORA.NVIDIA;

/// <summary>Ubicación del componente nativo opcional junto al ejecutable.</summary>
public static class NLNVIDIAPaths
{
    public static string NativeBridge(string baseDirectory)
        => Path.Combine(Path.GetFullPath(baseDirectory), "NVIDIA", "Native", "NOVORA.NVIDIA.Native.dll");
}
