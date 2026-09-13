using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Net;
using Android.OS;
using Java.IO;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public sealed class TunnelNetworkLE :
    IAsyncDisposable
{
    private readonly VpnService _vpnService;
    private readonly ParcelFileDescriptor _vpnInterface;

    private readonly FileInputStream _vpnInput;
    private readonly FileOutputStream _vpnOutput;

    private readonly DataTransportLE _data =
        new();

    private CancellationTokenSource? _sessionCts;

    private bool _disposed;

    public int? RelayClientIdLE =>
        _data.RelayClientIdLE;

    public TunnelNetworkLE(
        VpnService vpnService,
        ParcelFileDescriptor vpnInterface)
    {
        _vpnService =
            vpnService ??
            throw new ArgumentNullException(
                nameof(vpnService));

        _vpnInterface =
            vpnInterface ??
            throw new ArgumentNullException(
                nameof(vpnInterface));

        _vpnInput =
            new FileInputStream(
                _vpnInterface.FileDescriptor);

        _vpnOutput =
            new FileOutputStream(
                _vpnInterface.FileDescriptor);
    }

    public async Task RunAsync(
        Action<int> onConnected,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposedLE();

        ArgumentNullException.ThrowIfNull(
            onConnected);

        await _data
            .ConnectAsync(
                _vpnService,
                TimeSpan.FromSeconds(30),
                cancellationToken)
            .ConfigureAwait(false);

        int relayId =
            _data.RelayClientIdLE ??
            throw new InvalidOperationException(
                "Relay DATA no entregÃ³ ClientId.");

        onConnected(
            relayId);

        _sessionCts =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        CancellationToken token =
            _sessionCts.Token;

        Task deviceToRelay =
            Task.Run(
                () =>
                    DeviceToRelayLE(
                        token),
                CancellationToken.None);

        Task relayToDevice =
            Task.Run(
                () =>
                    RelayToDeviceLE(
                        token),
                CancellationToken.None);

        Task completed =
            await Task
                .WhenAny(
                    deviceToRelay,
                    relayToDevice)
                .ConfigureAwait(false);

        try
        {
            _sessionCts.Cancel();
        }
        catch
        {
        }

        _data.CloseLE();

        try
        {
            _vpnInterface.Close();
        }
        catch
        {
        }

        await completed
            .ConfigureAwait(false);
    }

    private void DeviceToRelayLE(
        CancellationToken cancellationToken)
    {
        byte[] packet =
            new byte[
                PacketNetworkLE.MaximumIpv4PacketLengthLE
            ];

        while (!cancellationToken.IsCancellationRequested)
        {
            int read =
                _vpnInput.Read(
                    packet,
                    0,
                    packet.Length);

            if (read < 0)
            {
                throw new EndOfStreamException(
                    "TUN Android cerrado.");
            }

            if (read == 0)
            {
                continue;
            }

            if (!PacketNetworkLE.IsIpv4LE(
                    packet,
                    read))
            {
                /*
                 * LE-006 first playable:
                 * IPv4 only.
                 */
                continue;
            }

            _data.SendPacketLE(
                packet,
                read);
        }
    }

    private void RelayToDeviceLE(
        CancellationToken cancellationToken)
    {
        byte[] packet =
            new byte[
                PacketNetworkLE.MaximumIpv4PacketLengthLE
            ];

        while (!cancellationToken.IsCancellationRequested)
        {
            int length =
                _data.ReceivePacketLE(
                    packet);

            if (length <= 0)
            {
                throw new EndOfStreamException(
                    "Relay DATA cerrado.");
            }

            /*
             * FileOutputStream escribe directamente sobre el
             * descriptor TUN.
             *
             * No hacemos Flush() por paquete.
             *
             * El Flush individual:
             *
             * - no define framing;
             * - no es necesario para el TUN;
             * - añade trabajo a cada paquete;
             * - aumenta jitter bajo carga.
             */
            _vpnOutput.Write(
                packet,
                0,
                length);
        }
    }

    public Task StopAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        try
        {
            _sessionCts?.Cancel();
        }
        catch
        {
        }

        _data.CloseLE();

        try
        {
            _vpnInterface.Close();
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private void ThrowIfDisposedLE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync()
            .ConfigureAwait(false);

        try
        {
            _vpnInput.Close();
        }
        catch
        {
        }

        try
        {
            _vpnOutput.Close();
        }
        catch
        {
        }

        try
        {
            _vpnInterface.Close();
        }
        catch
        {
        }

        _data.Dispose();

        _sessionCts?.Dispose();

        _disposed =
            true;
    }
}
