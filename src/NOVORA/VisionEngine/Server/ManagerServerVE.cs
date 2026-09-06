using NOVORA.Services;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Transport;
using System.Diagnostics;
using System.IO;

namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Despliega y arranca el servidor Android de VisionEngine.
///
/// Block B reutiliza el binario scrcpy-server 4.1 incluido en Tools,
/// pero NO ejecuta scrcpy.exe: el cliente, túnel, protocolo y decoder
/// son gestionados por VisionEngine. El launch compatible invoca
/// com.genymobile.scrcpy.Server 4.1 con scid=..., audio y control habilitables durante Block B.
/// </summary>
public sealed class ManagerServerVE
{
    private readonly AdbService _adb;
    private readonly NovoraPaths _paths;
    private readonly object _statusGate = new();

    private StatusServerVE _status =
        StatusServerVE.CreateInitialVE();

    public ManagerServerVE(
        AdbService adb,
        NovoraPaths paths)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler<StatusServerVE>? StatusChangedVE;

    public StatusServerVE StatusVE
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    public async Task<SessionServerVE> StartAsync(
        SessionDeviceVE device,
        TunnelTransportVE tunnel,
        OptionsServerVE options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(tunnel);
        ArgumentNullException.ThrowIfNull(options);

        options.ValidateVE();

        if (!File.Exists(_paths.ScrcpyServer))
        {
            throw new FileNotFoundException(
                "VisionEngine no encuentra Tools\\scrcpy-server.",
                _paths.ScrcpyServer);
        }

        PublishStatusVE(
            new StatusServerVE(
                State: StatesServerVE.Deploying,
                Serial: device.Serial,
                Scid: tunnel.Scid,
                Backend: BackendServerVE.Scrcpy41Compatibility,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                Message: "Desplegando servidor Android de VisionEngine.",
                LastError: null));

        Process? processToCleanupVE = null;

        try
        {
            await _adb.PushAsync(
                    device.Serial,
                    _paths.ScrcpyServer,
                    ConstantsProtocolVE.RemoteServerPathVE,
                    cancellationToken)
                .ConfigureAwait(false);

            PublishStatusVE(
                _status with
                {
                    State = StatesServerVE.Starting,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Iniciando servidor Android de VisionEngine."
                });

            ProcessStartInfo startInfo =
                CreateStartInfoVE(
                    device,
                    tunnel,
                    options);

            Process process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "No fue posible iniciar el proceso ADB del servidor VisionEngine.");

            processToCleanupVE = process;

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            await Task.Delay(
                    TimeSpan.FromMilliseconds(125),
                    cancellationToken)
                .ConfigureAwait(false);

            if (process.HasExited)
            {
                string output = await outputTask.ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);
                int exitCode = process.ExitCode;
                processToCleanupVE = null;
                process.Dispose();

                string detail =
                    string.IsNullOrWhiteSpace(error)
                        ? output.Trim()
                        : error.Trim();

                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(detail)
                        ? $"El servidor Android VisionEngine terminó prematuramente con código {exitCode}."
                        : $"El servidor Android VisionEngine terminó prematuramente: {detail}");
            }

            SessionServerVE session =
                new(
                    serial: device.Serial,
                    scid: tunnel.Scid,
                    socketName: tunnel.SocketName,
                    backend: BackendServerVE.Scrcpy41Compatibility,
                    process: process,
                    standardOutputTask: outputTask,
                    standardErrorTask: errorTask);

            processToCleanupVE = null;

            PublishStatusVE(
                _status with
                {
                    State = StatesServerVE.Running,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Servidor Android VisionEngine activo.",
                    LastError = null
                });

            return session;
        }
        catch (OperationCanceledException)
        {
            StopProcessVE(processToCleanupVE);
            throw;
        }
        catch (Exception ex)
        {
            StopProcessVE(processToCleanupVE);
            PublishStatusVE(
                new StatusServerVE(
                    State: StatesServerVE.Failed,
                    Serial: device.Serial,
                    Scid: tunnel.Scid,
                    Backend: BackendServerVE.Scrcpy41Compatibility,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Message: "Falló el servidor Android de VisionEngine.",
                    LastError: ex.Message));

            throw;
        }
    }

    public void MarkStoppedVE()
    {
        PublishStatusVE(
            StatusServerVE.CreateInitialVE());
    }

    private static void StopProcessVE(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private ProcessStartInfo CreateStartInfoVE(
        SessionDeviceVE device,
        TunnelTransportVE tunnel,
        OptionsServerVE options)
    {
        ProcessStartInfo startInfo =
            new()
            {
                FileName = _paths.Adb,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _paths.ToolsDirectory
            };

        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add(device.Serial);
        startInfo.ArgumentList.Add("shell");
        startInfo.ArgumentList.Add(
            $"CLASSPATH={ConstantsProtocolVE.RemoteServerPathVE}");
        startInfo.ArgumentList.Add("app_process");
        startInfo.ArgumentList.Add("/");
        startInfo.ArgumentList.Add(
            ConstantsProtocolVE.ScrcpyServerClassVE);
        startInfo.ArgumentList.Add(
            ConstantsProtocolVE.ScrcpyCompatibilityVersionVE);

        IReadOnlyList<string> serverArguments =
            options.BuildArgumentsVE(
                tunnel.Scid,
                tunnel.Mode == ModeTransportVE.Forward);

        foreach (string argument in serverArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private void PublishStatusVE(StatusServerVE status)
    {
        EventHandler<StatusServerVE>? handler;

        lock (_statusGate)
        {
            _status = status;
            handler = StatusChangedVE;
        }

        handler?.Invoke(this, status);
    }
}
