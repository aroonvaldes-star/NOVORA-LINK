# LinkEngine Network Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Regular el tráfico que atraviesa LE, medir latencia bajo carga y proteger flujos interactivos sin romper DATA v1.

**Architecture:** Contrato común PC/Android y presupuestos por ruta/dirección; policy aplicada a proxies y scheduler de paquetes reales. Observación y límites manuales preceden a la adaptación. Configuración Rust por stdin con readiness/ACK y telemetría separada de LE_METRIC.

**Tech Stack:** C# net8.0/net10.0-android, Rust 2021/Tokio/tcp_ip, TimeProvider, pruebas deterministas y TCP loopback.

**Spec:** [Diseño común](../specs/2026-10-13-le-ve-estabilidad-red-design.md).

Base de todas las rutas: E:/NOVORA/NOVORA LINK/GITHUB/NL. Los archivos Create son propuestas; Modify existen en el árbol local revisado. Ejecutar el bloque 0 del [plan general](2026-10-13-le-ve-estabilidad-y-capacidad.md) primero.

## Global Constraints

- CONTROL Version=1 y cuatro bytes de ID+IPv4 DATA v1 se conservan.
- Conservar formato LE_METRIC; agregar LE_QOS v1, máximo 4096 bytes y 1 Hz.
- Observe inicialmente; límite válido positivo hasta 1 250 000 000 B/s; null no es capacidad medida.
- No abrir endpoint LAN de configuración; conservar TLS/pinning y autorización actual.
- No dar prioridad automática a todo UDP; mantener FIFO por flujo y límites de memoria.
- Las direcciones y restricciones comunes se definen en el diseño; no reinterpretar TX/RX.

## Review Focus

- Peer/binario antiguo: ningún estado QoS activo sin ACK/readiness.
- Ocho flujos de descarga frente a uno interactivo: reparto acotado y sin inanición.
- Fragmentos IPv4/cabeceras inválidas: parser no clasifica leyendo puertos inexistentes.
- Cancelación mientras espera tokens y generación reemplazada: finalizar sin enviar a la nueva sesión.
- Métricas caducadas o error upstream ajeno a LE: no liberar límites ni diagnosticar causalidad falsa.

## Tarea LE T1 Contratos y medidas bajo carga

**Files:**
- Create: src/NOVORA/Control/NLControlNetworkPolicy.cs, src/NOVORA/Network/NLNetworkBudget.cs, src/NOVORA/Network/NLNetworkPacer.cs.
- Create: src/NOVORA/LinkEngine/Network/Native/NovoraRelay/src/telemetry.rs.
- Create: tests/NOVORA.NetworkPolicy.Tests/NOVORA.NetworkPolicy.Tests.csproj y Program.cs; proyecto net8.0 de consola, enlaza los tres archivos puros anteriores sin WPF/Android ni paquetes nuevos.
- Modify: src/NOVORA/Control/NLControlProtocol.cs, src/NOVORA/Control/NLControlVpnProtocol.cs y src/NOVORA/Control/NLControlCommands.cs; src/NOVORA/UI/NLUIWindowMainAndroidControl.cs; src/NOVORA.Android/NLProjectAndroid.csproj, src/NOVORA.Android/AndroidService/NLAndroidServiceControl.cs y src/NOVORA.Android/AndroidVpn/NLAndroidVpnService.cs.
- Modify: src/NOVORA/LinkEngine/Network/Native/NovoraRelay/src/main.rs, src/NOVORA/LinkEngine/Network/LENetworkRelay.cs, src/NOVORA/LinkEngine/Metrics/LESpeedTestServer.cs, scripts/Verify-NOVORA-Repository.ps1.

