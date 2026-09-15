using System.IO;

namespace NOVORA.Control;

public enum NLControlSessionPhase { Disconnected, Connecting, Connected, Lost }

public sealed record NLControlSessionState(long Generation, NLControlSessionPhase Phase,
    string Transport, string Message, NLControlSnapshot? Snapshot, bool Busy = false);

/// <summary>Owns a connection independently of UI subscriptions. Never retries a mutation or stores a credential.</summary>
public sealed class NLControlSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private NLControlClient? _client;
    private bool _disposed;
    private NLControlSessionState _current = new(0, NLControlSessionPhase.Disconnected, "",
        "Sin conexión. Prepara un código USB o QR LAN en PC.", null);
    public NLControlSessionState Current { get { lock (_gate) return _current; } }
    public event EventHandler<NLControlSessionState>? Changed;

    public Task ConnectUsbAsync(string code, int port = NLControlProtocol.Port) =>
        ConnectAsync("USB", client => client.ConnectAsync(code, port));

    public Task ConnectLanAsync(NLControlLanInvitation invitation) =>
        ConnectAsync("LAN", client => client.ConnectLanAsync(invitation));

    public Task ConnectTrustedAsync(NLControlTrustedPc pc, bool allowLoopback = false) =>
        ConnectAsync("LAN", client => client.ConnectTrustedAsync(pc, allowLoopback));

    private async Task ConnectAsync(string transport, Func<NLControlClient, Task<NLControlReply>> connect)
    {
        NLControlClient? previous;
        NLControlClient client;
        lock (_gate)
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(NLControlSession)); }
            client = new NLControlClient();
            previous = _client;
            _client = client;
            _current = new(_current.Generation + 1, NLControlSessionPhase.Connecting, transport,
                "Verificando conexión con NOVORA PC…", null);
        }
        client.StateChanged += (_, snapshot) =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_client, client) || _current.Phase is not (NLControlSessionPhase.Connected or NLControlSessionPhase.Connecting)) return;
                if (_current.Snapshot is { } old && snapshot.Revision < old.Revision) return;
                _current = _current with { Snapshot = snapshot };
            }
            Notify();
        };
        client.Disconnected += (_, message) =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_client, client)) return;
                _client = null;
                _current = new(_current.Generation + 1, NLControlSessionPhase.Lost, transport,
                    message, null);
            }
            Notify();
            // A receiver callback must not await disposal of its own receive loop.
            _ = DisposeClientAsync(client);
        };
        Notify();
        try
        {
            if (previous is not null) await DisposeClientAsync(previous);
            lock (_gate)
                if (!ReferenceEquals(_client, client)) throw new OperationCanceledException("La conexión fue cancelada.");
            NLControlReply reply = await connect(client);
            lock (_gate)
            {
                if (!ReferenceEquals(_client, client)) throw new OperationCanceledException("La sesión cambió.");
                if (!reply.Success || reply.Snapshot is null) throw new IOException("PC no confirmó el enlace. Prepara una invitación nueva.");
                _current = _current with { Phase = NLControlSessionPhase.Connected, Message = reply.Message, Snapshot = _current.Snapshot is { } pending && pending.Revision > reply.Snapshot.Revision ? pending : reply.Snapshot };
            }
            Notify();
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_client, client))
                {
                    _client = null;
                    _current = new(_current.Generation + 1, NLControlSessionPhase.Lost, transport,
                        "No se confirmó el enlace. Prepara un nuevo código o QR en PC.", null);
                }
            }
            Notify();
            await DisposeClientAsync(client);
            throw;
        }
    }

    public async Task<NLControlReply> SendAsync(string action, string? value = null)
    {
        NLControlClient client;
        long revision;
        lock (_gate)
        {
            if (_client is null || _current.Phase != NLControlSessionPhase.Connected || _current.Snapshot is null)
                throw new InvalidOperationException("Sin sesión confirmada.");
            if (_current.Busy) throw new InvalidOperationException("Hay una orden pendiente.");
            client = _client;
            revision = _current.Snapshot.Revision;
            _current = _current with { Busy = true };
        }
        Notify();
        try
        {
            var reply = await client.SendAsync(action, value, revision);
            lock (_gate)
            {
                if (!ReferenceEquals(_client, client)) throw new OperationCanceledException("La sesión cambió durante la orden.");
                _current = _current with { Message = reply.Message, Busy = false };
            }
            Notify();
            return reply;
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_client, client))
                {
                    _client = null;
                    _current = new(_current.Generation + 1, NLControlSessionPhase.Lost, _current.Transport,
                        "Orden sin confirmación. No se repetirá automáticamente; revisa PC y vuelve a enlazar.", null);
                }
            }
            Notify();
            await DisposeClientAsync(client);
            throw;
        }
    }

    public Task DisconnectAsync(string message = "Desconectado por el usuario.") =>
        CloseAsync(NLControlSessionPhase.Disconnected, message);

    public Task LoseAsync(string message) => CloseAsync(NLControlSessionPhase.Lost, message);

    private async Task CloseAsync(NLControlSessionPhase phase, string message)
    {
        NLControlClient? client;
        lock (_gate)
        {
            client = _client;
            _client = null;
            _current = new(_current.Generation + 1, phase, _current.Transport, message, null);
        }
        Notify();
        if (client is not null) await DisposeClientAsync(client);
    }

    private void Notify() => Changed?.Invoke(this, Current);

    private static async Task DisposeClientAsync(NLControlClient client)
    {
        try { await client.DisposeAsync(); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        await DisconnectAsync("Servicio de NOVORA detenido.");
    }
}
