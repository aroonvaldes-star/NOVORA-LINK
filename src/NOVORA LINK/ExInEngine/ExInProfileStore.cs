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
        string path = GetPathVE(profileKey);
        if (!File.Exists(path))
        {
            AdoptSameModelVE(profileKey, keysInUse);
            if (!File.Exists(path)) return null;
        }

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

    /// <summary>
    /// A controller without a serial is keyed by its SDL path or instance id, which can change
    /// after a restart or a rebuild. When the current key has no profile and exactly one saved
    /// profile of the same model exists that no connected controller is using, it belongs to
    /// this controller: move it (with its paddle mappings) to the current key, so that only
    /// the latest calibration of that model is ever kept.
    /// </summary>
    private void AdoptSameModelVE(string profileKey, IReadOnlyCollection<string>? keysInUse)
    {
        string? prefix = GetModelPrefixVE(profileKey);
        if (prefix is null) return;

        try
        {
            if (!Directory.Exists(_directoryVE)) return;

            string[] candidates = Directory.GetFiles(_directoryVE, prefix + "*.json")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.EndsWith(".paddles.json", StringComparison.OrdinalIgnoreCase))
                .Select(name => name[..^".json".Length])
                .Where(key => !string.Equals(key, profileKey, StringComparison.Ordinal))
                .ToArray();
            if (candidates.Length != 1) return;

            string previousKey = candidates[0];
            if (keysInUse?.Contains(previousKey) == true) return;

            string previousPath = GetPathVE(previousKey);
            ExInCalibrationProfile? previous = JsonSerializer.Deserialize<ExInCalibrationProfile>(
                File.ReadAllText(previousPath), JsonOptionsVE);
            if (previous?.IsValidVE(previousKey) != true) return;
            if (!SaveVE(previous with { ProfileKey = profileKey })) return;

            string previousPaddles = GetPaddleMappingsPathVE(previousKey);
            string currentPaddles = GetPaddleMappingsPathVE(profileKey);
            if (File.Exists(previousPaddles) && !File.Exists(currentPaddles))
                File.Move(previousPaddles, currentPaddles);
            File.Delete(previousPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        catch (ArgumentException) { }
    }

    // Profile keys are exin-{vid:x4}-{pid:x4}-{digest}; the prefix identifies the model.
    private static string? GetModelPrefixVE(string profileKey)
    {
        string[] parts = profileKey.Split('-');
        return parts.Length == 4 && parts[0] == "exin" && parts[1].Length == 4 && parts[2].Length == 4
            ? $"exin-{parts[1]}-{parts[2]}-"
            : null;
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
