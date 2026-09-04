# Verificación de copyright y licencias — NOVORA-LINK

**Revisión base: 4 de septiembre de 2026**

Este documento registra la revisión técnica de autoría, licencias y redistribución de componentes conocidos usados por NOVORA-LINK 1.3. No sustituye asesoría jurídica profesional.

## 1. Código original de NOVORA

El repositorio declara como propietario del código, documentación, arquitectura, interfaz, branding y activos originales de NOVORA a **Aaron Yair Galarza Valdes**, salvo componentes identificados como software de terceros.

La licencia raíz `LICENSE` es propietaria y no intenta relicenciar componentes de terceros. `COPYRIGHT.md`, `ACKNOWLEDGEMENTS.md` y `THIRD-PARTY-NOTICES.md` separan expresamente la propiedad intelectual original de las dependencias.

**Estado:** coherente a nivel documental.

## 2. scrcpy 4.1

- Proyecto: Genymobile / Romain Vimont.
- Versión fijada por NOVORA-LINK: 4.1 Windows x64.
- Licencia upstream: Apache License 2.0.
- Copyright upstream verificado en la etiqueta v4.1: Copyright (C) 2018 Genymobile y Copyright (C) 2018-2026 Romain Vimont.
- Fuente: https://github.com/Genymobile/scrcpy/tree/v4.1

La distribución Windows oficial de scrcpy copia su `LICENSE` al paquete como `LICENSE.txt`. `scripts/Setup-Tools.ps1` copia el contenido del paquete de scrcpy a `Tools`, preserva esa licencia y crea `Tools/Legal/` para que los avisos legales formen parte del artefacto instalado.

**Regla para ScreenEngine:** si se reutiliza o modifica código Apache-2.0 de scrcpy, conservar encabezados y avisos aplicables, marcar archivos modificados y distribuir la licencia Apache-2.0. No presentar el código reutilizado como autoría original de NOVORA.

## 3. Gnirehtet 2.5.1

- Proyecto: Genymobile.
- Versión fijada por NOVORA-LINK: 2.5.1 Rust Windows x64.
- Licencia upstream: Apache License 2.0.
- Copyright upstream: Copyright (C) 2017 Genymobile.
- Fuente: https://github.com/Genymobile/gnirehtet/tree/v2.5.1

El ZIP Windows oficial contiene `gnirehtet.exe`, `gnirehtet.apk` y `gnirehtet-run.cmd`; no incluye por sí mismo una copia de `LICENSE`. Por eso `Setup-Tools.ps1` adjunta una copia preservada de Apache-2.0 y un aviso específico de Gnirehtet dentro de `Tools/Legal/`.

**Regla para LinkEngine:** cualquier código reutilizado o adaptado desde Gnirehtet conserva sus obligaciones Apache-2.0 y debe marcar modificaciones relevantes.

## 4. Android Debug Bridge (ADB)

- Proyecto: Android Open Source Project.
- Componente: Android Debug Bridge y archivos Windows asociados incluidos dentro de la distribución de scrcpy.
- Licencia del módulo ADB verificada en AOSP: Apache-2.0 en los componentes aplicables, con archivo `NOTICE` como texto de licencia del módulo.
- Fuente: https://android.googlesource.com/platform/packages/modules/adb/

NOVORA-LINK no reclama autoría sobre ADB. Deben preservarse los avisos que acompañen el paquete oficial utilizado.

## 5. Dependencias binarias incluidas por scrcpy 4.1

La build oficial Windows de scrcpy 4.1 no contiene únicamente `scrcpy.exe`: su script upstream construye/copia dependencias binarias. En la etiqueta v4.1 se verificaron estas versiones:

