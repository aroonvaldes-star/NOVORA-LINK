using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using NOVORA.Control;

namespace NOVORA.AndroidStorage;

/// <summary>Credentials are encrypted with a non-exportable Android Keystore key, outside backups.</summary>
public sealed class NLAndroidStorageTrustedPcs(Context context)
{
    private const string Alias = "novora.control.trust.v1";
    private readonly object _gate = new();
    private string FilePath => System.IO.Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "trusted-pcs.bin");
    private IKey Key(bool create)
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(Alias)) return store.GetKey(Alias, null)!;
        if (!create) throw new InvalidOperationException("No se puede recuperar la clave de las PC guardadas. Borra los datos de NOVORA y vuelve a enlazar.");
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        using var specification = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!.SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!.SetKeySize(256)!.Build()!;
        generator.Init(specification);
        return generator.GenerateKey()!;
    }
    public IReadOnlyList<NLControlTrustedPc> Read()
    {
        lock (_gate)
        {
            if (!System.IO.File.Exists(FilePath)) return [];
            if (new System.IO.FileInfo(FilePath).Length > 65536) throw new InvalidDataException("El almacén de PC guardadas está dañado. Borra los datos de NOVORA y vuelve a enlazar.");
            try
            {
                byte[] data = System.IO.File.ReadAllBytes(FilePath);
                if (data.Length < 29 || data[0] != 1) throw new InvalidDataException();
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                using var key = Key(false);
                using var parameters = new GCMParameterSpec(128, data[1..13]);
                cipher.Init(CipherMode.DecryptMode, key, parameters);
                byte[] plain = cipher.DoFinal(data[13..])!;
                try
                {
                    var peers = JsonSerializer.Deserialize<NLControlTrustedPc[]>(plain) ?? throw new InvalidDataException();
                    if (peers.Length > 16 || peers.Select(p => p.Fingerprint).Distinct().Count() != peers.Length) throw new InvalidDataException();
                    foreach (var peer in peers) peer.Validate();
                    return peers;
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { throw new InvalidDataException("No se pueden leer las PC guardadas. No se borraron ni reemplazaron. Borra los datos de NOVORA y vuelve a enlazar para recuperar el almacén.", ex); }
        }
    }
    public void Save(NLControlTrustedPc peer)
    {
        peer.Validate();
        lock (_gate)
        {
            var peers = Read().Where(p => p.Fingerprint != peer.Fingerprint).Append(peer).ToArray();
            if (peers.Length > 16) throw new InvalidOperationException("Máximo de 16 PC guardadas. Olvida una antes de agregar otra.");
            Write(peers);
        }
    }
    public void Forget(string fingerprint)
    { lock (_gate) Write(Read().Where(p => p.Fingerprint != fingerprint).ToArray()); }
    private void Write(NLControlTrustedPc[] peers)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(peers);
        try
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var key = Key(true);
            cipher.Init(CipherMode.EncryptMode, key);
            byte[] iv = cipher.GetIV()!;
            if (iv.Length != 12) throw new InvalidDataException("Vector de cifrado inesperado.");
            byte[] encrypted = cipher.DoFinal(plain)!;
            string temporary = FilePath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.WriteByte(1); stream.Write(iv); stream.Write(encrypted); stream.Flush(true); }
            System.IO.File.Move(temporary, FilePath, true);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); }
    }
}
