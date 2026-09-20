using NOVORA.Service;
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
public sealed class VEServerManager
{
    private readonly NLServiceADB _adb;
    private readonly NLServiceNovoraPaths _paths;
    private readonly object _statusGate = new();

    private VEServerStatus _status =
        VEServerStatus.CreateInitialVE();

    public VEServerManager(
        NLServiceADB adb,
        NLServiceNovoraPaths paths)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler<VEServerStatus>? StatusChangedVE;

    public VEServerStatus StatusVE
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    public async Task<VEServerSession> StartAsync(
        VEDeviceSession device,
        VETransportTunnel tunnel,
        VEServerOptions options,
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
            new VEServerStatus(
                State: VEServerStates.Deploying,
                Serial: device.Serial,
                Scid: tunnel.Scid,
                Backend: VEServerBackend.Scrcpy41Compatibility,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                Message: "Desplegando servidor Android de VisionEngine.",
                LastError: null));

        Process? processToCleanupVE = null;

        try
        {
            await _adb.PushAsync(
                    device.Serial,
                    _paths.ScrcpyServer,
                    VEProtocolConstants.RemoteServerPathVE,
                    cancellationToken)
                .ConfigureAwait(false);

            PublishStatusVE(
                _status with
                {
                    State = VEServerStates.Starting,
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

            VEServerSession session =
                new(
                    serial: device.Serial,
                    scid: tunnel.Scid,
                    socketName: tunnel.SocketName,
                    backend: VEServerBackend.Scrcpy41Compatibility,
                    process: process,
                    standardOutputTask: outputTask,
                    standardErrorTask: errorTask);

            processToCleanupVE = null;

            PublishStatusVE(
                _status with
                {
                    State = VEServerStates.Running,
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
                new VEServerStatus(
                    State: VEServerStates.Failed,
                    Serial: device.Serial,
                    Scid: tunnel.Scid,
                    Backend: VEServerBackend.Scrcpy41Compatibility,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Message: "Falló el servidor Android de VisionEngine.",
                    LastError: ex.Message));

            throw;
        }
    }

    public void MarkStoppedVE()
    {
        PublishStatusVE(
            VEServerStatus.CreateInitialVE());
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
        VEDeviceSession device,
        VETransportTunnel tunnel,
        VEServerOptions options)
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
            $"CLASSPATH={VEProtocolConstants.RemoteServerPathVE}");
        startInfo.ArgumentList.Add("app_process");
        startInfo.ArgumentList.Add("/");
        startInfo.ArgumentList.Add(
            VEProtocolConstants.ScrcpyServerClassVE);
        startInfo.ArgumentList.Add(
            VEProtocolConstants.ScrcpyCompatibilityVersionVE);

        IReadOnlyList<string> serverArguments =
            options.BuildArgumentsVE(
                tunnel.Scid,
                tunnel.Mode == VETransportMode.Forward);

        foreach (string argument in serverArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private void PublishStatusVE(VEServerStatus status)
    {
        EventHandler<VEServerStatus>? handler;

        lock (_statusGate)
        {
            _status = status;
            handler = StatusChangedVE;
        }

        handler?.Invoke(this, status);
    }
}
