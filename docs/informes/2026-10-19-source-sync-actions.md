# Publicación de fuente y Actions — 2026-10-19

Sincroniza el código local NOVORA-LINK y LINK Control 1.4.113, excluyendo bin/obj, target, APK y material privado de firma. Conserva el historial y los archivos exclusivamente remotos. Los workflows usan Actions fijados a commits verificados.

CI ejecuta las suites actuales, omitiendo únicamente el harness físico y el Benchmark con entradas externas. Rust apunta a NovoraRelay. CodeQL compila proyectos actuales; SecurityCodeScan restaura solo el proyecto que analiza.

El flujo Release Windows requiere una APK ya firmada de una release privada existente; configurar `APPCONTROL_RELEASE_TOKEN` con acceso de lectura y `LINK_CONTROL_RELEASE_TAG` para lanzamientos por tag. No se ha generado, instalado ni publicado una APK nueva con esta sincronización. Los escáneres externos conservan sus requisitos de configuración y su carácter opcional/manual.
