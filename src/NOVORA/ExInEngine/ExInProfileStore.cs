using System.Text.Json;

namespace NOVORA.ExInEngine;

public sealed class ExInProfileStore
{
    private static readonly JsonSerializerOptions JsonOptionsVE = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directoryVE;

    public ExInProfileStore(string directory)
        => _directoryVE = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));

    public ExInCalibrationProfile? LoadVE(string profileKey)
    {
        string path = GetPathVE(profileKey);
        if (!File.Exists(path)) return null;

        try
        {
            ExInCalibrationProfile? profile = JsonSerializer.Deserialize<ExInCalibrationProfile>(File.ReadAllText(path), JsonOptionsVE);
            return profile?.IsValidVE(profileKey) == true ? profile : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public bool SaveVE(ExInCalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsValidVE(profile.ProfileKey)) return false;

        Directory.CreateDirectory(_directoryVE);
        string path = GetPathVE(profile.ProfileKey);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, JsonOptionsVE);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    public void DeleteVE(string profileKey)
    {
        try { File.Delete(GetPathVE(profileKey)); } catch (IOException) { }
    }

    private string GetPathVE(string profileKey)
    {
        if (string.IsNullOrWhiteSpace(profileKey) || profileKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Clave de perfil ExIn inválida.", nameof(profileKey));
        return Path.Combine(_directoryVE, profileKey + ".json");
    }
}
