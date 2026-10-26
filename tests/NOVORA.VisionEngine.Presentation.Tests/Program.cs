using NOVORA.VisionEngine.Renderer;

const long Frequency = 1_000_000;
var tests = new (string Name, Action Run)[]
{
    ("First picture is eligible immediately", FirstPicture),
    ("120 arrivals and 60 composition ticks yield 60 presentations", Arrival120Composition60),
    ("Healthy composition owns presentation instead of arrivals", HealthyComposition),
    ("Repeated composition timestamp does not present twice", DuplicateComposition),
    ("60 Hz jitter does not halve presentation rate", Jitter60),
    ("59.94 Hz keeps every intended composition tick", Jitter5994),
    ("Composition cadence tolerates callback jitter of plus/minus 1 ms", CallbackJitter),
    ("Composition cadence tolerates callback jitter of 20 percent", CallbackJitter20Percent),
    ("Composition phase is independent from the first immediate picture", InitialPicturePhase),
    ("A delayed composition callback cannot produce a near-immediate second picture", DelayedCallbackWithoutBurst),
    ("Faster composition cannot exceed selected monitor cadence", Composition120Target60),
    ("Missing composition falls back within three display periods", MissingComposition),
    ("Fallback respects presentation cadence", FallbackCadence),
    ("Long composition pause never emits a catch-up burst", PauseWithoutCatchup),
    ("Off-phase pause cannot replay the nearby expired cadence", PauseOffPhase),
    ("Disable restores immediate eligibility", DisableImmediate),
    ("Reconfigure discards old composition and presentation timestamps", ReconfigureClearsHistory),
    ("Reset preserves selected rate and starts with an immediate image", ResetKeepsConfiguration),
    ("Out-of-order monotonic timestamps cannot advance the presentation", OlderTimestamp),
    ("Invalid clocks and refresh values are rejected atomically", InvalidConfiguration),
    ("Stopwatch and WPF clocks have independent units", IndependentClockUnits)
};

int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"Presentation timing: {tests.Length - failures}/{tests.Length} passed; {failures} failed.");
return failures == 0 ? 0 : 1;

VERendererPresentationTiming Enabled(double hz = 60, double startMs = 0)
{
    var policy = new VERendererPresentationTiming(Frequency);
    policy.ConfigureVE(true, hz, Stamp(startMs));
    return policy;
}

long Stamp(double ms) => (long)Math.Round(ms * 1000, MidpointRounding.AwayFromZero);
long WpfTime(double ms) => (long)Math.Round(ms * 10_000, MidpointRounding.AwayFromZero);
void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void False(bool value, string message) => True(!value, message);
void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}
void Throws<T>(Action run, string message) where T : Exception
{
    try { run(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

void FirstPicture()
{
    // Break caught: holding the initial picture until a compositor heartbeat arrives.
    var policy = Enabled();
    True(policy.ShouldPresentOnArrivalVE(0), "Initial picture waited for composition");
    policy.RecordPresentationVE(0);
    False(policy.ShouldPresentOnArrivalVE(Stamp(8.333)), "A second image escaped the cadence");
}

void Arrival120Composition60()
{
    // Break caught: presenting each input frame or accidentally skipping alternate TV ticks.
    var policy = Enabled();
    int presented = 0;
    for (int arrival = 0; arrival < 120; arrival++)
    {
        long now = (long)Math.Round(arrival * (Frequency / 120d));
        if (policy.ShouldPresentOnArrivalVE(now)) { policy.RecordPresentationVE(now); presented++; }
        if (arrival % 2 == 0 && policy.ShouldPresentOnCompositionVE(
            (long)Math.Round((arrival / 2) * (TimeSpan.TicksPerSecond / 60d)), now))
        { policy.RecordPresentationVE(now, compositionDrivenVE: true); presented++; }
    }
    Equal(60, presented, "TV cadence from one second of incoming frames");
}

void HealthyComposition()
{
    // Break caught: a decoder arrival racing the normal compositor presenter.
    var policy = Enabled();
    True(policy.ShouldPresentOnArrivalVE(0), "First picture");
    policy.RecordPresentationVE(0);
    False(policy.ShouldPresentOnCompositionVE(0, 0), "Double initial presentation");
    False(policy.ShouldPresentOnArrivalVE(Stamp(16.7)), "Arrival bypassed healthy composition");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(16.7), Stamp(16.7)), "TV tick was suppressed");
}

void DuplicateComposition()
{
    // Break caught: WPF emits the same RenderingTime more than once.
    var policy = Enabled();
    True(policy.ShouldPresentOnCompositionVE(WpfTime(100), Stamp(100)), "Initial TV tick");
    policy.RecordPresentationVE(Stamp(100), compositionDrivenVE: true);
    False(policy.ShouldPresentOnCompositionVE(WpfTime(100), Stamp(130)), "Duplicate tick became a new picture");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(133.3), Stamp(133.3)), "Next distinct TV tick");
}

