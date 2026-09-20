using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NOVORA.Service;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestAndroidPackage
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsActualBinaryManifestIdentity(bool utf8)
    {
        using var fixture = new NLTestPackageFixture(Manifest(utf8));
        var info = NLServiceAndroidPackage.Load(fixture.Directory);
        Assert.Equal("com.novora.appcontrol", info.PackageId);
        Assert.Equal(6, info.VersionCode);
        Assert.Equal("1.4.5", info.VersionName);
        Assert.Equal(26, info.MinSdk);
    }

    [Fact]
    public void RejectsTamperedApk()
    {
        using var fixture = new NLTestPackageFixture(Manifest());
        File.AppendAllText(fixture.Apk, "tampered");
        Assert.Throws<InvalidDataException>(() => NLServiceAndroidPackage.Load(fixture.Directory));
    }

    [Theory]
    [InlineData("VersionCode", "7")]
    [InlineData("VersionName", "\"1.4.6\"")]
    [InlineData("MinSdk", "27")]
    [InlineData("PackageId", "\"com.other.app\"")]
    [InlineData("ControlProtocol", "2")]
    [InlineData("PcSeries", "\"1.5\"")]
    [InlineData("SchemaVersion", "2")]
    public void RejectsMismatchedDescriptor(string field, string value)
    {
        using var fixture = new NLTestPackageFixture(Manifest());
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(fixture.Release))!;
        dict[field] = JsonDocument.Parse(value).RootElement.Clone();
        File.WriteAllText(fixture.Release, JsonSerializer.Serialize(dict));
        Assert.Throws<InvalidDataException>(() => NLServiceAndroidPackage.Load(fixture.Directory));
    }

    [Fact]
    public void RejectsDuplicateJsonProperty()
    {
        using var fixture = new NLTestPackageFixture(Manifest());
        var json = File.ReadAllText(fixture.Release);
        File.WriteAllText(fixture.Release, json[..^1] + ",\"VersionCode\":6}");
        Assert.Throws<InvalidDataException>(() => NLServiceAndroidPackage.Load(fixture.Directory));
    }

    [Fact]
    public void RejectsDuplicateZipManifest()
    {
        using var fixture = new NLTestPackageFixture(Manifest(), duplicate: true);
        Assert.Throws<InvalidDataException>(() => NLServiceAndroidPackage.Load(fixture.Directory));
    }

    [Fact]
    public void AcceptsCanonicalAndroidResourceMap()
    {
        using var fixture = new NLTestPackageFixture(Manifest(fault: "map"));
        Assert.Equal(6, NLServiceAndroidPackage.Load(fixture.Directory).VersionCode);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reference")]
    [InlineData("truncated")]
    [InlineData("chunk")]
    [InlineData("text")]
    [InlineData("oversize")]
    [InlineData("badmap")]
    public void RejectsMalformedOrUnsupportedManifest(string fault)
    {
        var manifest = fault == "text" ? Encoding.UTF8.GetBytes("<manifest package='com.novora.appcontrol'/>")
            : fault == "oversize" ? new byte[1024 * 1024 + 1] : Manifest(fault: fault);
        using var fixture = new NLTestPackageFixture(manifest);
        Assert.Throws<InvalidDataException>(() => NLServiceAndroidPackage.Load(fixture.Directory));
    }

    private static byte[] Manifest(bool utf8 = true, string fault = "")
    {
        string[] strings = ["manifest", "package", "com.novora.appcontrol", "http://schemas.android.com/apk/res/android", "versionCode", "versionName", "1.4.5", "uses-sdk", "minSdkVersion"];
        using var data = new MemoryStream(); using var dataWriter = new BinaryWriter(data);
        var offsets = new List<uint>();
        foreach (var text in strings)
        {
            offsets.Add((uint)data.Position);
            if (utf8) { var b = Encoding.UTF8.GetBytes(text); dataWriter.Write((byte)text.Length); dataWriter.Write((byte)b.Length); dataWriter.Write(b); dataWriter.Write((byte)0); }
            else { dataWriter.Write((ushort)text.Length); dataWriter.Write(Encoding.Unicode.GetBytes(text)); dataWriter.Write((ushort)0); }
        }
        while (data.Length % 4 != 0) dataWriter.Write((byte)0);
        using var body = new MemoryStream(); using var writer = new BinaryWriter(body);
        void Header(ushort type, ushort header, int size) { writer.Write(type); writer.Write(header); writer.Write(size); }
        int poolStart = 28 + strings.Length * 4;
        Header(1, 28, poolStart + (int)data.Length); writer.Write(strings.Length); writer.Write(0); writer.Write(utf8 ? 0x100 : 0); writer.Write(poolStart); writer.Write(0);
        foreach (var offset in offsets) writer.Write(offset); writer.Write(data.ToArray());
        if (fault is "map" or "badmap")
        {
            Header(0x180, 8, 8 + strings.Length * 4);
            uint[] resourceIds = [0, 0, 0, 0, fault == "badmap" ? 0x0101021cU : 0x0101021bU, 0x0101021c, 0, 0, 0x0101020c];
            foreach (var id in resourceIds) writer.Write(id);
        }
        void Start(uint tag, (uint Ns, uint Name, byte Kind, uint Value)[] attributes)
        {
            Header(0x102, 16, 36 + attributes.Length * 20); writer.Write(1); writer.Write(uint.MaxValue); writer.Write(uint.MaxValue); writer.Write(tag);
            writer.Write((ushort)20); writer.Write((ushort)20); writer.Write((ushort)attributes.Length); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            foreach (var a in attributes) { writer.Write(a.Ns); writer.Write(a.Name); writer.Write(uint.MaxValue); writer.Write((ushort)8); writer.Write((byte)0); writer.Write(a.Kind); writer.Write(a.Value); }
        }
        void End(uint tag) { Header(0x103, 16, 24); writer.Write(1); writer.Write(uint.MaxValue); writer.Write(uint.MaxValue); writer.Write(tag); }
        var root = new List<(uint, uint, byte, uint)> { (uint.MaxValue, 1, 3, 2), (3, 4, 0x10, 6), (3, 5, fault == "reference" ? (byte)1 : (byte)3, 6) };
        if (fault == "duplicate") root.Add((3, 4, 0x10, 7));
        Start(0, root.ToArray()); Start(7, [(3, 8, 0x10, 26)]); End(7); End(0);
        using var result = new MemoryStream(); using var output = new BinaryWriter(result);
        output.Write((ushort)3); output.Write((ushort)8); output.Write(8 + (int)body.Length); output.Write(body.ToArray());
        var bytes = result.ToArray();
        if (fault == "truncated") return bytes[..^1];
        if (fault == "chunk") { bytes[12] = 0xff; bytes[13] = 0xff; bytes[14] = 0xff; bytes[15] = 0x7f; }
        return bytes;
    }

    private sealed class NLTestPackageFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "novora-package-test-" + Guid.NewGuid().ToString("N"));
        public string Apk => Path.Combine(Directory, NLServiceAndroidPackage.ApkFileName);
        public string Release => Path.Combine(Directory, NLServiceAndroidPackage.ReleaseFileName);
        public NLTestPackageFixture(byte[] manifest, bool duplicate = false)
        {
            System.IO.Directory.CreateDirectory(Directory);
            using (var zip = ZipFile.Open(Apk, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry("AndroidManifest.xml").Open()) stream.Write(manifest);
                if (duplicate) { using var stream = zip.CreateEntry("AndroidManifest.xml").Open(); stream.Write(manifest); }
            }
            File.WriteAllText(Release, JsonSerializer.Serialize(new { SchemaVersion = 1, PackageId = "com.novora.appcontrol", VersionCode = 6,
                VersionName = "1.4.5", MinSdk = 26, ControlProtocol = 1, PcSeries = "1.4", Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Apk))) }));
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
