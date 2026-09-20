# Security / Privacidad

## Minimización de datos

NOVORA no debe guardar innecesariamente:

- passwords;
- accounts;
- emails;
- contacts;
- OTP codes;
- tokens;
- cookies;
- clipboard history;
- private content.

Los perfiles persistentes deben contener principalmente información técnica necesaria para funcionamiento/compatibilidad.

## Reporte

No publique credenciales, tokens, capturas privadas ni archivos personales al reportar un error. Comparta únicamente el primer error técnico, versión, logs sanitizados y pasos de reproducción.

## Dependencias

No se recomienda desactivar Windows Security ni crear exclusiones globales para usar NOVORA. Los falsos positivos deben investigarse por archivo y versión.

## Contenido protegido

VisionEngine debe respetar protecciones de Android y no debe introducir mecanismos destinados a evadir contenido marcado como seguro.

## Release gate

Una publicación estable requiere revisión de seguridad y privacidad además de compilación.
