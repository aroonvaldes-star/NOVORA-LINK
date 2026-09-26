using NOVORA.Service;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestServiceProcess
{
    [Fact]
    public async Task Controlled_process_requires_absolute_paths_and_safe_arguments()
    {
        NLServiceProcess process = new();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            process.RunAsync("cmd.exe", ["/c", "exit 0"]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            process.RunAsync(Environment.GetEnvironmentVariable("ComSpec")!, ["bad\nargument"]));
    }

    [Fact]
    public async Task Controlled_process_captures_output_error_and_exit_code()
    {
        NLServiceProcess process = new();
        NLServiceProcessResult result = await process.RunAsync(
            Environment.GetEnvironmentVariable("ComSpec")!,
            ["/d", "/s", "/c", "echo out & echo err 1>&2 & exit /b 7"]);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("out", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("err", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Controlled_process_cancellation_terminates_the_process()
    {
        NLServiceProcess process = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            process.RunAsync(
                Environment.GetEnvironmentVariable("ComSpec")!,
                ["/d", "/s", "/c", "ping -n 30 127.0.0.1 >nul"],
                cancellationToken: timeout.Token));
    }
}
