# Política de Seguridad de NOVORA-LINK

Esta política aplica al repositorio oficial `aroonvaldes-star/NOVORA-LINK` y a los instaladores publicados desde sus releases oficiales.

## Versiones con soporte

| Versión | Estado |
|---|---|
| 1.3.x | Soportada como base estable |
| Versiones anteriores | Sin soporte de seguridad garantizado |
| Ramas experimentales | Solo para pruebas; no se consideran releases estables |

## Reportar una vulnerabilidad

No publiques detalles explotables, credenciales, datos personales, claves, tokens, APK firmadas privadas ni pruebas de concepto peligrosas en un issue público.

Canal preferido:

1. Abre la pestaña **Security** del repositorio.
2. Usa **Report a vulnerability / Private vulnerability reporting** si GitHub lo ofrece para el repositorio.
3. Incluye versión afectada, componente, impacto, pasos mínimos para reproducir y una propuesta de mitigación si la tienes.

Si el canal privado no está disponible, abre únicamente un issue público titulado `Security contact request` sin incluir detalles técnicos sensibles. El objetivo será mover la conversación a un canal privado antes de compartir la vulnerabilidad.

## Qué consideramos vulnerabilidad de seguridad

Ejemplos dentro de alcance:

- ejecución de comandos no intencionada o inyección de argumentos;
- escalación de privilegios;
- descarga o ejecución de actualizaciones sin verificar integridad;
- sustitución de binarios de `Tools` o fallos en verificación SHA-256;
- exposición no autorizada de identificadores, archivos, portapapeles, pantalla, audio o tráfico de red;
- bypass de autorización ADB/VPN;
- escritura fuera de rutas esperadas;
- fallos que permitan a un dispositivo conectado afectar a otro dispositivo o a Windows;
- vulnerabilidades en LinkEngine, ScreenEngine o componentes Android propios cuando formen parte de una release.

Fuera de alcance como vulnerabilidad de NOVORA-LINK, salvo que exista una integración insegura propia:

- fallos exclusivos de Android, Windows, ADB, scrcpy, Gnirehtet u otra dependencia upstream;
- advertencias de antivirus sin evidencia de comportamiento malicioso;
- ataques que requieren acceso físico total y una sesión de Windows ya comprometida.

## Principios de seguridad del proyecto

NOVORA-LINK debe mantener estas reglas:

- descargar dependencias únicamente desde fuentes oficiales fijadas;
- verificar SHA-256 antes de integrar binarios descargados durante el build;
- usar HTTPS para actualizaciones y descargas;
- verificar la integridad del instalador antes de ejecutarlo;
- evitar credenciales y secretos dentro del repositorio;
- no solicitar al usuario desactivar Defender o excluir toda la carpeta de la aplicación como solución normal;
- aplicar privilegio mínimo siempre que la arquitectura lo permita;
- separar estado y sesiones por dispositivo;
- registrar cambios de seguridad relevantes en commits y releases.

## Dependencias de terceros

NOVORA-LINK utiliza componentes de terceros. Una vulnerabilidad upstream no cambia la autoría ni la licencia del componente. Cuando exista una corrección relevante, el proyecto debe actualizar o mitigar la dependencia sin eliminar sus avisos legales.

Consulta también:

- `THIRD-PARTY-NOTICES.md`
- `ACKNOWLEDGEMENTS.md`
- `docs/COPYRIGHT-COMPLIANCE.md`

## Divulgación coordinada

Se solicita dar un margen razonable para investigar y publicar una corrección antes de divulgar detalles explotables. No se promete una fecha fija de resolución: la prioridad depende del impacto, reproducibilidad y disponibilidad de una mitigación segura.

## Limitaciones

Esta política describe el proceso de seguridad del proyecto; no constituye un programa de recompensas ni una promesa contractual de compensación.
