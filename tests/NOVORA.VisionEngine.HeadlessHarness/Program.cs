using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Server;

if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine(
        "Uso: dotnet run --project tests\\NOVORA.VisionEngine.HeadlessHarness -- <ADB_SERIAL> [SEGUNDOS]");
    return 64;
}

string serial = args[0].Trim();
int seconds =
    args.Length >= 2 && int.TryParse(args[1], out int parsed) && parsed > 0
        ? parsed
        : 20;

using CancellationTokenSource stopCts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopCts.Cancel();
};

await using EngineCoreVE engine = new();
engine.StatusChangedVE += (_, status) =>
{
    Console.WriteLine(
        $"[{DateTime.Now:HH:mm:ss}] " +
        $"State={status.State} " +
        $"Device={status.DeviceReady} " +
        $"Server={status.ServerRunning} " +
        $"Transport={status.TransportConnected} " +
        $"Video={status.VideoStreaming} " +
        $"VPackets={status.VideoPacketsReceived} " +
        $"VFrames={status.VideoFramesDecoded} " +
        $"Audio={status.AudioStreaming} " +
        $"APackets={status.AudioPacketsReceived} " +
        $"AFrames={status.AudioFramesDecoded} " +
        $"Control={status.ControlReady} " +
        $"Gamepads={status.ConnectedGamepads} " +
        $"Renderer={status.RendererEnabled}");
};

Console.WriteLine("NOVORA VisionEngine Block B - HEADLESS MULTIMEDIA HARNESS");
Console.WriteLine($"Device: {serial}");
Console.WriteLine($"Duration: {seconds}s");
Console.WriteLine("Video renderer: DISABLED by design");
Console.WriteLine("Audio/control/gamepad: ENABLED");
Console.WriteLine("Tip: reproduce audio en el A56 durante la prueba para validar AudioVE.");
Console.WriteLine();

ResultCoreVE initialize = await engine.InitializeAsync(stopCts.Token);
if (!initialize.Success)
{
    Console.Error.WriteLine(initialize.Message);
    Console.Error.WriteLine(initialize.Exception?.Message);
    return 1;
}

OptionsServerVE options = OptionsServerVE.CreateDefaultVE();
ResultCoreVE start = await engine.StartAsync(serial, options, stopCts.Token);
if (!start.Success)
{
    Console.Error.WriteLine(start.Message);
    Console.Error.WriteLine(start.Exception?.Message);
    return 2;
}

try
{
    await Task.Delay(TimeSpan.FromSeconds(seconds), stopCts.Token);
}
catch (OperationCanceledException)
{
}

StatusCoreVE status = engine.StatusVE;
StatusAudioVE audio = engine.RuntimeVE.AudioVE.StatusVE;
StatusControlVE control = engine.RuntimeVE.ControlVE.StatusVE;

Console.WriteLine();
Console.WriteLine("=== BLOCK B RESULT ===");
Console.WriteLine($"Video packets        : {status.VideoPacketsReceived}");
Console.WriteLine($"Video frames         : {status.VideoFramesDecoded}");
Console.WriteLine($"Video decode errors  : {status.VideoDecodeErrors}");
Console.WriteLine($"Audio state          : {audio.State}");
Console.WriteLine($"Audio packets        : {status.AudioPacketsReceived}");
Console.WriteLine($"Audio frames         : {status.AudioFramesDecoded}");
Console.WriteLine($"Audio decode errors  : {status.AudioDecodeErrors}");
Console.WriteLine($"Audio playback       : {status.AudioPlaybackEnabled}");
Console.WriteLine($"Control state        : {control.State}");
Console.WriteLine($"Control sent         : {status.ControlMessagesSent}");
Console.WriteLine($"Control received     : {status.ControlMessagesReceived}");
Console.WriteLine($"Gamepads             : {status.ConnectedGamepads}");
Console.WriteLine($"Gamepad reports      : {status.GamepadReportsSent}");
Console.WriteLine($"Renderer enabled     : {status.RendererEnabled}");

ResultCoreVE stop = await engine.StopAsync();
if (!stop.Success)
{
    Console.Error.WriteLine(stop.Message);
    return 3;
}

if (status.RendererEnabled)
{
    Console.Error.WriteLine("FAIL: el renderer de video se activó durante Block B.");
    return 4;
}

if (status.VideoPacketsReceived <= 0)
{
    Console.Error.WriteLine("FAIL: no se recibieron paquetes de video.");
    return 5;
}

if (status.VideoFramesDecoded <= 0)
{
    Console.Error.WriteLine("FAIL: FFmpeg no produjo frames de video.");
    return 6;
}

if (status.VideoDecodeErrors != 0)
{
    Console.Error.WriteLine("FAIL: se registraron errores de decode de video.");
    return 7;
}

if (!status.ControlReady && control.State is not StatesControlVE.EndOfStream)
{
    Console.Error.WriteLine("FAIL: ControlVE no llegó a estado READY.");
    return 8;
}

if (audio.State == StatesAudioVE.Failed || status.AudioDecodeErrors != 0)
{
    Console.Error.WriteLine("FAIL: AudioVE falló.");
    return 9;
}

Console.WriteLine("PASS: Block B mantiene video headless y habilita audio/control/gamepad/exchange.");
return 0;
