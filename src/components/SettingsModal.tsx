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
    <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="w-full max-w-sm rounded-2xl bg-surface-container-lowest border border-primary/30 p-5 space-y-4 shadow-2xl animate-in fade-in zoom-in-95 duration-150">
        <div className="flex items-center justify-between border-b border-outline-variant/30 pb-3">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-primary text-[22px]">tune</span>
            <h3 className="font-headline font-bold text-base text-on-surface">Ajustes NOVORA-LINK</h3>
          </div>
          <button
            onClick={onClose}
            className="text-outline hover:text-on-surface p-1 rounded-lg hover:bg-surface-container transition-colors cursor-pointer"
          >
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        <div className="space-y-3.5 text-xs font-body">
          {/* Tema */}
          <div className="flex items-center justify-between p-2.5 rounded-xl bg-surface-container-low border border-outline-variant/30">
            <div>
              <p className="font-headline font-bold text-on-surface">Tema de Interfaz</p>
              <p className="text-[11px] text-on-surface-variant">
                {isDarkMode ? 'Modo Oscuro (Obsidian Cyber)' : 'Modo Claro (Clean Light)'}
              </p>
            </div>
            <button
              onClick={onToggleTheme}
              className="py-1.5 px-3 rounded-lg bg-surface-container hover:bg-surface-container-high border border-outline-variant font-semibold text-primary cursor-pointer"
            >
              {isDarkMode ? 'Cambiar a Claro' : 'Cambiar a Oscuro'}
            </button>
          </div>

          {/* Polling Rate */}
          <div className="p-2.5 rounded-xl bg-surface-container-low border border-outline-variant/30 space-y-1.5">
            <div className="flex justify-between items-center">
              <p className="font-headline font-bold text-on-surface">Frecuencia de Sondeo HID</p>
              <span className="font-mono text-primary font-bold">{pollingRate} Hz</span>
            </div>
            <div className="grid grid-cols-3 gap-1.5 pt-1">
              {[250, 500, 1000].map((rate) => (
                <button
                  key={rate}
                  onClick={() => {
                    onChangePollingRate(rate);
                    onShowToast(`Polling rate ajustado a ${rate}Hz`);
                  }}
                  className={`py-1 text-xs font-headline rounded-lg border transition-colors cursor-pointer ${
                    pollingRate === rate
                      ? 'border-primary bg-primary/10 text-primary font-bold'
                      : 'border-outline-variant text-on-surface hover:bg-surface-container'
                  }`}
                >
                  {rate} Hz
                </button>
              ))}
            </div>
          </div>

          {/* Burbuja flotante */}
          <div className="flex items-center justify-between p-2.5 rounded-xl bg-surface-container-low border border-outline-variant/30">
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
              <div className="w-10 h-5 bg-surface-container-highest peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-surface-variant after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-primary"></div>
            </label>
          </div>

          {/* Acceso Rápido a Telemetría */}
          <div className="pt-1 space-y-2">
            <button
              onClick={() => {
                onClose();
                onNavigate('hardware');
              }}
              className="w-full py-2.5 px-3 rounded-xl bg-surface-container-high hover:bg-surface-container text-on-surface border border-outline-variant/40 font-headline font-semibold text-xs flex items-center justify-between cursor-pointer"
            >
              <span className="flex items-center gap-2">
                <span className="material-symbols-outlined text-primary text-[18px]">terminal</span>
                <span>Inspeccionar Hardware &amp; Kernel</span>
              </span>
              <span className="material-symbols-outlined text-[16px] text-outline">arrow_forward</span>
            </button>

            <button
              onClick={() => {
                onClose();
                onNavigate('live_test');
              }}
              className="w-full py-2.5 px-3 rounded-xl bg-surface-container-high hover:bg-surface-container text-on-surface border border-outline-variant/40 font-headline font-semibold text-xs flex items-center justify-between cursor-pointer"
            >
              <span className="flex items-center gap-2">
                <span className="material-symbols-outlined text-secondary text-[18px]">sports_esports</span>
                <span>Calibración de Mandos HID</span>
              </span>
              <span className="material-symbols-outlined text-[16px] text-outline">arrow_forward</span>
            </button>
          </div>
        </div>

        <button
          onClick={onClose}
          className="w-full py-2.5 bg-primary text-on-primary rounded-xl font-headline font-bold text-xs hover:opacity-95 transition-opacity cursor-pointer shadow-xs"
        >
          Guardar y Cerrar
        </button>
      </div>
    </div>
  );
};
