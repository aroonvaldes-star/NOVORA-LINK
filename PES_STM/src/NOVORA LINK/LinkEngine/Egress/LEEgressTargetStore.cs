using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.LinkEngine.Egress;

/// <summary>Guarda el destino de transmisión con DPAPI del usuario actual.</summary>
public static class LEEgressTargetStore
{
    private static string PathLE => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVORA-LINK", "LEEgressTarget.dat");

    public static LEEgressTarget? Load()
    {
        try
        {
            if (!File.Exists(PathLE)) return null;
            byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(PathLE), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LEEgressTarget>(clear);
        }
        catch { return null; }
    }

    public static void Save(LEEgressTarget target)
    {
        _ = target.ServerUri;
        Directory.CreateDirectory(Path.GetDirectoryName(PathLE)!);
        byte[] cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(target), null, DataProtectionScope.CurrentUser);
        string temporary = PathLE + ".partial";
        File.WriteAllBytes(temporary, cipher);
        File.Move(temporary, PathLE, true);
    }

    public static void Clear() { try { File.Delete(PathLE); } catch { } }
}
