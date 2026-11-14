import React, { useState } from 'react';
import { ScreenType } from '../types/index.ts';

interface FloatingBubbleProps {
  enabled: boolean;
  opacity: number; // 20 to 100
  size: 'small' | 'medium' | 'large';
  isRecording: boolean;
  onToggleRecord: () => void;
  onNavigate: (screen: ScreenType) => void;
}

export const FloatingBubble: React.FC<FloatingBubbleProps> = ({
  enabled,
  opacity,
  size,
  isRecording,
  onToggleRecord,
  onNavigate,
}) => {
  const [isOpen, setIsOpen] = useState(false);

  if (!enabled) return null;

  const sizeClasses = {
    small: 'size-10 text-[18px]',
    medium: 'size-12 text-[22px]',
    large: 'size-14 text-[26px]',
  }[size];

  return (
    <div className="fixed right-4 bottom-20 z-50 flex flex-col items-end select-none">
      {/* Quick Action Overlay Menu */}
      {isOpen && (
        <div
          className="mb-2 w-56 bg-surface-container-lowest border border-on-surface p-3 space-y-2 animate-in fade-in slide-in-from-bottom-2 duration-150"
          style={{ opacity: opacity / 100 }}
        >
          <div className="flex items-center justify-between pb-1.5 border-b border-outline-variant">
            <span className="font-headline font-bold text-xs text-on-surface">NOVORA Quick Overlay</span>
            <button
              onClick={() => setIsOpen(false)}
              aria-label="Cerrar menú rápido"
              className="text-on-surface-variant hover:text-on-surface p-0.5 cursor-pointer"
            >
              <span className="material-symbols-outlined text-[16px]">close</span>
            </button>
          </div>

          <div className="grid grid-cols-2 gap-1.5 text-xs font-label">
            <button
              onClick={() => {
                onToggleRecord();
              }}
              className={`p-2 flex flex-col items-center gap-1 border transition-colors cursor-pointer ${
                isRecording
                  ? 'bg-error-container border-error text-error'
                  : 'bg-surface-container hover:bg-surface-container-high border-outline-variant text-on-surface'
              }`}
            >
              <span className="material-symbols-outlined text-[18px]">
                {isRecording ? 'stop_circle' : 'fiber_manual_record'}
              </span>
              <span className="text-[10px] font-bold">{isRecording ? 'REC 04:18' : 'Grabar'}</span>
            </button>

            <button
              onClick={() => {
                onNavigate('live_test');
                setIsOpen(false);
              }}
              className="p-2 bg-surface-container hover:bg-surface-container-high border border-outline text-on-surface flex flex-col items-center gap-1 transition-colors cursor-pointer"
            >
              <span className="material-symbols-outlined text-[18px]">sports_esports</span>
              <span className="text-[10px] font-bold">Mando Test</span>
            </button>

            <button
              onClick={() => {
                onNavigate('hardware');
                setIsOpen(false);
              }}
              className="p-2 bg-surface-container hover:bg-surface-container-high border border-outline text-on-surface flex flex-col items-center gap-1 transition-colors cursor-pointer"
            >
              <span className="material-symbols-outlined text-[18px]">speed</span>
              <span className="text-[10px] font-bold">Telemetría</span>
            </button>

            <button
              onClick={() => {
                onNavigate('archivos');
                setIsOpen(false);
              }}
              className="p-2 bg-surface-container hover:bg-surface-container-high border border-outline text-on-surface flex flex-col items-center gap-1 transition-colors cursor-pointer"
            >
              <span className="material-symbols-outlined text-[18px]">folder_shared</span>
              <span className="text-[10px] font-bold">Archivos</span>
            </button>
          </div>

          <div className="pt-1 flex items-center justify-between text-[10px] font-label text-on-surface-variant border-t border-outline-variant">
            <span>Latencia: <strong className="text-on-surface">1.2ms</strong></span>
            <span>60 FPS</span>
          </div>
        </div>
      )}

      {/* Floating Trigger Button */}
      <button
        onClick={() => setIsOpen(!isOpen)}
        aria-expanded={isOpen}
        aria-label="Burbuja flotante de acceso rápido"
        style={{ opacity: opacity / 100 }}
        className={`${sizeClasses} rounded-full bg-primary text-on-primary border-2 border-primary flex items-center justify-center transition-transform hover:scale-105 active:scale-95 cursor-pointer relative`}
        title="Burbuja flotante de acceso rápido"
      >
        {isRecording ? (
          <span className="absolute -top-0.5 -right-0.5 size-3 rounded-full bg-error animate-ping"></span>
        ) : null}
        <span className="material-symbols-outlined">
          {isOpen ? 'close' : 'bubble_chart'}
        </span>
      </button>
    </div>
  );
};
