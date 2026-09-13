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
public sealed class PolicyPrivacyVE
{
    private readonly object _gateVE =
        new();

    private readonly HashSet<string>
        _sensitivePackagesVE =
            new(
                StringComparer.OrdinalIgnoreCase);

    public static bool CanSendControlVE(
        bool protectedVE,
        TypeControlVE type)
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
            TypeControlVE.ResetVideo or
            TypeControlVE.UhidCreate or
            TypeControlVE.UhidInput or
            TypeControlVE.UhidDestroy;
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

    public StatusPrivacyVE EvaluateVE(
        ContextPrivacyVE context)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        if (context.SystemSecure)
        {
            return
                StatusPrivacyVE.ProtectedVE(
                    ClassificationPrivacyVE.SystemSecure,
                    "Contenido protegido por Android.");
        }

        if (context.SecureInput)
        {
            return
                StatusPrivacyVE.ProtectedVE(
                    ClassificationPrivacyVE.SecureInput,
                    "Entrada sensible protegida.");
        }

        if (context.ManualShield)
        {
            return
                StatusPrivacyVE.ProtectedVE(
                    ClassificationPrivacyVE.ManualShield,
                    "Privacy Shield activado manualmente.");
        }

        if (
            IsSensitivePackageVE(
                context.ForegroundPackage)
        ) {
            return
                StatusPrivacyVE.ProtectedVE(
                    ClassificationPrivacyVE.SensitiveApplication,
                    "Aplicacion marcada como sensible.");
        }

        return
            StatusPrivacyVE.NormalVE();
    }
}