| Componente | Versión usada por build upstream v4.1 | Licencia principal conocida | Consideración |
|---|---:|---|---|
| FFmpeg | 8.1.2 | LGPL-2.1-or-later bajo la configuración sin `--enable-gpl` | Requiere especial atención a redistribución y disponibilidad del código fuente correspondiente. |
| SDL3 | 3.4.12 | zlib | Conservar aviso/licencia aplicable. |
| dav1d | 1.5.3 | BSD-2-Clause | Conservar avisos BSD aplicables; scrcpy lo compila estáticamente. |
| libusb | 1.0.30 | LGPL-2.1-or-later | Requiere conservar licencia y disponibilidad de fuente correspondiente cuando se redistribuye el binario. |
| ADB | versión incluida por scrcpy 4.1 | Apache-2.0 en componentes aplicables | Conservar NOTICE/licencias del paquete. |

Referencias de versiones: scripts `app/deps/*.sh` de scrcpy v4.1.

## 6. Hallazgo importante: FFmpeg/libusb

FFmpeg publica una lista de comprobación de cumplimiento que recomienda, entre otras medidas, compilar sin GPL/no-free, usar enlace dinámico y proporcionar el código fuente exacto correspondiente a los binarios distribuidos. El script de scrcpy v4.1 verificado construye FFmpeg 8.1.2 como librerías compartidas y no activa `--enable-gpl`.

Para reducir el riesgo de redistribuir DLLs LGPL sin material correspondiente, la rama de políticas incorpora `scripts/Prepare-ThirdPartyCompliance.ps1`. El workflow de release ejecuta ese script para:

- descargar y verificar por SHA-256 las fuentes exactas de FFmpeg 8.1.2 y libusb 1.0.30;
- incluir esos archivos fuente como assets junto al instalador;
- extraer y colocar dentro de `Tools/Legal/` las licencias de FFmpeg, libusb, SDL3 y dav1d;
- generar `THIRD-PARTY-SOURCE-NOTES.txt` con versiones, fuentes, hashes y referencias de build.

Esta automatización mejora materialmente la trazabilidad, pero no reemplaza una revisión jurídica profesional ni garantiza por sí sola que toda obligación posible de cada licencia haya sido satisfecha.

## 7. Reglas obligatorias para futuros motores propios

Cuando LinkEngine o ScreenEngine reutilicen código de scrcpy/Gnirehtet:

1. Identificar el archivo y el proyecto de origen.
2. Conservar copyright y encabezados de licencia aplicables.
3. Marcar de forma visible los archivos modificados cuando lo exija la licencia.
4. Mantener una entrada en `THIRD-PARTY-NOTICES.md`.
5. No eliminar `NOTICE`, `LICENSE`, `COPYING` u otros textos upstream exigidos.
6. Revisar dependencias transitivas, no solo el proyecto principal.
7. Repetir la auditoría antes de cada release que cambie versiones, codecs, librerías o paquetes Android.

## 8. Estado de la revisión

| Área | Estado |
|---|---|
| Autoría original NOVORA separada de terceros | Verificado documentalmente |
| scrcpy Apache-2.0 y autoría | Verificado |
| Gnirehtet Apache-2.0 y autoría | Verificado |
| ADB Apache-2.0 aplicable / NOTICE | Verificado a nivel de módulo AOSP |
| Dependencias transitivas de scrcpy identificadas | Verificado para la build v4.1 |
| Avisos legales incluidos automáticamente en publish | Implementado en `Setup-Tools.ps1`; sujeto a CI |
| Fuentes correspondientes de FFmpeg/libusb junto a releases | Automatización implementada; sujeto a ejecución del workflow |
| Licencias FFmpeg/libusb/SDL3/dav1d dentro del instalador | Automatización implementada; sujeto a ejecución del workflow |
| Revisión jurídica profesional | No realizada |

## 9. Criterio de release

Una release no debería declararse “license-compliant verified” únicamente porque `THIRD-PARTY-NOTICES.md` exista. Antes de publicar se debe comprobar el contenido real del instalador, los binarios presentes, los textos legales incluidos y los assets de fuente correspondientes.
