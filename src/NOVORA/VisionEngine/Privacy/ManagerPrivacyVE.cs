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
public sealed class ManagerPrivacyVE
{
    private readonly object _gateVE =
        new();

    private bool _systemSecureVE;

    private bool _secureInputVE;

    private bool _manualShieldVE;

    private string? _foregroundPackageVE;

    private StatusPrivacyVE _statusVE =
        StatusPrivacyVE.CreateInitialVE();

    private ContextPrivacyVE _contextVE =
        ContextPrivacyVE.CreateDefaultVE();

    private SessionPrivacyVE? _sessionVE;

    public ManagerPrivacyVE(
        PolicyPrivacyVE? policy = null)
    {
        PolicyVE =
            policy ??
            new PolicyPrivacyVE();

        SensitiveAppsVE =
            new SensitiveAppPrivacyVE(
                this);
    }

    public event EventHandler<StatusPrivacyVE>?
        StatusChangedVE;

    public PolicyPrivacyVE PolicyVE { get; }

    public SensitiveAppPrivacyVE SensitiveAppsVE { get; }

    public StatusPrivacyVE StatusVE
    {
        get
        {
            lock (_gateVE)
            {
                return _statusVE;
            }
        }
    }

    public ContextPrivacyVE ContextVE
    {
        get
        {
            lock (_gateVE)
            {
                return _contextVE;
            }
        }
    }

    public SessionPrivacyVE? SessionVE
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
        TypeControlVE type)
        =>
            PolicyPrivacyVE.CanSendControlVE(
                IsProtectedVE,
                type);

    public void BeginSessionVE()
    {
        StatusPrivacyVE? changed;

        lock (_gateVE)
        {
            _sessionVE =
                SessionPrivacyVE.CreateVE();

            _systemSecureVE =
                false;

            _secureInputVE =
                false;

            _manualShieldVE =
                false;

            _foregroundPackageVE =
                null;

            _contextVE =
                ContextPrivacyVE.CreateDefaultVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void EndSessionVE()
    {
        StatusPrivacyVE? changed;

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
                ContextPrivacyVE.CreateDefaultVE();

            changed =
                ReevaluateLockedVE();
        }

        PublishVE(
            changed);
    }

    public void SetSystemSecureVE(
        bool value)
    {
        StatusPrivacyVE? changed;

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
        StatusPrivacyVE? changed;

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
        StatusPrivacyVE? changed;

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
        StatusPrivacyVE? changed;

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

        StatusPrivacyVE? changed;

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
            new ContextPrivacyVE(
                _systemSecureVE,
                _secureInputVE,
                _foregroundPackageVE,
                _manualShieldVE);
    }

    private StatusPrivacyVE?
        ReevaluateLockedVE()
    {
        ClassificationPrivacyVE classification =
            ClassificationPrivacyVE.Normal;

        if (_systemSecureVE)
        {
            classification =
                ClassificationPrivacyVE.SystemSecure;
        }
        else if (_secureInputVE)
        {
            classification =
                ClassificationPrivacyVE.SecureInput;
        }
        else if (_manualShieldVE)
        {
            classification =
                ClassificationPrivacyVE.ManualShield;
        }
        else if (
            _foregroundPackageVE is not null &&
            PolicyVE.IsSensitivePackageVE(
                _foregroundPackageVE)
        ) {
            classification =
                ClassificationPrivacyVE
                    .SensitiveApplication;
        }

        StatesPrivacyVE state =
            classification ==
                ClassificationPrivacyVE.Normal
                ? StatesPrivacyVE.Normal
                : StatesPrivacyVE.Protected;

        if (
            _statusVE.State ==
                state &&
            _statusVE.Classification ==
                classification
        ) {
            return null;
        }

        _statusVE =
            new StatusPrivacyVE(
                state,
                classification,
                DateTimeOffset.UtcNow,
                state ==
                    StatesPrivacyVE.Normal
                    ? "Contenido normal."
                    : "Contenido protegido.");

        return _statusVE;
    }

    private void PublishVE(
        StatusPrivacyVE? status)
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