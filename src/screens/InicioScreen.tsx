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
      <section className="relative overflow-hidden border border-outline-variant bg-surface-container-lowest flex items-center justify-center">
        <NovoraLogo variant="banner" theme={isDarkMode ? 'dark' : 'light'} />
        <div className="absolute top-3 left-3 z-10">
          <div className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-primary text-on-primary text-[10px] font-label font-bold uppercase tracking-wider">
            <span className="material-symbols-outlined text-[14px]">bolt</span>
            <span>Sistema Operativo</span>
          </div>
        </div>
      </section>

      {/* 2x2 Quick Access Grid */}
      <section>
        <div className="flex items-center justify-between mb-2.5">
          <h3 className="font-headline text-xs font-bold uppercase tracking-wider text-on-surface-variant flex items-center gap-1.5">
            <span className="material-symbols-outlined text-[16px] text-on-surface">apps</span>
            Accesos Rápidos
          </h3>
          <span className="font-label text-[10px] text-on-surface-variant font-medium">Panel Primario</span>
        </div>

        <div className="grid grid-cols-2 gap-2.5">
          {/* Card 1: CONECTAR */}
          <button
            onClick={() => onNavigate('conectar')}
            className="group flex flex-col justify-between p-3.5 bg-surface border border-outline hover:border-on-surface transition-colors text-left cursor-pointer active:opacity-80"
          >
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 bg-surface-container-high text-on-surface flex items-center justify-center border border-outline-variant group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">cable</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-on-surface-variant group-hover:text-on-surface transition-colors">
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
            className="group flex flex-col justify-between p-3.5 bg-surface border border-outline hover:border-on-surface transition-colors text-left cursor-pointer active:opacity-80"
          >
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 bg-surface-container-high text-on-surface flex items-center justify-center border border-outline-variant group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">tune</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-on-surface-variant group-hover:text-on-surface transition-colors">
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
            className="group flex flex-col justify-between p-3.5 bg-surface border border-outline hover:border-on-surface transition-colors text-left cursor-pointer active:opacity-80"
          >
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 bg-surface-container-high text-on-surface flex items-center justify-center border border-outline-variant group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">folder_shared</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-on-surface-variant group-hover:text-on-surface transition-colors">
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
            className="group flex flex-col justify-between p-3.5 bg-surface border border-outline hover:border-on-surface transition-colors text-left cursor-pointer active:opacity-80"
          >
            <div className="flex items-center justify-between mb-3 w-full">
              <div className="w-9 h-9 bg-surface-container-high text-on-surface flex items-center justify-center border border-outline-variant group-hover:bg-primary group-hover:text-on-primary transition-colors">
                <span className="material-symbols-outlined text-[20px]">screen_record</span>
              </div>
              <span className="material-symbols-outlined text-[16px] text-on-surface-variant group-hover:text-on-surface transition-colors">
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
            <span className="material-symbols-outlined text-[16px] text-on-surface">memory</span>
            Motores Oficiales (Tiempo Real)
          </h3>
          <span className="font-label text-[10px] text-on-surface font-bold bg-surface-container-high px-2 py-0.5 border border-outline-variant">
            {stEngineActive ? '4/4 ACTIVOS' : '3/4 ACTIVOS'}
          </span>
        </div>

        <div className="flex flex-col gap-2 bg-surface-container-low p-2.5 border border-outline-variant">
          {/* Engine 1: LinkEngine */}
          <div
            onClick={() => onNavigate('engines')}
            className="flex items-center justify-between p-2.5 bg-surface-container-lowest border border-outline hover:border-on-surface transition-colors cursor-pointer"
          >
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 bg-surface-container-high text-on-surface flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">hub</span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none">LinkEngine</p>
                <p className="font-label text-[11px] text-on-surface-variant mt-0.5">USB conectado</p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="inline-block w-2 h-2 rounded-full bg-success animate-pulse"></span>
              <span className="font-label text-xs font-semibold text-success">Activo</span>
            </div>
          </div>

          {/* Engine 2: VisionEngine */}
          <div
            onClick={() => onNavigate('engines')}
            className="flex items-center justify-between p-2.5 bg-surface-container-lowest border border-outline hover:border-on-surface transition-colors cursor-pointer"
          >
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 bg-surface-container-high text-on-surface flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">videocam</span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none">VisionEngine</p>
                <p className="font-label text-[11px] text-on-surface-variant mt-0.5">60 FPS, 1080p</p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="inline-block w-2 h-2 rounded-full bg-success animate-pulse"></span>
              <span className="font-label text-xs font-semibold text-success">Activo</span>
            </div>
          </div>

          {/* Engine 3: ExInEngine */}
          <div
            onClick={() => onNavigate('live_test')}
            className="flex items-center justify-between p-2.5 bg-surface-container-lowest border border-outline hover:border-on-surface transition-colors cursor-pointer group"
          >
            <div className="flex items-center gap-2.5">
              <div className="w-7 h-7 bg-surface-container-high text-on-surface-variant flex items-center justify-center">
                <span className="material-symbols-outlined text-[18px]">sports_esports</span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none group-hover:underline transition-colors">
                  ExInEngine
                </p>
                <p className="font-label text-[11px] text-on-surface-variant mt-0.5">Control conectado (Tocar para probar)</p>
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
            className={`flex items-center justify-between p-2.5 bg-surface-container-lowest border border-outline hover:border-on-surface transition-colors cursor-pointer ${
              !stEngineActive ? 'opacity-80' : ''
            }`}
          >
            <div className="flex items-center gap-2.5">
              <div
                className={`w-7 h-7 flex items-center justify-center ${
                  stEngineActive
                    ? 'bg-success-container text-success'
                    : 'bg-error-container text-error'
                }`}
              >
                <span className="material-symbols-outlined text-[18px]">
                  {stEngineActive ? 'sensors' : 'sensors_off'}
                </span>
              </div>
              <div>
                <p className="font-headline text-xs font-bold text-on-surface leading-none">STEngine</p>
                <p className="font-label text-[11px] text-on-surface-variant mt-0.5">
                  {stEngineActive ? 'Spatio-Temporal Sincronizado' : 'Servicio inactivo (Tocar para iniciar)'}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span
                className={`inline-block w-2 h-2 rounded-full ${
                  stEngineActive ? 'bg-success' : 'bg-error'
                }`}
              ></span>
              <span
                className={`font-label text-xs font-semibold ${
                  stEngineActive ? 'text-success' : 'text-error'
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
