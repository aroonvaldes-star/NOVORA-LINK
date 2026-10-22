import React, { useState, useEffect, useRef } from 'react';
import { ScreenType } from '../types/index.ts';

interface LiveTestScreenProps {
  onNavigate: (screen: ScreenType) => void;
  onShowToast: (msg: string) => void;
}

export const LiveTestScreen: React.FC<LiveTestScreenProps> = ({ onShowToast }) => {
  // Gamepad state
  const [stickL, setStickL] = useState({ x: 0.04, y: 0.7 });
  const [stickR, setStickR] = useState({ x: 0.0, y: 0.0 });
  const [ltVal, setLtVal] = useState(67);
  const [rtVal, setRtVal] = useState(12);
  const [lbPressed, setLbPressed] = useState(false);
  const [rbPressed, setRbPressed] = useState(false);
  const [lastPressed, setLastPressed] = useState('X (Presionado)');
  const [activeButtons, setActiveButtons] = useState<{ [key: string]: boolean }>({
    x: true,
  });
  const [dpad, setDpad] = useState({ up: false, down: false, left: true, right: false });

  // Calibration Wizard state
  const [calibrating, setCalibrating] = useState(false);
  const [activeCalibStep, setActiveCalibStep] = useState(3); // 1, 2, 3, 4
  const [isGamepadReal, setIsGamepadReal] = useState(false);
  const isDraggingL = useRef(false);
  const isDraggingR = useRef(false);

  // Poll real gamepads if available, otherwise gentle realistic demonstration simulation
  useEffect(() => {
    let animId: number;
    let angle = 0;

    const checkGamepads = () => {
      const gamepads = navigator.getGamepads ? navigator.getGamepads() : [];
      const gp = gamepads && (gamepads[0] || gamepads[1] || gamepads[2] || gamepads[3]);

      if (gp && gp.connected) {
        setIsGamepadReal(true);
        // Map axes
        const lx = Number(gp.axes[0]?.toFixed(2) || 0);
        const ly = Number(gp.axes[1]?.toFixed(2) || 0);
        const rx = Number(gp.axes[2]?.toFixed(2) || 0);
        const ry = Number(gp.axes[3]?.toFixed(2) || 0);
        setStickL({ x: lx, y: ly });
        setStickR({ x: rx, y: ry });

        // Map buttons
        const btnA = gp.buttons[0]?.pressed || false;
        const btnB = gp.buttons[1]?.pressed || false;
        const btnX = gp.buttons[2]?.pressed || false;
        const btnY = gp.buttons[3]?.pressed || false;
        const btnLb = gp.buttons[4]?.pressed || false;
        const btnRb = gp.buttons[5]?.pressed || false;
        const btnLt = Math.round((gp.buttons[6]?.value || 0) * 100);
        const btnRt = Math.round((gp.buttons[7]?.value || 0) * 100);
        const btnView = gp.buttons[8]?.pressed || false;
        const btnMenu = gp.buttons[9]?.pressed || false;

        const dUp = gp.buttons[12]?.pressed || false;
        const dDown = gp.buttons[13]?.pressed || false;
        const dLeft = gp.buttons[14]?.pressed || false;
        const dRight = gp.buttons[15]?.pressed || false;

        setLtVal(btnLt);
        setRtVal(btnRt);
        setLbPressed(btnLb);
        setRbPressed(btnRb);
        setDpad({ up: dUp, down: dDown, left: dLeft, right: dRight });

        const pressedList: { [k: string]: boolean } = {};
        if (btnA) pressedList.a = true;
        if (btnB) pressedList.b = true;
        if (btnX) pressedList.x = true;
        if (btnY) pressedList.y = true;
        if (btnView) pressedList.view = true;
        if (btnMenu) pressedList.menu = true;
        setActiveButtons(pressedList);

        if (btnA) setLastPressed('A (Presionado)');
        else if (btnB) setLastPressed('B (Presionado)');
        else if (btnX) setLastPressed('X (Presionado)');
        else if (btnY) setLastPressed('Y (Presionado)');
        else if (btnLb) setLastPressed('LB (Presionado)');
        else if (btnRb) setLastPressed('RB (Presionado)');
      } else {
        setIsGamepadReal(false);
        // Simulation animation if not manually dragging
        if (!isDraggingL.current && !isDraggingR.current) {
          angle += 0.04;
          const simX = Math.sin(angle) * 0.7;
          const simY = Math.cos(angle * 0.8) * 0.6;
          setStickL({ x: Number(simX.toFixed(2)), y: Number(simY.toFixed(2)) });

          const simLt = Math.abs(Math.sin(angle * 0.5) * 85 + 5);
          setLtVal(Math.round(simLt));

          const simRt = Math.abs(Math.cos(angle * 0.3) * 60);
          setRtVal(Math.round(simRt));
        }
      }

      animId = requestAnimationFrame(checkGamepads);
    };

    animId = requestAnimationFrame(checkGamepads);
    return () => cancelAnimationFrame(animId);
  }, []);

  // Trigger button click simulation
  const handlePressButton = (name: string) => {
    setActiveButtons((prev) => ({ ...prev, [name]: true }));
    setLastPressed(`${name.toUpperCase()} (Presionado)`);
    setTimeout(() => {
      setActiveButtons((prev) => ({ ...prev, [name]: false }));
    }, 250);
  };

  // Calibration Wizard run
  const handleStartCalibration = () => {
    setCalibrating(true);
    setActiveCalibStep(1);
    onShowToast('Paso 1: Suelte ambos joysticks para centro neutro...');

    setTimeout(() => {
      setActiveCalibStep(2);
      onShowToast('Paso 2: Gire ambos joysticks 360° en sus bordes...');
    }, 2000);

    setTimeout(() => {
      setActiveCalibStep(3);
      onShowToast('Paso 3: Presione LT / RT al máximo fondo...');
    }, 4500);

    setTimeout(() => {
      setActiveCalibStep(4);
      onShowToast('Paso 4: Calculando y guardando perfil...');
    }, 6500);

    setTimeout(() => {
      setCalibrating(false);
      onShowToast('Calibración completada con éxito • Curva de respuesta optimizada');
    }, 8500);
  };

  return (
    <div className="w-full max-w-2xl mx-auto flex-1 flex flex-col bg-surface-container-lowest shadow-xs pb-24">
      {/* Estado del Control Workflow: Detectado -> Calibrado -> Listo */}
      <section className="p-4 bg-surface-container-low border-b border-outline-variant/30">
        <div className="flex items-center justify-between relative max-w-md mx-auto">
          <div className="absolute left-6 right-6 top-1/2 -translate-y-1/2 h-0.5 bg-outline-variant -z-0"></div>
          <div className="absolute left-6 right-1/2 top-1/2 -translate-y-1/2 h-0.5 bg-primary -z-0"></div>
          <div className="absolute left-1/2 right-6 top-1/2 -translate-y-1/2 h-0.5 bg-primary -z-0"></div>

          {/* Paso 1 */}
          <div className="flex flex-col items-center gap-1 z-10">
            <div className="flex size-8 rounded-full bg-primary text-on-primary items-center justify-center ring-4 ring-surface-container-low shadow-xs">
              <span className="material-symbols-outlined text-[18px] material-symbols-filled">
                check_circle
              </span>
            </div>
            <span className="text-xs font-headline font-bold text-on-surface">Detectado</span>
          </div>

          {/* Paso 2 */}
          <div className="flex flex-col items-center gap-1 z-10">
            <div className="flex size-8 rounded-full bg-primary text-on-primary items-center justify-center ring-4 ring-surface-container-low shadow-xs">
              <span className="material-symbols-outlined text-[18px] material-symbols-filled">
                check_circle
              </span>
            </div>
            <span className="text-xs font-headline font-bold text-on-surface">Calibrado</span>
          </div>

          {/* Paso 3 */}
          <div className="flex flex-col items-center gap-1 z-10">
            <div className="flex size-8 rounded-full bg-primary-container text-on-primary-container items-center justify-center ring-4 ring-surface-container-low shadow-xs border border-primary">
              <span className="material-symbols-outlined text-[18px] material-symbols-filled">
                done_all
              </span>
            </div>
            <span className="text-xs font-headline font-bold text-primary">Listo</span>
          </div>
        </div>
      </section>

      {/* Especificaciones de Dispositivo Real */}
      <section className="p-4 grid grid-cols-2 gap-3 text-xs font-body border-b border-outline-variant/30 bg-surface-container-lowest">
        <div className="flex flex-col p-2.5 rounded bg-surface-container-low">
          <span className="text-outline font-label uppercase text-[10px] tracking-wider mb-0.5">
            Dispositivo
          </span>
          <span className="text-on-surface font-semibold truncate">
            {isGamepadReal ? 'Control Físico Conectado' : 'Xbox Wireless Controller'}
          </span>
        </div>
        <div className="flex flex-col p-2.5 rounded bg-surface-container-low">
          <span className="text-outline font-label uppercase text-[10px] tracking-wider mb-0.5">
            Conexión
          </span>
          <span className="text-primary font-semibold flex items-center gap-1">
            <span className="material-symbols-outlined text-[14px]">bluetooth</span>
            Bluetooth (Baja Latencia)
          </span>
        </div>
        <div className="flex flex-col p-2.5 rounded bg-surface-container-low">
          <span className="text-outline font-label uppercase text-[10px] tracking-wider mb-0.5">
            VID / PID
          </span>
          <span className="text-on-surface font-label font-medium">045E : 02FD</span>
        </div>
        <div className="flex flex-col p-2.5 rounded bg-surface-container-low">
          <span className="text-outline font-label uppercase text-[10px] tracking-wider mb-0.5">
            Perfil Activo
          </span>
          <span className="text-secondary font-semibold truncate">Default Gaming Profile</span>
        </div>
      </section>

      {/* Diagnóstico Operacional: Telemetría Rápida */}
      <section className="px-4 py-3 bg-surface-container flex items-center justify-between border-b border-outline-variant/30 text-xs">
        <div className="flex items-center gap-2">
          <span className="material-symbols-outlined text-primary text-[18px]">verified</span>
          <span className="font-headline font-semibold text-on-surface">Diagnóstico de Sensor:</span>
        </div>
        <div className="flex items-center gap-4 font-label">
          <div className="flex items-center gap-1">
            <span className="text-outline">Drift:</span>
            <span className="font-semibold text-primary">0.02%</span>
          </div>
          <div className="flex items-center gap-1">
            <span className="text-outline">Deadzone:</span>
            <span className="font-semibold text-on-surface">5.0%</span>
          </div>
        </div>
      </section>

      {/* Live Input Test (Visualizador Interactivo de Hardware) */}
      <section className="p-4 flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-on-surface text-base font-headline font-bold leading-tight">
              Prueba en Vivo (Live Input Test)
            </h2>
            <p className="text-xs text-outline">Monitoreo de señales físicas en tiempo real</p>
          </div>
          <span className="flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[11px] font-label font-semibold bg-primary-container/20 text-primary border border-primary/30">
            <span className="size-2 rounded-full bg-primary animate-pulse"></span> LIVE 250Hz
          </span>
        </div>

        {/* Diagrama Representativo de Mandos */}
        <div className="bg-surface-container-low rounded-xl p-4 border border-outline-variant/40">
          {/* Gatillos Analógicos LT / RT y Bumpers LB / RB */}
          <div className="grid grid-cols-2 gap-4 pb-4 border-b border-outline-variant/30">
            {/* Lado Izquierdo: LT & LB */}
            <div className="flex flex-col gap-2">
              <div className="flex justify-between items-center text-xs">
                <span className="font-headline font-bold text-on-surface flex items-center gap-1.5">
                  <button
                    onClick={() => handlePressButton('lb')}
                    className={`px-2 py-0.5 rounded font-label border transition-colors cursor-pointer ${
                      lbPressed || activeButtons.lb
                        ? 'bg-primary text-on-primary border-primary'
                        : 'bg-surface-container-highest border-outline-variant'
                    }`}
                  >
                    LB
                  </button>
                  <span>Gatillo LT</span>
                </span>
                <span className="font-label text-primary font-bold text-[11px]">{ltVal}%</span>
              </div>
              <div className="w-full h-3 bg-surface-container-highest rounded-full overflow-hidden">
                <div
                  className="h-full bg-primary transition-all duration-75 rounded-full"
                  style={{ width: `${ltVal}%` }}
                ></div>
              </div>
            </div>

            {/* Lado Derecho: RT & RB */}
            <div className="flex flex-col gap-2">
              <div className="flex justify-between items-center text-xs">
                <span className="font-headline font-bold text-on-surface flex items-center gap-1.5">
                  <span>Gatillo RT</span>
                  <button
                    onClick={() => handlePressButton('rb')}
                    className={`px-2 py-0.5 rounded font-label border transition-colors cursor-pointer ${
                      rbPressed || activeButtons.rb
                        ? 'bg-primary text-on-primary border-primary'
                        : 'bg-surface-container-highest border-outline-variant'
                    }`}
                  >
                    RB
                  </button>
                </span>
                <span className="font-label text-primary font-bold text-[11px]">{rtVal}%</span>
              </div>
              <div className="w-full h-3 bg-surface-container-highest rounded-full overflow-hidden">
                <div
                  className="h-full bg-primary transition-all duration-75 rounded-full"
                  style={{ width: `${rtVal}%` }}
                ></div>
              </div>
            </div>
          </div>

          {/* Cuerpo del Gamepad: Sticks, D-Pad, Botones Frontales */}
          <div className="grid grid-cols-3 gap-2 pt-4 items-center">
            {/* Sección Izquierda: Stick Izquierdo (L) + D-Pad */}
            <div className="flex flex-col items-center gap-3">
              <span className="text-[11px] font-headline font-bold text-on-surface uppercase tracking-wider">
                Stick L (Analógico)
              </span>

              {/* Visor Coordenadas L Stick */}
              <div
                onMouseDown={() => (isDraggingL.current = true)}
                onMouseUp={() => (isDraggingL.current = false)}
                onMouseMove={(e) => {
                  if (!isDraggingL.current) return;
                  const rect = e.currentTarget.getBoundingClientRect();
                  const cx = rect.width / 2;
                  const cy = rect.height / 2;
                  const x = (e.clientX - rect.left - cx) / cx;
                  const y = (e.clientY - rect.top - cy) / cy;
                  setStickL({
                    x: Number(Math.max(-1, Math.min(1, x)).toFixed(2)),
                    y: Number(Math.max(-1, Math.min(1, y)).toFixed(2)),
                  });
                }}
                className="relative size-28 rounded-full bg-surface-container-highest border-2 border-outline-variant flex items-center justify-center shadow-inner cursor-crosshair"
              >
                {/* Deadzone Ring */}
                <div className="absolute size-9 rounded-full border border-dashed border-outline pointer-events-none"></div>
                {/* Crosshair Grid */}
                <div className="absolute w-full h-[1px] bg-outline-variant pointer-events-none"></div>
                <div className="absolute h-full w-[1px] bg-outline-variant pointer-events-none"></div>
                {/* Pointer Stick */}
                <div
                  className="size-7 rounded-full bg-primary border-2 border-on-primary shadow-md flex items-center justify-center transition-transform duration-75"
                  style={{
                    transform: `translate(${stickL.x * 32}px, ${stickL.y * 32}px)`,
                  }}
                >
                  <div className="size-2 rounded-full bg-white"></div>
                </div>
              </div>

              <div className="font-label text-[10px] text-outline flex gap-2">
                <span>
                  X: <strong className="text-on-surface">{stickL.x.toFixed(2)}</strong>
                </span>
                <span>
                  Y: <strong className="text-on-surface">{stickL.y.toFixed(2)}</strong>
                </span>
              </div>

              {/* Mini D-Pad */}
              <div className="relative size-16 grid grid-cols-3 grid-rows-3 gap-0.5 p-1 bg-surface-container rounded-lg border border-outline-variant mt-1">
                <div></div>
                <button
                  onClick={() => handlePressButton('up')}
                  className={`rounded-t flex items-center justify-center cursor-pointer transition-colors ${
                    dpad.up || activeButtons.up ? 'bg-primary text-on-primary' : 'bg-surface-container-high'
                  }`}
                >
                  <span className="material-symbols-outlined text-[14px]">arrow_drop_up</span>
                </button>
                <div></div>
                <button
                  onClick={() => handlePressButton('left')}
                  className={`rounded-l flex items-center justify-center cursor-pointer transition-colors ${
                    dpad.left || activeButtons.left ? 'bg-primary text-on-primary' : 'bg-surface-container-high'
                  }`}
                >
                  <span className="material-symbols-outlined text-[14px]">arrow_left</span>
                </button>
                <div className="bg-surface-container-highest"></div>
                <button
                  onClick={() => handlePressButton('right')}
                  className={`rounded-r flex items-center justify-center cursor-pointer transition-colors ${
                    dpad.right || activeButtons.right ? 'bg-primary text-on-primary' : 'bg-surface-container-high'
                  }`}
                >
                  <span className="material-symbols-outlined text-[14px]">arrow_right</span>
                </button>
                <div></div>
                <button
                  onClick={() => handlePressButton('down')}
                  className={`rounded-b flex items-center justify-center cursor-pointer transition-colors ${
                    dpad.down || activeButtons.down ? 'bg-primary text-on-primary' : 'bg-surface-container-high'
                  }`}
                >
                  <span className="material-symbols-outlined text-[14px]">arrow_drop_down</span>
                </button>
                <div></div>
              </div>
            </div>

            {/* Centro: Indicadores Centrales & Estado Funcional */}
            <div className="flex flex-col items-center justify-center gap-3">
              <div className="flex items-center gap-2">
                <button
                  onClick={() => handlePressButton('view')}
                  className={`size-6 rounded-full border flex items-center justify-center text-[10px] font-label transition-colors cursor-pointer ${
                    activeButtons.view
                      ? 'bg-primary text-on-primary border-primary'
                      : 'bg-surface-container-highest border-outline-variant text-on-surface'
                  }`}
                >
                  ⧉
                </button>
                <button
                  onClick={() => handlePressButton('home')}
                  className="size-8 rounded-full bg-primary text-on-primary flex items-center justify-center shadow-md active:scale-95 transition-transform cursor-pointer"
                >
                  <span className="material-symbols-outlined text-[18px]">videogame_asset</span>
                </button>
                <button
                  onClick={() => handlePressButton('menu')}
                  className={`size-6 rounded-full border flex items-center justify-center text-[10px] font-label transition-colors cursor-pointer ${
                    activeButtons.menu
                      ? 'bg-primary text-on-primary border-primary'
                      : 'bg-surface-container-highest border-outline-variant text-on-surface'
                  }`}
                >
                  ≡
                </button>
              </div>

              <div className="px-3 py-1.5 rounded bg-surface-container-highest border border-outline-variant text-center w-full max-w-[120px]">
                <span className="block text-[9px] font-label uppercase text-outline">Latencia</span>
                <span className="font-label font-bold text-xs text-primary">3.2 ms</span>
              </div>

              <div className="text-[10px] text-outline text-center">
                Pulsando: <span className="font-headline font-bold text-primary">{lastPressed}</span>
              </div>
            </div>

            {/* Sección Derecha: Botones ABXY + Stick Derecho (R) */}
            <div className="flex flex-col items-center gap-3">
              {/* Botones de Acción A/B/X/Y */}
              <div className="relative size-20 grid grid-cols-3 grid-rows-3 gap-1 items-center justify-items-center">
                <div></div>
                <button
                  onClick={() => handlePressButton('y')}
                  className={`size-6 rounded-full border font-headline font-bold text-xs flex items-center justify-center cursor-pointer transition-colors ${
                    activeButtons.y
                      ? 'bg-primary text-on-primary border-primary shadow-xs'
                      : 'bg-surface-container-high border-outline-variant text-on-surface'
                  }`}
                >
                  Y
                </button>
                <div></div>
                <button
                  onClick={() => handlePressButton('x')}
                  className={`size-6 rounded-full border font-headline font-bold text-xs flex items-center justify-center cursor-pointer transition-colors ${
                    activeButtons.x
                      ? 'bg-primary text-on-primary border-primary shadow-xs'
                      : 'bg-surface-container-high border-outline-variant text-on-surface'
                  }`}
                >
                  X
                </button>
                <div></div>
                <button
                  onClick={() => handlePressButton('b')}
                  className={`size-6 rounded-full border font-headline font-bold text-xs flex items-center justify-center cursor-pointer transition-colors ${
                    activeButtons.b
                      ? 'bg-primary text-on-primary border-primary shadow-xs'
                      : 'bg-surface-container-high border-outline-variant text-on-surface'
                  }`}
                >
                  B
                </button>
                <div></div>
                <button
                  onClick={() => handlePressButton('a')}
                  className={`size-6 rounded-full border font-headline font-bold text-xs flex items-center justify-center cursor-pointer transition-colors ${
                    activeButtons.a
                      ? 'bg-primary text-on-primary border-primary shadow-xs'
                      : 'bg-surface-container-high border-outline-variant text-on-surface'
                  }`}
                >
                  A
                </button>
                <div></div>
              </div>

              <span className="text-[11px] font-headline font-bold text-on-surface uppercase tracking-wider mt-1">
                Stick R (Analógico)
              </span>

              {/* Visor Coordenadas R Stick */}
              <div
                onMouseDown={() => (isDraggingR.current = true)}
                onMouseUp={() => (isDraggingR.current = false)}
                onMouseMove={(e) => {
                  if (!isDraggingR.current) return;
                  const rect = e.currentTarget.getBoundingClientRect();
                  const cx = rect.width / 2;
                  const cy = rect.height / 2;
                  const x = (e.clientX - rect.left - cx) / cx;
                  const y = (e.clientY - rect.top - cy) / cy;
                  setStickR({
                    x: Number(Math.max(-1, Math.min(1, x)).toFixed(2)),
                    y: Number(Math.max(-1, Math.min(1, y)).toFixed(2)),
                  });
                }}
                className="relative size-28 rounded-full bg-surface-container-highest border-2 border-outline-variant flex items-center justify-center shadow-inner cursor-crosshair"
              >
                {/* Deadzone Ring */}
                <div className="absolute size-9 rounded-full border border-dashed border-outline pointer-events-none"></div>
                {/* Crosshair Grid */}
                <div className="absolute w-full h-[1px] bg-outline-variant pointer-events-none"></div>
                <div className="absolute h-full w-[1px] bg-outline-variant pointer-events-none"></div>
                {/* Pointer Stick */}
                <div
                  className="size-7 rounded-full bg-secondary border-2 border-on-secondary shadow-md flex items-center justify-center transition-transform duration-75"
                  style={{
                    transform: `translate(${stickR.x * 32}px, ${stickR.y * 32}px)`,
                  }}
                >
                  <div className="size-2 rounded-full bg-secondary-container"></div>
                </div>
              </div>

              <div className="font-label text-[10px] text-outline flex gap-2">
                <span>
                  X: <strong className="text-on-surface">{stickR.x.toFixed(2)}</strong>
                </span>
                <span>
                  Y: <strong className="text-on-surface">{stickR.y.toFixed(2)}</strong>
                </span>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Módulo de Calibración Automática Guiada */}
      <section className="p-4 pt-1">
        <div className="bg-surface-container-low rounded-xl border border-outline-variant/40 p-4">
          <div className="flex items-center justify-between mb-3">
            <div className="flex items-center gap-2">
              <span className="material-symbols-outlined text-primary text-[20px]">tune</span>
              <h3 className="font-headline font-bold text-sm text-on-surface">Calibración Automática del Engine</h3>
            </div>
            <span className="text-[11px] font-label font-semibold text-primary px-2 py-0.5 rounded bg-primary-container/20 border border-primary/30">
              4 Pasos
            </span>
          </div>

          {/* Pasos Sugeridos */}
          <ol className="space-y-2 text-xs font-body mb-4">
            <li
              className={`flex items-start gap-2.5 p-2 rounded bg-surface-container-lowest border transition-colors ${
                activeCalibStep === 1
                  ? 'border-primary ring-1 ring-primary/40'
                  : 'border-outline-variant/30'
              }`}
            >
              <span
                className={`flex size-5 rounded-full text-[10px] font-label font-bold items-center justify-center shrink-0 ${
                  activeCalibStep > 1
                    ? 'bg-primary text-on-primary'
                    : activeCalibStep === 1
                    ? 'bg-primary-container text-on-primary-container animate-pulse'
                    : 'bg-surface-container text-outline'
                }`}
              >
                1
              </span>
              <div className="flex-1">
                <span className="font-semibold text-on-surface">Centrar sticks analógicos:</span>
                <p className="text-outline text-[11px]">Suelte ambos joysticks para establecer el centro neutro de compensación.</p>
              </div>
              {activeCalibStep > 1 && (
                <span className="material-symbols-outlined text-primary text-[16px]">check</span>
              )}
            </li>

            <li
              className={`flex items-start gap-2.5 p-2 rounded bg-surface-container-lowest border transition-colors ${
                activeCalibStep === 2
                  ? 'border-primary ring-1 ring-primary/40'
                  : 'border-outline-variant/30'
              }`}
            >
              <span
                className={`flex size-5 rounded-full text-[10px] font-label font-bold items-center justify-center shrink-0 ${
                  activeCalibStep > 2
                    ? 'bg-primary text-on-primary'
                    : activeCalibStep === 2
                    ? 'bg-primary-container text-on-primary-container animate-pulse'
                    : 'bg-surface-container text-outline'
                }`}
              >
                2
              </span>
              <div className="flex-1">
                <span className="font-semibold text-on-surface">Mover recorrido completo:</span>
                <p className="text-outline text-[11px]">Gire ambos sticks 360° en sus bordes físicos para delimitar los ejes máximos.</p>
              </div>
              {activeCalibStep > 2 && (
                <span className="material-symbols-outlined text-primary text-[16px]">check</span>
              )}
            </li>

            <li
              className={`flex items-start gap-2.5 p-2 rounded bg-surface-container-lowest border transition-colors ${
                activeCalibStep === 3
                  ? 'border-secondary ring-1 ring-secondary/40'
                  : 'border-outline-variant/30'
              }`}
            >
              <span
                className={`flex size-5 rounded-full text-[10px] font-label font-bold items-center justify-center shrink-0 ${
                  activeCalibStep > 3
                    ? 'bg-primary text-on-primary'
                    : activeCalibStep === 3
                    ? 'bg-secondary text-on-secondary'
                    : 'bg-surface-container text-outline'
                }`}
              >
                3
              </span>
              <div className="flex-1">
                <span className="font-semibold text-on-surface">Presionar LT / RT al máximo:</span>
                <p className="text-outline text-[11px]">Oprima ambos gatillos a fondo para registrar el recorrido analógico 0 - 255.</p>
              </div>
              {activeCalibStep === 3 && (
                <span className="text-[10px] font-label font-semibold text-secondary">ACTIVO</span>
              )}
              {activeCalibStep > 3 && (
                <span className="material-symbols-outlined text-primary text-[16px]">check</span>
              )}
            </li>

            <li
              className={`flex items-start gap-2.5 p-2 rounded bg-surface-container-lowest border transition-colors ${
                activeCalibStep === 4
                  ? 'border-primary ring-1 ring-primary/40'
                  : 'border-outline-variant/30 opacity-75'
              }`}
            >
              <span
                className={`flex size-5 rounded-full text-[10px] font-label font-bold items-center justify-center shrink-0 ${
                  activeCalibStep === 4
                    ? 'bg-primary text-on-primary animate-pulse'
                    : 'bg-surface-container text-outline'
                }`}
              >
                4
              </span>
              <div className="flex-1">
                <span className="font-semibold text-on-surface">Calcular valores y guardar perfil:</span>
                <p className="text-outline text-[11px]">Guardar curva de respuesta y aplicar a 'Default Gaming Profile'.</p>
              </div>
              {activeCalibStep < 4 ? (
                <span className="material-symbols-outlined text-outline text-[16px]">hourglass_empty</span>
              ) : (
                <span className="material-symbols-outlined text-primary text-[16px]">check</span>
              )}
            </li>
          </ol>

          {/* Botones de Ejecución / Recalibrar */}
          <div className="flex gap-2">
            <button
              onClick={handleStartCalibration}
              disabled={calibrating}
              className="flex-1 py-2.5 px-4 bg-primary text-on-primary rounded-lg font-headline font-bold text-xs flex items-center justify-center gap-2 shadow-xs hover:opacity-95 active:scale-[0.99] transition-all cursor-pointer"
            >
              <span className={`material-symbols-outlined text-[16px] ${calibrating ? 'animate-spin' : ''}`}>
                restart_alt
              </span>
              <span>{calibrating ? 'Calibrando...' : 'Iniciar Calibración Automática'}</span>
            </button>
            <button
              onClick={() => onShowToast("Perfil guardado: 'Default Gaming Profile' actualizado con curva de 1000Hz")}
              className="py-2.5 px-3 bg-surface-container-highest text-on-surface rounded-lg font-headline font-medium text-xs flex items-center justify-center gap-1 border border-outline-variant hover:bg-surface-container transition-colors cursor-pointer"
            >
              <span className="material-symbols-outlined text-[16px]">save</span>
              <span>Guardar Perfil</span>
            </button>
          </div>
        </div>
      </section>
    </div>
  );
};
