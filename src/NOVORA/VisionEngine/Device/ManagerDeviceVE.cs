using NOVORA.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Descubre y valida el Android que utilizará VisionEngine.
/// Reutiliza AdbService: VisionEngine no crea un segundo mecanismo de polling ADB.
/// </summary>
public sealed partial class ManagerDeviceVE
{
    private readonly AdbService _adb;
    private readonly object _statusGate = new();

    private StatusDeviceVE _status =
        StatusDeviceVE.CreateInitialVE();

    public ManagerDeviceVE(AdbService adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    }

    public event EventHandler<StatusDeviceVE>? StatusChangedVE;

    public StatusDeviceVE StatusVE
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    public async Task<SessionDeviceVE> OpenAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        string normalizedSerial = serial.Trim();

        PublishStatusVE(
            new StatusDeviceVE(
                State: StatesDeviceVE.Checking,
                Serial: normalizedSerial,
                Capabilities: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                Message: "Validando dispositivo para VisionEngine.",
                LastError: null));

        try
        {
            await _adb.StartServerAsync(cancellationToken)
                .ConfigureAwait(false);

            bool online = await _adb.IsDeviceOnlineAsync(
                    normalizedSerial,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!online)
            {
                throw new InvalidOperationException(
                    $"El dispositivo '{normalizedSerial}' no está online o no está autorizado por ADB.");
            }

            string manufacturer = await TryShellAsync(
                    normalizedSerial,
                    "getprop ro.product.manufacturer",
                    cancellationToken)
                .ConfigureAwait(false);

            string model = await TryShellAsync(
                    normalizedSerial,
                    "getprop ro.product.model",
                    cancellationToken)
                .ConfigureAwait(false);

            string sdkText = await TryShellAsync(
                    normalizedSerial,
                    "getprop ro.build.version.sdk",
                    cancellationToken)
                .ConfigureAwait(false);

            string abi = await TryShellAsync(
                    normalizedSerial,
                    "getprop ro.product.cpu.abi",
                    cancellationToken)
                .ConfigureAwait(false);

            string wmSize = await TryShellAsync(
                    normalizedSerial,
                    "wm size",
                    cancellationToken)
                .ConfigureAwait(false);

            int? sdk = int.TryParse(
                sdkText.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedSdk)
                    ? parsedSdk
                    : null;

            (int? width, int? height) =
                ParsePhysicalSizeVE(wmSize);

            CapabilitiesDeviceVE capabilities =
                new(
                    Manufacturer: manufacturer.Trim(),
                    Model: model.Trim(),
                    AndroidSdk: sdk,
                    Abi: abi.Trim(),
                    PhysicalWidth: width,
                    PhysicalHeight: height);

            SessionDeviceVE session =
                new(
                    Serial: normalizedSerial,
                    Capabilities: capabilities,
                    ConnectedAtUtc: DateTimeOffset.UtcNow);

            PublishStatusVE(
                new StatusDeviceVE(
                    State: StatesDeviceVE.Ready,
                    Serial: normalizedSerial,
                    Capabilities: capabilities,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Message: $"{capabilities.DisplayNameVE} listo para VisionEngine.",
                    LastError: null));

            return session;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PublishStatusVE(
                new StatusDeviceVE(
                    State: StatesDeviceVE.Failed,
                    Serial: normalizedSerial,
                    Capabilities: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Message: "No fue posible preparar el dispositivo para VisionEngine.",
                    LastError: ex.Message));

            throw;
        }
    }

    public void CloseVE()
    {
        PublishStatusVE(
            StatusDeviceVE.CreateInitialVE());
    }

    private async Task<string> TryShellAsync(
        string serial,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adb.ShellAsync(
                    serial,
                    command,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (int? Width, int? Height) ParsePhysicalSizeVE(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        Match match = PhysicalSizeRegexVE().Match(value);
        if (!match.Success)
        {
            return (null, null);
        }

        bool hasWidth = int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int width);

        bool hasHeight = int.TryParse(
            match.Groups[2].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int height);

        return hasWidth && hasHeight && width > 0 && height > 0
            ? (width, height)
            : (null, null);
    }

    private void PublishStatusVE(StatusDeviceVE status)
    {
        EventHandler<StatusDeviceVE>? handler;

        lock (_statusGate)
        {
            _status = status;
            handler = StatusChangedVE;
        }

        handler?.Invoke(this, status);
    }

    [GeneratedRegex(
        @"Physical\s+size:\s*(\d+)x(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhysicalSizeRegexVE();
}
