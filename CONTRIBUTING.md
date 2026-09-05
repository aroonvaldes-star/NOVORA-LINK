# NOVORA-LINK — Guía de contribución / Contributing Guide

> **Español (principal) · English below**

## Español

Gracias por tu interés en contribuir a NOVORA-LINK.

### Reglas básicas

Toda contribución debe:

- corresponder a código, documentación o recursos que tengas derecho legal a aportar;
- respetar la estructura y convenciones del proyecto;
- mantener separadas las atribuciones de terceros;
- evitar introducir secretos, credenciales, claves privadas, certificados, tokens o datos personales;
- incluir una descripción clara del cambio y su propósito;
- mantener compatibilidad con las políticas de seguridad, privacidad y licencias del repositorio.

### Convención de motores

Los motores propios de NOVORA siguen la forma:

`<Función><Carpeta><Motor>`

Ejemplos:

- `StatusCoreLE.cs`
- `MonitorRecoveryLE.cs`
- `StatusCoreVE.cs`
- `MonitorRecoveryVE.cs`

`LE` corresponde a LinkEngine y `VE` a VisionEngine.

### Software de terceros

No copies código propietario, filtrado o no autorizado.

Cuando una contribución reutilice o adapte código de un proyecto de terceros cuya licencia lo permita, debe:

1. identificar el origen;
2. conservar avisos y atribuciones requeridas;
3. respetar la licencia correspondiente;
4. actualizar `THIRD-PARTY-NOTICES.md` cuando sea necesario;
5. no presentar la porción derivada como autoría original de NOVORA.

### Seguridad

No publiques vulnerabilidades sensibles en Pull Requests o Issues públicos. Sigue `SECURITY.md`.

Nunca incluyas:

- contraseñas;
- API keys;
- tokens de GitHub;
- certificados de firma;
- archivos `.pfx`, `.p12` o claves privadas;
- datos personales de usuarios;
- logs sin revisar que contengan información sensible.

### Pull Requests

Un Pull Request debe explicar, como mínimo:

- qué cambia;
- por qué cambia;
- qué componentes afecta;
- cómo fue probado;
- si introduce o modifica dependencias de terceros;
- si afecta seguridad, privacidad, red, control de dispositivos o permisos.

Las contribuciones pueden ser rechazadas si rompen la arquitectura, reducen la seguridad, eliminan atribuciones o introducen una obligación de licencia incompatible sin revisión previa.

### Licencia de las contribuciones

Al enviar una contribución declaras que tienes derecho a aportarla y aceptas que pueda integrarse al proyecto bajo los términos aplicables a la parte del repositorio donde sea incorporada. Esto no transfiere a NOVORA derechos que no poseas sobre software de terceros.

---

## English

Thank you for your interest in contributing to NOVORA-LINK.

### Basic rules

Every contribution must be material you have the legal right to submit, follow project structure and conventions, preserve third-party attribution, avoid secrets or personal data, clearly describe its purpose, and comply with repository security, privacy and licensing policies.

### Engine convention

NOVORA-owned engines follow:

`<Function><Folder><Engine>`

Examples include `StatusCoreLE.cs`, `MonitorRecoveryLE.cs`, `StatusCoreVE.cs` and `MonitorRecoveryVE.cs`. `LE` means LinkEngine and `VE` means VisionEngine.

### Third-party software

Do not submit proprietary, leaked or unauthorized source code. If a contribution reuses or adapts permitted third-party code, identify the source, preserve required notices, comply with its license, update `THIRD-PARTY-NOTICES.md` when necessary, and do not present derived portions as original NOVORA authorship.

### Security

Do not disclose sensitive vulnerabilities in public Pull Requests or Issues. Follow `SECURITY.md`. Never commit passwords, API keys, GitHub tokens, signing certificates, private keys, personal user data or unreviewed sensitive logs.

### Pull Requests

A Pull Request should explain what changed, why it changed, affected components, testing performed, third-party dependency changes and any effect on security, privacy, networking, device control or permissions.

Contributions may be rejected if they break project architecture, weaken security, remove attribution or introduce incompatible licensing obligations without prior review.

### Contribution licensing

By submitting a contribution, you represent that you have the right to contribute it and agree that it may be incorporated under the terms applicable to the part of the repository where it is included. This does not transfer rights you do not own in third-party software.
