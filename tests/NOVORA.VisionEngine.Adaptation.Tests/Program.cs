using NOVORA.VisionEngine.Adaptation;

int passed = 0, failed = 0;

void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static VEAdaptiveSample Good() => new(RttMs: 15, LossRatio: 0, QueueAgeMs: 10, WriteWaitMs: 2);
static VEAdaptiveSample Congested(double rtt = 260) => new(RttMs: rtt, LossRatio: 0.05, QueueAgeMs: 300, WriteWaitMs: 150);

Check("StableNetwork_KeepsTopLevel", () =>
{
    var policy = new VEAdaptivePolicy();
    VEAdaptiveDecision last = default!;
    for (int i = 0; i < 60; i++) last = policy.Tick(Good());
    Require(last.Level == 0 && !last.Changed, "Stable network changed level.");
});

Check("Congestion_DowngradesOnlyAfterTwoSeconds", () =>
{
    var policy = new VEAdaptivePolicy();
    Require(!policy.Tick(Congested()).Changed, "Downgraded after 1 s of congestion.");
    Require(policy.Tick(Congested()).Changed, "Did not downgrade after 2 s of congestion.");
});

Check("Recovery_UpgradesOnlyAfterEightStableSecondsAndOneStep", () =>
{
    var policy = new VEAdaptivePolicy();
    policy.Tick(Congested()); policy.Tick(Congested());
    int level = policy.Current.Level;
    Require(level > 0, "Setup did not downgrade.");
    for (int i = 0; i < 7; i++) Require(!policy.Tick(Good()).Changed, "Upgraded before 8 s stable.");
    var up = policy.Tick(Good());
    Require(up.Changed && up.Level == level - 1, "Did not upgrade exactly one step after 8 s.");
});

Check("FpsNeverBelowThirty_WhileResolutionStepsRemain", () =>
{
    var policy = new VEAdaptivePolicy();
    for (int i = 0; i < 60; i++) policy.Tick(Congested());
    for (int level = 0; level <= policy.MaxLevel; level++)
    {
        var d = VEAdaptivePolicy.LevelFor(level);
        if (level < policy.MaxLevel) Require(d.Fps >= 30, $"Level {level} dropped fps below 30 too early.");
        if (level > 0) Require(d.MaxSize <= VEAdaptivePolicy.LevelFor(level - 1).MaxSize, "Resolution increased when degrading.");
    }
});

Check("Hysteresis_AlternatingSamplesDoNotOscillate", () =>
{
    var policy = new VEAdaptivePolicy();
    int changes = 0;
    for (int i = 0; i < 60; i++) if (policy.Tick(i % 2 == 0 ? Congested() : Good()).Changed) changes++;
    Require(changes == 0, $"Alternating samples changed level {changes} times.");
});

Check("SevereLatency_DowngradesWithinTwoSeconds", () =>
{
    var policy = new VEAdaptivePolicy();
    policy.Tick(Congested(400));
    Require(policy.Tick(Congested(400)).Changed, "Severe latency did not downgrade in 2 s.");
});

Console.WriteLine($"VisionEngine Adaptation: {passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;
