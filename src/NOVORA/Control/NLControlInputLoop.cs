namespace NOVORA.Control;

public static class NLControlInputLoop
{
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
                NLControlInputResponse? response = await execute(command, deadline.Token).ConfigureAwait(false);
                if (response is not null)
                    await NLControlProtocol.WriteAsync(stream, response, deadline.Token).ConfigureAwait(false);
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
}
