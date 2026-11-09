using NOVORA.Control;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}
var phones = new[] { new NLControlUsbCandidate("A", true, false), new NLControlUsbCandidate("B", true, false) };
Check(NLControlUsbAutoPolicy.SelectPhysicalSerial(phones, null, null) == "", "Two phones require selection");
Check(NLControlUsbAutoPolicy.SelectPhysicalSerial(phones, null, "B") == "B", "Explicit selection wins");
Check(NLControlUsbAutoPolicy.SelectPhysicalSerial(phones[..1], null, null) == "A", "Single phone is automatic");
Check(NLControlUsbAutoPolicy.SelectPhysicalSerial(new[] { new NLControlUsbCandidate("wifi:5555", true, true) }, null, null) == "", "Wireless ADB is not USB");
var clock = new ManualClock();
var recovery = new NLControlUsbRecovery(clock);
recovery.Observe("A|1");
Check(recovery.TryBegin(), "First bootstrap starts immediately");
clock.Advance(19);
Check(!recovery.ShouldRetry(false, false), "Pending authorization is bounded at twenty seconds");
clock.Advance(1);
Check(recovery.ShouldRetry(false, false), "Timed out bootstrap can recover");
Check(recovery.TryBegin(), "Second attempt starts");
clock.Advance(20);
Check(recovery.ShouldRetry(false, false) && recovery.TryBegin(), "Third attempt starts");
clock.Advance(20);
Check(!recovery.ShouldRetry(false, false) && !recovery.TryBegin(), "Only three bootstraps per connection");
Check(recovery.Exhausted && recovery.AuthorizationExpired(false), "Third authorization timeout still tears down the listener");
recovery.Observe("A|2");
Check(recovery.TryBegin(), "New physical connection resets attempts");
Check(!recovery.ShouldRetry(true, false), "Authorized session is left alone");
Check(!recovery.ShouldRetry(false, true), "Closed session waits two seconds");
clock.Advance(2);
Check(recovery.ShouldRetry(false, true), "Closed session recovers after delay");
recovery.Pause();
Check(!recovery.TryBegin() && !recovery.ShouldRetry(false, true), "Explicit stop pauses recovery");
recovery.Reset();
Check(recovery.TryBegin(), "Manual retry resumes");
Check(NLControlLanAutoPolicy.ChooseAddress(new[] { "192.168.2.5", "10.0.0.2" }, "192.168.2.5") == "192.168.2.5", "Keep available listener address");
Check(NLControlLanAutoPolicy.ChooseAddress(new[] { "10.0.0.2", "192.168.2.5" }, "10.9.9.9") == "10.0.0.2", "Follow ranked Windows network candidates");
Check(NLControlLanAutoPolicy.ChooseAddress(Array.Empty<string>(), null) is null, "No network is not a connection");
Check(new[] { 0, 1, 2, 3, 4, 5, 100 }.Select(NLControlLanAutoPolicy.RetryDelay)
    .SequenceEqual(new[] { 0, 1, 2, 4, 8, 15, 15 }.Select(seconds => TimeSpan.FromSeconds(seconds))), "LAN retries have a capped backoff");
var window = new NOVORA.NLUIWindowMain();
var oldServer = new NLControlTrustServer();
window._androidLanControl = oldServer;
Task<NLControlReply> oldRequest = window.Send("LAN", oldServer);
window._androidLanControl = new NLControlTrustServer();
window.Dispatcher.Drain();
Check(!(await oldRequest).Success && window.Applied == 0, "Queued old LAN request cannot mutate its replacement");
Task<NLControlReply> newRequest = window.Send("LAN", window._androidLanControl);
window.Dispatcher.Drain();
Check((await newRequest).Success && window.Applied == 1, "Current LAN request applies");
window._androidControl = new NLControlTrustServer();
Task<NLControlReply> usbRequest = window.Send("USB", window._androidControl);
window._androidLanControl = new NLControlTrustServer();
window.Dispatcher.Drain();
Check((await usbRequest).Success && window.Applied == 2, "LAN replacement preserves queued USB command");
Task<NLControlReply> revoked = window.Send("USB", window._androidControl);
window._androidControlGeneration++;
window.Dispatcher.Drain();
Check(!(await revoked).Success && window.Applied == 2, "Revoked generation rejects queued command");
Console.WriteLine($"PASS {checks} Windows connection checks");

sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(int seconds) => _now += TimeSpan.FromSeconds(seconds);
}
