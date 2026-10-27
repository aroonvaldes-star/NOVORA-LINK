import React from 'react';
import { ScreenType } from '../types/index.ts';

interface SettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
  isDarkMode: boolean;
  onToggleTheme: () => void;
  pollingRate: number;
  onChangePollingRate: (rate: number) => void;
  bubbleEnabled: boolean;
  onToggleBubble: (enabled: boolean) => void;
  onNavigate: (screen: ScreenType) => void;
  onShowToast: (msg: string) => void;
}

export const SettingsModal: React.FC<SettingsModalProps> = ({
  isOpen,
  onClose,
  isDarkMode,
  onToggleTheme,
  pollingRate,
  onChangePollingRate,
  bubbleEnabled,
  onToggleBubble,
  onNavigate,
  onShowToast,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 bg-scrim flex items-center justify-center p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="settings-title"
        className="w-full max-w-sm bg-surface-container-lowest border border-on-surface p-5 space-y-4 animate-in fade-in zoom-in-95 duration-150"
      >
        <div className="flex items-center justify-between border-b border-outline-variant pb-3">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-on-surface text-[22px]">tune</span>
            <h3 id="settings-title" className="font-headline font-bold text-base text-on-surface">Ajustes NOVORA-LINK</h3>
          </div>
          <button
            onClick={onClose}
            aria-label="Cerrar ajustes"
            className="text-on-surface-variant hover:text-on-surface p-1 hover:bg-surface-container transition-colors cursor-pointer"
          >
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        <div className="space-y-3.5 text-xs font-body">
          {/* Tema */}
          <div className="flex items-center justify-between p-2.5 bg-surface-container-low border border-outline-variant">
            <div>
              <p className="font-headline font-bold text-on-surface">Tema de Interfaz</p>
              <p className="text-[11px] text-on-surface-variant">
                {isDarkMode ? 'Modo Oscuro (Obsidian Cyber)' : 'Modo Claro (Clean Light)'}
              </p>
            </div>
            <button
              onClick={onToggleTheme}
              className="py-1.5 px-3 bg-surface-container hover:bg-surface-container-high border border-outline font-semibold text-on-surface cursor-pointer transition-colors"
            >
              {isDarkMode ? 'Cambiar a Claro' : 'Cambiar a Oscuro'}
            </button>
          </div>

          {/* Polling Rate */}
          <div className="p-2.5 bg-surface-container-low border border-outline-variant space-y-1.5">
            <div className="flex justify-between items-center">
              <p className="font-headline font-bold text-on-surface">Frecuencia de Sondeo HID</p>
              <span className="font-mono text-on-surface font-bold">{pollingRate} Hz</span>
            </div>
            <div className="grid grid-cols-3 gap-1.5 pt-1">
              {[250, 500, 1000].map((rate) => {
                const isSelected = pollingRate === rate;
                return (
                  <button
                    key={rate}
                    aria-pressed={isSelected}
                    onClick={() => {
                      onChangePollingRate(rate);
                      onShowToast(`Polling rate ajustado a ${rate}Hz`);
                    }}
                    className={`py-1 text-xs font-headline border transition-colors cursor-pointer ${
                      isSelected
                        ? 'border-primary bg-primary text-on-primary font-bold'
                        : 'border-outline-variant text-on-surface hover:bg-surface-container'
                    }`}
                  >
                    {rate} Hz
                  </button>
                );
              })}
            </div>
          </div>

          {/* Burbuja flotante */}
          <div className="flex items-center justify-between p-2.5 bg-surface-container-low border border-outline-variant">
            <div>
              <p className="font-headline font-bold text-on-surface">Burbuja Rápida en Pantalla</p>
              <p className="text-[11px] text-on-surface-variant">Acceso flotante a controles</p>
            </div>
            <label className="relative inline-flex items-center cursor-pointer">
              <input
                type="checkbox"
                checked={bubbleEnabled}
                onChange={(e) => onToggleBubble(e.target.checked)}
                className="sr-only peer"
              />
              <div className="w-10 h-5 bg-surface-container-highest border border-outline peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-primary peer-checked:bg-primary peer-checked:border-primary after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-outline after:h-4 after:w-4 after:transition-all peer-checked:after:translate-x-full peer-checked:after:bg-on-primary"></div>
            </label>
          </div>

          {/* Acceso Rápido a Telemetría */}
          <div className="pt-1 space-y-2">
            <button
              onClick={() => {
                onClose();
                onNavigate('hardware');
              }}
              className="w-full py-2.5 px-3 bg-surface-container-high hover:bg-surface-container text-on-surface border border-outline font-headline font-semibold text-xs flex items-center justify-between cursor-pointer transition-colors"
            >
              <span className="flex items-center gap-2">
                <span className="material-symbols-outlined text-on-surface text-[18px]">terminal</span>
                <span>Inspeccionar Hardware &amp; Kernel</span>
              </span>
              <span className="material-symbols-outlined text-[16px] text-on-surface-variant">arrow_forward</span>
            </button>

            <button
              onClick={() => {
                onClose();
                onNavigate('live_test');
              }}
              className="w-full py-2.5 px-3 bg-surface-container-high hover:bg-surface-container text-on-surface border border-outline font-headline font-semibold text-xs flex items-center justify-between cursor-pointer transition-colors"
            >
              <span className="flex items-center gap-2">
                <span className="material-symbols-outlined text-on-surface-variant text-[18px]">sports_esports</span>
                <span>Calibración de Mandos HID</span>
              </span>
              <span className="material-symbols-outlined text-[16px] text-on-surface-variant">arrow_forward</span>
            </button>
          </div>
        </div>

        <button
          onClick={onClose}
          className="w-full py-2.5 bg-primary text-on-primary font-headline font-bold text-xs hover:opacity-90 transition-opacity cursor-pointer"
        >
          Guardar y Cerrar
        </button>
      </div>
    </div>
  );
};
