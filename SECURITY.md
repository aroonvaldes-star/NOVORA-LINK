# Política de Seguridad de NOVORA-LINK

**Vigente desde:** 21 de octubre de 2026

## 1. Versiones soportadas

| Serie | Soporte |
|---|---|
| 1.4.x | Soporte activo: correcciones de seguridad y fiabilidad |
| 1.3.x | Solo vulnerabilidades críticas, hasta el 31 de diciembre de 2026 |
| < 1.3 | Sin soporte |

El número de versión debe tomarse de la compilación realmente instalada,
no de un tag, un commit o un documento. Incluya siempre la versión de
NOVORA-LINK, la de LINK Control y el transporte usado (USB o LAN).

## 2. Cómo reportar una vulnerabilidad

**No abra un issue público para vulnerabilidades.**

Use uno de estos canales:

1. **GitHub Private Vulnerability Reporting** (preferido):  
   https://github.com/aroonvaldes-star/NOVORA-LINK/security/advisories/new
2. **Correo:** aroonvaldes@gmail.com  
   Asunto: `[SECURITY] NOVORA-LINK`

Incluya, en la medida de lo posible:

- Versión de NOVORA-LINK y de LINK Control.
- Sistema operativo y versión de Android.
- Transporte (USB o LAN) y estado de los motores.
- Descripción del impacto y del componente afectado.
- Pasos de reproducción mínimos.
- Prueba de concepto, capturas o registros **sin datos personales**.
- Si lo desea, su nombre y forma de contacto para acreditar el reporte.

No envíe claves privadas, tokens, certificados, bases de datos ni
contenido de terceros.

## 3. Alcance

Se consideran dentro del alcance:

- Aplicación Windows (`src/NOVORA`).
- Aplicación Android LINK Control (`src/NOVORA.Android`).
- Relay `NovoraRelay` y protocolos de LinkEngine.
- Emparejamiento LAN, TLS y almacenamiento de credenciales.
- Protocolos de VisionEngine y ExInEngine.
- Instalador y scripts de compilación o release.
- Workflows de GitHub Actions del repositorio.

Quedan fuera del alcance:

- Vulnerabilidades en FFmpeg, ADB, SDL3, `tcp_ip` u otros terceros, que
  deben reportarse a sus proyectos.
- Ingeniería social, phishing o acceso físico.
- Ataques que requieran un dispositivo con root o un PC ya comprometido.
- Agotamiento de recursos por configuraciones deliberadamente inseguras.
- Falta de cabeceras, TLS o mejores prácticas en servicios de terceros.

## 4. Tiempos de respuesta

| Etapa | Objetivo |
|---|---|
| Acuse de recibo | 72 horas hábiles |
| Clasificación inicial | 7 días hábiles |
| Corrección o mitigación | Según severidad y complejidad, con actualizaciones periódicas |
| Divulgación coordinada | 90 días desde el reporte, salvo acuerdo distinto |

Estos plazos son objetivos de buena fe y no constituyen una garantía
contractual.

## 5. Divulgación coordinada

Se solicita no divulgar públicamente la vulnerabilidad hasta que exista una
corrección o mitigación disponible, o hasta que transcurra el plazo de 90
días. El Titular acreditará el reporte cuando el reportero lo solicite y
coordinará la fecha de publicación.

## 6. Salvo conducto para investigación de buena fe

Si usted investiga de buena fe, siguiendo esta política, sin acceder a
datos ajenos, sin degradar servicios y sin extorsionar al Titular, este no
iniciará acciones legales por la investigación. Esta cláusula no autoriza
accesos no autorizados ni exime de responsabilidades frente a terceros.

## 7. Recompensas

No existe un programa de bug bounty ni compensación económica garantizada.
El Titular podrá reconocer públicamente las contribuciones relevantes si el
reportero lo autoriza.

## 8. Controles de seguridad relevantes

- Emparejamiento LAN con token y TLS con huella fijada (pinning).
- Credenciales de Windows protegidas con DPAPI.
- Secretos de Android en AndroidKeyStore con AES‑GCM.
- Límites de cola y de sesión en los motores.
- Ausencia de telemetría remota centralizada.
- Actualizaciones entregadas desde GitHub Releases.

## 9. Contacto

- Correo: aroonvaldes@gmail.com
- Repositorio: https://github.com/aroonvaldes-star/NOVORA-LINK
- Aviso de privacidad: `PRIVACY.md`
