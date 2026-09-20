# Verificación del aviso de NOVORA-LINK 1.4 oficial

Revisión Windows: 1.4.1-experimental. Fecha: 15 de septiembre de 2026.

## Comprobado

- Segunda revisión independiente del código antes de aplicarlo.
- Compilación y suite completa .NET: 269 pruebas aprobadas, 0 fallidas, 0 omitidas (39 pruebas nuevas de versiones, canal, cancelación, consulta única e integridad de enlaces).
- Publicación Windows x64 autocontenida y compilación del instalador con Inno Setup: correctas.
- Prueba de la interfaz compilada con respuesta simulada de una futura v1.4.0: botón VER 1.4 OFICIAL visible, una consulta; el botón permanece tras otro mensaje de estado. No se abrió navegador ni se descargó o instaló una actualización.
- Instalación real sobre el primer instalador experimental: versión actualizada correctamente. Dos ciclos de instalación/desinstalación, códigos de salida 0.
- 493 archivos del paquete comprobados por huella; tres accesos en Inicio; ventana NOVORA-LINK 1.4 abierta.
- Runtime .NET local y Visual C++ local cargados. Relay local respondió 00000000.
- Archivo personal conservado. Inicio automático de otra copia conservado; entrada correspondiente a la copia desinstalada eliminada.
- APK Android conservado: e8de4485731bb35f0939bbadfecd442636f1d4076f252ae933d338cc41fe2157.

## Límites

La prueba de futura release utiliza datos simulados: no existe una publicación oficial nueva por efecto de estas pruebas. La revisión de 1.3/1.3.1 fue de fuentes y API; falta comprobar su migración real desde el instalador antiguo con administrador y otra carpeta. No se repitieron pruebas físicas Android, tráfico USB extremo a extremo ni pruebas en todos los fabricantes. El instalador no tiene firma digital.

El código fuente modificado se adjunta como Codigo-aviso-1.4.zip, con manifiesto y entrega PowerShell. Es un conjunto de cambios sobre el proyecto actual, no un repositorio completo. El tag experimental original y sus archivos Source code automáticos se conservan; no representan por sí solos esta revisión añadida.

## Instalador

Nombre: NOVORA-LINK-1.4.1-experimental-Setup-x64.exe

Tamaño: 143517383 bytes.

SHA-256: 0a15e0fb14f05c104b566637e02228356f0b8b5fcd53a12cdee967916b5b5af2
