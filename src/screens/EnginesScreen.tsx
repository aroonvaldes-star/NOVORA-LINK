import React, { useState } from 'react';
import { ScreenType } from '../types/index.ts';

interface EnginesScreenProps {
  onNavigate: (screen: ScreenType) => void;
  onShowToast: (msg: string) => void;
}

export const EnginesScreen: React.FC<EnginesScreenProps> = ({ onNavigate, onShowToast }) => {
  // VisionEngine states
  const [selectedMonitor, setSelectedMonitor] = useState('Monitor 1 (Principal)');
  const [isFullScreen, setIsFullScreen] = useState(true);
  const [resolution, setResolution] = useState<'1080p' | '1440p'>('1080p');
  const [fps, setFps] = useState<60 | 120>(120);
  const [bitrate, setBitrate] = useState<number>(25);

  // Restart / Detect states
  const [isRestarting, setIsRestarting] = useState(false);
  const [isDetecting, setIsDetecting] = useState(false);

  const handleRestartEngines = () => {
    setIsRestarting(true);
    setTimeout(() => {
      setIsRestarting(false);
      onShowToast('Engines reiniciados con éxito • Pipeline reseteado en 0.8ms');
    }, 700);
  };

  const handleDetect = () => {
    setIsDetecting(true);
    setTimeout(() => {
      setIsDetecting(false);
      onShowToast('Verificación completada: 4/4 Engines respondiendo a 1000Hz');
    }, 600);
  };

  return (
    <div className="flex-1 flex flex-col pb-28 max-w-md mx-auto w-full">
      {/* Global Metrics Bar */}
      <section className="p-4 grid grid-cols-3 gap-2.5 border-b border-outline-variant/20 bg-surface-container-lowest">
        <div className="flex flex-col gap-1 rounded bg-surface-container-low p-3 border border-outline-variant/30">
          <div className="flex items-center justify-between text-on-surface-variant">
            <span className="text-[10px] font-medium font-body uppercase tracking-wider">Link Latency</span>
            <span className="material-symbols-outlined text-[14px] text-primary">bolt</span>
          </div>
          <p className="font-headline text-lg sm:text-xl font-bold text-on-surface leading-none mt-1">1.2 ms</p>
          <span className="text-[10px] font-label text-primary font-medium mt-0.5">USB 3.0 Ultra</span>
        </div>

        <div className="flex flex-col gap-1 rounded bg-surface-container-low p-3 border border-outline-variant/30">
          <div className="flex items-center justify-between text-on-surface-variant">
            <span className="text-[10px] font-medium font-body uppercase tracking-wider">Frame Timing</span>
            <span className="material-symbols-outlined text-[14px] text-primary">speed</span>
          </div>
          <p className="font-headline text-lg sm:text-xl font-bold text-on-surface leading-none mt-1">0.8 ms</p>
          <span className="text-[10px] font-label text-primary font-medium mt-0.5">Jitter &lt;0.05ms</span>
        </div>

        <div className="flex flex-col gap-1 rounded bg-surface-container-low p-3 border border-outline-variant/30">
          <div className="flex items-center justify-between text-on-surface-variant">
            <span className="text-[10px] font-medium font-body uppercase tracking-wider">Engines Listos</span>
            <span className="material-symbols-outlined text-[14px] text-emerald-500">verified</span>
          </div>
          <p className="font-headline text-lg sm:text-xl font-bold text-on-surface leading-none mt-1">4 / 4</p>
          <span className="text-[10px] font-label text-emerald-500 font-medium mt-0.5">100% Sincronía</span>
        </div>
      </section>

      {/* Engines Master Controls */}
      <section className="px-4 py-3 flex gap-2.5 bg-surface-container-low border-b border-outline-variant/30">
        <button
          onClick={handleDetect}
          disabled={isDetecting}
          className="flex-1 h-9 px-3 rounded bg-surface-container-highest hover:bg-surface-container border border-outline-variant/40 text-on-surface text-xs font-headline font-bold flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
        >
          <span className={`material-symbols-outlined text-[16px] text-primary ${isDetecting ? 'animate-spin' : ''}`}>
            radar
          </span>
          <span>{isDetecting ? 'Detectando...' : 'Detectar Engines'}</span>
        </button>

        <button
          onClick={handleRestartEngines}
          disabled={isRestarting}
          className="flex-1 h-9 px-3 rounded bg-surface-container-highest hover:bg-surface-container border border-outline-variant/40 text-on-surface text-xs font-headline font-bold flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
        >
          <span className={`material-symbols-outlined text-[16px] text-primary ${isRestarting ? 'animate-spin' : ''}`}>
            restart_alt
          </span>
          <span>{isRestarting ? 'Reiniciando...' : 'Reiniciar Engines'}</span>
        </button>
      </section>

      {/* Engines List */}
      <main className="px-4 py-4 space-y-4">
        {/* ENGINE 1: LinkEngine */}
        <article className="rounded-xl bg-surface-container-lowest border border-outline-variant/40 shadow-xs overflow-hidden">
          <div className="p-3.5 bg-surface-container-low flex items-center justify-between border-b border-outline-variant/20">
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-primary/10 text-primary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">cable</span>
              </div>
              <div>
                <h2 className="font-headline font-bold text-sm text-on-surface leading-tight">
                  1. LinkEngine (PC ↔ Android)
                </h2>
                <p className="text-[11px] text-on-surface-variant font-label">
                  Protocolo Transporte Bidireccional Directo
                </p>
              </div>
            </div>
            <span className="px-2 py-0.5 rounded text-[11px] font-label font-bold bg-primary/15 text-primary border border-primary/20">
              ACTIVO
            </span>
          </div>

          <div className="p-3.5 space-y-3">
            <div className="grid grid-cols-2 gap-2 text-xs">
              <div className="p-2.5 rounded bg-surface-container-low border border-outline-variant/20">
                <span className="text-on-surface-variant block text-[10px] uppercase font-label">Medio de Enlace</span>
                <span className="font-headline font-bold text-on-surface mt-0.5 inline-flex items-center gap-1">
                  <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                  USB 3.0 Link Activo
                </span>
              </div>
              <div className="p-2.5 rounded bg-surface-container-low border border-outline-variant/20">
                <span className="text-on-surface-variant block text-[10px] uppercase font-label">Latencia de Bus</span>
                <span className="font-headline font-bold text-primary mt-0.5 block">1.2 ms (RTT)</span>
              </div>
            </div>
            <div className="flex items-center justify-between text-xs px-1 text-on-surface-variant font-label">
              <span>Túnel ADB: Autenticado</span>
              <span>Paquetes perdidos: 0.00%</span>
            </div>
          </div>
        </article>

        {/* ENGINE 2: VisionEngine */}
        <article className="rounded-xl bg-surface-container-lowest border border-outline-variant/40 shadow-xs overflow-hidden">
          <div className="p-3.5 bg-surface-container-low flex items-center justify-between border-b border-outline-variant/20">
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-primary/10 text-primary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">display_settings</span>
              </div>
              <div>
                <h2 className="font-headline font-bold text-sm text-on-surface leading-tight">
                  2. VisionEngine (Mirroring)
                </h2>
                <p className="text-[11px] text-on-surface-variant font-label">
                  Renderizado y pipeline de display de baja latencia
                </p>
              </div>
            </div>
            <span className="px-2 py-0.5 rounded text-[11px] font-label font-bold bg-primary/15 text-primary border border-primary/20">
              ACTIVO
            </span>
          </div>

          <div className="p-3.5 space-y-3.5">
            {/* Selector Monitor & Display Mode */}
            <div className="grid grid-cols-2 gap-2.5">
              <div>
                <label className="block text-[11px] font-semibold text-on-surface-variant mb-1 font-label">
                  MONITOR FUENTE
                </label>
                <div className="relative">
                  <select
                    value={selectedMonitor}
                    onChange={(e) => setSelectedMonitor(e.target.value)}
                    className="w-full appearance-none px-2.5 py-1.5 pr-7 rounded bg-surface-container-low border border-outline-variant/40 text-xs font-medium text-on-surface focus:outline-none focus:border-primary cursor-pointer"
                  >
                    <option>Monitor 1 (Principal)</option>
                    <option>Monitor 2 (Secundario)</option>
                    <option>Ventana de Juego Activa</option>
                  </select>
                  <span className="material-symbols-outlined text-[16px] text-outline absolute right-2 top-2 pointer-events-none">
                    unfold_more
                  </span>
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-semibold text-on-surface-variant mb-1 font-label">
                  MODO VENTANA
                </label>
                <div className="flex items-center justify-between px-2.5 py-1.5 rounded bg-surface-container-low border border-outline-variant/40 text-xs">
                  <span className="text-on-surface font-medium truncate mr-1">Pantalla completa</span>
                  <button
                    type="button"
                    onClick={() => setIsFullScreen(!isFullScreen)}
                    className={`w-8 h-4 rounded-full relative transition-colors focus:outline-none cursor-pointer ${
                      isFullScreen ? 'bg-primary' : 'bg-surface-container-highest'
                    }`}
                  >
                    <span
                      className={`w-3 h-3 bg-white rounded-full absolute top-0.5 transition-transform shadow ${
                        isFullScreen ? 'right-0.5' : 'left-0.5'
                      }`}
                    ></span>
                  </button>
                </div>
              </div>
            </div>

            {/* Resolution & FPS Selectors */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[11px] font-semibold text-on-surface-variant font-label">RESOLUCIÓN</span>
                <div className="inline-flex rounded-lg bg-surface-container-high p-0.5 text-xs font-label">
                  <button
                    onClick={() => setResolution('1080p')}
                    className={`px-3 py-1 rounded-md transition-all cursor-pointer ${
                      resolution === '1080p'
                        ? 'bg-surface-container-lowest text-on-surface font-bold shadow-xs'
                        : 'text-on-surface-variant hover:text-on-surface font-medium'
                    }`}
                  >
                    1080p
                  </button>
                  <button
                    onClick={() => setResolution('1440p')}
                    className={`px-3 py-1 rounded-md transition-all cursor-pointer ${
                      resolution === '1440p'
                        ? 'bg-surface-container-lowest text-on-surface font-bold shadow-xs'
                        : 'text-on-surface-variant hover:text-on-surface font-medium'
                    }`}
                  >
                    1440p
                  </button>
                </div>
              </div>

              <div className="flex items-center justify-between">
                <span className="text-[11px] font-semibold text-on-surface-variant font-label">
                  TASA DE REFRESCO (FPS)
                </span>
                <div className="inline-flex rounded-lg bg-surface-container-high p-0.5 text-xs font-label">
                  <button
                    onClick={() => setFps(60)}
                    className={`px-3 py-1 rounded-md transition-all cursor-pointer ${
                      fps === 60
                        ? 'bg-surface-container-lowest text-on-surface font-bold shadow-xs'
                        : 'text-on-surface-variant hover:text-on-surface font-medium'
                    }`}
                  >
                    60 FPS
                  </button>
                  <button
                    onClick={() => setFps(120)}
                    className={`px-3 py-1 rounded-md transition-all cursor-pointer ${
                      fps === 120
                        ? 'bg-surface-container-lowest text-on-surface font-bold shadow-xs'
                        : 'text-on-surface-variant hover:text-on-surface font-medium'
                    }`}
                  >
                    120 FPS
                  </button>
                </div>
              </div>
            </div>

            {/* Bitrate Slider */}
            <div className="p-2.5 rounded bg-surface-container-low border border-outline-variant/20 space-y-1.5">
              <div className="flex items-center justify-between text-xs">
                <span className="font-label text-[11px] text-on-surface-variant uppercase">
                  Flujo de datos (Bitrate)
                </span>
                <span className="font-label font-bold text-primary">{bitrate} Mbps</span>
              </div>
              <input
                type="range"
                min="5"
                max="50"
                value={bitrate}
                onChange={(e) => setBitrate(Number(e.target.value))}
                className="w-full h-1.5 bg-surface-container-highest rounded-lg appearance-none cursor-pointer accent-primary"
              />
              <div className="flex justify-between text-[10px] font-label text-outline">
                <span>5 Mbps</span>
                <span className="text-primary font-semibold">25 Mbps (Recomendado)</span>
                <span>50 Mbps</span>
              </div>
            </div>
          </div>
        </article>

        {/* ENGINE 3: ExInEngine */}
        <article className="rounded-xl bg-surface-container-lowest border border-outline-variant/40 shadow-xs overflow-hidden">
          <div className="p-3.5 bg-surface-container-low flex items-center justify-between border-b border-outline-variant/20">
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-secondary-container/50 text-secondary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">sports_esports</span>
              </div>
              <div>
                <h2 className="font-headline font-bold text-sm text-on-surface leading-tight">
                  3. ExInEngine (Mando &amp; Entradas)
                </h2>
                <p className="text-[11px] text-on-surface-variant font-label">
                  Subordinación Hardware HID &amp; Passthrough
                </p>
              </div>
            </div>
            <span className="px-2 py-0.5 rounded text-[11px] font-label font-bold bg-secondary-container/30 text-secondary border border-secondary/20">
              DETECTADO / LISTO
            </span>
          </div>

          <div className="p-3.5 space-y-3">
            <div className="flex items-center justify-between p-2.5 rounded bg-surface-container-low border border-outline-variant/20 text-xs font-label">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-primary text-[18px]">videogame_asset</span>
                <div>
                  <span className="text-on-surface font-semibold block">Dispositivo HID Principal</span>
                  <span className="text-[10px] text-on-surface-variant">XInput Controller 0 • Polling 1000Hz</span>
                </div>
              </div>
              <span className="px-1.5 py-0.5 bg-emerald-500/10 text-emerald-500 text-[10px] font-bold rounded border border-emerald-500/20">
                OK
              </span>
            </div>

            <div className="flex gap-2">
              <button
                onClick={() => onNavigate('live_test')}
                className="flex-1 py-2 px-3 rounded-lg bg-surface-container-high hover:bg-surface-container border border-outline-variant/40 text-on-surface text-xs font-headline font-semibold flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
              >
                <span className="material-symbols-outlined text-[16px] text-primary">tune</span>
                <span>Calibración</span>
              </button>
              <button
                onClick={() => onNavigate('live_test')}
                className="flex-1 py-2 px-3 rounded-lg bg-primary text-on-primary text-xs font-headline font-semibold flex items-center justify-center gap-1.5 transition-all hover:opacity-95 cursor-pointer shadow-xs"
              >
                <span className="material-symbols-outlined text-[16px]">motion_sensor_active</span>
                <span>Prueba en vivo</span>
              </button>
            </div>
          </div>
        </article>

        {/* ENGINE 4: STEngine */}
        <article className="rounded-xl bg-surface-container-lowest border border-outline-variant/40 shadow-xs overflow-hidden">
          <div className="p-3.5 bg-surface-container-low flex items-center justify-between border-b border-outline-variant/20">
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-primary/10 text-primary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">dns</span>
              </div>
              <div>
                <h2 className="font-headline font-bold text-sm text-on-surface leading-tight">
                  4. STEngine (Subordinación Térmica)
                </h2>
                <p className="text-[11px] text-on-surface-variant font-label">
                  Telemetría de estado y diagnóstico de ejecución
                </p>
              </div>
            </div>
            <span className="px-2 py-0.5 rounded text-[11px] font-label font-bold bg-surface-container-highest text-on-surface-variant border border-outline-variant/40">
              DETECTADO
            </span>
          </div>

          <div className="p-3.5 space-y-2.5">
            <div className="grid grid-cols-2 gap-2 text-xs font-label">
              <div className="p-2 rounded bg-surface-container-low border border-outline-variant/20">
                <span className="text-[10px] text-on-surface-variant uppercase">CPU Thermal Throttling</span>
                <p className="font-headline font-bold text-on-surface mt-0.5">Nominal (38°C)</p>
              </div>
              <div className="p-2 rounded bg-surface-container-low border border-outline-variant/20">
                <span className="text-[10px] text-on-surface-variant uppercase">Hilos Sincronizados</span>
                <p className="font-headline font-bold text-primary mt-0.5">8 Threads Lock</p>
              </div>
            </div>

            <div className="flex items-center justify-between text-[11px] font-label text-on-surface-variant px-1 pt-1 border-t border-outline-variant/20">
              <span className="flex items-center gap-1">
                <span className="material-symbols-outlined text-[14px] text-primary">terminal</span>
                Diagnóstico ST: Pipeline estable
              </span>
              <button
                onClick={() => onNavigate('hardware')}
                className="text-primary font-medium hover:underline cursor-pointer flex items-center gap-0.5"
              >
                <span>Ver Raw Logs</span>
                <span className="material-symbols-outlined text-[14px]">arrow_forward</span>
              </button>
            </div>
          </div>
        </article>
      </main>
    </div>
  );
};
