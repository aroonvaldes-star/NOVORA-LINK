using NOVORA.Integration;
using NOVORA.ExInEngine;
using NOVORA.Service;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

string register = ExInHidProtocol.Register(7, "NOVORA Pad", 0x045e, 0x028e, [0x05, 0x01]);
using (JsonDocument doc = JsonDocument.Parse(register))
{
    JsonElement root = doc.RootElement;
    Require(root.GetProperty("id").GetInt32() == 7);
    Require(root.GetProperty("command").GetString() == "register");
    Require(root.GetProperty("vid").GetInt32() == 0x045e);
    Require(root.GetProperty("pid").GetInt32() == 0x028e);
    Require(root.GetProperty("bus").GetString() == "usb");
    Require(root.GetProperty("descriptor").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual([5, 1]));
}

string report = ExInHidProtocol.Report(7, [0, 255]);
// The hand-built line must stay byte-identical to the former serializer output.
Require(report == JsonSerializer.Serialize(new { id = (ushort)7, command = "report", report = new[] { 0, 255 } }));
using (JsonDocument doc = JsonDocument.Parse(report))
{
    JsonElement root = doc.RootElement;
    Require(root.GetProperty("command").GetString() == "report");
    Require(root.GetProperty("report").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual([0, 255]));
}

try { ExInHidProtocol.Report(7, new byte[1025]); throw new Exception("Expected size rejection."); }
catch (ArgumentOutOfRangeException) { }

var pointer = new ExInPointerTranslator();
_ = pointer.FromXboxVE(default);
pointer.TouchDownVE(0, 1, 0.2f, 0.2f, false);
pointer.TouchDownVE(0, 2, 0.4f, 0.4f, false);
try { pointer.TouchDownVE(0, 3, 0.6f, 0.6f, false); throw new Exception("Expected contact limit."); }
catch (InvalidOperationException) { }
pointer.TouchDownVE(0, 4, 0.2f, 0.2f, false);
pointer.TouchUpVE(0, 4, false);

int sampleCount;
await using (var engine = new ExInCoreEngine(new NLServiceNovoraPaths(AppContext.BaseDirectory)))
{
    Require(engine.Manager.PointerSensitivityVE is >= 0.1 and <= 0.75);
    MethodInfo recordSample = typeof(ExInManager).GetMethod("RecordSampleVE", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new Exception("Missing sample recorder.");
    FieldInfo sampleTicks = typeof(ExInManager).GetField("_sampleTicksVE", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new Exception("Missing sample window.");
    for (int i = 0; i < 1024; i++) recordSample.Invoke(engine.Manager, [Stopwatch.GetTimestamp()]);
    var samples = (System.Collections.ICollection?)sampleTicks.GetValue(engine.Manager);
    Require(samples is { Count: <= 512 });
    sampleCount = samples!.Count;
}

Console.WriteLine($"NOVORA ExIn HID and bounds: PASS (samples={sampleCount})");

static void Require(bool valid)
{
    if (!valid) throw new Exception("Unexpected HID command.");
}
