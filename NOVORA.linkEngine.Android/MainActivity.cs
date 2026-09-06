using System;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Views;
using Android.Widget;
using NOVORA.LinkEngine.Android.LinkEngine;

namespace NOVORA.LinkEngine.Android;

[Activity(
    Label = "NOVORA LinkEngine",
    MainLauncher = true,
    Exported = true)]
public sealed class MainActivity :
    Activity
{
    private const int VpnRequestCodeLE =
        27184;

    private TextView? _stateTextLE;
    private TextView? _controlTextLE;
    private TextView? _vpnTextLE;
    private TextView? _dataTextLE;
    private TextView? _messageTextLE;

    protected override void OnCreate(
        Bundle? savedInstanceState)
    {
        base.OnCreate(
            savedInstanceState);

        BuildInterfaceLE();

        VpnNetworkLE.StatusChangedLE +=
            Vpn_StatusChangedLE;

        UpdateInterfaceLE(
            VpnNetworkLE.StatusLE);
    }

    private void BuildInterfaceLE()
    {
        var root =
            new LinearLayout(this)
            {
                Orientation =
                    Orientation.Vertical
            };

        root.SetGravity(
            GravityFlags.Center);

        root.SetPadding(
            DpLE(28),
            DpLE(36),
            DpLE(28),
            DpLE(36));

        var title =
            new TextView(this)
            {
                Text =
                    "NOVORA",
                TextSize =
                    34f,
                Gravity =
                    GravityFlags.Center
            };

        var subtitle =
            new TextView(this)
            {
                Text =
                    "LINKENGINE · LE-006",
                TextSize =
                    18f,
                Gravity =
                    GravityFlags.Center
            };

        _stateTextLE =
            new TextView(this)
            {
                Text =
                    "STOPPED",
                TextSize =
                    22f,
                Gravity =
                    GravityFlags.Center
            };

        _controlTextLE =
            new TextView(this)
            {
                Text =
                    "CONTROL 27183: OFFLINE",
                TextSize =
                    15f,
                Gravity =
                    GravityFlags.Center
            };

        _vpnTextLE =
            new TextView(this)
            {
                Text =
                    "VPN: OFFLINE",
                TextSize =
                    15f,
                Gravity =
                    GravityFlags.Center
            };

        _dataTextLE =
            new TextView(this)
            {
                Text =
                    "DATA 27184: OFFLINE",
                TextSize =
                    15f,
                Gravity =
                    GravityFlags.Center
            };

        _messageTextLE =
            new TextView(this)
            {
                Text =
                    "Primero START LINKENGINE en Windows.",
                TextSize =
                    13f,
                Gravity =
                    GravityFlags.Center
            };

        var start =
            new Button(this)
            {
                Text =
                    "START LINKENGINE VPN"
            };

        var stop =
            new Button(this)
            {
                Text =
                    "STOP LINKENGINE VPN"
            };

        start.Click +=
            (_, _) =>
                RequestVpnLE();

        stop.Click +=
            (_, _) =>
                VpnNetworkLE.StopLE(
                    this);

        subtitle.SetPadding(
            0,
            0,
            0,
            DpLE(28));

        _stateTextLE.SetPadding(
            0,
            0,
            0,
            DpLE(22));

        _controlTextLE.SetPadding(
            0,
            0,
            0,
            DpLE(8));

        _vpnTextLE.SetPadding(
            0,
            0,
            0,
            DpLE(8));

        _dataTextLE.SetPadding(
            0,
            0,
            0,
            DpLE(20));

        _messageTextLE.SetPadding(
            0,
            0,
            0,
            DpLE(22));

        root.AddView(
            title);

        root.AddView(
            subtitle);

        root.AddView(
            _stateTextLE);

        root.AddView(
            _controlTextLE);

        root.AddView(
            _vpnTextLE);

        root.AddView(
            _dataTextLE);

        root.AddView(
            _messageTextLE);

        root.AddView(
            start);

        root.AddView(
            stop);

        SetContentView(
            root);
    }

    private void RequestVpnLE()
    {
        UpdateMessageLE(
            "Solicitando permiso VPN...");

        Intent? vpnIntent =
            VpnService.Prepare(
                this);

        if (vpnIntent is null)
        {
            VpnNetworkLE.StartLE(
                this);

            return;
        }

#pragma warning disable CS0618
        StartActivityForResult(
            vpnIntent,
            VpnRequestCodeLE);
#pragma warning restore CS0618
    }

    protected override void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        base.OnActivityResult(
            requestCode,
            resultCode,
            data);

        if (requestCode !=
            VpnRequestCodeLE)
        {
            return;
        }

        if (resultCode ==
            Result.Ok)
        {
            VpnNetworkLE.StartLE(
                this);

            return;
        }

        UpdateMessageLE(
            "VPN permission DENIED.");
    }

    private void Vpn_StatusChangedLE(
        object? sender,
        StatusNetworkLE status)
    {
        RunOnUiThread(
            () =>
                UpdateInterfaceLE(
                    status));
    }

    private void UpdateInterfaceLE(
        StatusNetworkLE status)
    {
        if (_stateTextLE is not null)
        {
            _stateTextLE.Text =
                status.State
                    .ToString()
                    .ToUpperInvariant();
        }

        if (_controlTextLE is not null)
        {
            _controlTextLE.Text =
                status.HandshakeVerified
                    ? "CONTROL 27183: VERIFIED"
                    : status.ControlConnected
                        ? "CONTROL 27183: CONNECTED"
                        : "CONTROL 27183: WAITING";
        }

        if (_vpnTextLE is not null)
        {
            _vpnTextLE.Text =
                status.VpnActive
                    ? "VPN: ACTIVE"
                    : "VPN: OFFLINE";
        }

        if (_dataTextLE is not null)
        {
            _dataTextLE.Text =
                status.DataConnected
                    ? $"DATA 27184: ONLINE · RELAY #{status.RelayClientId}"
                    : "DATA 27184: WAITING";
        }

        UpdateMessageLE(
            status.Message);
    }

    private void UpdateMessageLE(
        string message)
    {
        if (_messageTextLE is not null)
        {
            _messageTextLE.Text =
                message;
        }
    }

    private int DpLE(
        int value)
    {
        float density =
            Resources
                ?.DisplayMetrics
                ?.Density ??
            1f;

        return
            (int)(
                value *
                density);
    }

    protected override void OnDestroy()
    {
        VpnNetworkLE.StatusChangedLE -=
            Vpn_StatusChangedLE;

        /*
         * DO NOT STOP SERVICE.
         *
         * The game may now become foreground while LinkEngine
         * CONTROL/VPN/DATA remain alive.
         */

        base.OnDestroy();
    }
}