using NOVORA.Control;

var state = new NLControlSnapshot(10, "Test PC", "1.4", "8M", "default", "auto", "auto", false,
    [new("8M", "8 Mbps")], [new("default", "Default")], [new("auto", "Auto")]);
int passed = 0;
void Check(string name, NLControlRequest request, bool accepted, NLControlSnapshot? snapshot = null)
{
    var error = NLControlCommands.Validate(request, snapshot ?? state);
    if ((error is null) != accepted)
        throw new InvalidOperationException($"FAIL {name}: expected accepted={accepted}, error={error ?? "none"}");
    Console.WriteLine($"PASS {name}");
    passed++;
}
NLControlRequest Request(string action, string? value = null) =>
    new(NLControlProtocol.Version, 1, action, value, state.Revision);

Check("State query", Request("get"), true);
Check("Unsupported protocol", Request("get") with { Version = NLControlProtocol.Version + 1 }, false);
Check("Invalid request identity", Request("get") with { Id = 0 }, false);
Check("Unknown command", Request("unknown"), false);
Check("Stale settings revision", Request("bitrate", "8M") with { Revision = 9 }, false);
Check("Supported bitrate", Request("bitrate", "8M"), true);
Check("Unsupported bitrate", Request("bitrate", "invalid"), false);
Check("Files disabled", Request("file.begin"), false);
Check("Files enabled", Request("file.begin"), true, state with { FileSharing = true });
Check("Restart requires running video", Request("restartVideo"), false);
Check("Restart running video", Request("restartVideo"), true, state with { VideoRunning = true });
Console.WriteLine($"{passed} control validation checks passed.");
