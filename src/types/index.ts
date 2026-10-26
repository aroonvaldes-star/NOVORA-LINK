export type ScreenType = 'inicio' | 'conectar' | 'engines' | 'archivos' | 'multimedia' | 'live_test' | 'hardware';

export interface EngineStatus {
  name: string;
  tag: string;
  subsystem: string;
  status: 'Activo' | 'Detectado' | 'Standby' | 'No detectado' | 'En línea' | 'Sincronizado';
  details: string;
  latency?: string;
  color?: string;
}

export interface FileItem {
  id: string;
  name: string;
  size: string;
  time: string;
  status: 'Completado' | 'Transfiriendo' | 'Pausado' | 'Cancelado';
  type: 'movie' | 'code' | 'log' | 'archive' | 'image';
  progress?: number;
  speed?: string;
  totalBytes?: number;
  transferredBytes?: number;
}

export interface GamepadState {
  connected: boolean;
  id: string;
  stickL: { x: number; y: number };
  stickR: { x: number; y: number };
  lt: number; // 0 to 1
  rt: number; // 0 to 1
  lb: boolean;
  rb: boolean;
  buttons: {
    a: boolean;
    b: boolean;
    x: boolean;
    y: boolean;
    view: boolean;
    menu: boolean;
    home: boolean;
  };
  dpad: {
    up: boolean;
    down: boolean;
    left: boolean;
    right: boolean;
  };
  lastPressed: string;
  latencyMs: number;
}
