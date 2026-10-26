import React, { useState, useEffect, useRef } from 'react';
import { ScreenType, FileItem } from '../types/index.ts';

interface ArchivosMultimediaScreenProps {
  initialTab?: 'archivos' | 'multimedia';
  onNavigate: (screen: ScreenType) => void;
  onShowToast: (msg: string) => void;
  isRecording: boolean;
  onToggleRecord: () => void;
  bubbleEnabled: boolean;
  onToggleBubble: (enabled: boolean) => void;
  bubbleOpacity: number;
  onChangeBubbleOpacity: (opacity: number) => void;
  bubbleSize: 'small' | 'medium' | 'large';
  onChangeBubbleSize: (size: 'small' | 'medium' | 'large') => void;
}

export const ArchivosMultimediaScreen: React.FC<ArchivosMultimediaScreenProps> = ({
  initialTab = 'archivos',
  onShowToast,
  isRecording,
  onToggleRecord,
  bubbleEnabled,
  onToggleBubble,
  bubbleOpacity,
  onChangeBubbleOpacity,
  bubbleSize,
  onChangeBubbleSize,
}) => {
  const [mainTab, setMainTab] = useState<'archivos' | 'multimedia'>(initialTab);
  const [fileFilter, setFileFilter] = useState<'recibidos' | 'enviados'>('recibidos');

  // Active Transfer simulation
  const [transferPaused, setTransferPaused] = useState(false);
  const [transferProgress, setTransferProgress] = useState(72);
  const [transferCanceled, setTransferCanceled] = useState(false);

  // Audio mode state
  const [audioMode, setAudioMode] = useState<'internal' | 'bidirectional' | 'independent'>('bidirectional');
  const [audioOutput, setAudioOutput] = useState('Auriculares Bluetooth NOVORA Pro (Realtek ASIO)');

  // Recording timer
  const [recordSeconds, setRecordSeconds] = useState(258); // 00:04:18
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setMainTab(initialTab);
  }, [initialTab]);

  // Timer loop when recording
  useEffect(() => {
    if (!isRecording) return;
    const interval = setInterval(() => {
      setRecordSeconds((s) => s + 1);
    }, 1000);
    return () => clearInterval(interval);
  }, [isRecording]);

  // Transfer simulation progress
  useEffect(() => {
    if (transferPaused || transferCanceled) return;
    const interval = setInterval(() => {
      setTransferProgress((p) => {
        if (p >= 100) return 100;
        return Number((p + 0.3).toFixed(1));
      });
    }, 300);
    return () => clearInterval(interval);
  }, [transferPaused, transferCanceled]);

  const formatTimer = (totalSeconds: number) => {
    const hrs = Math.floor(totalSeconds / 3600);
    const mins = Math.floor((totalSeconds % 3600) / 60);
    const secs = totalSeconds % 60;
    return `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  };

  // Recent files state
  const [receivedFiles, setReceivedFiles] = useState<FileItem[]>([
    {
      id: '1',
      name: 'Patch_update_1.4.19.bin',
      size: '420 MB',
      time: 'Hoy 14:32',
      status: 'Completado',
      type: 'code',
    },
    {
      id: '2',
      name: 'registro_sesion_win11.log',
      size: '64.2 KB',
      time: 'Ayer 21:05',
      status: 'Completado',
      type: 'log',
    },
    {
      id: '3',
      name: 'render_capture_4k.mkv',
      size: '2.4 GB',
      time: 'Hoy 11:15',
      status: 'Completado',
      type: 'movie',
    },
  ]);

  const sentFiles: FileItem[] = [
    {
      id: 's1',
      name: 'screenshot_desktop_mirror.png',
      size: '3.4 MB',
      time: 'Hoy 15:40',
      status: 'Completado',
      type: 'image',
    },
    {
      id: 's2',
      name: 'controller_calibration_preset.json',
      size: '12 KB',
      time: 'Hoy 13:20',
      status: 'Completado',
      type: 'code',
    },
    {
      id: 's3',
      name: 'android_touch_event_macro.bin',
      size: '88 KB',
      time: 'Ayer 19:12',
      status: 'Completado',
      type: 'code',
    },
  ];

  // Screen capture flash effect
  const handleScreenCapture = () => {
    onShowToast('Captura remota de escritorio guardada en Galería (1080p)');
    // Add to received files
    const newFile: FileItem = {
      id: Date.now().toString(),
      name: `captura_${Date.now().toString().slice(-4)}.png`,
      size: '2.8 MB',
      time: 'Ahora',
      status: 'Completado',
      type: 'image',
    };
    setReceivedFiles((prev) => [newFile, ...prev]);
  };

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      onShowToast(`Enviando "${file.name}" a PC Windows...`);
      setTransferCanceled(false);
      setTransferProgress(5);
    }
  };

  return (
    <div className="w-full max-w-md mx-auto flex-1 flex flex-col pb-28">
      {/* Hidden file input */}
      <input
        type="file"
        ref={fileInputRef}
        onChange={handleFileUpload}
        className="hidden"
      />

      {/* Main Tab Bar: Archivos vs Multimedia */}
      <div className="px-4 bg-surface-container-lowest border-b border-outline-variant/30 sticky top-[57px] z-20">
        <div className="flex">
          <button
            onClick={() => setMainTab('archivos')}
            className={`flex flex-col items-center justify-center pb-3 pt-3 flex-1 font-headline font-bold text-sm transition-colors cursor-pointer ${
              mainTab === 'archivos'
                ? 'border-b-2 border-primary text-primary'
                : 'border-b-2 border-transparent text-outline hover:text-on-surface'
            }`}
          >
            <div className="flex items-center gap-1.5">
              <span className="material-symbols-outlined text-base">folder_open</span>
              <span>Archivos</span>
            </div>
          </button>

          <button
            onClick={() => setMainTab('multimedia')}
            className={`flex flex-col items-center justify-center pb-3 pt-3 flex-1 font-headline font-bold text-sm transition-colors cursor-pointer ${
              mainTab === 'multimedia'
                ? 'border-b-2 border-primary text-primary'
                : 'border-b-2 border-transparent text-outline hover:text-on-surface'
            }`}
          >
            <div className="flex items-center gap-1.5">
              <span className="material-symbols-outlined text-base">perm_media</span>
              <span>Multimedia</span>
            </div>
          </button>
        </div>
      </div>

      {/* TAB 1: ARCHIVOS */}
      {mainTab === 'archivos' && (
        <main className="flex-1 flex flex-col p-4 space-y-4">
          {/* Sub-tab Selector: Recibidos vs Enviados */}
          <div className="flex">
            <div className="flex h-10 flex-1 items-center justify-center rounded-lg bg-surface-container p-1 border border-outline-variant/40">
              <button
                onClick={() => setFileFilter('recibidos')}
                className={`flex h-full grow items-center justify-center rounded-md px-3 text-xs font-semibold font-headline transition-all cursor-pointer ${
                  fileFilter === 'recibidos'
                    ? 'bg-surface-container-lowest shadow-xs text-on-surface font-bold'
                    : 'text-outline hover:text-on-surface'
                }`}
              >
                Recibidos ({receivedFiles.length + (transferCanceled ? 0 : 1)})
              </button>
              <button
                onClick={() => setFileFilter('enviados')}
                className={`flex h-full grow items-center justify-center rounded-md px-3 text-xs font-semibold font-headline transition-all cursor-pointer ${
                  fileFilter === 'enviados'
                    ? 'bg-surface-container-lowest shadow-xs text-on-surface font-bold'
                    : 'text-outline hover:text-on-surface'
                }`}
              >
                Enviados ({sentFiles.length})
              </button>
            </div>
          </div>

          {/* Botón de acción: Enviar archivo a PC */}
          <button
            onClick={() => fileInputRef.current?.click()}
            className="w-full bg-primary hover:opacity-95 text-on-primary flex items-center justify-center gap-2 py-3 px-4 rounded-xl font-headline font-semibold text-sm shadow-xs transition-all active:scale-[0.99] cursor-pointer"
          >
            <span className="material-symbols-outlined text-xl">upload_file</span>
            <span>Enviar archivo a PC</span>
          </button>

          {/* Transferencias en curso */}
          {!transferCanceled && fileFilter === 'recibidos' && (
            <div>
              <div className="flex items-center justify-between pb-2 pt-1">
                <h3 className="text-on-surface font-headline text-sm font-bold tracking-tight flex items-center gap-2">
                  <span className="size-2 rounded-full bg-primary animate-pulse"></span>
                  Transferencias en curso
                </h3>
                <span className="font-label text-xs text-primary font-semibold">1 activo</span>
              </div>

              <div className="p-3.5 bg-surface-container-low rounded-xl border border-outline-variant/50 relative overflow-hidden">
                <div className="flex items-start gap-3">
                  <div className="p-2.5 bg-primary/10 text-primary rounded-lg flex items-center justify-center shrink-0 border border-primary/20">
                    <span className="material-symbols-outlined text-2xl">movie</span>
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between gap-1">
                      <h4 className="font-headline text-sm font-bold text-on-surface truncate">Gameplay.mp4</h4>
                      <span className="font-label text-[10px] bg-primary/15 text-primary border border-primary/30 px-2 py-0.5 rounded font-bold">
                        {transferProgress >= 100 ? 'Finalizado' : transferPaused ? 'Pausado' : 'Transfiriendo'}
                      </span>
                    </div>

                    <div className="flex justify-between items-center text-xs font-label text-outline mt-1">
                      <span>921.6 MB / 1.28 GB</span>
                      <span className="text-primary font-bold">
                        {Math.round(transferProgress)}% • {transferPaused ? '0.0 MB/s' : '18.4 MB/s'}
                      </span>
                    </div>

                    {/* Barra de progreso */}
                    <div className="w-full bg-surface-container-highest rounded-full h-1.5 mt-2 overflow-hidden">
                      <div
                        className="bg-primary h-1.5 rounded-full transition-all duration-300"
                        style={{ width: `${transferProgress}%` }}
                      ></div>
                    </div>

                    <div className="flex justify-end gap-3 mt-2 text-xs font-label">
                      <button
                        onClick={() => {
                          setTransferCanceled(true);
                          onShowToast('Transferencia cancelada');
                        }}
                        className="text-error flex items-center gap-1 hover:underline cursor-pointer"
                      >
                        <span className="material-symbols-outlined text-sm">cancel</span>
                        <span>Cancelar</span>
                      </button>
                      <button
                        onClick={() => setTransferPaused(!transferPaused)}
                        className="text-primary flex items-center gap-1 hover:underline cursor-pointer"
                      >
                        <span className="material-symbols-outlined text-sm">
                          {transferPaused ? 'play_arrow' : 'pause'}
                        </span>
                        <span>{transferPaused ? 'Reanudar' : 'Pausar'}</span>
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* Historial de Archivos Recientes */}
          <div className="pt-2">
            <h3 className="text-on-surface font-headline text-sm font-bold tracking-tight pb-2">
              {fileFilter === 'recibidos' ? 'Archivos recibidos recientemente' : 'Archivos enviados al PC'}
            </h3>

            <div className="space-y-2">
              {(fileFilter === 'recibidos' ? receivedFiles : sentFiles).map((file) => (
                <div
                  key={file.id}
                  className="flex items-center justify-between p-3 bg-surface-container-lowest rounded-xl border border-outline-variant/40 hover:bg-surface-container-low transition-colors"
                >
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="p-2 bg-secondary-container/40 text-secondary rounded-lg flex items-center justify-center shrink-0 border border-secondary/20">
                      <span className="material-symbols-outlined text-xl">
                        {file.type === 'movie'
                          ? 'movie'
                          : file.type === 'image'
                          ? 'image'
                          : file.type === 'code'
                          ? 'terminal'
                          : 'description'}
                      </span>
                    </div>
                    <div className="truncate">
                      <p className="font-headline text-xs font-bold text-on-surface truncate">{file.name}</p>
                      <p className="font-label text-[11px] text-outline">
                        {file.size} • {file.time}
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center gap-2">
                    <span className="font-label text-[11px] text-emerald-500 font-semibold flex items-center gap-0.5">
                      <span className="material-symbols-outlined text-sm">check_circle</span>
                      Completado
                    </span>
                    <button
                      onClick={() => onShowToast(`Abriendo ${file.name}...`)}
                      className="text-outline hover:text-on-surface p-1 rounded hover:bg-surface-container transition-colors cursor-pointer"
                      title="Abrir"
                    >
                      <span className="material-symbols-outlined text-base">folder_open</span>
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </main>
      )}

      {/* TAB 2: MULTIMEDIA */}
      {mainTab === 'multimedia' && (
        <main className="flex-1 flex flex-col p-4 space-y-5">
          {/* Grabación y Captura */}
          <section className="p-4 bg-surface-container-lowest rounded-xl border border-outline-variant/40 shadow-xs space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-primary">fiber_manual_record</span>
                <h3 className="font-headline text-sm font-bold text-on-surface">Grabación y Captura remota</h3>
              </div>
              {isRecording ? (
                <div className="flex items-center gap-1.5 bg-rose-500/10 text-rose-500 border border-rose-500/30 px-2 py-0.5 rounded font-label text-xs font-semibold">
                  <span className="size-2 rounded-full bg-rose-500 animate-ping"></span>
                  <span>{formatTimer(recordSeconds)} REC</span>
                </div>
              ) : (
                <div className="flex items-center gap-1.5 bg-surface-container text-outline px-2 py-0.5 rounded font-label text-xs">
                  <span>STANDBY</span>
                </div>
              )}
            </div>

            <p className="text-xs text-on-surface-variant font-body leading-relaxed">
              Captura directa del escritorio remoto de Windows y audio sintetizado sin retraso.
            </p>

            <div className="grid grid-cols-2 gap-3 pt-1">
              {/* Botón Iniciar/Detener Grabación */}
              <button
                onClick={onToggleRecord}
                className={`flex flex-col items-center justify-center p-3 rounded-xl font-headline font-bold text-xs gap-1 transition-all cursor-pointer ${
                  isRecording
                    ? 'bg-rose-600 hover:bg-rose-700 text-white'
                    : 'bg-primary hover:opacity-95 text-on-primary'
                }`}
              >
                <span className="material-symbols-outlined text-2xl">
                  {isRecording ? 'stop_circle' : 'fiber_manual_record'}
                </span>
                <span>{isRecording ? 'Detener grabación' : 'Iniciar grabación'}</span>
              </button>

              {/* Botón Captura de pantalla */}
              <button
                onClick={handleScreenCapture}
                className="flex flex-col items-center justify-center p-3 rounded-xl bg-surface-container hover:bg-surface-container-high text-on-surface font-headline font-bold text-xs gap-1 border border-outline-variant/60 transition-all cursor-pointer"
              >
                <span className="material-symbols-outlined text-2xl text-primary">screenshot_monitor</span>
                <span>Captura de pantalla</span>
              </button>
            </div>
          </section>

          {/* Control de Audio */}
          <section className="p-4 bg-surface-container-lowest rounded-xl border border-outline-variant/40 shadow-xs space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-primary">volume_up</span>
                <h3 className="font-headline text-sm font-bold text-on-surface">Canal de Audio Transmitido</h3>
              </div>
              <span className="font-label text-[11px] text-outline font-semibold">48 kHz / 24-bit</span>
            </div>

            <div className="space-y-2">
              {/* Modo 1: Audio interno teléfono */}
              <label
                onClick={() => setAudioMode('internal')}
                className={`flex items-center justify-between p-3 rounded-xl border cursor-pointer transition-all ${
                  audioMode === 'internal'
                    ? 'border-primary bg-primary/10'
                    : 'border-outline-variant/40 bg-surface-container-low hover:border-primary/50'
                }`}
              >
                <div className="flex items-center gap-3">
                  <input
                    type="radio"
                    name="audio-mode"
                    checked={audioMode === 'internal'}
                    onChange={() => setAudioMode('internal')}
                    className="text-primary focus:ring-primary h-4 w-4"
                  />
                  <div>
                    <p className="font-headline text-xs font-bold text-on-surface">Audio interno teléfono</p>
                    <p className="text-[11px] text-on-surface-variant">Reproducir exclusivamente en el altavoz del móvil</p>
                  </div>
                </div>
                <span className="material-symbols-outlined text-outline">smartphone</span>
              </label>

              {/* Modo 2: Audio interno <-> interno */}
              <label
                onClick={() => setAudioMode('bidirectional')}
                className={`flex items-center justify-between p-3 rounded-xl border cursor-pointer transition-all ${
                  audioMode === 'bidirectional'
                    ? 'border-primary bg-primary/10'
                    : 'border-outline-variant/40 bg-surface-container-low hover:border-primary/50'
                }`}
              >
                <div className="flex items-center gap-3">
                  <input
                    type="radio"
                    name="audio-mode"
                    checked={audioMode === 'bidirectional'}
                    onChange={() => setAudioMode('bidirectional')}
                    className="text-primary focus:ring-primary h-4 w-4"
                  />
                  <div>
                    <p className="font-headline text-xs font-bold text-on-surface">Audio interno ↔ interno</p>
                    <p className="text-[11px] text-on-surface-variant">Sincronización simultánea PC + Android en tiempo real</p>
                  </div>
                </div>
                <span className="material-symbols-outlined text-primary">sync_alt</span>
              </label>

              {/* Modo 3: Audio independiente */}
              <div
                className={`p-3 rounded-xl border space-y-2 transition-all ${
                  audioMode === 'independent'
                    ? 'border-primary bg-primary/10'
                    : 'border-outline-variant/40 bg-surface-container-low'
                }`}
              >
                <label
                  onClick={() => setAudioMode('independent')}
                  className="flex items-center justify-between cursor-pointer"
                >
                  <div className="flex items-center gap-3">
                    <input
                      type="radio"
                      name="audio-mode"
                      checked={audioMode === 'independent'}
                      onChange={() => setAudioMode('independent')}
                      className="text-primary focus:ring-primary h-4 w-4"
                    />
                    <div>
                      <p className="font-headline text-xs font-bold text-on-surface">Audio independiente</p>
                      <p className="text-[11px] text-on-surface-variant">Enrutamiento selectivo a tarjeta de sonido remota</p>
                    </div>
                  </div>
                  <span className="material-symbols-outlined text-outline">headphones</span>
                </label>

                {/* Selector de Dispositivo Activo */}
                <div className="pt-2 pl-7">
                  <label className="block font-label text-[10px] uppercase text-outline mb-1 font-semibold">
                    Salida activa Windows/BT:
                  </label>
                  <select
                    value={audioOutput}
                    onChange={(e) => {
                      setAudioOutput(e.target.value);
                      onShowToast(`Salida asignada a ${e.target.value}`);
                    }}
                    className="w-full bg-surface-container-lowest text-on-surface text-xs font-label py-1.5 px-2.5 rounded-lg border border-outline-variant focus:border-primary cursor-pointer"
                  >
                    <option>Auriculares Bluetooth NOVORA Pro (Realtek ASIO)</option>
                    <option>Altavoces de escritorio Realtek Audio HD</option>
                    <option>Salida Virtual Novora Link Audio Driver</option>
                  </select>
                </div>
              </div>
            </div>
          </section>

          {/* Ajuste Rápido Burbuja Flotante */}
          <section className="p-4 bg-surface-container-lowest rounded-xl border border-outline-variant/40 shadow-xs space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-secondary">bubble_chart</span>
                <div>
                  <h3 className="font-headline text-sm font-bold text-on-surface">
                    Burbuja flotante de acceso rápido
                  </h3>
                  <p className="text-[11px] text-on-surface-variant">Overlay flotante durante streaming/juego</p>
                </div>
              </div>

              {/* Switch Toggle ON/OFF */}
              <label className="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  checked={bubbleEnabled}
                  onChange={(e) => onToggleBubble(e.target.checked)}
                  className="sr-only peer"
                />
                <div className="w-11 h-6 bg-surface-container-highest peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-surface-variant after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-primary"></div>
              </label>
            </div>

            {/* Controles secundarios de la burbuja (Opacidad y Tamaño) */}
            <div
              className={`pt-2 space-y-3 border-t border-outline-variant/30 transition-opacity ${
                bubbleEnabled ? 'opacity-100' : 'opacity-40 pointer-events-none'
              }`}
            >
              <div>
                <div className="flex justify-between text-xs font-label text-outline mb-1">
                  <span>Opacidad</span>
                  <span className="font-bold text-on-surface">{bubbleOpacity}%</span>
                </div>
                <input
                  type="range"
                  min="20"
                  max="100"
                  value={bubbleOpacity}
                  onChange={(e) => onChangeBubbleOpacity(Number(e.target.value))}
                  className="w-full h-1.5 bg-surface-container-highest rounded-lg appearance-none cursor-pointer accent-primary"
                />
              </div>

              <div>
                <div className="flex justify-between text-xs font-label text-outline mb-1">
                  <span>Tamaño del Overlay</span>
                  <span className="font-bold text-on-surface">
                    {bubbleSize === 'small' ? 'Pequeño (40 dp)' : bubbleSize === 'medium' ? 'Medio (48 dp)' : 'Grande (56 dp)'}
                  </span>
                </div>
                <div className="grid grid-cols-3 gap-2">
                  <button
                    onClick={() => onChangeBubbleSize('small')}
                    className={`py-1.5 text-xs font-headline rounded-lg border transition-colors cursor-pointer ${
                      bubbleSize === 'small'
                        ? 'border-primary bg-primary/10 text-primary font-bold'
                        : 'border-outline-variant text-on-surface-variant hover:bg-surface-container'
                    }`}
                  >
                    Pequeño
                  </button>
                  <button
                    onClick={() => onChangeBubbleSize('medium')}
                    className={`py-1.5 text-xs font-headline rounded-lg border transition-colors cursor-pointer ${
                      bubbleSize === 'medium'
                        ? 'border-primary bg-primary/10 text-primary font-bold'
                        : 'border-outline-variant text-on-surface-variant hover:bg-surface-container'
                    }`}
                  >
                    Medio
                  </button>
                  <button
                    onClick={() => onChangeBubbleSize('large')}
                    className={`py-1.5 text-xs font-headline rounded-lg border transition-colors cursor-pointer ${
                      bubbleSize === 'large'
                        ? 'border-primary bg-primary/10 text-primary font-bold'
                        : 'border-outline-variant text-on-surface-variant hover:bg-surface-container'
                    }`}
                  >
                    Grande
                  </button>
                </div>
              </div>
            </div>
          </section>
        </main>
      )}
    </div>
  );
};
