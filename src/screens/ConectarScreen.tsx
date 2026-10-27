import React, { useState } from 'react';
import { ScreenType } from '../types/index.ts';

interface ConectarScreenProps {
  onNavigate: (screen: ScreenType) => void;
  onShowToast: (msg: string) => void;
}

export const ConectarScreen: React.FC<ConectarScreenProps> = ({ onNavigate, onShowToast }) => {
  const [isScanning, setIsScanning] = useState(false);
  const [scanStep, setScanStep] = useState(4); // 0 to 4
  const [usbStatus, setUsbStatus] = useState<'checking' | 'active'>('active');
  const [lanIp, setLanIp] = useState('192.168.1.140:9098');
  const [isLanLinked, setIsLanLinked] = useState(true);
  const [isAdbSyncing, setIsAdbSyncing] = useState(false);
  const [showQrModal, setShowQrModal] = useState(false);

  const handleCheckUsb = () => {
    setUsbStatus('checking');
    setTimeout(() => {
      setUsbStatus('active');
      onShowToast('Enlace USB 3.2 Operativo • Ancho de banda verificado: 480 Mbps');
    }, 700);
  };

  const handleDetectEngines = () => {
    if (isScanning) return;
    setIsScanning(true);
    setScanStep(0);

    const interval = setInterval(() => {
      setScanStep((prev) => {
        if (prev >= 4) {
          clearInterval(interval);
          setIsScanning(false);
          onShowToast('Detección completada: 4/4 Engines en línea');
          return 4;
        }
        return prev + 1;
      });
    }, 400);
  };

  const handleSyncAdb = () => {
    setIsAdbSyncing(true);
    setTimeout(() => {
      setIsAdbSyncing(false);
      onShowToast('Wi-Fi ADB Bridge sincronizado con host');
    }, 900);
  };

  return (
    <div className="flex-1 flex flex-col px-4 py-4 pb-28 space-y-6 max-w-md mx-auto w-full">
      {/* SECCIÓN 1: USB FÍSICO (OBLIGATORIO) */}
      <section className="space-y-2.5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="flex size-5 items-center justify-center bg-primary text-on-primary text-xs font-mono font-bold">
              1
            </span>
            <h2 className="text-xs font-bold tracking-tight text-on-surface uppercase font-headline">
              Conexión Física de Datos
            </h2>
          </div>
          <span className="text-[10px] font-mono px-2 py-0.5 bg-error-container text-error border border-error font-bold tracking-wide">
            OBLIGATORIO
          </span>
        </div>

        {/* Featured Physical USB Card */}
        <div className="bg-surface-container-lowest border border-on-surface p-4 relative overflow-hidden">
          <div className="flex items-start justify-between gap-3 mb-3">
            <div className="flex items-center gap-3">
              <div className="flex size-11 items-center justify-center bg-surface-container-high text-on-surface border border-outline-variant">
                <span className="material-symbols-outlined text-2xl text-on-surface">cable</span>
              </div>
              <div>
                <p className="text-[10px] font-mono font-bold tracking-wider text-on-surface-variant uppercase">ENLACE PRINCIPAL</p>
                <h3 className="text-sm font-bold text-on-surface leading-snug">USB Físico de Alta Velocidad</h3>
              </div>
            </div>
            <div className="flex items-center gap-1.5 px-2.5 py-1 bg-success-container border border-success text-success">
              <span className="size-2 rounded-full bg-success animate-pulse"></span>
              <span className="text-xs font-mono font-bold tracking-tight">Detectado</span>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2 my-3 font-mono text-xs">
            <div className="bg-surface-container-low p-2.5 border border-outline-variant">
              <span className="text-[10px] block text-on-surface-variant font-medium">ANCHO DE BANDA</span>
              <span className="font-bold text-xs text-on-surface">480 Mbps / USB 2.0+</span>
            </div>
            <div className="bg-surface-container-low p-2.5 border border-outline-variant">
              <span className="text-[10px] block text-on-surface-variant font-medium">ESTADO DEL CABLE</span>
              <span className="font-bold text-xs text-success flex items-center gap-1">
                <span className="material-symbols-outlined text-sm text-success">check_circle</span> Cable Activo
              </span>
            </div>
          </div>

          {/* Subsystem Warning Card */}
          <div role="alert" className="bg-error-container border border-error p-3 mt-3 flex items-start gap-2.5">
            <span className="material-symbols-outlined text-error text-lg shrink-0 mt-0.5">warning</span>
            <div className="space-y-0.5">
              <p className="text-[11px] font-bold text-error uppercase tracking-wider font-headline">
                Aviso Crítico de Subsistema
              </p>
              <p className="text-xs text-on-surface leading-relaxed">
                El enlace USB por cable es <span className="font-bold underline decoration-error text-error">indispensable y obligatorio</span> para LinkEngine en tiempo real. La red inalámbrica sólo actúa como interfaz auxiliar.
              </p>
            </div>
          </div>

          <div className="mt-3.5 flex justify-end">
            <button
              onClick={handleCheckUsb}
              disabled={usbStatus === 'checking'}
              className="inline-flex items-center gap-2 bg-primary text-on-primary px-4 py-2 text-xs font-semibold hover:opacity-90 transition-opacity active:opacity-80 cursor-pointer disabled:cursor-wait disabled:opacity-60"
            >
              <span className={`material-symbols-outlined text-sm ${usbStatus === 'checking' ? 'animate-spin' : ''}`}>
                sync
              </span>
              <span>{usbStatus === 'checking' ? 'Verificando enlace...' : 'Comprobar enlace USB'}</span>
            </button>
          </div>
        </div>
      </section>

      {/* SECCIÓN 2: EMPAREJAMIENTO AUXILIAR PC ↔ ANDROID */}
      <section className="space-y-2.5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="flex size-5 items-center justify-center bg-surface-container-high text-on-surface border border-outline-variant font-mono text-xs font-bold">
              2
            </span>
            <h2 className="text-xs font-bold tracking-tight text-on-surface uppercase font-headline">
              Emparejamiento Auxiliar PC ↔ Android
            </h2>
          </div>
          <span className="text-[10px] font-mono px-2 py-0.5 bg-surface-container-high text-on-surface-variant border border-outline-variant font-semibold">
            AUXILIAR
          </span>
        </div>

        {/* Warning Ribbon: No sustituye USB */}
        <div className="bg-surface-container-lowest border border-outline-variant border-l-4 border-l-on-surface px-3 py-2 text-[11px] text-on-surface-variant flex items-center justify-between">
          <span className="flex items-center gap-1.5 text-on-surface">
            <span className="material-symbols-outlined text-on-surface text-sm">info</span>
            Métodos complementarios de control y sincronización.
          </span>
          <span className="font-bold text-on-surface text-[10px] uppercase font-mono tracking-tight shrink-0 ml-2">
            No sustituyen USB
          </span>
        </div>

        {/* Auxiliary Method 1: QR Code */}
        <div className="bg-surface-container-lowest border border-outline p-3.5 flex items-center justify-between hover:border-on-surface transition-colors">
          <div className="flex items-center gap-3">
            <div className="flex size-10 items-center justify-center bg-surface-container text-on-surface border border-outline-variant">
              <span className="material-symbols-outlined text-xl">qr_code_scanner</span>
            </div>
            <div>
              <h4 className="text-xs font-bold text-on-surface font-headline">Código QR Dinámico</h4>
              <p className="text-[11px] text-on-surface-variant">Escanea el código generado en NOVORA Desktop</p>
            </div>
          </div>
          <button
            onClick={() => setShowQrModal(true)}
            className="px-3 py-1.5 border border-outline hover:border-on-surface text-on-surface font-semibold text-xs bg-surface-container-lowest hover:bg-surface-container-low transition-colors cursor-pointer"
          >
            Escanear
          </button>
        </div>

        {/* Auxiliary Method 2: LAN Directa */}
        <div className="bg-surface-container-lowest border border-outline p-3.5 space-y-2.5 hover:border-on-surface transition-colors">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="flex size-10 items-center justify-center bg-surface-container text-on-surface border border-outline-variant">
                <span className="material-symbols-outlined text-xl">lan</span>
              </div>
              <div>
                <h4 className="text-xs font-bold text-on-surface font-headline">LAN Directa</h4>
                <p className="text-[11px] text-on-surface-variant">Emparejamiento por IP de red local y puerto UDP</p>
              </div>
            </div>
            <span className="text-[10px] font-mono text-success bg-success-container px-2 py-0.5 border border-success font-semibold">
              192.168.1.140
            </span>
          </div>

          <div className="flex gap-2">
            <input
              type="text"
              aria-label="IP y puerto de la LAN directa"
              value={lanIp}
              onChange={(e) => setLanIp(e.target.value)}
              className="flex-1 bg-surface-container-low border border-outline-variant px-2.5 py-1.5 text-xs font-mono text-on-surface focus:border-on-surface focus:bg-surface-container-lowest"
              placeholder="IP:PUERTO"
            />
            <button
              onClick={() => {
                setIsLanLinked(true);
                onShowToast(`Vínculo LAN verificado en ${lanIp}`);
              }}
              className="px-3.5 py-1.5 bg-surface-container hover:bg-surface-container-high text-on-surface text-xs font-semibold border border-outline transition-colors cursor-pointer"
            >
              {isLanLinked ? 'Vinculado' : 'Vincular'}
            </button>
          </div>
        </div>

        {/* Auxiliary Method 3: Wi-Fi ADB */}
        <div className="bg-surface-container-lowest border border-outline p-3.5 flex items-center justify-between hover:border-on-surface transition-colors">
          <div className="flex items-center gap-3">
            <div className="flex size-10 items-center justify-center bg-surface-container text-on-surface border border-outline-variant">
              <span className="material-symbols-outlined text-xl">wifi_tethering</span>
            </div>
            <div>
              <div className="flex items-center gap-1.5">
                <h4 className="text-xs font-bold text-on-surface font-headline">Wi-Fi ADB Bridge</h4>
                <span className="size-2 rounded-full bg-success" aria-label="Activo"></span>
              </div>
              <p className="text-[11px] text-on-surface-variant">Sincronización remota de parámetros y buffers</p>
            </div>
          </div>
          <button
            onClick={handleSyncAdb}
            disabled={isAdbSyncing}
            className="flex items-center gap-1 px-3 py-1.5 bg-secondary-container text-on-secondary-container font-semibold hover:opacity-90 text-xs transition-opacity border border-outline cursor-pointer disabled:cursor-wait disabled:opacity-60"
          >
            <span className={`material-symbols-outlined text-sm ${isAdbSyncing ? 'animate-spin' : ''}`}>
              tune
            </span>
            <span>{isAdbSyncing ? 'Sincronizando...' : 'Sincronizar'}</span>
          </button>
        </div>
      </section>

      {/* SECCIÓN 3: DETECCIÓN DE ENGINES */}
      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="flex size-5 items-center justify-center bg-primary text-on-primary text-xs font-mono font-bold">
              3
            </span>
            <h2 className="text-xs font-bold tracking-tight text-on-surface uppercase font-headline">
              Detección de Engines
            </h2>
          </div>
          <span className="text-[10px] font-mono text-on-surface-variant font-medium">
            {isScanning ? 'Escaneando subsistemas...' : '4 subsistemas listos'}
          </span>
        </div>

        {/* Main Trigger Button */}
        <button
          onClick={handleDetectEngines}
          disabled={isScanning}
          className="w-full relative group bg-primary text-on-primary px-4 py-3.5 text-center font-headline font-bold text-sm tracking-wide transition-opacity hover:opacity-90 active:opacity-80 flex items-center justify-center gap-2 cursor-pointer disabled:cursor-wait disabled:opacity-70"
        >
          <span className={`material-symbols-outlined text-xl ${isScanning ? 'animate-spin' : 'group-hover:rotate-180 transition-transform'}`}>
            radar
          </span>
          <span>{isScanning ? 'Consultando subsistemas...' : 'Detectar Engines'}</span>
        </button>

        {/* Engine Cards Grid */}
        <div className="grid grid-cols-2 gap-2.5 pt-1">
          {/* 1. LinkEngine */}
          <div
            onClick={() => onNavigate('engines')}
            className={`bg-surface-container-lowest border p-3 flex flex-col justify-between transition-colors cursor-pointer hover:border-on-surface ${
              scanStep >= 1 ? 'border-on-surface' : 'border-outline-variant opacity-70'
            }`}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-[10px] font-mono px-1.5 py-0.5 bg-primary text-on-primary font-bold">
                CORE USB
              </span>
              <span className={`size-2.5 rounded-full ${scanStep >= 1 ? 'bg-success' : 'bg-warning animate-pulse'}`}></span>
            </div>
            <div>
              <h5 className="text-xs font-bold text-on-surface font-headline">LinkEngine</h5>
              <p className="text-[10px] text-on-surface-variant font-medium">I/O Direct Pipeline</p>
            </div>
            <div className="mt-2 pt-2 border-t border-outline-variant flex items-center justify-between text-[10px] font-mono">
              <span className="text-on-surface-variant">ESTADO</span>
              <span className={`font-bold ${scanStep >= 1 ? 'text-success' : 'text-warning'}`}>
                {scanStep >= 1 ? 'EN LÍNEA (1ms)' : 'SONDEANDO...'}
              </span>
            </div>
          </div>

          {/* 2. VisionEngine */}
          <div
            onClick={() => onNavigate('engines')}
            className={`bg-surface-container-lowest border p-3 flex flex-col justify-between transition-colors cursor-pointer hover:border-on-surface ${
              scanStep >= 2 ? 'border-on-surface' : 'border-outline-variant opacity-70'
            }`}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-[10px] font-mono px-1.5 py-0.5 bg-surface-container-high text-on-surface font-semibold">
                NEURAL
              </span>
              <span className={`size-2.5 rounded-full ${scanStep >= 2 ? 'bg-success' : 'bg-warning animate-pulse'}`}></span>
            </div>
            <div>
              <h5 className="text-xs font-bold text-on-surface font-headline">VisionEngine</h5>
              <p className="text-[10px] text-on-surface-variant font-medium">Buffer de Video 4K</p>
            </div>
            <div className="mt-2 pt-2 border-t border-outline-variant flex items-center justify-between text-[10px] font-mono">
              <span className="text-on-surface-variant">ESTADO</span>
              <span className={`font-bold ${scanStep >= 2 ? 'text-success' : 'text-warning'}`}>
                {scanStep >= 2 ? 'STANDBY (60 FPS)' : 'SONDEANDO...'}
              </span>
            </div>
          </div>

          {/* 3. ExInEngine */}
          <div
            onClick={() => onNavigate('live_test')}
            className={`bg-surface-container-lowest border p-3 flex flex-col justify-between transition-colors cursor-pointer hover:border-on-surface ${
              scanStep >= 3 ? 'border-on-surface' : 'border-outline-variant opacity-70'
            }`}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-[10px] font-mono px-1.5 py-0.5 bg-surface-container-high text-on-surface font-semibold">
                SENSOR BUS
              </span>
              <span className={`size-2.5 rounded-full ${scanStep >= 3 ? 'bg-success' : 'bg-warning animate-pulse'}`}></span>
            </div>
            <div>
              <h5 className="text-xs font-bold text-on-surface font-headline">ExInEngine</h5>
              <p className="text-[10px] text-on-surface-variant font-medium">Telemetría Externa</p>
            </div>
            <div className="mt-2 pt-2 border-t border-outline-variant flex items-center justify-between text-[10px] font-mono">
              <span className="text-on-surface-variant">ESTADO</span>
              <span className={`font-bold ${scanStep >= 3 ? 'text-success' : 'text-warning'}`}>
                {scanStep >= 3 ? 'ACTIVO (250Hz)' : 'SONDEANDO...'}
              </span>
            </div>
          </div>

          {/* 4. STEngine */}
          <div
            onClick={() => onNavigate('hardware')}
            className={`bg-surface-container-lowest border p-3 flex flex-col justify-between transition-colors cursor-pointer hover:border-on-surface ${
              scanStep >= 4 ? 'border-on-surface' : 'border-outline-variant opacity-70'
            }`}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-[10px] font-mono px-1.5 py-0.5 bg-surface-container-high text-on-surface font-semibold">
                AUDIO/SYNC
              </span>
              <span className={`size-2.5 rounded-full ${scanStep >= 4 ? 'bg-success' : 'bg-warning animate-pulse'}`}></span>
            </div>
            <div>
              <h5 className="text-xs font-bold text-on-surface font-headline">STEngine</h5>
              <p className="text-[10px] text-on-surface-variant font-medium">Spatio-Temporal Sync</p>
            </div>
            <div className="mt-2 pt-2 border-t border-outline-variant flex items-center justify-between text-[10px] font-mono">
              <span className="text-on-surface-variant">ESTADO</span>
              <span className={`font-bold ${scanStep >= 4 ? 'text-success' : 'text-warning'}`}>
                {scanStep >= 4 ? 'SINCRONIZADO' : 'SONDEANDO...'}
              </span>
            </div>
          </div>
        </div>
      </section>

      {/* QR Code Scanner Dialog Modal */}
      {showQrModal && (
        <div className="fixed inset-0 z-50 bg-scrim flex items-center justify-center p-4">
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="qr-title"
            className="w-full max-w-sm bg-surface-container-lowest border border-on-surface p-5 space-y-4"
          >
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-on-surface">qr_code_scanner</span>
                <h3 id="qr-title" className="font-headline font-bold text-sm text-on-surface">Escanear NOVORA Desktop</h3>
              </div>
              <button
                onClick={() => setShowQrModal(false)}
                aria-label="Cerrar escáner"
                className="text-on-surface-variant hover:text-on-surface p-1 cursor-pointer"
              >
                <span className="material-symbols-outlined">close</span>
              </button>
            </div>

            <div className="flex flex-col items-center justify-center p-6 bg-surface-container-low border border-dashed border-outline relative overflow-hidden">
              {/* Dynamic QR visualizer. Black on white is intentional: a scannable code needs it in both themes. */}
              <div className="relative size-40 bg-white p-3 flex items-center justify-center">
                <svg viewBox="0 0 100 100" className="w-full h-full" role="img" aria-label="Código QR de emparejamiento">
                  {/* Outer corner patterns */}
                  <rect x="5" y="5" width="28" height="28" fill="#000" />
                  <rect x="9" y="9" width="20" height="20" fill="#fff" />
                  <rect x="13" y="13" width="12" height="12" fill="#000" />

                  <rect x="67" y="5" width="28" height="28" fill="#000" />
                  <rect x="71" y="9" width="20" height="20" fill="#fff" />
                  <rect x="75" y="13" width="12" height="12" fill="#000" />

                  <rect x="5" y="67" width="28" height="28" fill="#000" />
                  <rect x="9" y="71" width="20" height="20" fill="#fff" />
                  <rect x="13" y="75" width="12" height="12" fill="#000" />

                  {/* Matrix elements */}
                  <circle cx="50" cy="20" r="3" fill="#000" />
                  <circle cx="50" cy="50" r="4" fill="#000" />
                  <circle cx="50" cy="80" r="3" fill="#000" />
                  <rect x="38" y="38" width="6" height="6" fill="#000" />
                  <rect x="56" y="38" width="6" height="6" fill="#000" />
                  <rect x="38" y="56" width="6" height="6" fill="#000" />
                  <rect x="56" y="56" width="6" height="6" fill="#000" />
                </svg>
                {/* Scan line animation */}
                <div className="absolute left-2 right-2 top-0 h-0.5 bg-primary animate-[bounce_2s_infinite]"></div>
              </div>
              <p className="text-xs text-on-surface-variant font-label mt-3 text-center">
                Alinea el código con la cámara o sincroniza vía USB
              </p>
            </div>

            <div className="flex gap-2">
              <button
                onClick={() => {
                  setShowQrModal(false);
                  onShowToast('Código QR validado • Parámetros sincronizados');
                }}
                className="flex-1 py-2 bg-primary text-on-primary font-headline font-bold text-xs cursor-pointer hover:opacity-90"
              >
                Confirmar Emparejamiento
              </button>
              <button
                onClick={() => setShowQrModal(false)}
                className="py-2 px-3 border border-outline text-on-surface font-headline text-xs cursor-pointer hover:bg-surface-container"
              >
                Cerrar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
