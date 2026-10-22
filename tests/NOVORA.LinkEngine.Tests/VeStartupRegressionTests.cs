using NOVORA.Control;

internal static class VeStartupRegressionTests
{
    internal static void Run()
    {
        foreach (string action in new[] { "startAppVideo", "startVideoLan" })
        {
            NLControlSnapshot state = ReadyState();
            var request = new NLControlRequest(1, 7, action, Revision: 10);
            string? error = NLControlCommands.Validate(request, state);
            if (error is not null)
                throw new InvalidOperationException(
                    $"{action} rejected a valid capture after an unrelated state update: {error}");

            error = NLControlCommands.Validate(request,
                state with { Engines = state.Engines! with { VideoCanStart = false } });
            if (error is null)
                throw new InvalidOperationException($"{action} accepted a source that cannot start.");

            error = NLControlCommands.Validate(request with { Value = "unexpected" }, state);
            if (error is null)
                throw new InvalidOperationException($"{action} accepted an invalid capture option.");

            error = NLControlCommands.Validate(request with { Value = "video-only" }, state);
            if (error is not null)
                throw new InvalidOperationException($"{action} rejected authorized video-only capture: {error}");
        }

        if (NLControlCommands.Validate(new(1, 8, "bitrate", "8000000", Revision: 10), ReadyState()) is null)
            throw new InvalidOperationException("A stale settings edit must still be rejected.");
        if (NLControlCommands.Validate(new(2, 9, "startVideoLan", Revision: 11), ReadyState()) is null)
            throw new InvalidOperationException("An incompatible control protocol must still be rejected.");
        if (NLControlCommands.Validate(new(1, 10, "exin.reactivate"), ReadyState()) != "Acción no disponible en este bloque.")
            throw new InvalidOperationException("Manual ExIn recovery must not be an accepted control action.");
    }

    private static NLControlSnapshot ReadyState() => new(
        11, "PC", "1.4", "8000000", "Gaming", "disabled", "disabled", false,
        [new("8000000", "8 Mbps")], [], [],
        new(true, false, false, false, false, "Stopped", "", "Android"));
}
