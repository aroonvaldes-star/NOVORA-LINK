using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NOVORA.Control;

public static class NLControlInputLoop
{
    // Commands read ahead of execution. Bounded, so a stalled executor still applies TCP
    // backpressure instead of buffering input without limit.
    private const int ReadAheadCommands = 64;
    private const int TouchCommandType = 2;

    /// <summary>
    /// A touch move superseded by the next queued move of the same pointer and buttons.
    /// Each move is dispatched as an accessibility gesture that must complete before the
    /// next one, so executing every intermediate position queues input behind the finger.
    /// </summary>
    internal static bool IsSupersededMove(NLControlInputCommand command, NLControlInputCommand next) =>
        command.Type == TouchCommandType && next.Type == TouchCommandType &&
        command.MotionAction is 2 or 7 && next.MotionAction == command.MotionAction &&
        next.PointerId == command.PointerId && next.Buttons == command.Buttons;

    public static async Task RunAsync(
        Stream stream,
        Func<NLControlInputCommand, CancellationToken, Task> execute,
        Func<NLControlInputCommand, Exception, CancellationToken, Task> recover,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(recover);
        if (commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));

        while (!cancellationToken.IsCancellationRequested)
        {
            NLControlInputCommand command = await NLControlProtocol.ReadAsync<NLControlInputCommand>(
                stream, cancellationToken).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(commandTimeout);
            try
            {
                await execute(command, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await recover(command, ex, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task RunDuplexAsync(
        Stream stream,
        Func<NLControlInputCommand, CancellationToken, Task<NLControlInputResponse?>> execute,
        Func<NLControlInputCommand, Exception, CancellationToken, Task> recover,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken,
        Func<NLControlInputResponse?>? telemetryProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(recover);
        if (commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var writer = new SemaphoreSlim(1, 1);
        async Task SendAsync(NLControlInputResponse response, CancellationToken token)
        {
            await writer.WaitAsync(token).ConfigureAwait(false);
            try { await NLControlProtocol.WriteAsync(stream, response, token).ConfigureAwait(false); }
            finally { writer.Release(); }
        }
        var pending = Channel.CreateBounded<NLControlInputCommand>(new BoundedChannelOptions(ReadAheadCommands)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
        });
        async Task ReadAheadAsync()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    NLControlInputCommand command = await NLControlProtocol.ReadAsync<NLControlInputCommand>(
                        stream, lifetime.Token).ConfigureAwait(false);
                    await pending.Writer.WriteAsync(command, lifetime.Token).ConfigureAwait(false);
                }
                pending.Writer.TryComplete();
            }
            catch (Exception ex) { pending.Writer.TryComplete(ex); }
        }
        // The reader is owned by this method so that every exit path, including a send
        // failure, waits for it before the caller can dispose the stream.
        Task reader = ReadAheadAsync();
        async Task ReadCommandsAsync()
        {
            while (!lifetime.IsCancellationRequested)
            {
                // WaitToReadAsync rethrows the reader's own failure (EOF, invalid frame).
                if (!await pending.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
                {
                    await reader.ConfigureAwait(false);
                    throw new EndOfStreamException("El canal de control terminó.");
                }
                if (!pending.Reader.TryRead(out NLControlInputCommand? command)) continue;
                if (pending.Reader.TryPeek(out NLControlInputCommand? next) && IsSupersededMove(command, next))
                    continue;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(commandTimeout);
                NLControlInputResponse? response = null;
                try
                {
                    response = await execute(command, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                catch (Exception ex) { await recover(command, ex, lifetime.Token).ConfigureAwait(false); }
                // A partially sent frame cannot be recovered by executing the next command.
                if (response is not null) await SendAsync(response, deadline.Token).ConfigureAwait(false);
            }
        }
        async Task PublishTelemetryAsync()
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), lifetime.Token).ConfigureAwait(false);
                NLControlInputResponse? response = telemetryProvider!();
                if (response is not null)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    deadline.CancelAfter(commandTimeout);
                    // One telemetry sample in flight; no queue of obsolete windows.
                    await SendAsync(response, deadline.Token).ConfigureAwait(false);
                }
            }
        }
        Task commands = ReadCommandsAsync();
        Task telemetry = telemetryProvider is null
            ? Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token) : PublishTelemetryAsync();
        try { await await Task.WhenAny(commands, telemetry).ConfigureAwait(false); }
        finally
        {
            lifetime.Cancel();
            try { await Task.WhenAll(commands, telemetry, reader).ConfigureAwait(false); }
            catch (Exception) { /* The original completion or failure is propagated above. */ }
        }
    }
}
