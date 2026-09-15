using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NOVORA.Service;

public sealed record NLServiceAndroidPackageInfo(string ApkPath, string PackageId, long VersionCode,
    string VersionName, int MinSdk, string Sha256);

/// <summary>Validates the canonical release pair. APK signature enforcement remains Android's responsibility.</summary>
public static class NLServiceAndroidPackage
{
    public const string ApkFileName = "NLAndroidApp.apk";
    public const string ReleaseFileName = "NLAndroidRelease.json";
    public const string ExpectedPackageId = "com.novora.appcontrol";
    private const long MaxApk = 256L * 1024 * 1024;
    private const int MaxManifest = 1024 * 1024;

    public static NLServiceAndroidPackageInfo Load(string directory)
    {
        var root = Path.GetFullPath(directory);
        var apk = Path.Combine(root, ApkFileName);
        var release = Path.Combine(root, ReleaseFileName);
        RejectLink(root);
        RejectLink(apk);
        RejectLink(release);
        using var jsonFile = new FileStream(release, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (jsonFile.Length is <= 0 or > 16384) throw Invalid("El descriptor de Android excede el límite permitido.");
        using var json = JsonDocument.Parse(jsonFile, new JsonDocumentOptions { MaxDepth = 4 });
        var obj = json.RootElement;
        if (obj.ValueKind != JsonValueKind.Object) throw Invalid("El descriptor de Android debe ser un objeto.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        string[] expected = ["SchemaVersion", "PackageId", "VersionCode", "VersionName", "MinSdk", "ControlProtocol", "PcSeries", "Sha256"];
        foreach (var property in obj.EnumerateObject())
            if (!fields.Add(property.Name) || !expected.Contains(property.Name)) throw Invalid("Campo desconocido o duplicado en el descriptor Android.");
        if (fields.Count != expected.Length) throw Invalid("El descriptor Android está incompleto.");
        long Number(string name) => obj.GetProperty(name).ValueKind == JsonValueKind.Number && obj.GetProperty(name).TryGetInt64(out var value) ? value : throw Invalid($"{name} debe ser entero.");
        string Text(string name) => obj.GetProperty(name).ValueKind == JsonValueKind.String
            ? obj.GetProperty(name).GetString()! : throw Invalid($"{name} debe ser texto.");
        var package = Text("PackageId"); var version = Number("VersionCode");
        var name = Text("VersionName"); var sdk = Number("MinSdk"); var hash = Text("Sha256");
        if (Number("SchemaVersion") != 1 || Number("ControlProtocol") != 1 || Text("PcSeries") != "1.4"
            || package != ExpectedPackageId || version <= 0 || version > int.MaxValue
            || sdk is < 26 or > 10000 || !ValidName(name) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw Invalid("El descriptor no corresponde a una versión compatible de NOVORA Android 1.4.");
        using var file = new FileStream(apk, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > MaxApk) throw Invalid("El APK excede el límite permitido de 256 MiB.");
        var actualHash = Convert.ToHexString(SHA256.HashData(file));
        if (!actualHash.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw Invalid("La huella SHA256 del APK no coincide con su descriptor.");
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 65536) throw Invalid("El APK contiene demasiadas entradas.");
        var manifests = zip.Entries.Where(e => e.FullName.Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (manifests.Length != 1 || manifests[0].FullName != "AndroidManifest.xml") throw Invalid("El APK no contiene un manifiesto único y canónico.");
        var entry = manifests[0];
        if (entry.Length is <= 0 or > MaxManifest) throw Invalid("El manifiesto APK excede el límite permitido.");
        var bytes = new byte[(int)entry.Length];
        using (var input = entry.Open()) { input.ReadExactly(bytes); if (input.ReadByte() != -1) throw Invalid("Manifiesto APK inconsistente."); }
        var manifest = new NLServiceAndroidBinaryManifest(bytes).Read();
        if (manifest.Package != package || manifest.Version != version || manifest.Name != name || manifest.Sdk != sdk)
            throw Invalid("La identidad, versión o Android mínimo del APK no coincide con su descriptor.");
        return new(apk, package, version, name, (int)sdk, actualHash);
    }

    private static bool ValidName(string value) => value.Length is > 0 and <= 128 && !value.Any(char.IsControl);
    private static void RejectLink(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Invalid("La entrega Android no admite enlaces de archivos o carpetas.");
    }
    private static InvalidDataException Invalid(string message) => new(message);

    // Bounded reader for AOSP ResXMLTree/ResStringPool structures (ResourceTypes.h).
    // Resource references in release identity fields are deliberately unsupported.
    private sealed class NLServiceAndroidBinaryManifest(byte[] bytes)
    {
        private string[]? _strings;
        private uint[]? _resources;
        private const uint None = uint.MaxValue;
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private int U16(int p) { Bounds(p, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p, 2)); }
        private uint U32(int p) { Bounds(p, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p, 4)); }
        private int Int(int p) { var n = U32(p); return n <= int.MaxValue ? (int)n : throw Invalid("Desplazamiento APK inválido."); }
        private void Bounds(int p, int n) { if (p < 0 || n < 0 || p > bytes.Length - n) throw Invalid("Manifiesto binario truncado."); }
        private string String(uint index) => _strings != null && index < _strings.Length ? _strings[index] : throw Invalid("Índice de cadena APK inválido.");
        public (string Package, long Version, string Name, int Sdk) Read()
        {
            if (U16(0) != 3 || U16(2) != 8 || Int(4) != bytes.Length) throw Invalid("Se requiere AndroidManifest.xml binario compilado.");
            var stack = new Stack<(uint Ns, uint Name)>();
            string? package = null, versionName = null; long version = -1; int sdk = -1; bool rootClosed = false, usesSdk = false;
            for (int p = 8; p < bytes.Length;)
            {
                int type = U16(p), header = U16(p + 2), size = Int(p + 4);
                if (header < 8 || size < header || p > bytes.Length - size) throw Invalid("Bloque binario APK inválido.");
                int end = p + size;
                if (type == 1)
                {
                    if (_strings != null || package != null) throw Invalid("Tabla de cadenas duplicada o fuera de orden.");
                    ReadStrings(p, header, size);
                }
                else if (type == 0x180)
                {
                    if (_strings == null || _resources != null || package != null || header != 8 || (size - 8) % 4 != 0 || (size - 8) / 4 > _strings.Length) throw Invalid("Mapa de recursos APK inválido.");
                    _resources = new uint[(size - 8) / 4];
                    for (int r = 0; r < _resources.Length; r++) _resources[r] = U32(p + 8 + r * 4);
                }
                else if (type is 0x100 or 0x101)
                {
                    if (_strings == null || header != 16 || size != 24) throw Invalid("Espacio de nombres APK inválido.");
                    String(U32(p + 20));
                }
                else if (type == 0x102)
                {
                    if (_strings == null || header != 16 || size < 36 || rootClosed || stack.Count >= 128) throw Invalid("Elemento APK inválido.");
                    uint ns = U32(p + 16), tagId = U32(p + 20); var tag = String(tagId);
                    if (ns != None) String(ns);
                    int start = U16(p + 24), attrSize = U16(p + 26), count = U16(p + 28);
                    if (start != 20 || attrSize != 20 || count > 4096 || p + 16L + start + count * 20L != end) throw Invalid("Atributos APK inválidos.");
                    bool root = stack.Count == 0;
                    if (root && (tag != "manifest" || ns != None || package != null)) throw Invalid("Raíz APK inválida.");
                    bool targetSdk = stack.Count == 1 && tag == "uses-sdk" && ns == None;
                    if (targetSdk && usesSdk) throw Invalid("uses-sdk duplicado.");
                    if (targetSdk) usesSdk = true;
                    var seen = new HashSet<(string Ns, string Name)>();
                    for (int a = p + 36; a < end; a += 20)
                    {
                        var attrNs = U32(a) == None ? "" : String(U32(a)); var attrName = String(U32(a + 4));
                        if (_resources != null)
                        {
                            uint index = U32(a + 4), resource = index < _resources.Length ? _resources[index] : 0;
                            uint expectedResource = attrNs == AndroidNs ? attrName switch { "versionCode" => 0x0101021bU, "versionName" => 0x0101021cU, "minSdkVersion" => 0x0101020cU, _ => 0U } : 0U;
                            if ((expectedResource != 0 && resource != expectedResource)
                                || (resource is 0x0101021b or 0x0101021c or 0x0101020c && resource != expectedResource))
                                throw Invalid("La identidad de recursos Android es ambigua.");
                        }
                        if (!seen.Add((attrNs, attrName)) || U16(a + 12) != 8 || bytes[a + 14] != 0) throw Invalid("Atributo APK duplicado o inválido.");
                        var raw = U32(a + 8); if (raw != None) String(raw);
                        int kind = bytes[a + 15]; uint data = U32(a + 16);
                        string Literal() => kind == 3 ? String(data) : throw Invalid("Las versiones APK deben usar valores literales; no referencias de recursos.");
                        int Integer() => kind is 0x10 or 0x11 && data <= int.MaxValue ? (int)data : throw Invalid("La versión y Android mínimo deben ser enteros literales.");
                        if (root && attrNs == "" && attrName == "package") package = Literal();
                        if (root && attrNs == AndroidNs && attrName == "versionName") versionName = Literal();
                        if (root && attrNs == AndroidNs && attrName == "versionCode") version = Integer();
                        if (root && attrNs == AndroidNs && attrName == "versionCodeMajor") throw Invalid("versionCodeMajor no es compatible con esta entrega.");
                        if (targetSdk && attrNs == AndroidNs && attrName == "minSdkVersion") sdk = Integer();
                    }
                    stack.Push((ns, tagId));
                }
                else if (type == 0x103)
                {
                    if (header != 16 || size != 24 || stack.Count == 0 || stack.Pop() != (U32(p + 16), U32(p + 20))) throw Invalid("Cierre de elemento APK inválido.");
                    if (stack.Count == 0) rootClosed = true;
                }
                else throw Invalid("El manifiesto contiene un bloque binario no compatible.");
                p = end;
            }
            if (!rootClosed || stack.Count != 0 || package == null || versionName == null || version <= 0 || sdk < 26 || !ValidName(versionName)) throw Invalid("Identidad APK incompleta.");
            return (package, version, versionName, sdk);
        }

        private void ReadStrings(int p, int header, int size)
        {
            if (header != 28 || size < 28) throw Invalid("Tabla de cadenas APK inválida.");
            int count = Int(p + 8), styles = Int(p + 12), flags = Int(p + 16), start = Int(p + 20), styleStart = Int(p + 24);
            if (count > 65536 || styles != 0 || styleStart != 0 || (flags & ~0x101) != 0 || start < 28L + count * 4L || start > size) throw Invalid("Tabla de cadenas APK no compatible.");
            _strings = new string[count]; bool utf8 = (flags & 0x100) != 0;
            var utf8Decoder = new UTF8Encoding(false, true); var utf16Decoder = new UnicodeEncoding(false, false, true);
            int totalCharacters = 0;
            for (int i = 0; i < count; i++)
            {
                int offset = Int(p + 28 + i * 4);
                if (offset >= size - start) throw Invalid("Cadena APK fuera del bloque.");
                int q = p + start + offset, end = p + size;
                int Length8() { if (q >= end) throw Invalid("Cadena APK truncada."); int n = bytes[q++]; if ((n & 0x80) == 0) return n; if (q >= end) throw Invalid("Cadena APK truncada."); return ((n & 0x7f) << 8) | bytes[q++]; }
                int Length16() { if (q > end - 2) throw Invalid("Cadena APK truncada."); int n = U16(q); q += 2; if ((n & 0x8000) == 0) return n; if (q > end - 2) throw Invalid("Cadena APK truncada."); long value = ((long)(n & 0x7fff) << 16) | (uint)U16(q); q += 2; return value <= int.MaxValue ? (int)value : throw Invalid("Cadena APK demasiado larga."); }
                if (utf8)
                {
                    int chars = Length8(), length = Length8();
                    if (chars > MaxManifest - totalCharacters) throw Invalid("La tabla de cadenas APK excede el límite total.");
                    if (length > end - q - 1 || bytes[q + length] != 0) throw Invalid("Cadena UTF8 APK truncada.");
                    _strings[i] = utf8Decoder.GetString(bytes, q, length);
                    if (_strings[i].Length != chars) throw Invalid("Longitud UTF8 APK inconsistente.");
                }
                else
                {
                    int length = Length16();
                    if (length > MaxManifest - totalCharacters) throw Invalid("La tabla de cadenas APK excede el límite total.");
                    if (length > (end - q - 2) / 2 || U16(q + length * 2) != 0) throw Invalid("Cadena UTF16 APK truncada.");
                    _strings[i] = utf16Decoder.GetString(bytes, q, length * 2);
                }
                totalCharacters += _strings[i].Length;
            }
        }
    }
}
