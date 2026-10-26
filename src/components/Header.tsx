import React from 'react';
import { ScreenType } from '../types/index.ts';

interface HeaderProps {
  currentScreen: ScreenType;
  onNavigate: (screen: ScreenType) => void;
  isDarkMode: boolean;
  onToggleTheme: () => void;
  onOpenSettings?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  currentScreen,
  onNavigate,
  isDarkMode,
  onToggleTheme,
  onOpenSettings,
}) => {
  // Back navigation handler
  const handleBack = () => {
    if (currentScreen === 'live_test') {
      onNavigate('engines');
    } else if (currentScreen === 'hardware') {
      onNavigate('engines');
    } else {
      onNavigate('inicio');
    }
  };

  // Specific custom headers based on screen
  if (currentScreen === 'inicio') {
    return (
      <header className="sticky top-0 z-30 bg-surface-container-lowest/90 backdrop-blur-md border-b border-outline-variant/30 px-4 py-3">
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-primary/10 border border-primary/20 flex items-center justify-center text-primary">
              <span className="material-symbols-outlined text-[20px]">sync_saved_locally</span>
            </div>
            <div>
              <div className="flex items-center gap-1.5">
                <h1 className="font-headline font-bold text-base tracking-tight text-on-surface leading-none">NOVORA-LINK</h1>
                <span className="font-label text-[10px] font-bold px-1.5 py-0.5 rounded bg-surface-container-high text-primary tracking-normal">
                  v1.4.19
                </span>
              </div>
              <p className="text-[10px] text-outline font-label uppercase tracking-wider mt-0.5">Control Remoto &amp; Link</p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <div className="hidden xs:flex items-center gap-1.5 bg-surface-container-low border border-primary/30 px-2.5 py-1 rounded-full">
              <span className="relative flex h-2 w-2">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-primary opacity-75"></span>
                <span className="relative inline-flex rounded-full h-2 w-2 bg-primary"></span>
              </span>
              <span className="font-label text-[10px] font-bold text-primary uppercase tracking-wider">USB</span>
              <span className="text-outline text-xs">|</span>
              <span className="font-body text-[10px] font-medium text-on-surface-variant">PC OK</span>
            </div>

            <button
              onClick={onToggleTheme}
              className="flex size-9 items-center justify-center rounded-lg text-on-surface hover:bg-surface-container transition-colors"
              title={isDarkMode ? 'Cambiar a Modo Claro' : 'Cambiar a Modo Oscuro'}
              aria-label="Cambiar tema"
            >
              <span className="material-symbols-outlined text-[20px]">
                {isDarkMode ? 'light_mode' : 'dark_mode'}
              </span>
            </button>

            {onOpenSettings && (
              <button
                onClick={onOpenSettings}
                className="flex size-9 items-center justify-center rounded-lg text-on-surface hover:bg-surface-container transition-colors"
                title="Ajustes de Subsistema"
                aria-label="Ajustes"
              >
                <span className="material-symbols-outlined text-[20px]">tune</span>
              </button>
            )}
          </div>
        </div>
      </header>
    );
  }

  if (currentScreen === 'conectar') {
    return (
      <header className="sticky top-0 z-40 flex items-center justify-between border-b border-outline-variant/40 bg-surface-container-lowest/90 backdrop-blur-md px-4 py-3">
        <div className="flex size-10 items-center justify-center rounded-xl bg-surface-container border border-outline-variant text-primary shadow-xs">
          <span className="material-symbols-outlined text-2xl">memory</span>
        </div>
        <div className="flex flex-col items-center">
          <span className="text-[10px] tracking-widest uppercase font-label text-primary font-bold">
            NOVORA SYSTEM BUS
          </span>
          <h1 className="text-sm font-bold tracking-tight text-on-surface font-headline flex items-center gap-1.5">
            NOVORA-LINK{' '}
            <span className="text-[10px] font-label px-1.5 py-0.5 rounded bg-surface-container-high text-secondary border border-outline-variant/70 font-semibold">
              v1.4.19
            </span>
          </h1>
        </div>
        <div className="flex items-center gap-1.5">
          <button
            onClick={onToggleTheme}
            className="flex size-10 items-center justify-center rounded-xl bg-surface-container border border-outline-variant text-on-surface-variant hover:text-primary hover:border-primary/40 transition-colors"
            title={isDarkMode ? 'Modo Claro' : 'Modo Oscuro'}
            aria-label="Cambiar tema"
          >
            <span className="material-symbols-outlined text-[20px]">
              {isDarkMode ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
          <button
            onClick={onOpenSettings}
            className="flex size-10 items-center justify-center rounded-xl bg-surface-container border border-outline-variant text-on-surface-variant hover:text-primary hover:border-primary/40 transition-colors"
            title="Ajustes"
            aria-label="Ajustes"
          >
            <span className="material-symbols-outlined text-[20px]">tune</span>
          </button>
        </div>
      </header>
    );
  }

  if (currentScreen === 'engines') {
    return (
      <header className="sticky top-0 z-30 bg-surface-container-lowest/90 backdrop-blur-md border-b border-outline-variant/30 px-4 py-3 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-lg bg-primary-container/20 flex items-center justify-center text-primary">
            <span className="material-symbols-outlined text-[20px]">memory</span>
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="font-headline font-bold text-base tracking-tight text-on-surface">Engines • NOVORA-LINK</h1>
              <span className="px-1.5 py-0.5 rounded bg-surface-container-highest text-[10px] font-label font-semibold text-primary">
                v1.4.19
              </span>
            </div>
            <p className="text-xs text-on-surface-variant font-label">Subordinación Modular Android</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={onToggleTheme}
            aria-label="Alternar modo oscuro o claro"
            className="w-8 h-8 rounded-lg bg-surface-container hover:bg-surface-container-high border border-outline-variant/30 flex items-center justify-center text-on-surface-variant hover:text-on-surface transition-colors cursor-pointer"
          >
            <span className="material-symbols-outlined text-[18px]">
              {isDarkMode ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
          <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-surface-container border border-outline-variant/30 text-xs font-label">
            <span className="inline-block w-2 h-2 rounded-full bg-primary animate-pulse"></span>
            <span className="text-on-surface font-medium">Link OK</span>
          </div>
        </div>
      </header>
    );
  }

  if (currentScreen === 'live_test') {
    return (
      <header className="sticky top-0 z-30 flex items-center bg-surface-container-lowest px-4 py-3 justify-between border-b border-outline-variant/30">
        <button
          onClick={handleBack}
          className="text-on-surface flex size-10 shrink-0 items-center justify-center rounded-lg hover:bg-surface-container-low transition-colors"
          title="Retorno a Engines"
          aria-label="Volver"
        >
          <span className="material-symbols-outlined text-[24px]">arrow_back</span>
        </button>
        <div className="flex-1 text-center">
          <div className="flex items-center justify-center gap-2">
            <h1 className="text-on-surface font-headline text-lg font-bold tracking-tight">ExInEngine</h1>
            <span className="text-[10px] font-label font-semibold px-1.5 py-0.5 rounded bg-surface-container-high text-primary tracking-widest">
              v1.4.19
            </span>
          </div>
          <p className="text-xs text-outline font-label uppercase tracking-wider">NOVORA-LINK Hardware Diagnostic</p>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={onToggleTheme}
            className="flex size-10 shrink-0 items-center justify-center rounded-lg text-on-surface hover:bg-surface-container-low transition-colors"
            title="Cambiar tema Claro / Oscuro"
            aria-label="Cambiar tema"
          >
            <span className="material-symbols-outlined text-[22px]">
              {isDarkMode ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
          <button
            onClick={onOpenSettings}
            className="flex size-10 shrink-0 items-center justify-center rounded-lg text-on-surface hover:bg-surface-container-low transition-colors"
            title="Ajustes de Engine"
            aria-label="Ajustes"
          >
            <span className="material-symbols-outlined text-[22px]">tune</span>
          </button>
        </div>
      </header>
    );
  }

  if (currentScreen === 'hardware') {
    return (
      <header className="sticky top-0 z-40 bg-surface-container-lowest border-b border-outline-variant/40 flex justify-between items-center w-full px-4 py-3">
        <div className="flex items-center gap-2">
          <button
            onClick={handleBack}
            aria-label="Volver"
            className="flex items-center gap-1.5 py-1 px-2 rounded hover:bg-surface-container text-on-surface-variant hover:text-primary transition-all active:scale-95"
          >
            <span className="material-symbols-outlined text-[18px]">arrow_back</span>
            <span className="font-label text-xs uppercase font-semibold">Volver</span>
          </button>
          <div className="h-4 w-px bg-outline-variant/60 mx-1"></div>
          <div className="flex items-center gap-1.5">
            <span className="material-symbols-outlined text-primary text-[20px]">memory</span>
            <span className="font-headline text-sm font-bold text-on-surface tracking-tight">Hardware &amp; Kernel</span>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <div className="hidden xs:flex items-center gap-1 px-2 py-0.5 rounded bg-surface-container border border-outline-variant/50">
            <span className="w-1.5 h-1.5 rounded-full bg-primary animate-pulse"></span>
            <span className="font-label text-[10px] text-primary font-mono font-bold">SYS_STAT: NOMINAL</span>
          </div>
          <button
            onClick={onToggleTheme}
            className="flex size-8 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container transition-colors"
            title="Cambiar tema"
          >
            <span className="material-symbols-outlined text-[18px]">
              {isDarkMode ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
        </div>
      </header>
    );
  }

  // Default / Archivos / Multimedia
  return (
    <header className="sticky top-0 z-30 bg-surface-container-lowest/95 backdrop-blur-md border-b border-outline-variant/30 px-4 py-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <button
            onClick={() => onNavigate('inicio')}
            aria-label="Volver"
            className="text-on-surface flex size-9 shrink-0 items-center justify-center rounded-lg hover:bg-surface-container transition-colors"
          >
            <span className="material-symbols-outlined text-[22px]">arrow_back</span>
          </button>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-on-surface font-headline text-base font-bold tracking-tight">
                {currentScreen === 'multimedia' ? 'Multimedia & Captura' : 'Archivos y Multimedia'}
              </h1>
              <span className="font-label text-[10px] bg-primary/10 text-primary border border-primary/20 px-1.5 py-0.5 rounded font-bold uppercase">
                v1.4.19
              </span>
            </div>
            <p className="text-[11px] text-outline font-label">NOVORA-LINK • Enlace Windows Activo</p>
          </div>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={onToggleTheme}
            className="flex size-9 items-center justify-center rounded-lg text-on-surface hover:bg-surface-container transition-colors"
            title="Cambiar tema"
          >
            <span className="material-symbols-outlined text-[20px]">
              {isDarkMode ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
          {onOpenSettings && (
            <button
              onClick={onOpenSettings}
              className="flex size-9 items-center justify-center rounded-lg text-on-surface hover:bg-surface-container transition-colors"
              title="Opciones"
            >
              <span className="material-symbols-outlined text-[20px]">more_vert</span>
            </button>
          )}
        </div>
      </div>
    </header>
  );
};