**Interfaces:** Consumes tipos exactos del diseño. Produces NLNetworkBudget.Update(policy, sampledAtTicks), NLNetworkBudget.Allocate(direction, demands, nowTicks), NLNetworkPacer.WaitAsync(grant, bytes, CancellationToken); métodos usan TimeProvider local. demands es IReadOnlyDictionary<NLNetworkTrafficClass,long> en B/s; Allocate devuelve IReadOnlyList<NLNetworkGrant>. Publicar métricas por origen sin restar timestamps remotos.

- [ ] Escribir pruebas BaselineNotCapacity, InvalidSampleIgnored, SeparateLinkKeys, SeparateDomainsSameLink, SeparateRttSources, GenerationReset, GrantSumWithinBudget. Con techo 1 000 000 B/s, comprobar suma≤techo y que otra LinkKey/Domain no consume ese presupuesto; NaN, edad 3001 ms y generación vieja no producen decisión.
- [ ] Ejecutar dotnet run --project tests/NOVORA.NetworkPolicy.Tests/NOVORA.NetworkPolicy.Tests.csproj -c Release; observar fallo por tipos ausentes antes de implementar.
- [ ] Implementar contratos y registros opcionales de capacidades en snapshot/offers, con validación y dispatcher de network.capabilities/network.echo. El cliente Android anuncia capacidades mediante acción network.capabilities autorizada solo después de ver StabilityTelemetry del PC; el PC almacena anuncio por generación. network.echo devuelve la secuencia recibida sin payload arbitrario, para RTT local Android. Estado inicial Observe y campos de capacidades ausentes conservan v1.
- [ ] Medir waits/cola dentro de main.rs y VPN con reloj local. El heartbeat USB conserva wire; para LAN agregar eco negociado con secuencia sobre control autorizado. HTTP concurrente durante descarga/subida se etiqueta como HTTP y mantiene muestras iniciales separadas.
- [ ] Implementar payload LE_QOS separado, parser acotado, secuencia/generación, counters por ventana y causa de descarte; fixture LE_METRIC antiguo debe seguir aceptándose sin cambios.
- [ ] Hacer pasar pruebas anteriores y fixtures protocolo antiguo/nuevo, cambio de ruta, RTT negativo y counters reiniciados; extender verifier con esta suite sin sustituir suites existentes.

## Tarea LE T2 Límites manuales y configuración del proceso

**Files:**
- Create: Native/NovoraRelay/src/rate_limit.rs y proxy_copy.rs bajo src/NOVORA/LinkEngine/Network.
- Create: src/NOVORA/LinkEngine/Network/LECongestionOptions.cs.
- Modify: Native/NovoraRelay/src/main.rs y Cargo.toml; LENetworkRelay.cs, ILENetworkRelay.cs; src/NOVORA/Service/NLServiceSettings.cs.
- Test: módulos Rust de rate_limit/proxy_copy y tests/NOVORA.LinkEngine.Tests/Program.cs.

**Interfaces:** RateLimiter::reserve(bytes: usize, now: Duration) -> Duration calcula espera monotónica; actualización conserva límite y acota créditos. Rust consume LE_CONFIG JSON del stdin, con v=1, revision, generation, LinkKey, mode, wanUploadBps/wanDownloadBps/deviceFromAndroidBps/deviceToAndroidBps y grants con Domain/Direction/TrafficClass. Produces LE_READY y LE_CONFIG_ACK con revisión/generación, techos y grants aceptados. ILENetworkRelay añade ApplyPolicyAsync(NLNetworkPolicy,IReadOnlyList<NLNetworkGrant>,CancellationToken); el controller espera ACK de todos los alcances antes de mostrar Manual.

