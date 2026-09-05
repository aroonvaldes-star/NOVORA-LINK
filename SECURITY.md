# NOVORA-LINK — Política de Seguridad / Security Policy

> **Español (principal) · English below**

## Español

Esta política aplica al repositorio oficial `aroonvaldes-star/NOVORA-LINK` y a sus releases oficiales.

### Versiones con soporte

| Versión | Estado |
|---|---|
| 1.3.x | Base estable con soporte |
| 1.4 | Desarrollo activo; no se considera estable hasta su release |
| Versiones anteriores | Sin soporte de seguridad garantizado |
| Ramas experimentales | Solo pruebas |

### Reporte responsable

No publiques vulnerabilidades explotables, credenciales, tokens, claves privadas, certificados, datos personales ni pruebas de concepto peligrosas en Issues públicos.

Canal preferido:

1. Abre la pestaña **Security** del repositorio.
2. Utiliza **Report a vulnerability / Private vulnerability reporting** cuando esté disponible.
3. Incluye versión afectada, componente, impacto, pasos mínimos de reproducción y mitigación propuesta si la tienes.

Si el canal privado no está disponible, abre únicamente un Issue titulado `Security contact request` sin detalles técnicos sensibles.

### Alcance

Se consideran dentro de alcance, entre otros:

- ejecución de comandos o inyección no intencionada;
- escalación de privilegios;
- fallos de verificación de integridad;
- sustitución de binarios o componentes confiables;
- exposición no autorizada de pantalla, audio, portapapeles, archivos, identificadores o tráfico;
- bypass de autorización ADB/VPN;
- aislamiento incorrecto entre dispositivos;
- vulnerabilidades propias de LinkEngine, VisionEngine o clientes Android de NOVORA;
- actualizaciones inseguras o ejecución de instaladores sin verificación.

Los fallos exclusivos de Android, Windows, ADB, scrcpy, Gnirehtet u otra dependencia upstream deben reportarse al proyecto correspondiente, salvo que NOVORA-LINK los integre de forma insegura.

### Principios del proyecto

NOVORA-LINK debe:

- usar fuentes oficiales y versiones controladas para dependencias;
- verificar SHA-256 cuando el pipeline lo contemple;
- usar HTTPS para descargas y actualizaciones;
- evitar secretos dentro del repositorio;
- aplicar privilegio mínimo cuando sea posible;
- exigir autorización explícita para dispositivos;
- mantener sesiones separadas por dispositivo;
- no pedir al usuario desactivar Defender como procedimiento normal;
- documentar cambios de seguridad relevantes;
- mantener análisis automáticos como complemento, no sustituto, de revisión humana.

### Divulgación coordinada

Se solicita tiempo razonable para investigar y corregir vulnerabilidades antes de publicar detalles explotables. Esta política no constituye un programa de recompensas ni una promesa contractual de compensación.

---

## English

This policy applies to the official `aroonvaldes-star/NOVORA-LINK` repository and its official releases.

### Supported versions

| Version | Status |
|---|---|
| 1.3.x | Supported stable base |
| 1.4 | Active development; not stable until released |
| Older versions | No guaranteed security support |
| Experimental branches | Testing only |

### Responsible reporting

Do not publish exploitable vulnerabilities, credentials, tokens, private keys, certificates, personal data or dangerous proofs of concept in public Issues.

Preferred channel:

1. Open the repository **Security** tab.
2. Use **Report a vulnerability / Private vulnerability reporting** when available.
3. Include affected version, component, impact, minimal reproduction steps and any proposed mitigation.

If private reporting is unavailable, open only an Issue titled `Security contact request` without sensitive technical details.

### Scope

In-scope examples include unintended command execution or injection, privilege escalation, integrity-verification failures, unauthorized exposure of screen/audio/clipboard/files/network data, ADB/VPN authorization bypass, cross-device isolation failures, vulnerabilities in NOVORA-owned LinkEngine/VisionEngine components, and unsafe update execution.

Issues entirely within upstream Android, Windows, ADB, scrcpy, Gnirehtet or other dependencies should be reported upstream unless NOVORA-LINK introduces an unsafe integration.

### Project principles

NOVORA-LINK should use official dependency sources, integrity verification where available, HTTPS, least privilege, explicit device authorization, per-device isolation, secure secret handling and documented security changes. Users should not be instructed to disable Defender as a normal installation procedure.

### Coordinated disclosure

Please allow reasonable time to investigate and fix vulnerabilities before publishing exploitable details. This policy is not a bug-bounty program or contractual promise of compensation.
