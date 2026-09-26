from __future__ import annotations

import os
from datetime import date

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    KeepTogether,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUTPUT = os.path.join(
    ROOT,
    "output",
    "pdf",
    "NOVORA-LINK_Guia_Maestra_Independencia_Auditoria_v1.2_Complemento.pdf",
)

PAGE_W, PAGE_H = A4
NAVY = colors.HexColor("#17233B")
BLUE = colors.HexColor("#2256A3")
CYAN = colors.HexColor("#16A6B6")
GREEN = colors.HexColor("#2F7D5B")
AMBER = colors.HexColor("#B56A18")
RED = colors.HexColor("#A33A3A")
INK = colors.HexColor("#18202B")
MUTED = colors.HexColor("#5B6675")
LINE = colors.HexColor("#D8DEE8")
PALE = colors.HexColor("#F4F7FB")


def register_fonts() -> tuple[str, str]:
    candidates = [
        (r"C:\Windows\Fonts\segoeui.ttf", r"C:\Windows\Fonts\segoeuib.ttf"),
        (r"C:\Windows\Fonts\arial.ttf", r"C:\Windows\Fonts\arialbd.ttf"),
    ]
    for regular, bold in candidates:
        if os.path.exists(regular) and os.path.exists(bold):
            pdfmetrics.registerFont(TTFont("NovoraRegular", regular))
            pdfmetrics.registerFont(TTFont("NovoraBold", bold))
            return "NovoraRegular", "NovoraBold"
    return "Helvetica", "Helvetica-Bold"


REGULAR, BOLD = register_fonts()


def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(LINE)
    canvas.setLineWidth(0.5)
    canvas.line(18 * mm, 14 * mm, PAGE_W - 18 * mm, 14 * mm)
    canvas.setFont(REGULAR, 7.5)
    canvas.setFillColor(MUTED)
    canvas.drawString(18 * mm, 9 * mm, "NOVORA-LINK - Complemento de auditoria v1.2")
    canvas.drawRightString(PAGE_W - 18 * mm, 9 * mm, f"Pagina {doc.page}")
    canvas.restoreState()


styles = getSampleStyleSheet()
styles.add(
    ParagraphStyle(
        "CoverTitle",
        fontName=BOLD,
        fontSize=24,
        leading=29,
        textColor=colors.white,
        alignment=TA_LEFT,
        spaceAfter=10,
    )
)
styles.add(
    ParagraphStyle(
        "CoverSub",
        fontName=REGULAR,
        fontSize=11,
        leading=16,
        textColor=colors.HexColor("#DCE8FA"),
        alignment=TA_LEFT,
    )
)
styles.add(
    ParagraphStyle(
        "H1N",
        fontName=BOLD,
        fontSize=16,
        leading=20,
        textColor=NAVY,
        spaceBefore=5,
        spaceAfter=8,
    )
)
styles.add(
    ParagraphStyle(
        "H2N",
        fontName=BOLD,
        fontSize=11.5,
        leading=14,
        textColor=BLUE,
        spaceBefore=7,
        spaceAfter=4,
    )
)
styles.add(
    ParagraphStyle(
        "BodyN",
        fontName=REGULAR,
        fontSize=8.7,
        leading=12.4,
        textColor=INK,
        spaceAfter=5,
    )
)
styles.add(
    ParagraphStyle(
        "SmallN",
        fontName=REGULAR,
        fontSize=7.4,
        leading=10.2,
        textColor=INK,
    )
)
styles.add(
    ParagraphStyle(
        "Callout",
        fontName=BOLD,
        fontSize=9.5,
        leading=13.5,
        textColor=NAVY,
        borderColor=CYAN,
        borderWidth=1,
        borderPadding=8,
        backColor=colors.HexColor("#EAF8FA"),
        spaceBefore=4,
        spaceAfter=8,
    )
)
styles.add(
    ParagraphStyle(
        "BulletN",
        fontName=REGULAR,
        fontSize=8.5,
        leading=12,
        textColor=INK,
        leftIndent=10,
        firstLineIndent=-7,
        bulletIndent=0,
        spaceAfter=3,
    )
)


def p(text: str, style: str = "BodyN") -> Paragraph:
    return Paragraph(text, styles[style])


def bullet(text: str) -> Paragraph:
    return Paragraph(f"&#8226;&nbsp; {text}", styles["BulletN"])