- [ ] Escribir tests exact_rate_no_burst_after_idle, cancel_pending_copy, shared_tcp_udp_budget, separate_wan_device_grants, old_binary_visible_fallback. A 1000 B/s y burst 1000 B, 2000 B requieren al menos 1 s virtual; 100 flujos no reciben 100 veces el presupuesto agregado. WAN y DeviceLink de la misma dirección mantienen límites independientes.
- [ ] Ejecutar cargo test --locked --manifest-path src/NOVORA/LinkEngine/Network/Native/NovoraRelay/Cargo.toml; confirmar fallo de las nuevas pruebas.
- [ ] Implementar dos copias TCP de buffers acotados en lugar de copy_bidirectional: reserva antes de escribir WAN para subida y antes de leer otro bloque upstream para bajada. Compartir bucket con UDP por dirección; nunca retener lock de control durante una espera de tokens.
- [ ] Aplicar también la asignación DeviceLink a la frontera IPv4 salida/entrada, sin contar el mismo tráfico dos veces en el mismo dominio. Excluir la ruta speedtest local del bucket Internet; registrar su consumo en DeviceLink.
- [ ] Configurar stdin del hijo y un lector dedicado a configuración, máximo 4096 bytes, validación de límites y monotonía. Revisión ajena/repetida es idempotente o rechazada; session replacement cancela waits. No persistir una configuración parcial ni liberar límite ante fallo de ACK.
- [ ] Ajustar burst al consumo permitido de 5 ms, con mínimo un MTU 1500 B y máximo 64 KiB. Para bloques grandes la copia fracciona el envío, preservando los bytes del stream.
- [ ] Pasar tests de exactitud, fuente lenta, short reads/writes, cero/negativo/overflow, timeout config, cierre y reconexión. Ejecutar suite LinkEngine para asegurar recuperación y DATA antiguo.

## Tarea LE T3 Reparto justo y colas acotadas

**Files:**
- Create: Native/NovoraRelay/src/flow_scheduler.rs y packet_queue.rs bajo src/NOVORA/LinkEngine/Network.
- Modify: Native/NovoraRelay/src/main.rs, telemetry.rs; tests internos Rust.

**Interfaces:** FlowKey::parse(&[u8]) -> Result<FlowKey> valida IPv4/longitud/fragmentación; Scheduler::enqueue(flow,packet,enqueuedAt) y Scheduler::dequeue(now) conservan FIFO por flujo. Quantum=1500 B; clase preferente≤20 % del servicio asignado; máximo 256 flujos activos. Límite global inicial 2 MiB, por flujo 64 KiB; medir antes de cambiar estos parámetros.

- [ ] Escribir fair_eight_bulk_one_sparse, no_starvation, preserve_order_per_flow, invalid_fragment_not_classified y bounded_memory_many_flows. Dos bulk equivalentes reciben proporción 0,8–1,2 tras 1000 rondas sin preferencia; cada bulk progresa bajo carga interactiva continua.
- [ ] Observar fallo e implementar scheduler en fronteras IPv4 explícitas, no en CopyToAsync TLS. No usar tamaño de un solo paquete para declarar juego; clase predeterminada BestEffort, prioridad explícita validada y acotada.
- [ ] Al alcanzar cota, bloquear productor TCP/entrada cuando sea posible y contabilizar descarte UDP completo. No cortar el stream DATA a mitad de IPv4 ni reordenar dentro del mismo flujo. No descartar bytes de copia TCP ya aceptados.
- [ ] Medir colas reales de tcp_ip con la versión fijada; comparar sus reservorios con los límites nuevos antes de reducir channel_size/tcp_channel_size/udp_channel_size. No modificar cwnd o SACK para sustituir el shaper.
- [ ] Ejecutar tests Rust; hacer benchmark de cola con RTT/pérdida controlados. Registrar HOL residual del TCP compartido.
- [ ] Evaluar un módulo CoDel separado únicamente después del benchmark; contrastarlo con RFC 8290. Si solo hay DRR/pacing, mostrar ese comportamiento sin etiquetarlo FQ-CoDel.

## Tarea LE T4 Controlador adaptativo

**Files:**
- Create: src/NOVORA/Network/NLNetworkStabilityController.cs.
- Modify: NLNetworkBudget.cs; src/NOVORA/UI/NLUIWindowMainLinkEngine.cs y NLUIWindowMainLinkSpeedTest.cs; src/NOVORA/Control/NLControlNetworkPolicy.cs.
- Test: tests/NOVORA.NetworkPolicy.Tests/Program.cs y csproj.

