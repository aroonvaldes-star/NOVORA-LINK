using System.Diagnostics;

namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Proceso ADB que mantiene vivo el servidor Android de VisionEngine.
/// </summary>
public sealed class VEServerSession : IAsyncDisposable
{
    private bool _disposed;

    internal VEServerSession(
        string serial,
        int scid,
        string socketName,
        VEServerBackend backend,
        Process process,
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        Serial = serial;
        Scid = scid;
        SocketName = socketName;
        Backend = backend;
        ProcessVE = process;
        StandardOutputTaskVE = standardOutputTask;
        StandardErrorTaskVE = standardErrorTask;
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public string Serial { get; }

    public int Scid { get; }

    public string SocketName { get; }

    public VEServerBackend Backend { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public bool IsRunningVE
        => !_disposed &&
           !ProcessVE.HasExited;

    internal Process ProcessVE { get; }

    internal Task<string> StandardOutputTaskVE { get; }

    internal Task<string> StandardErrorTaskVE { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!ProcessVE.HasExited)
            {
                ProcessVE.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            await ProcessVE.WaitForExitAsync()
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Task.WhenAll(
                    StandardOutputTaskVE,
                    StandardErrorTaskVE)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        ProcessVE.Dispose();
    }
}
