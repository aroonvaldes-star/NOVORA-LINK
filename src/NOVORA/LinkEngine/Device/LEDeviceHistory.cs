using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NOVORA.LinkEngine.Device;

/// <summary>
/// Cache persistente de perfiles técnicos por modelo/build.
///
/// PRIVACIDAD:
///
/// No almacena serial ADB ni una lista de dispositivos conocidos.
/// </summary>
internal sealed class LEDeviceHistory
{
    private const int MaxModelProfilesLE =
        128;

    private static readonly JsonSerializerOptions JsonOptionsLE =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private readonly SemaphoreSlim _gate =
        new(
            1,
            1);

    private readonly string _path;

    internal LEDeviceHistory(
        string? path = null)
    {
        _path =
            string.IsNullOrWhiteSpace(
                path)
                ? GetDefaultPathLE()
                : path;
    }

    internal static string CreateModelKeyLE(
        string manufacturer,
        string model,
        string buildFingerprint,
        int androidSdk)
    {
        /*
         * NO incluimos serial/device id.
         *
         * El hash sólo evita usar una cadena enorme como key.
         */

        string normalized =
            string.Join(
                "|",
                NormalizeLE(
                    manufacturer),
                NormalizeLE(
                    model),
                NormalizeLE(
                    buildFingerprint),
                androidSdk.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                LEDeviceProfileModel.CurrentSchemaVersionLE.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

        byte[] bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalized));

        return Convert.ToHexString(
            bytes);
    }

    internal async Task<LEDeviceProfileModel?> GetAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            modelKey);

        await _gate.WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            LEDeviceStoreHistory store =
                await ReadStoreInternalAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            LEDeviceProfileModel? found =
                store.Models.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.ModelKey,
                            modelKey,
                            StringComparison.Ordinal));

            if (found is null)
            {
                return null;
            }

            if (found.SchemaVersion !=
                LEDeviceProfileModel.CurrentSchemaVersionLE)
            {
                return null;
            }

            /*
             * Actualizamos únicamente información agregada.
             * No guardamos quién usó el perfil.
             */

            LEDeviceProfileModel updated =
                found with
                {
                    LastUsedUtc =
                        DateTimeOffset.UtcNow,

                    HitCount =
                        found.HitCount + 1
                };

            int index =
                store.Models.FindIndex(
                    item =>
                        string.Equals(
                            item.ModelKey,
                            modelKey,
                            StringComparison.Ordinal));

            if (index >= 0)
            {
                store.Models[index] =
                    updated;

                await WriteStoreInternalAsync(
                        store,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SaveAsync(
        LEDeviceProfileModel profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        await _gate.WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            LEDeviceStoreHistory store =
                await ReadStoreInternalAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            int index =
                store.Models.FindIndex(
                    item =>
                        string.Equals(
                            item.ModelKey,
                            profile.ModelKey,
                            StringComparison.Ordinal));

            LEDeviceProfileModel normalized =
                profile with
                {
                    SchemaVersion =
                        LEDeviceProfileModel.CurrentSchemaVersionLE,

                    LastUsedUtc =
                        DateTimeOffset.UtcNow
                };

            if (index >= 0)
            {
                store.Models[index] =
                    normalized;
            }
            else
            {
                store.Models.Add(
                    normalized);
            }

            /*
             * Evita que la cache crezca indefinidamente.
             *
             * Conservamos los perfiles usados más recientemente.
             */
            if (store.Models.Count >
                MaxModelProfilesLE)
            {
                store.Models =
                    store.Models
                        .OrderByDescending(
                            item =>
                                item.LastUsedUtc)
                        .Take(
                            MaxModelProfilesLE)
                        .ToList();
            }

            await WriteStoreInternalAsync(
                    store,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task InvalidateAsync(
        string modelKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            modelKey);

        await _gate.WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            LEDeviceStoreHistory store =
                await ReadStoreInternalAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            int removed =
                store.Models.RemoveAll(
                    item =>
                        string.Equals(
                            item.ModelKey,
                            modelKey,
                            StringComparison.Ordinal));

            if (removed > 0)
            {
                await WriteStoreInternalAsync(
                        store,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<int> CountAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            LEDeviceStoreHistory store =
                await ReadStoreInternalAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            return store.Models.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LEDeviceStoreHistory> ReadStoreInternalAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(
                _path))
            {
                return new LEDeviceStoreHistory();
            }

            await using FileStream stream =
                new(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 8192,
                    useAsync: true);

            LEDeviceStoreHistory? store =
                await JsonSerializer.DeserializeAsync<LEDeviceStoreHistory>(
                        stream,
                        JsonOptionsLE,
                        cancellationToken)
                    .ConfigureAwait(false);

            return store ??
                new LEDeviceStoreHistory();
        }
        catch (JsonException)
        {
            /*
             * Cache corrupta != LinkEngine roto.
             *
             * Empezamos una cache nueva.
             */
            return new LEDeviceStoreHistory();
        }
        catch (IOException)
        {
            return new LEDeviceStoreHistory();
        }
        catch (UnauthorizedAccessException)
        {
            return new LEDeviceStoreHistory();
        }
    }

    private async Task WriteStoreInternalAsync(
        LEDeviceStoreHistory store,
        CancellationToken cancellationToken)
    {
        string? directory =
            Path.GetDirectoryName(
                _path);

        if (!string.IsNullOrWhiteSpace(
            directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        string temporary =
            _path + ".tmp";

        await using (
            FileStream stream =
                new(
                    temporary,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 8192,
                    useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                    stream,
                    store,
                    JsonOptionsLE,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(
            temporary,
            _path,
            overwrite: true);
    }

    private static string NormalizeLE(
        string? value)
    {
        return (
            value ??
            string.Empty
        )
        .Trim()
        .ToUpperInvariant();
    }

    private static string GetDefaultPathLE()
    {
        string local =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            local,
            "NOVORA-LINK",
            "Cache",
            "model-capabilities-v1.json");
    }
}

internal sealed class LEDeviceStoreHistory
{
    public int SchemaVersion { get; init; } =
        LEDeviceProfileModel.CurrentSchemaVersionLE;

    public List<LEDeviceProfileModel> Models { get; set; } =
        [];
}
