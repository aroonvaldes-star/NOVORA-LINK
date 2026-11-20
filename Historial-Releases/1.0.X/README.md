# NOVORA Launcher V1.4 — Fase 1

Launcher WPF funcional orientado a conectar un Android por USB y preparar la mejor experiencia de duplicación/control posible con scrcpy, además de verificar Gnirehtet antes de declararlo activo.

## Abrir
1. Visual Studio 2026 + Desktop development with .NET.
2. Abrir `NOVORA.sln`.
3. Rebuild Solution.
4. Ejecutar.

## Herramientas
El runtime se encuentra en `Tools/Runtime`. No depende del PATH de Windows.

## Importante
NOVORA busca la mejor configuración disponible, pero no puede garantizar cero latencia ni que todos los teléfonos/PC puedan mantener la resolución y FPS máximos simultáneamente. La aplicación debe detectar, elegir y reportar en lugar de prometer capacidades que el hardware no puede entregar.

## Audio configurable
El Launcher incluye control para activar/desactivar el audio de scrcpy y mantener el modo sin audio cuando se priorice la mínima latencia de vídeo.

## Fase 1 — Acciones inferiores
Los botones inferiores ya tienen comportamiento real:
- Configuración: guarda el audio y el monitor/dispositivo seleccionado en `%LOCALAPPDATA%\NOVORA\settings.json`.
- Iniciar NOVORA: inicia únicamente scrcpy. Gnirehtet se activa manualmente cuando el usuario lo necesita.
- Optimizar sistema: reinicia/actualiza el servidor ADB y vuelve a detectar el dispositivo.
- Limpiar caché: solicita a Android liberar caché de paquetes sin borrar datos de aplicaciones.
- Respaldar dispositivo: genera un informe local con información del dispositivo y paquetes instalados.
- Actualizar Novora: utiliza `NOVORA.Update.exe` si existe; si no, informa que el actualizador todavía no está integrado.

> **Archivo histórico:** para instalación y actualizaciones actuales, consulta el [portal oficial](https://novora-link.aroonvaldes.workers.dev/) y las [releases de NOVORA-LINK](https://github.com/aroonvaldes-star/NOVORA-LINK/releases). Este snapshot no se publica como instalador vigente.
