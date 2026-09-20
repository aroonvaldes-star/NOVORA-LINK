using System;

namespace NOVORA.LinkEngine.Transport;

public sealed record LETransportSession(
    string Serial,
    int DevicePort,
    int HostPort,
    LETransportState State,
    bool ReverseConfigured,
    bool ReverseVerified,
    bool ListenerStarted,
    bool ClientConnected,
    bool HandshakeVerified,
    string? ClientId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastVerifiedAtUtc,
    DateTimeOffset? ConnectedAtUtc,
    string? LastError,
    bool SessionHealthy = false,
    long HeartbeatSequence = 0,
    long HeartbeatCount = 0,
    DateTimeOffset? LastHeartbeatAtUtc = null,
    DateTimeOffset? LastHeartbeatAckAtUtc = null,
    double? LastRoundTripMilliseconds = null);
