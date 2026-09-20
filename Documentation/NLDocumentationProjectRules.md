# Reglamento General de NOVORA-LINK — GPT / CODEX / Desarrollo

Este reglamento aplica a todas las Releases.

1. **Contexto obligatorio.** Cada chat/mensaje del proyecto debe partir del contexto anterior y de otros chats disponibles.
2. **Autosustentabilidad.** Reducir dependencias de terceros cuando exista sustitución propia validada; mientras tanto, conservar atribución/licencia.
3. **Motores independientes.** LE, VE, ST y motores futuros deben poder funcionar/detenerse de forma aislada cuando su función lo permita.
4. **Investigación antes de código propio.** Una referencia de terceros exige investigación ejecutiva; la solución permanente debe evolucionar hacia implementación propia.
5. **Gate de Release.** Rendimiento, seguridad, calidad, funcionamiento, pruebas virtuales y físicas antes de publicar.
6. **Zero sondeo / Zero Polling.** Preferir eventos/callbacks/push/readiness/notificaciones/estado en memoria. Timers sólo para deadline, timeout, debounce, pacing, scheduling o backoff legítimo.
7. **Recovery por motor.** Fallar VE no debe detener LE ni NOVORA completa cuando el error pueda aislarse.
8. **Reservada.**
9. **Un dispositivo por PC.** Una sesión Android activa por computadora para concentrar recursos.
10. **Nomenclatura.** Prefijos `NL`, `LE`, `VE`, `ST`, `NLNVIDIA`; `NLDocumentationNamingMap.json` es autoridad de migración.
11. **Integración completa.** Verificar rutas, namespaces, contratos compartidos, UI, Android, Windows, build y runtime. Si no queda completa, se retira/archiva.
12. **Baseline limpio.** Cada cambio parte del último baseline aprobado; no conservar duplicados/legacy/experimentos muertos en rama principal.
13. **Panel dinámico legible.** Tema/fuentes/colores coherentes; información imprescindible visible y lenguaje fácil.
14. **Privacidad.** No almacenar innecesariamente passwords, accounts, emails, contacts, OTP, tokens, cookies, clipboard history o private content. Sin telemetría oculta, robo de datos ni pagos internos; sólo donativo externo del repositorio.
15. **Documentación.** Comparar cada Release con la estable anterior y mantener registro de avance relevante.
16. **Manual bilingüe.** Manual dentro de PC y Android en Español/English, priorizando español; copia externa distribuida junto con la app.

## Código

Cuando se entregue código, debe ser completo. Para cambios extensos se prefiere un `.ps1` transaccional/verificable.

## Aceptación

Una integración no se considera terminada por compilar: debe estar conectada, probada en su capa y documentada. Una Release estable necesita además pruebas físicas y gates de rendimiento/seguridad/calidad.
