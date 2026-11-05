using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NOVORA.Linux.Control;

internal sealed record TrustedDevice(string DeviceId, string Name);

internal sealed class LinuxTrustStore : IDisposable
{
    private sealed record Entry(string DeviceId, string Name, string Hash);
    private sealed record StoreData(int Version, byte[] Pfx, Entry[] Entries);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileStream _profileLock;
    private StoreData _data;
    private bool _disposed;

    public X509Certificate2 Certificate { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Certificate.RawData));
    public IReadOnlyList<TrustedDevice> Devices
    {
        get
        {
            lock (_gate)
                return _data.Entries.Select(entry => new TrustedDevice(entry.DeviceId, entry.Name)).ToArray();
        }
    }

    public LinuxTrustStore(string directory)
    {
        Directory.CreateDirectory(directory);
        SetPrivateDirectoryPermissions(directory);
        _path = Path.Combine(directory, "control-trust.json");
        _profileLock = new FileStream(_path + ".lock",
            CreatePrivateFileOptions(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));

        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 131072)
                    throw new InvalidDataException("El almacén de confianza excede el tamaño permitido.");
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                byte[] saved = File.ReadAllBytes(_path);
                try
                {
                    _data = JsonSerializer.Deserialize<StoreData>(saved)
                        ?? throw new InvalidDataException("El almacén de confianza está vacío.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(saved);
                }
                Validate(_data);
            }
            else
            {
                using RSA key = RSA.Create(2048);
                var request = new CertificateRequest("CN=NOVORA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using X509Certificate2 generated = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
                _data = new StoreData(1, generated.Export(X509ContentType.Pfx), []);
                Save(_data);
            }

            Certificate = new X509Certificate2(_data.Pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
            if (!Certificate.HasPrivateKey || Certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
                Certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
            {
                Certificate.Dispose();
                throw new InvalidDataException("La identidad LAN venció o no es válida.");
            }
        }
        catch
        {
            _profileLock.Dispose();
            throw;
        }
    }

    public (string Id, string Token) Enroll(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl))
            throw new InvalidDataException("Nombre de Android no válido.");
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_data.Entries.Length >= 256)
                throw new InvalidDataException("El límite de 256 dispositivos de confianza fue alcanzado.");
            string id = Guid.NewGuid().ToString("N");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Commit(_data with { Entries = [.. _data.Entries, new Entry(id, name.Trim(), Hash(token))] });
            return (id, token);
        }
    }

    public bool Authenticate(string? id, string? token)
    {
        if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit) || id is not { Length: 32 })
            return false;
        lock (_gate)
        {
            ThrowIfDisposed();
            Entry? entry = _data.Entries.FirstOrDefault(candidate => candidate.DeviceId == id);
            return entry is not null && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(entry.Hash),
                Convert.FromHexString(Hash(token)));
        }
    }

    public bool Contains(string id)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _data.Entries.Any(entry => entry.DeviceId == id);
        }
    }

    public void Revoke(string deviceId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            Commit(_data with { Entries = _data.Entries.Where(entry => entry.DeviceId != deviceId).ToArray() });
        }
    }

    private static void Validate(StoreData data)
    {
        if (data.Version != 1 || data.Pfx is null || data.Pfx.Length is < 1 or > 65536 ||
            data.Entries is null || data.Entries.Length > 256 ||
            data.Entries.Any(entry => entry is null ||
                !Guid.TryParseExact(entry.DeviceId, "N", out _) ||
                string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 64 ||
                entry.Hash is not { Length: 64 } || !entry.Hash.All(Uri.IsHexDigit)) ||
            data.Entries.Select(entry => entry.DeviceId).Distinct(StringComparer.Ordinal).Count() != data.Entries.Length)
            throw new InvalidDataException("El almacén de confianza está dañado. No se regeneró la identidad.");
    }

    private void Commit(StoreData next)
    {
        Save(next);
        _data = next;
    }

    private void Save(StoreData data)
    {
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(data);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary,
                CreatePrivateFileOptions(FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.WriteThrough)))
            {
                file.Write(clear);
                file.Flush(true);
            }
            File.Move(temporary, _path, true);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void SetPrivateDirectoryPermissions(string directory)
    {
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static FileStreamOptions CreatePrivateFileOptions(
        FileMode mode, FileAccess access, FileShare share, FileOptions options = FileOptions.None)
    {
        var result = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = share,
            Options = options
        };
        if (OperatingSystem.IsLinux())
            result.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return result;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LinuxTrustStore));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            Certificate.Dispose();
            CryptographicOperations.ZeroMemory(_data.Pfx);
        }
        _profileLock.Dispose();
    }
}