void CheckTicks(VERendererPresentationTiming policy, double[] times)
{
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    foreach (double ms in times)
    {
        True(policy.ShouldPresentOnCompositionVE(WpfTime(ms), Stamp(ms)), $"Missed intended tick at {ms}ms");
        policy.RecordPresentationVE(Stamp(ms), compositionDrivenVE: true);
    }
}

void Jitter60()
{
    // Break caught: elapsed-time gating converts slightly early 60 Hz ticks into 30 FPS.
    CheckTicks(Enabled(), [16.2, 33.75, 49.6, 66.9, 83.25, 100.25, 116.3, 133.6]);
}

void Jitter5994()
{
    // Break caught: rounding a fractional TV rate loses phase or alternate ticks.
    CheckTicks(Enabled(59.94), [16.3, 33.7, 50.05, 66.9, 83.3, 100.2, 116.5, 133.75]);
}

void CallbackJitter()
{
    // Break caught: using delayed Stopwatch callback times turns 60 distinct WPF ticks into 30 FPS.
    CheckCallbackTimes([16.667, 33.333, 50, 66.667], [17.667, 32.333, 51, 65.667]);
}

void CallbackJitter20Percent()
{
    // Break caught: frame pacing is tied to UI callback jitter instead of the compositor time domain.
    CheckCallbackTimes([16.667, 33.333, 50, 66.667], [19.667, 30.133, 53.2, 63.467]);
}

void CheckCallbackTimes(double[] compositionTimes, double[] callbackTimes)
{
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    for (int i = 0; i < compositionTimes.Length; i++)
    {
        True(policy.ShouldPresentOnCompositionVE(WpfTime(compositionTimes[i]), Stamp(callbackTimes[i])),
            $"Compositor tick {compositionTimes[i]}ms was lost to callback at {callbackTimes[i]}ms");
        policy.RecordPresentationVE(Stamp(callbackTimes[i]), compositionDrivenVE: true);
    }
}

void InitialPicturePhase()
{
    // Break caught: a nonzero first-picture phase leaks into every subsequent compositor deadline.
    var policy = Enabled(startMs: 300);
    policy.RecordPresentationVE(Stamp(303));
    False(policy.ShouldPresentOnCompositionVE(WpfTime(5000), Stamp(303)), "Initial picture was presented twice");
    double[] renderingTimes = [5016.667, 5033.333, 5050, 5066.667];
    double[] callbackTimes = [320.667, 335.333, 354, 368.667];
    for (int i = 0; i < renderingTimes.Length; i++)
    {
        True(policy.ShouldPresentOnCompositionVE(WpfTime(renderingTimes[i]), Stamp(callbackTimes[i])), "Immediate-picture phase throttled the compositor");
        policy.RecordPresentationVE(Stamp(callbackTimes[i]), compositionDrivenVE: true);
    }
}

void DelayedCallbackWithoutBurst()
{
    // Break caught: a late callback and the next on-time tick present 1.2 ms apart.
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    True(policy.ShouldPresentOnCompositionVE(WpfTime(16.667), Stamp(32.2)), "Delayed recovery tick was rejected");
    policy.RecordPresentationVE(Stamp(32.2), compositionDrivenVE: true);
    False(policy.ShouldPresentOnCompositionVE(WpfTime(33.333), Stamp(33.4)), "A late callback caused a near-immediate second presentation");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(50), Stamp(50.05)), "Cadence did not recover after skipping the burst");
    policy.RecordPresentationVE(Stamp(50.05), compositionDrivenVE: true);
    True(policy.ShouldPresentOnCompositionVE(WpfTime(66.667), Stamp(66.7)), "Recovery reduced the following cadence");
}

