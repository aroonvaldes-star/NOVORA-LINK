using System.Reflection;
using System.Xml.Linq;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/NOVORA/UI/NLUIWindowMain.xaml")))
    root = root.Parent;
Check(root is not null, "Cannot locate source checkout.");
var xaml = XDocument.Load(Path.Combine(root!.FullName, "src/NOVORA/UI/NLUIWindowMain.xaml"));
XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
var panel = xaml.Descendants().SingleOrDefault(e => (string?)e.Attribute(names + "Name") == "VisionTelemetryExpander14");
Check(panel is not null && (string?)panel.Attribute("IsExpanded") == "False",
    "FAIL: visual telemetry is not opt-in and collapsed by default.");

Type? policy = Assembly.GetExecutingAssembly().GetType("NOVORA.VisionEngine.Metrics.VEOnDemandTelemetry`1");
Check(policy is not null, "FAIL: no on-demand sampling policy is available.");
dynamic gate = Activator.CreateInstance(policy!.MakeGenericType(typeof(string)))!;
int samples = 0;
Func<string?> sample = () => "sample-" + ++samples;
for (int i = 0; i < 10_000; i++)
{
    Check(gate.GetSample((long)i, sample) is null, "Hidden telemetry must not expose a sample.");
    Check(!gate.TryRequestRefresh((long)i), "Hidden telemetry must not schedule a UI update.");
}
Check(samples == 0, "Hidden telemetry collected samples.");
gate.SetRequested(true);
Check(gate.TryRequestRefresh(10_000L), "Opening must request an immediate update.");
Check((string?)gate.GetSample(10_000L, sample) == "sample-1", "First sample missing.");
for (int i = 0; i < 1_000; i++)
{
    Check(!gate.TryRequestRefresh(10_999L), "Refresh burst was not bounded.");
    Check((string?)gate.GetSample(10_999L, sample) == "sample-1", "Snapshot calls did not reuse the sample.");
}
Check(samples == 1, "Telemetry was sampled more than once per second.");
Check(gate.TryRequestRefresh(11_000L), "One-second refresh missing.");
Check((string?)gate.GetSample(11_000L, sample) == "sample-2", "One-second sample missing.");
gate.SetRequested(false);
Check(gate.GetSample(12_000L, sample) is null, "Closing retained a visible sample.");
Check(!gate.TryRequestRefresh(12_000L) && samples == 2, "Closing did not stop work.");
gate.SetRequested(true);
Check((string?)gate.GetSample(12_000L, sample) == "sample-3", "Reopening must take a fresh sample.");
gate.Reset();
Check((string?)gate.GetSample(12_001L, sample) == "sample-4", "New stream reused old telemetry.");
Console.WriteLine("PASS opt-in, zero hidden work, one-second sampling, cache reuse, close/reopen and stream reset.");

Type? notifications = Assembly.GetExecutingAssembly().GetType("NOVORA.AndroidService.NLAndroidNotificationPolicy");
Check(notifications is not null, "FAIL: notification deduplication policy is missing.");
dynamic notificationGate = Activator.CreateInstance(notifications!)!;
int queued = 0;
Parallel.For(0, 10_000, _ => { if (notificationGate.TryQueue()) Interlocked.Increment(ref queued); });
Check(queued == 1, "Notification callbacks can accumulate without a bound.");
notificationGate.BeginDrain();
Check(notificationGate.TryQueue(), "Draining must allow the next update.");
notificationGate.BeginDrain();
Type stateType = Assembly.GetExecutingAssembly().GetType("NOVORA.AndroidService.NLAndroidNotificationState")!;
dynamic state = Activator.CreateInstance(stateType, "Conexión con PC", "PC personal · LAN", "Conectado por Wi-Fi · VisionEngine activo", true, true, 1L)!;
Check(notificationGate.NeedsPublish(state), "Initial notification is missing.");
notificationGate.MarkPublished(state);
for (int i = 0; i < 10_000; i++)
    Check(!notificationGate.NeedsPublish(state), "Identical notifications must not be republished.");
dynamic next = Activator.CreateInstance(stateType, "Conexión con PC", "PC personal · LAN", "Conectado por Wi-Fi · VisionEngine activo", true, true, 2L)!;
Check(notificationGate.NeedsPublish(next), "New connection generation must be visible.");
notificationGate.Reset();
Check(notificationGate.NeedsPublish(state), "New foreground lifecycle must publish.");
Console.WriteLine("PASS bounded callback queue, duplicate suppression, generation changes and lifecycle reset.");

var summary = notifications!.GetMethod("PublicSummary")!;
string lan = (string)summary.Invoke(null, ["LAN", true, false, true, false, false, false])!;
Check(lan.Contains("Wi-Fi") && lan.Contains("VisionEngine comparte la pantalla"), "Safe summary must identify the active operation.");
string usb = (string)summary.Invoke(null, ["USB", true, false, false, true, true, false])!;
Check(usb.Contains("USB") && usb.Contains("Grabación activa") && usb.Contains("LinkEngine activo"), "USB, recording and Internet states are missing.");
string lost = (string)summary.Invoke(null, ["LAN", false, true, true, true, false, false])!;
Check(lost.Contains("perdido") && !lost.Contains("activo"), "Lost link must not claim active motors.");
var service = File.ReadAllText(Path.Combine(root.FullName, "src/NOVORA.Android/AndroidService/NLAndroidServiceControl.cs"));
Check(service.Contains("_notificationPolicy.NeedsPublish(") && service.Contains(".SetContentText(presentation.PublicMessage)"),
    "FAIL: the actual Android notification builder still republishes unchanged or generic-only notifications.");
Console.WriteLine("PASS useful public summaries and actual Android service integration.");
