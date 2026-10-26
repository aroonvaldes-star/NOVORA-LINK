import React from 'react';
import { ScreenType } from '../types/index.ts';

interface BottomNavProps {
  currentScreen: ScreenType;
  onNavigate: (screen: ScreenType) => void;
}

export const BottomNav: React.FC<BottomNavProps> = ({ currentScreen, onNavigate }) => {
  const tabs: { id: ScreenType; label: string; icon: string }[] = [
    { id: 'inicio', label: 'Inicio', icon: 'home' },
    { id: 'conectar', label: 'Conectar', icon: 'swap_horiz' },
    { id: 'engines', label: 'Engines', icon: 'memory' },
    { id: 'archivos', label: 'Archivos', icon: 'folder' },
    { id: 'multimedia', label: 'Multimedia', icon: 'perm_media' },
  ];

  return (
    <nav className="fixed bottom-0 left-0 right-0 z-40 mx-auto max-w-2xl bg-surface-container-lowest/95 backdrop-blur-md border-t border-outline-variant/30 px-2 py-1.5 shadow-[0_-2px_12px_rgba(0,0,0,0.06)]">
      <div className="flex items-center justify-around">
        {tabs.map((tab) => {
          const isActive =
            currentScreen === tab.id ||
            (tab.id === 'engines' && (currentScreen === 'live_test' || currentScreen === 'hardware'));

          return (
            <button
              key={tab.id}
              onClick={() => onNavigate(tab.id)}
              className={`flex flex-1 flex-col items-center justify-center py-1 gap-1 transition-all active:scale-95 ${
                isActive
                  ? 'text-primary font-bold'
                  : 'text-outline hover:text-on-surface'
              }`}
            >
              <div
                className={`w-8 h-8 rounded-full flex items-center justify-center transition-colors ${
                  isActive ? 'bg-primary/10 text-primary' : 'text-outline'
                }`}
              >
                <span
                  className={`material-symbols-outlined text-[22px] ${
                    isActive ? 'material-symbols-filled' : ''
                  }`}
                >
                  {tab.icon}
                </span>
              </div>
              <span className="font-headline text-[10px] sm:text-[11px] font-semibold tracking-tight leading-none">
                {tab.label}
              </span>
            </button>
          );
        })}
      </div>
    </nav>
  );
};
