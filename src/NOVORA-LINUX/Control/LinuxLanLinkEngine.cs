using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Linux.Control;

/// <summary>
/// LinkEngine over the authorized LAN session. Android receives the offer and
/// opens a pinned TLS connection to this gateway; the gateway authenticates
/// DATA against the native relay. No ADB reverse is involved.
/// </summary>
internal sealed class LinuxLanLinkEngine : ILinuxLinkEngine
{
    private const int DataPort = 27184;
    private readonly IPAddress _address;
    private readonly LinuxTrustStore _store;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private LinuxLinkRelay? _relay;
    private LinuxLanLinkGateway? _gateway;
    private LanLinkOffer? _offer;
    private string _state = "Stopped";
    private string _message = "LinkEngine LAN detenido.";
    private bool _stopping;

    public event EventHandler? StateChanged;

    public LinuxLanLinkEngine(IPAddress address, LinuxTrustStore store)
    {
        if (!LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("LinkEngine LAN requiere una dirección IPv4 privada.");
        _address = address;
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool IsStarted => Volatile.Read(ref _state) is not "Stopped";
    public bool IsConnected => _gateway?.IsConnected == true;
    public string State => Volatile.Read(ref _state);
    public string Message => Volatile.Read(ref _message);

    public async Task<string> StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsStarted)
            {
                if (State == "Error")
                    throw new InvalidOperationException(Message);
                return JsonSerializer.Serialize(_offer
                    ?? throw new InvalidOperationException("La sesión LinkEngine LAN no tiene oferta."));
            }

            _stopping = false;
            var relay = new LinuxLinkRelay();
            LinuxLanLinkGateway? gateway = null;
            try
            {
                string relayToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                await relay.StartAsync(relayToken, cancellationToken);
                relay.ExitedUnexpectedly += OnRelayExited;

                gateway = new LinuxLanLinkGateway(_address, _store, DataPort, relayToken);
                gateway.StateChanged += OnGatewayStateChanged;
                LanLinkOffer offer = gateway.Start();

                _relay = relay;
                _gateway = gateway;
                _offer = offer;
                relay = null;
                gateway = null;
                SetState("Waiting",
                    $"LinkEngine LAN preparado en {offer.Host}:{offer.Port}; esperando la VPN de Android.");
                return JsonSerializer.Serialize(offer);
            }
            catch
            {
                if (gateway is not null)
                {
                    gateway.StateChanged -= OnGatewayStateChanged;
                    await gateway.DisposeAsync();
                }
                if (relay is not null)
                {
                    relay.ExitedUnexpectedly -= OnRelayExited;
                    await relay.DisposeAsync();
                }
                _offer = null;
                SetState("Stopped", "LinkEngine LAN no pudo iniciarse.");
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (!IsStarted && _relay is null && _gateway is null)
                return;
            _stopping = true;
            LinuxLanLinkGateway? gateway = _gateway;
            LinuxLinkRelay? relay = _relay;
            _gateway = null;
            _relay = null;
            _offer = null;
            if (gateway is not null)
            {
                gateway.StateChanged -= OnGatewayStateChanged;
                await gateway.DisposeAsync();
            }
            if (relay is not null)
            {
                relay.ExitedUnexpectedly -= OnRelayExited;
                await relay.DisposeAsync();
            }
            SetState("Stopped", "LinkEngine LAN detenido.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void OnGatewayStateChanged(object? sender, EventArgs e)
    {
        if (IsConnected)
            SetState("Connected", "Canal DATA de LinkEngine conectado por LAN.");
        else if (!_stopping)
            SetState("Waiting", "Canal DATA LAN cerrado; LinkEngine espera la reconexión de Android.");
    }

    private void OnRelayExited(object? sender, string message)
    {
        if (_stopping)
            return;
        SetState("Error", message);
    }

    private void SetState(string state, string message)
    {
        Volatile.Write(ref _state, state);
        Volatile.Write(ref _message, message);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
