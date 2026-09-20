# Contributing

1. Parta del último baseline aprobado.
2. Revise `Documentation/NLDocumentationNamingMap.json` antes de crear/renombrar archivos.
3. No introduzca polling si existe una fuente event-driven fiable.
4. No use Recovery para congestión, backpressure o `WouldBlock`.
5. Mantenga independencia de motores.
6. No persista datos personales innecesarios.
7. Integre una función completa; no deje dos arquitecturas activas a medias.
8. Ejecute `Tool/NLToolReleaseGate.ps1` y documente las pruebas físicas que no puedan automatizarse.
9. Actualice manual ES/EN y CHANGELOG si cambia una función visible.
