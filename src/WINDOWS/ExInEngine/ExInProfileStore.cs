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

    public ExInCalibrationProfile? LoadVE(string profileKey, IReadOnlyCollection<string>? keysInUse = null)
    {
        _ = keysInUse;
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
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool SaveVE(ExInCalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsValidVE(profile.ProfileKey)) return false;

        string path = GetPathVE(profile.ProfileKey);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(_directoryVE);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, JsonOptionsVE);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    public ExInPaddleMappings LoadPaddleMappingsVE(string profileKey)
    {
        string path = GetPaddleMappingsPathVE(profileKey);
        if (!File.Exists(path)) return ExInPaddleMappings.DefaultVE;

        try
        {
            ExInPaddleMappings? mappings = JsonSerializer.Deserialize<ExInPaddleMappings>(File.ReadAllText(path), JsonOptionsVE);
            return mappings?.IsValidVE() == true ? mappings : ExInPaddleMappings.DefaultVE;
        }
        catch (JsonException)
        {
            return ExInPaddleMappings.DefaultVE;
        }
        catch (IOException)
        {
            return ExInPaddleMappings.DefaultVE;
        }
        catch (UnauthorizedAccessException)
        {
            return ExInPaddleMappings.DefaultVE;
        }
    }

    public bool SavePaddleMappingsVE(string profileKey, ExInPaddleMappings mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (!mappings.IsValidVE()) return false;

        string path = GetPaddleMappingsPathVE(profileKey);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(_directoryVE);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, mappings, JsonOptionsVE);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    public void DeleteVE(string profileKey)
    {
        try { File.Delete(GetPathVE(profileKey)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void DeletePaddleMappingsVE(string profileKey)
    {
        try { File.Delete(GetPaddleMappingsPathVE(profileKey)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void DeleteAllVE(string profileKey)
    {
        DeleteVE(profileKey);
        DeletePaddleMappingsVE(profileKey);
    }

    private string GetPathVE(string profileKey)
    {
        if (string.IsNullOrWhiteSpace(profileKey) || profileKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Clave de perfil ExIn inválida.", nameof(profileKey));
        return Path.Combine(_directoryVE, profileKey + ".json");
    }

    private string GetPaddleMappingsPathVE(string profileKey)
    {
        _ = GetPathVE(profileKey);
        return Path.Combine(_directoryVE, profileKey + ".paddles.json");
    }
}
