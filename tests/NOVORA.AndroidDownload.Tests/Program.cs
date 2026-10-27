using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

static void Check(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); }

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/NOVORA LINK/UI/NLUIWindowMain.xaml"))) root = root.Parent;
Check(root is not null, "Checkout not found.");
string xaml = File.ReadAllText(Path.Combine(root!.FullName, "src/NOVORA LINK/UI/NLUIWindowMain.xaml"));
Check(!xaml.Contains("AndroidInstallButton") && !xaml.Contains("COMPROBAR APP"), "FAIL: launcher still exposes the Android installer.");
Check(XDocument.Parse(xaml).Descendants().Any(e => e.Name.LocalName == "Image" && (string?)e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "AndroidDownloadQrImage14"),
    "FAIL: public APK QR is missing.");
Type? type = Assembly.GetExecutingAssembly().GetType("NOVORA.Service.NLServiceAndroidDownload");
Check(type is not null, "Public APK download selector is missing.");
var select = type!.GetMethod("SelectDownload")!;
object Release(string tag, bool prerelease, bool draft, string url, string name = "LinkControl.apk", string state = "uploaded") =>
    new { tag_name = tag, prerelease, draft, published_at = prerelease ? "2026-10-19T12:00:00Z" : "2026-10-18T12:00:00Z", assets = new[] { new { name, state, size = 12345L, browser_download_url = url } } };
const string stable = "https://github.com/aroonvaldes-star/NOVORA-LINK/releases/download/v1.4.113/LinkControl.apk";
const string experimental = "https://github.com/aroonvaldes-star/NOVORA-LINK/releases/download/v1.4.114-pre.1/LinkControl.apk";
dynamic selected = select.Invoke(null, [JsonSerializer.Serialize(new[] { Release("v1.4.113", false, false, stable), Release("v1.4.114-pre.1", true, false, experimental) })])!;
Check(selected.DownloadUri.AbsoluteUri == experimental && selected.IsPrerelease, "A newer published prerelease must be selectable.");
foreach (string invalid in new[] { "http://github.com/aroonvaldes-star/NOVORA-LINK/releases/download/v1/LinkControl.apk", "https://github.com/aroonvaldes-star/NOVORA-AppControl/releases/download/v1/LinkControl.apk", "https://example.com/LinkControl.apk", stable + "?token=secret", stable + "#token" })
    Check(select.Invoke(null, [JsonSerializer.Serialize(new[] { Release("v1", true, false, invalid) })]) is null, "Unsafe or private QR destination accepted.");
Check(select.Invoke(null, [JsonSerializer.Serialize(new[] { Release("draft", true, true, experimental) })]) is null, "Draft download accepted.");
Check(select.Invoke(null, [JsonSerializer.Serialize(new[] { Release("old", true, false, stable, "old.apk") })]) is null, "Legacy APK was advertised as the current distribution asset.");
Check(select.Invoke(null, [JsonSerializer.Serialize(new[] { Release("uploading", true, false, stable, state: "new") })]) is null, "Incomplete asset accepted.");
Check(select.Invoke(null, ["[]"]) is null, "Missing publication must not produce a QR target.");
byte[] png = (byte[])type.GetMethod("GenerateQrCode")!.Invoke(null, [selected])!;
Check(png.Length > 100 && png.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }), "QR is not a PNG.");
string startup = File.ReadAllText(Path.Combine(root.FullName, "src/NOVORA LINK/UI/NLUIWindowMain.xaml.cs"));
Check(!startup.Contains("RefreshAndroidInstallationAsync") && !startup.Contains("InitializeAndroidInstallation"), "Startup still performs APK/ADB installation checks.");
Console.WriteLine("PASS public prereleases, private/unsafe URL rejection, drafts/incomplete assets, missing publication, real QR PNG and no startup installation checks.");
