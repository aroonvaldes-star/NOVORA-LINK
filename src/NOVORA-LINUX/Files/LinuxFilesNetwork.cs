using System.Text.Json;
namespace NOVORA.Linux.Files;

internal sealed record LinuxFilesAllocation(long Revision,long Generation,string LinkKey,int Direction,long BytesPerSecond,int BurstBytes,int ValidForMilliseconds,int Mode);
internal static class LinuxFilesNetwork
{
    public static LinuxFilesAllocation Allocate(string json,long generation,long revision,string transport)
    {
        using var request=JsonDocument.Parse(json);
        if (generation<=0 || !request.RootElement.TryGetProperty("Generation",out var owner) || owner.GetInt64()!=generation ||
            !request.RootElement.TryGetProperty("Direction",out var direction) || direction.GetString()!="FromAndroid")
            throw new InvalidDataException("El presupuesto FILES no pertenece a esta sesión/dirección.");
        // Observe is the existing Android enum value 1: no adaptive shaping is advertised by Linux.
        return new(revision,generation,$"linux-{transport}-{generation}",0,0,65536,1500,1);
    }
    public static bool AdvertisesMobileData(string json)
    {
        using var request=JsonDocument.Parse(json);
        return request.RootElement.TryGetProperty("MobileData",out var mobile) && mobile.ValueKind==JsonValueKind.True;
    }

    public static void ValidateCapabilities(string json)
    {
        using var request=JsonDocument.Parse(json);
        if (!request.RootElement.TryGetProperty("Version",out var version) || version.GetInt32()!=1)
            throw new InvalidDataException("Versión de capacidades no admitida.");
        if (request.RootElement.TryGetProperty("FilesWireVersions",out var versions) && versions.ValueKind!=JsonValueKind.Null &&
            (versions.ValueKind!=JsonValueKind.Array || versions.GetArrayLength()>2 || versions.EnumerateArray().Any(v=>v.GetInt32() is not (1 or 2))))
            throw new InvalidDataException("Versiones FILES inválidas.");
    }
}
