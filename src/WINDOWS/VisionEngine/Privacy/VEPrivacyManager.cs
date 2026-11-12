using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Compuerta central de privacidad de VisionEngine.
///
/// Contrato combinado:
///
/// - Event-driven VE nuevo.
/// - Privacy/Integration avanzado.
///
/// No usa polling.
/// No guarda historial sensible.
/// No lee passwords.
/// No inspecciona URLs.
/// No intenta evadir FLAG_SECURE.
/// </summary>
public sealed class VEPrivacyManager
{
    private readonly object _gateVE =
        new();

    private bool _systemSecureVE;

    private bool _secureInputVE;

    private bool _manualShieldVE;

    private string? _foregroundPackageVE;

    private VEPrivacyStatus _statusVE =
        VEPrivacyStatus.CreateInitialVE();

    private VEPrivacyContext _contextVE =
        VEPrivacyContext.CreateDefaultVE();

    private VEPrivacySession? _sessionVE;

    public VEPrivacyManager(
        VEPrivacyPolicy? policy = null)
    {
        PolicyVE =
            policy ??
            new VEPrivacyPolicy();

        SensitiveAppsVE =
            new VEPrivacySensitiveApp(
                this);
    }

    public event EventHandler<VEPrivacyStatus>?
        StatusChangedVE;

    public VEPrivacyPolicy PolicyVE { get; }

    public VEPrivacySensitiveApp SensitiveAppsVE { get; }

    public VEPrivacyStatus StatusVE
    {
        get
        {
            lock (_gateVE)
            {
                return _statusVE;
            }
        }
    }

    public VEPrivacyContext ContextVE
    {
        get
        {
            lock (_gateVE)
            {
                return _contextVE;
            }
        }
    }

    public VEPrivacySession? SessionVE
    {
        get
        {
            lock (_gateVE)
            {
                return _sessionVE;
            }
        }
    }

    public bool IsProtectedVE =>
        StatusVE.IsProtectedVE;

    public bool CanExposeVideoVE =>
        !IsProtectedVE;

    public bool CanExposeAudioVE =>
        !IsProtectedVE;

    public bool CanUseClipboardVE =>
        !IsProtectedVE;

    public bool CanExchangeFilesVE =>
        !IsProtectedVE;

    public bool CanIntegrateVE =>
        !IsProtectedVE;

    public bool CanRecordVE =>
        !IsProtectedVE;

    public bool CanSendControlVE(
        VEControlType type)
        =>
            VEPrivacyPolicy.CanSendControlVE(
                IsProtectedVE,
                type);

    public void BeginSessionVE()
    {
        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            _sessionVE =
                VEPrivacySession.CreateVE();

            _systemSecureVE =
                false;

            _secureInputVE =
                false;

            _manualShieldVE =
                false;

            _foregroundPackageVE =
                null;

            _contextVE =
                VEPrivacyContext.CreateDefaultVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void EndSessionVE()
    {
        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            _sessionVE =
                null;

            _systemSecureVE =
                false;

            _secureInputVE =
                false;

            _manualShieldVE =
                false;

            _foregroundPackageVE =
                null;

            _contextVE =
                VEPrivacyContext.CreateDefaultVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void SetSystemSecureVE(
        bool value)
    {
        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            _systemSecureVE =
                value;

            UpdateContextLockedVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void SetSecureInputVE(
        bool value)
    {
        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            _secureInputVE =
                value;

            UpdateContextLockedVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void SetManualShieldVE(
        bool value)
    {
        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            _manualShieldVE =
                value;

            UpdateContextLockedVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void SetForegroundPackageVE(
        string? packageName)
    {
        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            _foregroundPackageVE =
                string.IsNullOrWhiteSpace(
                    packageName)
                    ? null
                    : packageName.Trim();

            UpdateContextLockedVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void SetSensitivePackagesVE(
        IEnumerable<string> packages)
    {
        ArgumentNullException.ThrowIfNull(
            packages);

        string[] normalized =
            packages
                .Where(
                    static value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    static value =>
                        value.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        PolicyVE.SetSensitivePackagesVE(
            normalized);

        VEPrivacyStatus? changed;

        lock (_gateVE)
        {
            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    private void UpdateContextLockedVE()
    {
        _contextVE =
            new VEPrivacyContext(
                _systemSecureVE,
                _secureInputVE,
                _foregroundPackageVE,
                _manualShieldVE);
    }

    private VEPrivacyStatus?
        ReevaluateLockedVE()
    {
        VEPrivacyClassification classification =
            VEPrivacyClassification.Normal;

        if (_systemSecureVE)
        {
            classification =
                VEPrivacyClassification.SystemSecure;
        }
        else if (_secureInputVE)
        {
            classification =
                VEPrivacyClassification.SecureInput;
        }
        else if (_manualShieldVE)
        {
            classification =
                VEPrivacyClassification.ManualShield;
        }
        else if (
            _foregroundPackageVE is not null &&
            PolicyVE.IsSensitivePackageVE(
                _foregroundPackageVE)
        ) {
            classification =
                VEPrivacyClassification
                    .SensitiveApplication;
        }

        VEPrivacyStates state =
            classification ==
                VEPrivacyClassification.Normal
                ? VEPrivacyStates.Normal
                : VEPrivacyStates.Protected;

        if (
            _statusVE.State ==
                state &&
            _statusVE.Classification ==
                classification
        ) {
            return null;
        }

        _statusVE =
            new VEPrivacyStatus(
                state,
                classification,
                DateTimeOffset.UtcNow,
                state ==
                    VEPrivacyStates.Normal
                    ? "Contenido normal."
                    : "Contenido protegido.");

        return _statusVE;
    }

    private void PublishVE(
        VEPrivacyStatus? status)
    {
        if (status is null)
        {
            return;
        }

        StatusChangedVE?.Invoke(
            this,
            status);
    }
}