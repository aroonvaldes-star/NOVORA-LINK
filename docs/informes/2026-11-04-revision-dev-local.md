# Revisión de Dev_Local — 2026-11-04

## Alcance

Revisión estática del checkout `swiss-retheme`, commit `607843fb2b13f6f823c7fcc3473d00661b31301a`. Se inspeccionaron los proyectos Windows y Android, los puntos de entrada de la aplicación, los motores LE/VE/ExIn, los workflows, scripts, documentación y pruebas. No se modificó código ni se ejecutaron builds de Windows/Android o pruebas en un teléfono.

El árbol tenía cambios locales sin seguimiento en `.claude/settings.local.json` y `PES_STM/`; se dejaron intactos.

## Estado y límites de plataforma

- LINK Control es el cliente Android (`src/LINK Control/NLProjectAndroid.csproj`): `net10.0-android`, `ApplicationVersion` 124 y `ApplicationDisplayVersion` 1.4.124.
- El escritorio está en `src/NOVORA LINK/NLProjectDesktop.csproj`, dirigido a `net8.0-windows10.0.26100.0` y dependiente de WPF y Windows Forms.
- `src/NOVORA-LINUX` no contiene archivos versionados ni implementación de interfaz Linux. El test de relay en Ubuntu no constituye una integración del escritorio.
- La documentación activa que afirmaba que el relay incluía código derivado de Gnirehtet no coincide con el README del relay ni con los avisos de terceros, que describen el reemplazo `NovoraRelay`. La procedencia histórica debe confirmarse antes de retirar o modificar atribuciones.

## Hallazgos

### Alta prioridad: rutas de workflows no coinciden con este checkout

Los proyectos viven bajo `src/NOVORA LINK/` y `src/LINK Control/`, pero varios workflows y scripts aún usan `src/NOVORA/` y `src/NOVORA.Android/`. Las referencias aparecen, entre otros, en:

- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `.github/workflows/codeql.yml`
- `.github/workflows/dependency-submission.yml`
- `.github/workflows/securitycodescan.yml`
- `scripts/Setup-Tools.ps1`
- `scripts/Prepare-ThirdPartyCompliance.ps1`

La CI local tampoco incluye `swiss-retheme` en su filtro de ramas para `push`. Por tanto, las compilaciones y publicaciones desde esta copia no están verificadas hasta sincronizar las rutas y los disparadores de CI con el árbol real.

### Media: el tema guardado se reemplaza por Light al iniciar

`NLUIWindowMain.xaml.cs` asigna `NLServiceTheme.Light` en `LoadSettingsToViewModel14()` en vez de usar `settings.Theme`. `NLApplicationApp.xaml.cs` también aplica Light en `ApplySavedTheme()` sin utilizar el tema cargado. Aunque la selección se persiste, no se restaura al abrir la aplicación.

### Documentación de versiones

El README principal declaraba revisión del 27 de octubre y LINK Control 1.4.120. El proyecto Android en este checkout declara 1.4.124. La versión fuente no confirma qué APK está instalada ni qué activos están publicados.

### Intento de pre-release v1.4.0

La ejecución de GitHub Actions del 4 de noviembre, run [41816533679](https://github.com/aroonvaldes-star/NOVORA-LINK/actions/runs/41816533679), falló en `Resolve version and signed Android release`. Recibió `REQUESTED_VERSION=v1.4.0 pre`; ese valor no satisface el validador numérico con sufijo separado por guion. Los pasos de descarga, compilación y publicación se omitieron, por lo que esa ejecución no publicó la pre-release.

Formato aceptado para ese workflow:

- Ejecución manual: `1.4.0-pre`.
- Disparo por tag: `v1.4.0-pre`.
- `v1.4.0` sin sufijo corresponde a una versión estable.

Esta ejecución usó el commit remoto `a492f6699ce3c4e26a2c8adfad35118dc840ff1b`, distinto del HEAD local revisado aquí. El problema de formato se confirma por los logs remotos; las discrepancias de rutas descritas arriba corresponden a `Dev_Local`.

## Verificación realizada

- `cargo test --release --locked` en `src/NOVORA LINK/LinkEngine/Network/Native/NovoraRelay`: 29 pruebas aprobadas.
- `npm run lint` y `npm run build`: bloqueados por dependencias nativas de plataforma ausentes en el `node_modules` existente (`@typescript/typescript-linux-x64` y binding de Rolldown para Linux). No se reinstalaron dependencias.
- No se ejecutó el build de escritorio Windows, la compilación Android ni validación física PC–teléfono en este entorno.

## Secuencia recomendada

1. Alinear rutas de proyecto en workflows y scripts, y filtros de rama en CI, CodeQL, análisis de dependencias y release con `src/NOVORA LINK/` y `src/LINK Control/`.
2. Corregir la carga y persistencia del tema y agregar una prueba de regresión.
3. Ejecutar los builds de Windows y Android y las suites CI en el checkout actualizado; después validar la sesión PC–Android en dispositivo físico.
4. Confirmar la procedencia del relay frente a la documentación histórica y conservar los avisos de terceros que correspondan.
