using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
namespace NOVORA.Control;
public sealed record NLControlTrustedDevice(string DeviceId, string Name);
/// <summary>Windows current-user DPAPI storage; corrupt identity never silently rotates.</summary>
public sealed class NLControlTrustStore : IDisposable
{
    public const string FileName = "NLControlTrust.dat";
    private sealed record NLControlTrustEntry(string DeviceId, string Name, string Hash);
    private sealed record NLControlTrustData(int Version, byte[] Pfx, NLControlTrustEntry[] Entries, string? LastHost, bool ListenEnabled);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileStream _profileLock;
    private NLControlTrustData _data;
    internal X509Certificate2 Certificate { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Certificate.RawData));
    public IReadOnlyList<NLControlTrustedDevice> Devices { get { lock (_gate) return _data.Entries.Select(e => new NLControlTrustedDevice(e.DeviceId, e.Name)).ToArray(); } }
    public string? LastHost { get { lock (_gate) return _data.LastHost; } }
    public bool ListenEnabled { get { lock (_gate) return _data.ListenEnabled; } }
    public NLControlTrustStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
        _profileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 131072) throw new InvalidDataException("Almacén de confianza demasiado grande.");
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
                try { _data = JsonSerializer.Deserialize<NLControlTrustData>(clear, new JsonSerializerOptions { MaxDepth = 8 }) ?? throw new InvalidDataException("Almacén vacío."); }
                finally { CryptographicOperations.ZeroMemory(clear); }
                if (_data.Version != 1 || _data.Pfx is null || _data.Entries is null || _data.Entries.Length > 16 ||
                    _data.Entries.Any(e => e is null || !Guid.TryParseExact(e.DeviceId, "N", out _) || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 64 || e.Hash is not { Length:64 } || !e.Hash.All(Uri.IsHexDigit)) ||
                    _data.Entries.Select(e => e.DeviceId).Distinct().Count() != _data.Entries.Length)
                    throw new InvalidDataException("Almacén de confianza dañado. Restaura su copia antes de vincular.");
            }
            else
            {
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=NOVORA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
                _data = new(1, generated.Export(X509ContentType.Pfx), [], null, false);
                Save(_data);
            }
            if (_data.LastHost is not null)
            {
                if (!System.Net.IPAddress.TryParse(_data.LastHost, out var savedAddress)) throw new InvalidDataException("Dirección guardada dañada.");
                NLControlLanInvitation.ValidateAddress(savedAddress, true);
            }
            else if (_data.ListenEnabled) throw new InvalidDataException("Falta la dirección del control LAN guardado.");
            Certificate = new X509Certificate2(_data.Pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
            if (!Certificate.HasPrivateKey || Certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || Certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
            { Certificate.Dispose(); throw new InvalidDataException("La identidad LAN venció o no es válida. Renueva la identidad y vuelve a vincular los teléfonos."); }
        }
        catch
        {
            _profileLock.Dispose();
            throw;
        }
    }
    public void SetListening(string host, bool enabled)
    {
        if (!System.Net.IPAddress.TryParse(host, out var ip)) throw new InvalidDataException("Dirección inválida.");
        NLControlLanInvitation.ValidateAddress(ip, true);
        lock (_gate) Commit(_data with { LastHost = host, ListenEnabled = enabled });
    }
    internal (string Id, string Token) Enroll(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl)) throw new InvalidDataException("Nombre de Android no válido.");
        lock (_gate)
        {
            if (_data.Entries.Length >= 16) throw new InvalidDataException("Límite de 16 dispositivos. Revoca uno antes de agregar otro.");
            string id = Guid.NewGuid().ToString("N"), token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Commit(_data with { Entries = [.._data.Entries, new(id, name.Trim(), Hash(token))] });
            return (id, token);
        }
    }
    internal bool Authenticate(string? id, string? token)
    {
        if (token is not { Length:64 } || !token.All(Uri.IsHexDigit) || id is not { Length:32 }) return false;
        lock (_gate)
        {
            var entry = _data.Entries.FirstOrDefault(e => e.DeviceId == id);
            return entry is not null && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.Hash), Convert.FromHexString(Hash(token)));
        }
    }
    internal bool Contains(string id) { lock (_gate) return _data.Entries.Any(e => e.DeviceId == id); }
    public void Revoke(string deviceId) { lock (_gate) Commit(_data with { Entries = _data.Entries.Where(e => e.DeviceId != deviceId).ToArray() }); }
    public void RevokeAll() { lock (_gate) Commit(_data with { Entries = [] }); }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private void Commit(NLControlTrustData next) { Save(next); _data = next; }
    private void Save(NLControlTrustData data)
    {
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(data);
        byte[] cipher;
        try { cipher = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(cipher); file.Flush(true); }
            if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose()
    {
        try { Certificate.Dispose(); CryptographicOperations.ZeroMemory(_data.Pfx); }
        finally { _profileLock.Dispose(); }
    }
}