**Interfaces:** Evaluate(NLNetworkPolicy, NLNetworkBaseline?, IReadOnlyList<NLNetworkSample>, IReadOnlyDictionary<NLNetworkTrafficClass,long> demands, long nowTicks) -> NLNetworkDecision. Nuevo public sealed record NLNetworkDecision(NLNetworkPolicy AppliedPolicy,IReadOnlyList<NLNetworkGrant> Grants,bool Congested,double VideoRateFactor,string Reason); consume la ventana y techos, no hace I/O ni reinicia sesiones. Tarea VE T3 consume VideoRateFactor; solo ella aplica encoder. Grants conserva los presupuestos Interactive/Audio y expresa reducción de Bulk separada del techo agregado; el relay recibe todas las asignaciones mediante ApplyPolicyAsync.

- [ ] Escribir NoDecisionFromStaleSample, ReduceBulkFirstPreservesAudioControl, TwoWindowsBeforeReduction, CooldownAndSlowRecovery, NoCapacityStaysObserve, NoRttBaselineStaysObserve, RouteReset y DecodeErrorNotWanCongestion; asserts de tiempos/porcentajes exactos del diseño. Verificar que reducción Bulk no se reasigna al mismo flujo como BestEffort.
- [ ] Implementar decisiones de 1 Hz y 2 ventanas; bulk -20 % con cooldown 2 s; recuperación +5 % tras 10 s sanos y cooldown 5 s. Delegar petición de video después de 3 s persistentes solo para Domain=DeviceLink de la misma ruta; decisiones WAN conservan VideoRateFactor=1. No tocar el perfil del usuario.
- [ ] Distinguir síntomas de red/compute/storage y no confundir bytes recibidos con capacidad. Si varias señales discrepan, observar y conservar límite antes de actuar agresivamente.
- [ ] Aplicar policy usando ApplyPolicyAsync y confirmar ACK. Al cerrar sesión, cancelar probes, escritor stdin y grants; una generación vieja nunca actualiza la actual.
- [ ] Pasar tests con reloj simulado y prueba loopback de ACK fuera de orden, timeout y fallo de relay. No activar Adaptive por defecto hasta el gate físico.

## Tarea LE T5 Interfaz y entrega verificable

**Files:**
- Create: src/NOVORA/UI/NLUIWindowMainNetworkStability.cs.
- Modify: src/NOVORA/UI/NLUIWindowMain.xaml, NLUIWindowSettings.xaml y code-behind; src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs; NLServiceSettings.cs; verifier y workflows después de reconciliarlos.
- Test: suites NetworkPolicy, LinkEngine, DynamicAddress y nuevos fixtures de settings/capabilities.

**Interfaces:** Consume capacidades, policy, ACK y muestras. Presenta Estado: Observación/Manual/Adaptativo/No compatible; límites de subida/bajada, fuente del RTT y causa de reducción. Preferencia inicial Estabilidad; controlar por sesión y permitir restaurar límites manuales.

- [ ] Probar persistencia/validación de Mbps→B/s, campo vacío, separador decimal español y límites fuera de rango. No habilitar Adaptive con techo desconocido.
- [ ] Mostrar datos caducados como No disponible, diferenciando Internet/túnel y posible congestión externa; no presentar counters de relay como ping.
- [ ] Ejecutar las suites y verifier indicados en el plan general, con Cargo --locked; registrar la ruta/hash del relay nuevo y fallback del antiguo.
- [ ] Completar gate físico antes de habilitar Adaptive en la entrega; empaquetado n+1, firma original, APK/descriptor/relay/Windows/instalador/hashes consistentes.
- [ ] Revisar y versionar solo archivos propios de cada tarea, con pruebas y Bitacora; no incorporar eliminaciones preexistentes por accidente.
