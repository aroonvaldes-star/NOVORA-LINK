using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidSession
{
    private static NLControlSnapshot State(long revision = 7) => new(revision, "PC", "1.4.0", "4M", "Gaming", "default", "default", true,
        [new("4M", "4 Mbps")], [new("Gaming", "Juegos")], [new("default", "Windows")]);
    private static NLControlReply Reply(NLControlRequest request) => new(1, request.Id, true, "OK", State());

    [Fact]
    public async Task RemovingUiObserverKeepsConnectionAndReturningObserverSeesLatestState()
    {
        await using var server = new NLControlServer("12345678", request => Task.FromResult(Reply(request)), 0);
        server.Start();
        await using var session = new NLControlSession();
        EventHandler<NLControlSessionState> screen = (_, _) => { };
        session.Changed += screen;
        await session.ConnectUsbAsync("12345678", server.Port);
        long generation = session.Current.Generation;
        session.Changed -= screen; // Activity leaves; service still owns this session.
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += (_, state) => { if (state.Snapshot?.Revision == 8) updated.TrySetResult(); };
        server.Publish(State(8));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NLControlSessionPhase.Connected, session.Current.Phase);
        Assert.Equal(generation, session.Current.Generation);
        Assert.Equal(8, session.Current.Snapshot!.Revision);
        Assert.True((await session.SendAsync("get")).Success);
    }

    [Fact]
    public async Task VoluntaryCloseClearsStateAndCannotSendMoreCommands()
    {
        await using var server = new NLControlServer("12345678", request => Task.FromResult(Reply(request)), 0);
        server.Start();
        await using var session = new NLControlSession();
        await session.ConnectUsbAsync("12345678", server.Port);
        await session.DisconnectAsync();
        Assert.Equal(NLControlSessionPhase.Disconnected, session.Current.Phase);
        Assert.Null(session.Current.Snapshot);
        Assert.False(session.Current.Busy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("bitrate", "4M"));
    }

    [Fact]
    public async Task PcCloseBecomesLostWithoutUiInteraction()
    {
        var server = new NLControlServer("12345678", request => Task.FromResult(Reply(request)), 0);
        server.Start();
        await using var session = new NLControlSession();
        await session.ConnectUsbAsync("12345678", server.Port);
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += (_, state) => { if (state.Phase == NLControlSessionPhase.Lost) lost.TrySetResult(); };
        await server.DisposeAsync();
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(session.Current.Snapshot);
        Assert.False(session.Current.Busy);
    }

    [Fact]
    public async Task DisconnectDuringPairingCannotResurrectSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new NLControlServer("12345678", async request =>
        { entered.TrySetResult(); await release.Task; return Reply(request); }, 0);
        server.Start();
        await using var session = new NLControlSession();
        var connect = session.ConnectUsbAsync("12345678", server.Port);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(NLControlSessionPhase.Connecting, session.Current.Phase);
            await session.DisconnectAsync();
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => connect);
            Assert.Equal(NLControlSessionPhase.Disconnected, session.Current.Phase);
            Assert.Null(session.Current.Snapshot);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task NetworkLossDoesNotRepeatUnconfirmedMutation()
    {
        int mutations = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new NLControlServer("12345678", async request =>
        {
            if (request.Action == "bitrate")
            { Interlocked.Increment(ref mutations); entered.TrySetResult(); await release.Task; }
            return Reply(request);
        }, 0);
        server.Start();
        await using var session = new NLControlSession();
        await session.ConnectUsbAsync("12345678", server.Port);
        var send = session.SendAsync("bitrate", "4M");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.Current.Busy);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("get"));
            await session.LoseAsync("La red cambió.");
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => send);
            Assert.Equal(1, mutations);
            Assert.Equal(NLControlSessionPhase.Lost, session.Current.Phase);
            Assert.Null(session.Current.Snapshot);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task OldConnectionFailureDoesNotClearReplacementSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = new NLControlServer("12345678", async request =>
        { entered.TrySetResult(); await release.Task; return Reply(request); }, 0);
        await using var second = new NLControlServer("87654321", request => Task.FromResult(Reply(request)), 0);
        first.Start(); second.Start();
        await using var session = new NLControlSession();
        var old = session.ConnectUsbAsync("12345678", first.Port);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.ConnectUsbAsync("87654321", second.Port);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => old);
            Assert.Equal(NLControlSessionPhase.Connected, session.Current.Phase);
            Assert.True((await session.SendAsync("get")).Success);
        }
        finally { release.TrySetResult(); }
    }
}
