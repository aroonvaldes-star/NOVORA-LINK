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
    <nav className="fixed bottom-0 left-0 right-0 z-40 mx-auto max-w-2xl bg-surface-container-lowest border-t border-outline-variant px-2 py-1.5">
      <div className="flex items-center justify-around">
        {tabs.map((tab) => {
          const isActive =
            currentScreen === tab.id ||
            (tab.id === 'engines' && (currentScreen === 'live_test' || currentScreen === 'hardware'));

          return (
            <button
              key={tab.id}
              onClick={() => onNavigate(tab.id)}
              aria-current={isActive ? 'page' : undefined}
              className={`relative flex flex-1 flex-col items-center justify-center py-1 gap-1 transition-colors duration-200 active:opacity-70 cursor-pointer ${
                isActive
                  ? 'text-on-surface font-bold'
                  : 'text-on-surface-variant hover:text-on-surface'
              }`}
            >
              {/* Swiss active marker: a solid 2px rule on the top edge, no tinted fill. */}
              <span
                aria-hidden="true"
                className={`absolute -top-1.5 left-2 right-2 h-0.5 transition-colors duration-200 ${
                  isActive ? 'bg-primary' : 'bg-transparent'
                }`}
              />
              <div className="w-8 h-8 flex items-center justify-center">
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
