using NOVORA.Control;
using NOVORA.Service;

if (string.Equals(Path.GetFileName(Environment.ProcessPath), "adb.exe", StringComparison.OrdinalIgnoreCase))
{
    string[] adbArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
    File.AppendAllLines(Environment.GetEnvironmentVariable("NOVORA_DYNAMIC_ADB_LOG")!,
        [string.Join('|', adbArgs)]);
    if (adbArgs.Length > 0 && adbArgs[0] == "connect") Console.WriteLine("connected to " + adbArgs[1]);
    if (adbArgs.Length > 2 && adbArgs[2] == "get-state") Console.WriteLine("device");
    return;
}

string fingerprint = new('A', 64);
var trusted = new NLControlTrustedPc("PC", "10.0.0.27", 27215, fingerprint,
    Guid.NewGuid().ToString("N"), new string('B', 64));
var moved = new NLControlLanPeer("PC", "10.0.0.40", 27215, fingerprint);
Require(trusted.AtDiscoveredLanAddress([moved])?.Host == "10.0.0.40",
    "La PC confiable debe poder cambiar de IP.");
Require(trusted.AtDiscoveredLanAddress([moved with { Fingerprint = new string('C', 64) }]) is null,
    "La búsqueda no debe aceptar otra identidad TLS.");
Require(trusted.AtDiscoveredLanAddress([moved, moved with { Host = "10.0.0.41" }]) is null,
    "Dos anuncios para la misma identidad deben ser ambiguos.");
Require((trusted with { Transport = "USB" }).AtDiscoveredLanAddress([moved]) is null,
    "La dirección USB no debe resolverse por LAN.");
string discoveredServices = "List of discovered mdns services\n" +
    "adb-phone-x _adb-tls-pairing._tcp 10.0.0.28:34187\n" +
    "adb-phone-y _adb-tls-connect._tcp 10.0.0.28:39001\n";
Require(NLServiceADB.ParseWirelessDebuggingEndpoints(discoveredServices).SequenceEqual(["10.0.0.28:39001"]),
    "ADB debe usar el puerto de conexión y descartar el de vinculación.");

string root = Path.Combine(Path.GetTempPath(), "novora-dynamic-address-" + Guid.NewGuid().ToString("N"));
string tools = Path.Combine(root, "Tools");
Directory.CreateDirectory(tools);
string log = Path.Combine(root, "adb.log");
try
{
    foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
        File.Copy(source, Path.Combine(tools, Path.GetFileName(source)), true);
    File.Copy(Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe"),
        Path.Combine(tools, "adb.exe"));
    File.WriteAllText(Path.Combine(tools, "AdbWinApi.dll"), "");
    File.WriteAllText(Path.Combine(tools, "AdbWinUsbApi.dll"), "");
    Environment.SetEnvironmentVariable("NOVORA_DYNAMIC_ADB_LOG", log);

    var adb = new NLServiceADB(new NLServiceNovoraPaths(root));
    string first = await adb.ConnectWirelessDebuggingAsync("10.0.0.28:34187");
    string changed = await adb.ConnectWirelessDebuggingAsync("10.0.0.29:39001");
    Require(first == "10.0.0.28:34187" && changed == "10.0.0.29:39001",
        "ADB debe conectar con el IP y puerto vigentes.");
    string[] calls = File.ReadAllLines(log);
    Require(calls.Contains("connect|10.0.0.28:34187") &&
            calls.Contains("connect|10.0.0.29:39001") &&
            calls.Contains("-s|10.0.0.29:39001|get-state"),
        "ADB debe verificar el dispositivo en el nuevo destino.");
    try
    {
        await adb.ConnectWirelessDebuggingAsync("10.0.0.29:0");
        throw new Exception("Se aceptó un puerto inválido.");
    }
    catch (ArgumentException) { }
    Console.WriteLine("PASS: identidad LAN y destino ADB variable");
}
finally
{
    Environment.SetEnvironmentVariable("NOVORA_DYNAMIC_ADB_LOG", null);
    Directory.Delete(root, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
