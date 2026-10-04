using System.Text.Json;
using NOVORA.Control;

namespace NOVORA.LinkClient;

internal sealed record NovoraVeStartDecision(
    bool Success,
    bool RequestProjection,
    bool StopRequired,
    string Message,
    NLControlVeLanOffer? Offer = null);

/// <summary>Keeps transport choice explicit and coordinates the short-lived LAN projection handshake.</summary>
internal sealed class NovoraVeCaptureCoordinator
{
    private readonly Func<string, string?, Task<NLControlReply>> _send;
    private readonly bool _allowLoopback;
    private bool _lanPreparationPending;

    internal NovoraVeCaptureCoordinator(
        Func<string, string?, Task<NLControlReply>> send,
        bool allowLoopback = false)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _allowLoopback = allowLoopback;
    }

    internal async Task<NovoraVeStartDecision> BeginAsync(
        NovoraVeTransportSelection selection,
        NLControlSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase != NLControlSessionPhase.Connected || state.Snapshot is null || state.Busy)
            return new(false, false, false, "NOVORA PC no está disponible para iniciar VisionEngine.");
        if (state.Snapshot.VideoRunning || state.Snapshot.Engines?.VideoCanStop == true)
            return new(false, false, true,
                "Detén VisionEngine antes de cambiar o volver a iniciar el transporte.");

        _lanPreparationPending = false;
        if (selection == NovoraVeTransportSelection.Usb)
        {
            NLControlReply usbReply = await _send("startVideo", null).ConfigureAwait(false);
            return new(usbReply.Success, false, false, usbReply.Message);
        }

        NLControlReply lanReply = await _send("startVideoLan", null).ConfigureAwait(false);
        if (!lanReply.Success)
            return new(false, false, false, lanReply.Message);
        _lanPreparationPending = true;
        try
        {
            NLControlVeLanOffer offer = string.IsNullOrWhiteSpace(lanReply.Value)
                ? throw new InvalidDataException("NOVORA PC no entregó la oferta VE LAN.")
                : JsonSerializer.Deserialize<NLControlVeLanOffer>(lanReply.Value)
                    ?? throw new InvalidDataException("La oferta VE LAN llegó vacía.");
            offer.Validate(DateTimeOffset.UtcNow, _allowLoopback);
            return new(true, true, false, lanReply.Message, offer);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException)
        {
            await StopPendingLanAsync().ConfigureAwait(false);
            return new(false, false, false, ex.Message);
        }
    }

    internal async Task ProjectionDeniedAsync() => await StopPendingLanAsync().ConfigureAwait(false);

    internal void ProjectionStarted() => _lanPreparationPending = false;

    private async Task StopPendingLanAsync()
    {
        if (!_lanPreparationPending) return;
        _lanPreparationPending = false;
        await _send("stopVideo", null).ConfigureAwait(false);
    }
}
