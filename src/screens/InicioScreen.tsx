import React from 'react';
import { ScreenType } from '../types/index.ts';
import { NovoraLogo } from '../components/NovoraLogo.tsx';

interface InicioScreenProps {
  onNavigate: (screen: ScreenType) => void;
  isDarkMode: boolean;
  stEngineActive: boolean;
  onToggleStEngine: () => void;
}

export const InicioScreen: React.FC<InicioScreenProps> = ({
  onNavigate,
  isDarkMode,
  stEngineActive,
  onToggleStEngine,
}) => {
  return (
    <div className="flex-1 flex flex-col px-4 pt-3 pb-24 gap-5 max-w-md mx-auto w-full">
      {/* Welcome Hero Banner */}
      <section className="relative overflow-hidden rounded-xl border border-primary/20 shadow-sm bg-black flex items-center justify-center">
        <NovoraLogo variant="banner" theme={isDarkMode ? 'dark' : 'light'} />
        <div className="absolute top-3 left-3 z-10">
          <div className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded bg-primary/20 border border-primary/40 backdrop-blur-sm text-primary text-[10px] font-label font-bold uppercase tracking-wider">
            <span className="material-symbols-outlined text-[14px]">bolt</span>
            <span>Sistema Operativo</span>
          </div>
        </div>
      </section>

      {/* 2x2 Quick Access Grid */}
      <section>
        <div className="flex items-center justify-between mb-2.5">
          <h3 className="font-headline text-xs font-bold uppercase tracking-wider text-on-surface-variant flex items-center gap-1.5">
            <span className="material-symbols-outlined text-[16px] text-primary">apps</span>
            Accesos Rápidos
          </h3>
          <span className="font-label text-[10px] text-outline font-medium">Panel Primario</span>
        </div>

        <div className="grid grid-cols-2 gap-2.5">
          {/* Card 1: CONECTAR */}
          <button
            onClick={() => onNavigate('conectar')}
            className="group relative flex flex-col justify-between p-3.5 rounded-lg bg-surface border border-primary/30 hover:border-primary shadow-xs hover:shadow transition-all overflow-hidden text-left cursor-pointer active:scale-[0.98]"
          >
            <div className="absolute -right-4 -top-4 w-12 h-12 rounded-full bg-primary/5 pointer-events-none group-hover:scale-150 transition-transform"></div>
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center border border-primary/20 group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">cable</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-outline group-hover:text-primary transition-colors">
                arrow_forward
              </span>
            </div>
            <div>
              <h4 className="font-headline text-sm font-bold text-on-surface tracking-tight leading-tight">
                CONECTAR
              </h4>
              <p className="font-body text-xs text-on-surface-variant font-medium mt-0.5">PC ↔ Android</p>
            </div>
          </button>

          {/* Card 2: ENGINES */}
          <button
            onClick={() => onNavigate('engines')}
            className="group relative flex flex-col justify-between p-3.5 rounded-lg bg-surface border border-primary/30 hover:border-primary shadow-xs hover:shadow transition-all overflow-hidden text-left cursor-pointer active:scale-[0.98]"
          >
            <div className="absolute -right-4 -top-4 w-12 h-12 rounded-full bg-primary/5 pointer-events-none group-hover:scale-150 transition-transform"></div>
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center border border-primary/20 group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">tune</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-outline group-hover:text-primary transition-colors">
                arrow_forward
              </span>
            </div>
            <div>
              <h4 className="font-headline text-sm font-bold text-on-surface tracking-tight leading-tight">
                ENGINES
              </h4>
              <p className="font-body text-xs text-on-surface-variant font-medium mt-0.5">Estado</p>
            </div>
          </button>

          {/* Card 3: ARCHIVOS */}
          <button
            onClick={() => onNavigate('archivos')}
            className="group relative flex flex-col justify-between p-3.5 rounded-lg bg-surface border border-primary/30 hover:border-primary shadow-xs hover:shadow transition-all overflow-hidden text-left cursor-pointer active:scale-[0.98]"
          >
            <div className="absolute -right-4 -top-4 w-12 h-12 rounded-full bg-primary/5 pointer-events-none group-hover:scale-150 transition-transform"></div>
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center border border-primary/20 group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">folder_shared</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-outline group-hover:text-primary transition-colors">
                arrow_forward
              </span>
            </div>
            <div>
              <h4 className="font-headline text-sm font-bold text-on-surface tracking-tight leading-tight">
                ARCHIVOS
              </h4>
              <p className="font-body text-xs text-on-surface-variant font-medium mt-0.5">Enviar/Rec.</p>
            </div>
          </button>

          {/* Card 4: MULTIMEDIA */}
          <button
            onClick={() => onNavigate('multimedia')}
            className="group relative flex flex-col justify-between p-3.5 rounded-lg bg-surface border border-primary/30 hover:border-primary shadow-xs hover:shadow transition-all overflow-hidden text-left cursor-pointer active:scale-[0.98]"
          >
            <div className="absolute -right-4 -top-4 w-12 h-12 rounded-full bg-primary/5 pointer-events-none group-hover:scale-150 transition-transform"></div>
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center border border-primary/20 group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">screen_record</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-outline group-hover:text-primary transition-colors">
                arrow_forward
              </span>
            </div>
            <div>
              <h4 className="font-headline text-sm font-bold text-on-surface tracking-tight leading-tight">
                MULTIMEDIA
              </h4>
              <p className="font-body text-xs text-on-surface-variant font-medium mt-0.5">Audio/Capt.</p>
            </div>
          </button>
        </div>
      </section>

      {/* Realtime Engine Status Section */}
      <section className="flex flex-col gap-2.5">
        <div className="flex items-center justify-between">
          <h3 className="font-headline text-xs font-bold uppercase tracking-wider text-on-surface-variant flex items-center gap-1.5">
            <span className="material-symbols-outlined text-[16px] text-primary">memory</span>
            Motores Oficiales (Tiempo Real)
          </h3>
          <span className="font-label text-[10px] text-primary font-bold bg-primary/10 px-2 py-0.5 rounded border border-primary/20">
            {stEngineActive ? '4/4 ACTIVOS' : '3/4 ACTIVOS'}
          </span>
        </div>

        <div className="flex flex-col gap-2 bg-surface-container-low p-2.5 rounded-xl border border-outline-variant/40">
          {/* Engine 1: LinkEngine */}
          <div
            onClick={() => onNavigate('engines')}
            className="flex items-center justify-between p-2.5 rounded-lg bg-surface-container-lowest border border-outline-variant/30 hover:border-primary/50 transition-colors cursor-pointer"
          >
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-primary/10 text-primary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">hub</span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none">LinkEngine</p>
                <p className="font-label text-[11px] text-outline mt-0.5">USB conectado</p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="inline-block w-2 h-2 rounded-full bg-emerald-500 animate-pulse"></span>
              <span className="font-label text-xs font-semibold text-emerald-500">Activo</span>
            </div>
          </div>

          {/* Engine 2: VisionEngine */}
          <div
            onClick={() => onNavigate('engines')}
            className="flex items-center justify-between p-2.5 rounded-lg bg-surface-container-lowest border border-outline-variant/30 hover:border-primary/50 transition-colors cursor-pointer"
          >
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-primary/10 text-primary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">videocam</span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none">VisionEngine</p>
                <p className="font-label text-[11px] text-outline mt-0.5">60 FPS, 1080p</p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="inline-block w-2 h-2 rounded-full bg-emerald-500 animate-pulse"></span>
              <span className="font-label text-xs font-semibold text-emerald-500">Activo</span>
            </div>
          </div>

          {/* Engine 3: ExInEngine */}
          <div
            onClick={() => onNavigate('live_test')}
            className="flex items-center justify-between p-2.5 rounded-lg bg-surface-container-lowest border border-outline-variant/30 hover:border-secondary/50 transition-colors cursor-pointer group"
          >
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 rounded bg-secondary-container/50 text-secondary flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">sports_esports</span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none group-hover:text-primary transition-colors">
                  ExInEngine
                </p>
                <p className="font-label text-[11px] text-outline mt-0.5">Control conectado (Tocar para probar)</p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="inline-block w-2 h-2 rounded-full bg-secondary"></span>
              <span className="font-label text-xs font-semibold text-secondary">Detectado</span>
            </div>
          </div>

          {/* Engine 4: STEngine */}
          <div
            onClick={onToggleStEngine}
            className={`flex items-center justify-between p-2.5 rounded-lg bg-surface-container-lowest border border-outline-variant/30 transition-colors cursor-pointer ${
              !stEngineActive ? 'opacity-80' : ''
            }`}
          >
            <div className="flex items-center gap-2.5">
              <div
                className={`w-7 h-7 rounded flex items-center justify-center ${
                  stEngineActive
                    ? 'bg-emerald-500/10 text-emerald-500'
                    : 'bg-error-container/40 text-error'
                }`}
              >
                <span className="material-symbols-outlined text-[18px]">
                  {stEngineActive ? 'sensors' : 'sensors_off'}
                </span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none">STEngine</p>
                <p className="font-label text-[11px] text-outline mt-0.5">
                  {stEngineActive ? 'Spatio-Temporal Sincronizado' : 'Servicio inactivo (Tocar para iniciar)'}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span
                className={`inline-block w-2 h-2 rounded-full ${
                  stEngineActive ? 'bg-emerald-500' : 'bg-rose-500'
                }`}
              ></span>
              <span
                className={`font-label text-xs font-semibold ${
                  stEngineActive ? 'text-emerald-500' : 'text-error'
                }`}
              >
                {stEngineActive ? 'Sincronizado' : 'No detectado'}
              </span>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
};