void Composition120Target60()
{
    // Break caught: multiple composition ticks on a faster display defeat the chosen cadence.
    var policy = Enabled();
    int presented = 0;
    for (int tick = 0; tick < 120; tick++)
    {
        long now = (long)Math.Round(tick * (Frequency / 120d));
        if (policy.ShouldPresentOnCompositionVE((long)Math.Round(tick * (TimeSpan.TicksPerSecond / 120d)), now))
        { policy.RecordPresentationVE(now, compositionDrivenVE: true); presented++; }
    }
    Equal(60, presented, "Presentation limit on faster composition");
}

void MissingComposition()
{
    // Break caught: freezing forever after a once-healthy compositor stops producing ticks.
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    False(policy.ShouldPresentOnArrivalVE(Stamp(30)), "Fallback before composition was stale");
    True(policy.ShouldPresentOnArrivalVE(Stamp(50.1)), "No fallback after three 60 Hz periods");
    True(policy.CompositionStallTimeoutTicksVE >= Stamp(33.333) &&
         policy.CompositionStallTimeoutTicksVE <= Stamp(50.001), "Unbounded composition stall window");
}

void FallbackCadence()
{
    // Break caught: stalled composition causes an immediate render on every 120 FPS arrival.
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    True(policy.ShouldPresentOnArrivalVE(Stamp(50)), "Fallback did not start");
    policy.RecordPresentationVE(Stamp(50));
    False(policy.ShouldPresentOnArrivalVE(Stamp(50)), "Same-time fallback burst");
    False(policy.ShouldPresentOnArrivalVE(Stamp(58.333)), "120 FPS fallback escaped TV cadence");
    True(policy.ShouldPresentOnArrivalVE(Stamp(66.667)), "Fallback lost next TV period");
}

void PauseWithoutCatchup()
{
    // Break caught: replaying overdue deadlines in a burst after a UI stall.
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    True(policy.ShouldPresentOnCompositionVE(WpfTime(200), Stamp(200)), "Late TV tick did not recover");
    policy.RecordPresentationVE(Stamp(200), compositionDrivenVE: true);
    False(policy.ShouldPresentOnCompositionVE(WpfTime(201), Stamp(201)), "Catch-up render after long pause");
    False(policy.ShouldPresentOnArrivalVE(Stamp(205)), "Catch-up arrival after long pause");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(216.667), Stamp(216.667)), "Cadence did not resume");
}

void PauseOffPhase()
{
    // Break caught: a pause resumes just before an old phase deadline, causing a 1.2 ms burst.
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ShouldPresentOnCompositionVE(0, 0);
    True(policy.ShouldPresentOnArrivalVE(Stamp(198)), "Off-phase recovery picture did not present");
    policy.RecordPresentationVE(Stamp(198));
    False(policy.ShouldPresentOnArrivalVE(Stamp(199.2)), "Old phase caused a fallback burst after the pause");
    False(policy.ShouldPresentOnCompositionVE(WpfTime(199.2), Stamp(199.2)), "Old phase caused a composition burst after the pause");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(214.667), Stamp(214.667)), "Off-phase recovery lost the next useful display period");
}

void DisableImmediate()
{
    // Break caught: turning the experiment off leaves pacing or composition suppression active.
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    policy.ConfigureVE(false, 60, Stamp(1));
    True(policy.ShouldPresentOnArrivalVE(Stamp(1)), "Disabled mode still throttles arrivals");
    policy.RecordPresentationVE(Stamp(1));
    True(policy.ShouldPresentOnArrivalVE(Stamp(1.1)), "Disabled mode keeps old deadline");
    False(policy.ShouldPresentOnCompositionVE(WpfTime(16.7), Stamp(16.7)), "Disabled composition competes with immediate mode");
}