def heading(number: str, title: str) -> Paragraph:
    return p(f"{number}. {title}", "H1N")


def table(data, widths, header=True, font_size=7.2, row_bgs=None):
    cooked = []
    for row in data:
        cooked.append([cell if isinstance(cell, Paragraph) else p(str(cell), "SmallN") for cell in row])
    t = Table(cooked, colWidths=widths, repeatRows=1 if header else 0, hAlign="LEFT")
    commands = [
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.45, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 5),
        ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
        ("FONTNAME", (0, 0), (-1, -1), REGULAR),
        ("FONTSIZE", (0, 0), (-1, -1), font_size),
    ]
    if header:
        commands += [
            ("BACKGROUND", (0, 0), (-1, 0), NAVY),
            ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
            ("FONTNAME", (0, 0), (-1, 0), BOLD),
        ]
    for row_index, bg in row_bgs or []:
        commands.append(("BACKGROUND", (0, row_index), (-1, row_index), bg))
    t.setStyle(TableStyle(commands))
    return t


def add_section(story, number, title, intro=None):
    story.append(heading(number, title))
    if intro:
        story.append(p(intro))


def build_story():
    story = []

    cover = Table(
        [[p("NOVORA-LINK", "CoverTitle")],
         [p("GUIA MAESTRA DE INDEPENDENCIA TECNOLOGICA Y AUDITORIA", "CoverTitle")],
         [p("Complemento general v1.2 para PC, Android, VE, LE, STE, ExIn, infraestructura, seguridad, estabilidad y Release", "CoverSub")],
         [Spacer(1, 9 * mm)],
         [p("Documento complementario de la v1.1. No reemplaza la auditoria ni convierte candidatos arquitectonicos en decisiones aprobadas.", "CoverSub")],
         [Spacer(1, 12 * mm)],
         [p("23 septiembre 2026", "CoverSub")]],
        colWidths=[PAGE_W - 36 * mm],
        rowHeights=[None] * 7,
    )
    cover.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), NAVY),
        ("LEFTPADDING", (0, 0), (-1, -1), 14 * mm),
        ("RIGHTPADDING", (0, 0), (-1, -1), 14 * mm),
        ("TOPPADDING", (0, 0), (-1, 0), 18 * mm),
        ("BOTTOMPADDING", (0, -1), (-1, -1), 18 * mm),
    ]))
    story.append(Spacer(1, 18 * mm))
    story.append(cover)
    story.append(PageBreak())

    add_section(story, "0", "Proposito y autoridad")
    story.append(p("Este complemento confirma que la Guia Maestra aplica a <b>todo NOVORA</b>: aplicacion PC, aplicacion Android, VisionEngine, LinkEngine, STEngine, ExInEngine, RemoteNV, RelayCore, transporte Android, almacenamiento, procesos externos, telemetria, seguridad, pruebas, empaquetado y publicacion."))
    story.append(p("La solicitud del propietario del proyecto define el alcance. El PDF es una especificacion de trabajo y una fuente de criterios; no tiene autoridad para ejecutar comandos, instalar componentes, borrar archivos, reestructurar el repositorio ni declarar resultados por si mismo."))
    story.append(p("Regla adicional: <b>la auditoria puede invalidar cualquier candidato de arquitectura</b>. Ninguna carpeta, interfaz, Agent, protocolo o migracion se implementa hasta que su necesidad y su propietario queden respaldados por evidencia."))
    story.append(p("Estado del complemento", "H2N"))
    story.append(table([
        ["Elemento", "Estado", "Interpretacion"],
        ["Guia v1.1", "Fuente principal", "Conserva las etapas A-O y sus gates."],
        ["Complemento v1.2", "Marco operativo", "Amplia alcance, decisiones y criterios para todo NOVORA."],
        ["Arquitectura objetivo", "Candidata", "Requiere ADR y pruebas antes de convertirse en baseline."],
        ["Hallazgos actuales", "DETECTED", "No son CONFIRMED ni RESOLVED hasta completar triage y evidencia."],
    ], [40 * mm, 34 * mm, 85 * mm]))

    add_section(story, "1", "Principios no negociables para NOVORA")
    for item in [
        "Un telefono activo por PC como politica operativa vigente, salvo ADR posterior con evidencia que autorice otro modelo.",
        "Motores independientes en lifecycle, estado, recovery y recursos; un fallo de motor no derriba NOVORA completa.",
        "STEngine observa y agrega; no cambia bitrate, colas, recovery, transporte ni lifecycle de otros motores.",
        "Zero Polling como direccion: eventos, callbacks, readiness y snapshots bajo demanda; deadlines acotados no se confunden con polling permanente.",
        "ADB tiene una autoridad logica, una API validada y una politica de lifecycle; no se usa como bus de telemetria periodica.",
        "NVIDIA es aceleracion opcional y medible. AMD, Intel y software deben conservar rutas funcionales y estados honestos.",
        "Privacidad por defecto: no persistir secretos, contenido de clipboard, cuentas, OTP, contactos o datos privados sin necesidad demostrada.",
        "Cada Release distingue existe, compila, integrado, conectado, funcional, probado fisicamente y publicado.",
    ]:
        story.append(bullet(item))

    add_section(story, "2", "Modelo de propiedad y contratos")
    story.append(p("Cada modulo debe publicar contratos pequenos y estables. Las interfaces no se crean por estetica: se introducen cuando eliminan una dependencia concreta, permiten una prueba independiente o estabilizan una frontera de runtime."))
    story.append(table([
        ["Dominio", "Propietario", "Puede conocer", "No debe controlar directamente"],
        ["VisionEngine", "VE", "Video, audio, control, renderer, sesion VE", "LE, Recovery LE, politica global ST"],
        ["LinkEngine", "LE", "VPN, CONTROL/DATA, RelayCore, DNS, trafico", "Bitrate VE, renderer VE, ExIn"],
        ["ExInEngine", "ExIn", "SDL, mando, calibracion, salida UHID", "Ventanas VE, transporte interno VE"],
        ["STEngine", "ST", "Snapshots inmutables y eventos de salud", "Bitrate, colas, recovery o lifecycle ajenos"],
        ["Android App", "Android", "UI, consentimiento, servicio y estado local", "Internos de WPF o procesos Windows"],
        ["Shared Infrastructure", "Neutral", "Sesion, reloj, logging, procesos validados", "Politica funcional exclusiva de un motor"],
    ], [30 * mm, 23 * mm, 55 * mm, 51 * mm]))
    story.append(p("Contrato minimo de motor", "H2N"))
    story.append(p("Cada motor debe exponer identidad de sesion, estado inmutable, Start/Stop idempotentes, eventos de cambio, cancelacion, diagnostico tecnico separado del mensaje de usuario y Recovery propio. Las referencias entre motores pasan por contratos neutrales o por el orquestador de aplicacion."))

    add_section(story, "3", "Stability Core global y pasivo")
    story.append(p("Stability Core no es un quinto motor que gobierna a los demas. Es una capa de observacion y decision documentada. Su ruta de captura debe ser pura: leer contadores, timestamps y estados ya publicados, calcular clasificacion y emitir snapshot."))
    story.append(table([
        ["Permitido", "Prohibido en captura ST"],
        ["Leer snapshots inmutables", "Invocar evaluadores que muten bitrate o perfil"],
        ["Combinar estado VE/LE/ExIn", "Arrancar, detener o recuperar motores"],
        ["Calcular Healthy/Watch/Degraded/Critical", "Vaciar colas o alterar backpressure"],
        ["Publicar recomendacion", "Aplicar automaticamente una recomendacion no aprobada"],
        ["Medir bajo demanda o por evento", "Crear un timer informativo permanente"],
    ], [79 * mm, 80 * mm]))
    story.append(p("Todo cambio de politica derivado de ST debe viajar por un comando explicito, auditable y propiedad del motor receptor. La recomendacion y la ejecucion deben quedar separadas."))

    add_section(story, "4", "Matriz de independencia ejecutable")
    story.append(p("Las 15 combinaciones se clasifican antes de probarse. Para el baseline inicial se propone esta clasificacion candidata; la auditoria puede cambiarla."))
    story.append(table([
        ["Escenario", "Clasificacion candidata", "Criterio minimo"],
        ["VE", "REQUIRED", "Video/control VE sin LE, ST ni ExIn."],
        ["LE", "REQUIRED", "Internet USB sin VE, ST ni ExIn."],
        ["ST", "SUPPORTED limitado", "Abre y reporta ausencia de motores sin fallo."],
        ["ExIn", "REQUIRED", "Detecta/calibra mando sin depender de una ventana VE."],
        ["VE + ExIn", "REQUIRED", "Fullscreen/foco no corta la sesion ExIn."],
        ["VE + ST", "REQUIRED", "ST observa sin mutar VE."],
        ["LE + ST", "REQUIRED", "ST observa trafico, backpressure y recovery."],
        ["LE + ExIn", "SUPPORTED", "Ambos operan sin acoplamiento funcional."],
        ["VE + LE", "REQUIRED", "Carga simultanea sin control cruzado."],
        ["ST + ExIn", "SUPPORTED", "Salud ExIn sin necesidad de VE."],
        ["Trios y cuatro motores", "TRIAGE", "Clasificar ownership, recursos y utilidad antes de PASS."],
    ], [35 * mm, 42 * mm, 82 * mm]))

    add_section(story, "5", "Android, PC y sesion activa")
    story.append(p("La aplicacion Android y la aplicacion PC son productos coordinados, no dos vistas de un mismo proceso. Deben compartir protocolo versionado y capacidades, no clases internas ni estado duplicado."))
    for item in [
        "DeviceId identifica hardware; ConnectionId identifica transporte; SessionId identifica una ejecucion. No deben usarse como sinonimos.",
        "USB autoriza la relacion local; QR/LAN descubre o invita, pero no sustituye autenticacion ni confianza.",
        "CONTROL y DATA de LinkEngine se habilitan despues de confirmar la sesion de control correspondiente al telefono activo.",
        "Los estados Android se derivan de respuestas/eventos reales del PC; no muestran activo por haber pulsado un boton.",
        "MediaProjection se evalua como capacidad de app con consentimiento y lifecycle propios, no como reemplazo transparente de shell/app_process.",
        "Cada artefacto Android tiene versionCode, versionName, hash, manifest inspeccionado, APK canonico y descriptor de Release sincronizados.",
    ]:
        story.append(bullet(item))

    add_section(story, "6", "ADB y procesos externos")
    story.append(p("El objetivo es ejecucion controlada, no Process.Start = 0. Toda ejecucion se inventaria y pasa por una politica comun cuando sea razonable."))
    story.append(table([
        ["Control", "Requisito"],
        ["Ejecutable", "Ruta canonica validada y allowlist por capacidad."],
        ["Argumentos", "ArgumentList o equivalente estructurado; no concatenacion de shell."],
        ["Sesion", "Serial explicito y telefono activo validado antes de operar."],
        ["Lifecycle", "Timeout, CancellationToken, ExitCode, stdout/stderr y cleanup."],
        ["Concurrencia", "Coordinacion de ADB server, tunnels y procesos por propietario."],
        ["Auditoria", "Evento con comando logico, no secretos ni contenido privado."],
    ], [43 * mm, 116 * mm]))

    add_section(story, "7", "scrcpy: encapsulacion y retirada")
    story.append(p("scrcpy-server permanece como backend temporal mientras cumpla procedencia, version y hash. La retirada se ejecuta por capacidades, no por busqueda textual."))
    story.append(table([
        ["Gate", "Evidencia necesaria"],
        ["G0 Inventario", "Version real, SHA-256, licencia, rutas, argumentos y protocolo usado."],
        ["G1 Encapsulacion", "VE consume IVideoSource/IAudioSource/IControlChannel o contratos equivalentes aprobados."],
        ["G2 Android Gate", "ADR compara Public API, shell/app_process e hibrido por capacidad y Android."],
        ["G3 Paridad", "Video, audio, control, rotacion, clipboard autorizado, UHID y cleanup dentro del envelope."],
        ["G4 Fallback", "Cambio de backend conserva sesion o explica reinicio; fallo no afecta LE/ExIn."],
        ["G5 Retirada", "Runtime Dependency = 0, pruebas fisicas y licencias actualizadas."],
    ], [38 * mm, 121 * mm]))

    add_section(story, "8", "GPU y politica neutral de proveedor")
    story.append(p("NVIDIA, Intel, AMD y software se tratan como backends de una capacidad, no como identidad de VisionEngine. La UI informa el backend realmente activo y la razon del fallback."))
    story.append(table([
        ["Estado", "Significado permitido"],
        ["Disponible", "El runtime y el decoder existen; no demuestra uso."],
        ["Seleccionado", "La politica intentara ese backend al iniciar."],
        ["Activo", "Frames reales fueron decodificados por esa ruta."],
        ["Beneficio medido", "Benchmark comparable muestra mejora con P50/P95/P99 y workload definido."],
        ["Fallback", "Software u otro proveedor conserva funcionalidad y reporta causa."],
    ], [42 * mm, 117 * mm]))

    add_section(story, "9", "Operational Envelope y evidencia")
    story.append(p("No se fijan limites numericos universales antes del baseline. Cada perfil y dispositivo produce una distribucion comparable."))
    story.append(table([
        ["Capa", "Metricas minimas", "Prueba"],
        ["PC", "CPU, Working Set, Private Bytes, threads, handles, GPU, I/O", "Idle, arranque, carga y cleanup"],
        ["VE", "TTFF, FPS, frame time, drops, bitrate, decode/render latency", "Movimiento, rotacion, bloqueo y recovery"],
        ["LE", "Throughput, RTT, jitter, loss, queue occupancy/age, WouldBlock", "Idle y carga equivalente"],
        ["ExIn", "Input latency, mapping, perdida/reconexion, bateria", "Mando real y traduccion HID"],
        ["Android", "CPU, memoria, energia, servicio, permisos", "USB/LAN, foreground/background"],
        ["Global", "P50/P95/P99, varianza, errores y tiempo de recuperacion", "Motores solos y combinaciones soportadas"],
    ], [29 * mm, 73 * mm, 57 * mm]))

    add_section(story, "10", "Fault injection y Recovery")
    story.append(p("Fault injection se ejecuta en un entorno controlado y registra estado antes/durante/despues. Recovery es el ultimo recurso: congestión, WouldBlock y colas altas pertenecen al control de trafico."))
    story.append(table([
        ["Fallo", "Propietario", "Resultado esperado"],
        ["USB desconectado", "Transporte", "Sesion termina o espera; recursos y tunnels se limpian."],
        ["ADB muerto", "Infra Android", "Deteccion acotada; otros motores no relacionados sobreviven."],
        ["Servidor VE termina", "VE Recovery", "Clasifica canal/sesion y recupera solo VE."],
        ["Relay termina", "LE Recovery", "Libera puertos, evita proceso huerfano y recupera solo LE."],
        ["Fullscreen/foco", "UI + ExIn", "No pierde ownership ni traduccion ExIn."],
        ["Frame/protocolo invalido", "Protocolo", "Rechazo limitado, diagnostico y cierre seguro."],
    ], [39 * mm, 35 * mm, 85 * mm]))

    add_section(story, "11", "Seguridad, privacidad y procedencia")
    for item in [
        "Threat model minimo para USB, LAN/QR, ADB, tunnels loopback, Android Agent y actualizador.",
        "Capability negotiation no equivale a autenticacion; sesion y autorizacion se validan por separado.",
        "Maximum payload, timeouts, malformed frames, unknown messages y version mismatch tienen comportamiento definido.",
        "Logs separan diagnostico de contenido: no registran tokens, OTP, clipboard, rutas privadas completas o payloads por defecto.",
        "THIRD_PARTY_MANIFEST registra componente, version, origen, licencia, hash, modified, derived, distributed y owner.",
        "La publicacion no recomienda desactivar Defender ni oculta advertencias de firma.",
    ]:
        story.append(bullet(item))

    add_section(story, "12", "Estado observado de la base 1.4.28")
    story.append(p("Snapshot de lectura del 23 de septiembre de 2026. Estos registros son DETECTED hasta completar triage formal."))
    story.append(table([
        ["ID", "Prioridad", "Deteccion", "Siguiente evidencia"],
        ["NOV-AUD-0001", "HIGH", "ST captura VE mediante un evaluador que muta estado interno de bitrate.", "Separar captura pura y demostrar que ST no cambia VE."],
        ["NOV-AUD-0002", "HIGH", "ST depende de clases concretas de VE/LE/ExIn.", "Mapa de contratos y prueba de motores ausentes."],
        ["NOV-AUD-0003", "HIGH", "ExIn y VE mantienen dependencias directas bidireccionales.", "Ownership de sesion/control y adaptador neutral."],
        ["NOV-AUD-0004", "HIGH", "VE requiere scrcpy-server 4.1 en runtime.", "Registrar como dependencia temporal y ejecutar gates G0-G5."],
        ["NOV-AUD-0005", "MEDIUM", "Politica de un telefono activo contradice documento de cinco sesiones.", "ADR de capacidad y modelo de sesion."],
        ["NOV-AUD-0006", "MEDIUM", "Varios lanzadores de procesos externos fuera de una interfaz comun.", "Process map, allowlist y clasificacion por riesgo."],
        ["NOV-AUD-0007", "MEDIUM", "No existe paquete formal A/B ni ADR 001-008.", "Crear entregables sin refactorizar runtime."],
        ["NOV-AUD-0008", "LOW", "RelayCore pasa tests con warnings y dependencia futura incompatible.", "Triage Rust y plan de actualizacion separado."],
    ], [27 * mm, 24 * mm, 63 * mm, 45 * mm]))
    story.append(p("Evidencia ejecutada: build Release de solucion PC 0 warnings/0 errores; 389/389 pruebas .NET; Android Compile 0 warnings/0 errores; RelayCore 34 pass, 1 ignored y 21 warnings. No hubo APK, instalacion, prueba fisica, benchmark ni fault injection."))

    add_section(story, "13", "Paquete inicial A/B")
    story.append(p("La primera entrega de auditoria no cambia arquitectura. Debe producir exactamente estos artefactos y conservar el working tree actual."))
    story.append(table([
        ["Entregable", "Contenido minimo", "Gate"],
        ["A1 Baseline", "HEAD, branch, status, diffs, untracked, OS/toolchain y hashes.", "Reproducible"],
        ["A2 Runtime", "Procesos, puertos, telefonos, versiones, estados de motores.", "Una sesion identificada"],
        ["B1 Project graph", "Proyectos, packages, binarios, procesos y direccion de dependencias.", "Sin nodos desconocidos"],
        ["B2 Motor map", "Owner, archivos, inicio, recursos, datos, ADB/scrcpy y modo standalone.", "VE/LE/ST/ExIn completos"],
        ["B3 State map", "Fuente autoritativa de Device/Connection/Session/FPS/bitrate/orientacion.", "Duplicados triaged"],
        ["B4 Storage map", "PC/Android: owner, creator, readers, lifetime y cleanup.", "Temporales identificados"],
        ["B5 Process map", "Ejecutable, ruta, argumentos, caller, timeout y cleanup.", "Todos clasificados"],
        ["B6 Findings", "DETECTED -> TRIAGED -> CONFIRMED con NOV-AUD-XXXX.", "Sin scan tratado como hallazgo"],
    ], [35 * mm, 86 * mm, 38 * mm]))

    add_section(story, "14", "Secuencia revisada de ejecucion")
    story.append(table([
        ["Fase", "Salida", "Prohibicion de avance"],
        ["A Preservacion", "Baseline reproducible", "No refactor sin rollback verificable"],
        ["B Descubrimiento", "Mapas y detecciones", "No convertir scans en conclusiones"],
        ["C Auditoria", "Hallazgos confirmados", "No diseñar target desde supuestos"],
        ["D Actual", "Arquitectura real", "No omitir Android/runtime/storage"],
        ["E ADR", "Decisiones 001-008 y adicionales", "No decidir por preferencia"],
        ["F-G Target/refactor", "Contratos y migraciones pequenas", "No integracion parcial"],
        ["H-K scrcpy/Android/protocolo", "Backend propio por capacidades", "No retirar fallback sin paridad"],
        ["L-O validacion/retiro", "Envelope, fault tests y Release", "No publicar evidencia incompleta"],
    ], [25 * mm, 70 * mm, 64 * mm]))

    add_section(story, "15", "Definition of Done por cambio")
    for item in [
        "Requisito y propietario identificados; dependencias y riesgos descritos.",
        "Cambio completo en PC, Android, contratos, UI, pruebas y runtime cuando corresponda.",
        "Build y tests relevantes ejecutados secuencialmente cuando comparten outputs o puertos.",
        "Prueba de fallo y cleanup para cambios de lifecycle, transporte o recovery.",
        "Version, manifest, descriptor, APK canonico y hash sincronizados en cambios Android empaquetados.",
        "Prueba fisica obligatoria antes de afirmar conectado, funcional, rendimiento o compatibilidad.",
        "Documentacion y THIRD_PARTY_MANIFEST actualizados si cambia procedencia o distribucion.",
        "Sin residuos generados ni archivos fuera de la nomenclatura oficial.",
    ]:
        story.append(bullet(item))

    add_section(story, "16", "Plantillas de auditoria")
    story.append(p("Hallazgo NOV-AUD", "H2N"))
    story.append(table([
        ["Campo", "Valor requerido"],
        ["ID / estado", "NOV-AUD-XXXX / DETECTED, TRIAGED, CONFIRMED o RESOLVED"],
        ["Alcance", "Componente, archivo, motor, version y entorno"],
        ["Evidencia", "Comando, salida, reproduccion, linea o medicion"],
        ["Impacto", "Usuario, estabilidad, seguridad, rendimiento o mantenimiento"],
        ["Accion", "KEEP, MOVE, SPLIT, REPLACE o DELETE"],
        ["Cierre", "Prueba que falla antes/pasa despues y evidencia de runtime aplicable"],
    ], [45 * mm, 114 * mm]))
    story.append(p("ADR", "H2N"))
    story.append(p("Contexto; opciones; evidencia; decision; consecuencias; alternativas rechazadas; compatibilidad; migracion/rollback; fecha; owner; estado."))
    story.append(p("Registro de prueba", "H2N"))
    story.append(p("Commit y working tree; entorno PC/Android; telefono/serial; comando; inicio/fin; resultado; archivos producidos; limitaciones; hash de evidencia."))

    add_section(story, "17", "Criterios finales para NOVORA general")
    for item in [
        "Los motores operan individualmente en todos los escenarios REQUIRED y fallan honestamente en los no soportados.",
        "No existen ciclos indebidos entre motores y la infraestructura compartida tiene owner neutral.",
        "Cada dato importante tiene una fuente autoritativa y un lifecycle verificable.",
        "STEngine puede observar todos los motores sin alterar sus estados ni decisiones internas.",
        "ADB y procesos externos estan abstraidos, validados, limitados y auditables.",
        "scrcpy deja de ser requisito funcional sólo despues de paridad, fallback y pruebas fisicas.",
        "NVIDIA/AMD/Intel/software reportan disponibilidad, activacion y beneficio sin confundirlos.",
        "Android y PC comparten protocolo versionado y conservan ownership independiente.",
        "Recovery, cleanup y fault injection cubren cierre normal, fallo y proceso huerfano.",
        "La Release contiene solamente funciones con evidencia correspondiente a la afirmacion publica.",
    ]:
        story.append(bullet(item))
    story.append(p("Criterio de aprobacion", "Callout"))
    story.append(p("NOVORA no se declara independiente por estructura de carpetas, compilacion verde o ausencia textual de scrcpy. Se declara independiente cuando las dependencias de runtime, ownership, lifecycle y escenarios soportados han sido medidas, documentadas y verificadas en los entornos que se anuncian."))

    add_section(story, "18", "Fuentes y trazabilidad")
    story.append(p("Fuente principal: NOVORA-LINK Guia Maestra de Independencia Tecnologica y Auditoria v1.1, 23 septiembre 2026."))
    story.append(p("Fuentes primarias conservadas: Genymobile/scrcpy develop.md y LICENSE; Android Developers para Android 14, MediaProjection y MediaCodec. Evidencia local: repositorio NOVORA-LINK, reglas de arquitectura, procedencia de scrcpy-server, proyectos, codigo de motores y suites de pruebas inspeccionadas el 23 septiembre 2026."))
    story.append(p("Proxima accion autorizable", "H2N"))
    story.append(p("Ejecutar y guardar el paquete A/B en un directorio de auditoria separado, sin reestructurar proyectos ni implementar Android Agent. El resultado debe presentarse para triage y aprobacion antes de iniciar ADR o refactor."))

    return story


def main():
    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    doc = BaseDocTemplate(
        OUTPUT,
        pagesize=A4,
        leftMargin=18 * mm,
        rightMargin=18 * mm,
        topMargin=17 * mm,
        bottomMargin=18 * mm,
        title="NOVORA-LINK Guia Maestra de Independencia y Auditoria v1.2 - Complemento",
        author="NOVORA-LINK",
        subject="Complemento general de auditoria e independencia tecnologica",
    )
    frame = Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="main")
    doc.addPageTemplates([PageTemplate(id="main", frames=[frame], onPage=footer)])
    doc.build(build_story())
    print(OUTPUT)


if __name__ == "__main__":
    main()
