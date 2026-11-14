/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

import { useState, useEffect } from 'react';
import { ScreenType } from './types/index.ts';
import { Header } from './components/Header.tsx';
import { BottomNav } from './components/BottomNav.tsx';
import { FloatingBubble } from './components/FloatingBubble.tsx';
import { SettingsModal } from './components/SettingsModal.tsx';
import { InicioScreen } from './screens/InicioScreen.tsx';
import { ConectarScreen } from './screens/ConectarScreen.tsx';
import { EnginesScreen } from './screens/EnginesScreen.tsx';
import { ArchivosMultimediaScreen } from './screens/ArchivosMultimediaScreen.tsx';
import { LiveTestScreen } from './screens/LiveTestScreen.tsx';
import { HardwareKernelScreen } from './screens/HardwareKernelScreen.tsx';

export default function App() {
  const [currentScreen, setCurrentScreen] = useState<ScreenType>('inicio');
  const [isDarkMode, setIsDarkMode] = useState<boolean>(true);
  const [toastMessage, setToastMessage] = useState<string | null>(null);
  const [toastTimeout, setToastTimeout] = useState<number | null>(null);
  const [isSettingsOpen, setIsSettingsOpen] = useState<boolean>(false);

  // Global Engine / App States
  const [stEngineActive, setStEngineActive] = useState<boolean>(true);
  const [isRecording, setIsRecording] = useState<boolean>(false);
  const [bubbleEnabled, setBubbleEnabled] = useState<boolean>(true);
  const [bubbleOpacity, setBubbleOpacity] = useState<number>(85);
  const [bubbleSize, setBubbleSize] = useState<'small' | 'medium' | 'large'>('medium');
  const [pollingRate, setPollingRate] = useState<number>(1000);

  // Sync dark class with document element
  useEffect(() => {
    if (isDarkMode) {
      document.documentElement.classList.add('dark');
      document.documentElement.classList.remove('light');
    } else {
      document.documentElement.classList.remove('dark');
      document.documentElement.classList.add('light');
    }
  }, [isDarkMode]);

  const showToast = (message: string) => {
    if (toastTimeout) clearTimeout(toastTimeout);
    setToastMessage(message);
    const id = window.setTimeout(() => {
      setToastMessage(null);
    }, 3000);
    setToastTimeout(id);
  };

  const handleToggleTheme = () => {
    setIsDarkMode((prev) => {
      const next = !prev;
      showToast(next ? 'Modo Oscuro Activado' : 'Modo Claro Activado');
      return next;
    });
  };

  const handleToggleRecord = () => {
    setIsRecording((prev) => {
      const next = !prev;
      showToast(next ? 'Grabación remota iniciada (1080p 60FPS)' : 'Grabación detenida y guardada');
      return next;
    });
  };

  const handleToggleStEngine = () => {
    setStEngineActive((prev) => {
      const next = !prev;
      showToast(next ? 'STEngine activado y sincronizado' : 'STEngine desactivado');
      return next;
    });
  };

  return (
    <div className="relative min-h-screen bg-background text-on-surface font-body flex flex-col justify-between selection:bg-primary/20 selection:text-primary">
      {/* App Container */}
      <div className="w-full flex-1 flex flex-col mx-auto bg-surface-container-lowest border-x border-outline-variant min-h-screen relative">
        {/* Dynamic Top Header */}
        <Header
          currentScreen={currentScreen}
          onNavigate={(screen) => setCurrentScreen(screen)}
          isDarkMode={isDarkMode}
          onToggleTheme={handleToggleTheme}
          onOpenSettings={() => setIsSettingsOpen(true)}
        />

        {/* Screen Routing */}
        <main className="flex-1 flex flex-col">
          {currentScreen === 'inicio' && (
            <InicioScreen
              onNavigate={(screen) => setCurrentScreen(screen)}
              isDarkMode={isDarkMode}
              stEngineActive={stEngineActive}
              onToggleStEngine={handleToggleStEngine}
            />
          )}

          {currentScreen === 'conectar' && (
            <ConectarScreen
              onNavigate={(screen) => setCurrentScreen(screen)}
              onShowToast={showToast}
            />
          )}

          {currentScreen === 'engines' && (
            <EnginesScreen
              onNavigate={(screen) => setCurrentScreen(screen)}
              onShowToast={showToast}
            />
          )}

          {currentScreen === 'archivos' && (
            <ArchivosMultimediaScreen
              initialTab="archivos"
              onNavigate={(screen) => setCurrentScreen(screen)}
              onShowToast={showToast}
              isRecording={isRecording}
              onToggleRecord={handleToggleRecord}
              bubbleEnabled={bubbleEnabled}
              onToggleBubble={setBubbleEnabled}
              bubbleOpacity={bubbleOpacity}
              onChangeBubbleOpacity={setBubbleOpacity}
              bubbleSize={bubbleSize}
              onChangeBubbleSize={setBubbleSize}
            />
          )}

          {currentScreen === 'multimedia' && (
            <ArchivosMultimediaScreen
              initialTab="multimedia"
              onNavigate={(screen) => setCurrentScreen(screen)}
              onShowToast={showToast}
              isRecording={isRecording}
              onToggleRecord={handleToggleRecord}
              bubbleEnabled={bubbleEnabled}
              onToggleBubble={setBubbleEnabled}
              bubbleOpacity={bubbleOpacity}
              onChangeBubbleOpacity={setBubbleOpacity}
              bubbleSize={bubbleSize}
              onChangeBubbleSize={setBubbleSize}
            />
          )}

          {currentScreen === 'live_test' && (
            <LiveTestScreen
              onNavigate={(screen) => setCurrentScreen(screen)}
              onShowToast={showToast}
            />
          )}

          {currentScreen === 'hardware' && (
            <HardwareKernelScreen
              onNavigate={(screen) => setCurrentScreen(screen)}
              onShowToast={showToast}
            />
          )}
        </main>

        {/* Floating Quick Overlay Bubble */}
        <FloatingBubble
          enabled={bubbleEnabled && currentScreen !== 'live_test'}
          opacity={bubbleOpacity}
          size={bubbleSize}
          isRecording={isRecording}
          onToggleRecord={handleToggleRecord}
          onNavigate={(screen) => setCurrentScreen(screen)}
        />

        {/* Persistent Bottom Navigation */}
        <BottomNav
          currentScreen={currentScreen}
          onNavigate={(screen) => setCurrentScreen(screen)}
        />
      </div>

      {/* Settings Modal */}
      <SettingsModal
        isOpen={isSettingsOpen}
        onClose={() => setIsSettingsOpen(false)}
        isDarkMode={isDarkMode}
        onToggleTheme={handleToggleTheme}
        pollingRate={pollingRate}
        onChangePollingRate={setPollingRate}
        bubbleEnabled={bubbleEnabled}
        onToggleBubble={setBubbleEnabled}
        onNavigate={(screen) => setCurrentScreen(screen)}
        onShowToast={showToast}
      />

      {/* Floating System Toast Notification */}
      {toastMessage && (
        <div role="status" aria-live="polite" className="fixed top-16 left-1/2 -translate-x-1/2 z-50 px-4 py-2 bg-surface-container-lowest text-on-surface border border-primary font-headline font-semibold text-xs tracking-wide flex items-center gap-2 animate-in fade-in slide-in-from-top-2 duration-150 max-w-sm text-center">
          <span className="material-symbols-outlined text-primary text-[18px]">verified</span>
          <span>{toastMessage}</span>
        </div>
      )}
    </div>
  );
}