void ReconfigureClearsHistory()
{
    // Break caught: old WPF/Stopwatch timestamps reject the first image after a new mode/monitor.
    var policy = Enabled();
    True(policy.ShouldPresentOnCompositionVE(WpfTime(1000), Stamp(1000)), "Initial composition");
    policy.RecordPresentationVE(Stamp(1000), compositionDrivenVE: true);
    policy.ConfigureVE(true, 120, Stamp(2000));
    True(policy.EnabledVE, "Configuration was not enabled");
    Equal(120d, policy.RefreshRateHzVE, "Selected refresh rate");
    True(policy.ShouldPresentOnCompositionVE(0, Stamp(2000)), "Old RenderingTime leaked into new configuration");
    policy.RecordPresentationVE(Stamp(2000), compositionDrivenVE: true);
    True(policy.ShouldPresentOnCompositionVE(WpfTime(8.333), Stamp(2008.333)), "New monitor cadence was ignored");
}

void ResetKeepsConfiguration()
{
    // Break caught: a new renderer host resets user configuration or keeps the old time domain.
    var policy = Enabled(59.94);
    policy.ShouldPresentOnCompositionVE(WpfTime(1000), Stamp(1000));
    policy.RecordPresentationVE(Stamp(1000), compositionDrivenVE: true);
    policy.ResetVE(Stamp(1200));
    True(policy.EnabledVE, "Reset disabled the selected experiment");
    Equal(59.94, policy.RefreshRateHzVE, "Reset lost fractional refresh rate");
    True(policy.ShouldPresentOnArrivalVE(Stamp(1200)), "Reset kept presentation history");
    policy.RecordPresentationVE(Stamp(1200));
    False(policy.ShouldPresentOnCompositionVE(0, Stamp(1200)), "Reset created a double first picture");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(16.7), Stamp(1216.7)), "Reset kept composition dedupe history");
}

void OlderTimestamp()
{
    // Break caught: delayed callbacks rewind deadlines and permit a duplicate picture.
    var policy = Enabled();
    policy.RecordPresentationVE(Stamp(100));
    policy.RecordPresentationVE(Stamp(90));
    False(policy.ShouldPresentOnArrivalVE(Stamp(99)), "Older arrival advanced presentation");
    False(policy.ShouldPresentOnCompositionVE(WpfTime(1), Stamp(99)), "Older composition advanced presentation");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(16.7), Stamp(116.7)), "Older record rewound the next TV period");
}

void InvalidConfiguration()
{
    // Break caught: invalid clock/rate makes deadlines unbounded, or a failed switch resets active state.
    Throws<ArgumentOutOfRangeException>(() => new VERendererPresentationTiming(0), "Zero-frequency clock accepted");
    Throws<ArgumentOutOfRangeException>(() => new VERendererPresentationTiming(-1), "Negative-frequency clock accepted");
    var policy = Enabled();
    policy.RecordPresentationVE(0);
    foreach (double invalid in new[] { 0d, -60d, double.NaN, double.PositiveInfinity })
        Throws<ArgumentOutOfRangeException>(() => policy.ConfigureVE(false, invalid, Stamp(1000)), $"Invalid rate accepted: {invalid}");
    True(policy.EnabledVE, "Failed configuration changed enabled state");
    Equal(60d, policy.RefreshRateHzVE, "Failed configuration changed refresh rate");
    False(policy.ShouldPresentOnArrivalVE(Stamp(1)), "Failed configuration reset presentation history");
}

void IndependentClockUnits()
{
    // Break caught: treating WPF TimeSpan ticks as Stopwatch ticks changes behavior by clock frequency.
    var policy = new VERendererPresentationTiming(10_000_000);
    policy.ConfigureVE(true, 60, 900_000_000);
    policy.RecordPresentationVE(900_000_000);
    False(policy.ShouldPresentOnCompositionVE(WpfTime(8000), 900_000_000), "Initial duplicate");
    True(policy.ShouldPresentOnCompositionVE(WpfTime(8016.7), 900_167_000), "Independent clock units lost TV tick");
    policy.RecordPresentationVE(900_167_000, compositionDrivenVE: true);
    False(policy.ShouldPresentOnArrivalVE(900_250_000), "Independent clocks bypassed healthy composition");
}
