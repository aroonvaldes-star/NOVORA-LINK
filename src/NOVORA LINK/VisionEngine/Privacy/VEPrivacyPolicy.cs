using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Política de PrivacyVE.
///
/// Une:
///
/// - política de ControlVE del núcleo nuevo;
/// - política de paquetes sensibles de IntegrationVE avanzado.
///
/// No usa polling.
/// No inspecciona Accessibility.
/// No persiste identidad ni contenido sensible.
/// </summary>
public sealed class VEPrivacyPolicy
{
    private readonly object _gateVE =
        new();

    private readonly HashSet<string>
        _sensitivePackagesVE =
            new(
                StringComparer.OrdinalIgnoreCase);

    public static bool CanSendControlVE(
        bool protectedVE,
        VEControlType type)
    {
        if (!protectedVE)
        {
            return true;
        }

        /*
         * Mensajes internos necesarios para liberar/administrar
         * el estado de una sesión protegida.
         *
         * El gamepad se neutraliza antes de bloquear su input real.
         */
        return type is
            VEControlType.ResetVideo or
            VEControlType.UhidCreate or
            VEControlType.UhidInput or
            VEControlType.UhidDestroy;
    }

    public void SetSensitivePackagesVE(
        IEnumerable<string> packageNames)
    {
        ArgumentNullException.ThrowIfNull(
            packageNames);

        string[] normalized =
            packageNames
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

        lock (_gateVE)
        {
            _sensitivePackagesVE.Clear();

            foreach (
                string packageName in normalized)
            {
                _sensitivePackagesVE.Add(
                    packageName);
            }
        }
    }

    public bool IsSensitivePackageVE(
        string? packageName)
    {
        if (
            string.IsNullOrWhiteSpace(
                packageName)
        ) {
            return false;
        }

        lock (_gateVE)
        {
            return
                _sensitivePackagesVE.Contains(
                    packageName.Trim());
        }
    }

    public VEPrivacyStatus EvaluateVE(
        VEPrivacyContext context)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        if (context.SystemSecure)
        {
            return
                VEPrivacyStatus.ProtectedVE(
                    VEPrivacyClassification.SystemSecure,
                    "Contenido protegido por Android.");
        }

        if (context.SecureInput)
        {
            return
                VEPrivacyStatus.ProtectedVE(
                    VEPrivacyClassification.SecureInput,
                    "Entrada sensible protegida.");
        }

        if (context.ManualShield)
        {
            return
                VEPrivacyStatus.ProtectedVE(
                    VEPrivacyClassification.ManualShield,
                    "Privacy Shield activado manualmente.");
        }

        if (
            IsSensitivePackageVE(
                context.ForegroundPackage)
        ) {
            return
                VEPrivacyStatus.ProtectedVE(
                    VEPrivacyClassification.SensitiveApplication,
                    "Aplicacion marcada como sensible.");
        }

        return
            VEPrivacyStatus.NormalVE();
    }
}