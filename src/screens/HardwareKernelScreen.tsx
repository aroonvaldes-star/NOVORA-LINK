import React, { useState, useEffect } from 'react';
import { ScreenType } from '../types/index.ts';

interface HardwareKernelScreenProps {
  onNavigate: (screen: ScreenType) => void;
  onShowToast: (msg: string) => void;
}

export const HardwareKernelScreen: React.FC<HardwareKernelScreenProps> = ({ onShowToast }) => {
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [temp, setTemp] = useState(34.2);
  const [cpuPerfLoad, setCpuPerfLoad] = useState(28.4);
  const [cpuEffLoad, setCpuEffLoad] = useState(14.1);

  // Live Logs state
  const [logs, setLogs] = useState<string[]>([
    '14:02:18.401 [SYS_MEM] Ring buffer allocated @ 0x7fa2bc000 (size: 64MB)',
    '14:02:18.409 [DMA] Zero-copy handle locked for ep_in=0x81',
    '14:02:18.412 [USB_CORE] SuperSpeedPlus bus established: negotiating 10Gbps phy',
    '14:02:18.415 [ENGINE] novora_vision_codec registered direct hardware hook',
    '14:02:18.420 [DAEMON] Handshake verified. Telemetry loop polling at 1000Hz',
    '14:02:22.115 [ST_SYNC] Frame timing locked jitter <0.04ms over USB bulk transport',
  ]);

  // Periodic log append
  useEffect(() => {
    const timer = setInterval(() => {
      const now = new Date();
      const timeStr = `${now.getHours().toString().padStart(2, '0')}:${now
        .getMinutes()
        .toString()
        .padStart(2, '0')}:${now.getSeconds().toString().padStart(2, '0')}.${now
        .getMilliseconds()
        .toString()
        .padStart(3, '0')}`;

      const sampleEvents = [
        `[PIPE] Zero-copy DMA packet burst ep_in=0x81 (size: 16384B)`,
        `[HID_POLL] ExInEngine raw sample polled: lat=0.9ms, drift=0.01%`,
        `[CODEC] Frame buffer encoded at 60.01 FPS (bitrate: 25.1 Mbps)`,
        `[IPC] novora_linkd ping ACK: latency 1.18ms RTT`,
      ];

      const chosen = sampleEvents[Math.floor(Math.random() * sampleEvents.length)];
      setLogs((prev) => [...prev.slice(-14), `${timeStr} ${chosen}`]);

      // Gentle load fluctuate
      setCpuPerfLoad((p) => Number((25 + Math.random() * 8).toFixed(1)));
      setCpuEffLoad((e) => Number((12 + Math.random() * 4).toFixed(1)));
    }, 3500);

    return () => clearInterval(timer);
  }, []);

  const handleRefresh = () => {
    setIsRefreshing(true);
    setTimeout(() => {
      setIsRefreshing(false);
      setTemp(34.1 + Math.random() * 0.4);
      onShowToast('Diagnóstico de Kernel y Hardware completado • Todo nominal');
    }, 600);
  };

  return (
    <div className="w-full max-w-2xl mx-auto flex-1 flex flex-col px-4 pt-3 pb-28 space-y-4">
      {/* Sub-status Bar for Mobile View */}
      <div className="flex items-center justify-between px-3 py-1.5 bg-surface-container-low border border-outline-variant">
        <div className="flex items-center gap-1.5">
          <span className="w-2 h-2 rounded-full bg-success animate-pulse"></span>
          <span className="font-label text-xs text-success font-bold">SYS_STAT: NOMINAL</span>
        </div>
        <div className="flex items-center gap-2">
          <span className="font-label text-xs text-on-surface-variant">NOVORA-LINK v1.4.19</span>
          <button
            onClick={handleRefresh}
            aria-label="Refrescar métricas"
            className="text-on-surface hover:text-on-surface-variant p-1 transition-colors cursor-pointer"
            title="Refrescar métricas"
          >
            <span className={`material-symbols-outlined text-[16px] ${isRefreshing ? 'animate-spin' : ''}`}>
              refresh
            </span>
          </button>
        </div>
      </div>

      {/* 1. Estado de Memoria y Kernel (RAM & Buffer DMA) */}
      <section className="bg-surface-container-low border border-outline-variant p-4 space-y-3">
        <div className="flex items-center justify-between border-b border-outline-variant pb-2">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-on-surface text-[18px]">developer_board</span>
            <h2 className="font-headline font-bold text-sm text-on-surface">Memoria Física &amp; Kernel DMA</h2>
          </div>
          <span className="font-label text-[10px] px-2 py-0.5 bg-surface-container-high text-on-surface border border-outline-variant font-mono font-bold">
            LPDDR5X @ 8533MT/s
          </span>
        </div>

        {/* Segmented Memory Allocation Bar */}
        <div className="space-y-1.5">
          <div className="flex justify-between items-baseline font-label text-xs">
            <span className="text-on-surface-variant">Asignación Total del Sistema</span>
            <span className="text-on-surface font-mono font-semibold">
              5.82 GB / 12.00 GB <span className="font-bold">(48.5%)</span>
            </span>
          </div>

          <div className="w-full h-2.5 bg-surface-container-highest overflow-hidden flex">
            <div className="h-full bg-on-surface" style={{ width: '4.2%' }} title="DMA Video Buffer"></div>
            <div className="h-full bg-secondary" style={{ width: '1.5%' }} title="IPC Queue"></div>
            <div className="h-full bg-outline" style={{ width: '8.5%' }} title="App Heap"></div>
            <div className="h-full bg-outline-variant" style={{ width: '34.3%' }} title="OS & Background"></div>
          </div>
        </div>

        {/* Micro Metrics Grid */}
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 pt-1 font-label text-xs">
          <div className="p-2.5 bg-surface-container border border-outline-variant flex flex-col">
            <span className="text-on-surface-variant text-[10px]">Buffer DMA Vídeo</span>
            <span className="font-mono text-on-surface font-bold text-xs mt-0.5">42.0 MB</span>
            <span className="text-[10px] text-on-surface-variant">Zero-copy locked</span>
          </div>

          <div className="p-2.5 bg-surface-container border border-outline-variant flex flex-col">
            <span className="text-on-surface-variant text-[10px]">IPC Packet Queue</span>
            <span className="font-mono text-secondary font-bold text-xs mt-0.5">8.4 MB</span>
            <span className="text-[10px] text-on-surface-variant">Sync Mutex OK</span>
          </div>

          <div className="p-2.5 bg-surface-container border border-outline-variant flex flex-col">
            <span className="text-on-surface-variant text-[10px]">Heap Daemon App</span>
            <span className="font-mono text-on-surface font-bold text-xs mt-0.5">116.2 MB</span>
            <span className="text-[10px] text-on-surface-variant">Max ceiling 512MB</span>
          </div>

          <div className="p-2.5 bg-surface-container border border-outline-variant flex flex-col">
            <span className="text-on-surface-variant text-[10px]">Ring Buffer Drop</span>
            <span className="font-mono text-success font-bold text-xs mt-0.5">0.00%</span>
            <span className="text-[10px] text-success">0 frames loss</span>
          </div>
        </div>
      </section>

      {/* 2. Telemetría de CPU & Arquitectura SoC */}
      <section className="bg-surface-container-low border border-outline-variant p-4 space-y-3">
        <div className="flex items-center justify-between border-b border-outline-variant pb-2">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-on-surface text-[18px]">speed</span>
            <h2 className="font-headline font-bold text-sm text-on-surface">Topología SoC &amp; Telemetría Térmica</h2>
          </div>
          <div className="flex items-center gap-1.5 font-label text-xs bg-surface-container px-2 py-0.5 border border-outline-variant text-on-surface">
            <span className="material-symbols-outlined text-[14px] text-warning">device_thermostat</span>
            <span className="font-mono">{temp.toFixed(1)} °C</span>
            <span className="text-success text-[10px] uppercase font-bold">Óptimo</span>
          </div>
        </div>

        {/* CPU Cluster Gauges */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {/* Performance Cluster */}
          <div className="p-3 bg-surface-container border border-outline-variant space-y-2">
            <div className="flex justify-between items-center text-xs font-label">
              <span className="text-on-surface font-semibold">Cluster Rendimiento (Cortex-X4)</span>
              <span className="font-mono text-on-surface font-bold">3.19 GHz</span>
            </div>
            <div className="w-full bg-surface-container-highest h-2 overflow-hidden">
              <div
                className="bg-on-surface h-full transition-all duration-300"
                style={{ width: `${cpuPerfLoad}%` }}
              ></div>
            </div>
            <div className="flex justify-between font-label text-[11px] text-on-surface-variant">
              <span>Cores [0-3] Load</span>
              <span className="font-mono text-on-surface">{cpuPerfLoad}% Activo</span>
            </div>
          </div>

          {/* Efficiency Cluster */}
          <div className="p-3 bg-surface-container border border-outline-variant space-y-2">
            <div className="flex justify-between items-center text-xs font-label">
              <span className="text-on-surface font-semibold">Cluster Eficiencia (Cortex-A720)</span>
              <span className="font-mono text-on-surface-variant font-bold">2.00 GHz</span>
            </div>
            <div className="w-full bg-surface-container-highest h-2 overflow-hidden">
              <div
                className="bg-secondary h-full transition-all duration-300"
                style={{ width: `${cpuEffLoad}%` }}
              ></div>
            </div>
            <div className="flex justify-between font-label text-[11px] text-on-surface-variant">
              <span>Cores [4-7] Load</span>
              <span className="font-mono text-on-surface">{cpuEffLoad}% Activo</span>
            </div>
          </div>
        </div>

        {/* Thermal and Governor state */}
        <div className="flex items-center justify-between text-on-surface-variant font-label text-xs pt-1">
          <div className="flex items-center gap-1.5">
            <span className="material-symbols-outlined text-[16px]">tune</span>
            <span>
              Governor: <code className="text-on-surface font-mono">schedutil</code>
            </span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="material-symbols-outlined text-[16px]">battery_charging_full</span>
            <span>
              Batería: <code className="text-on-surface font-mono">31.8 °C (4200mV)</code>
            </span>
          </div>
        </div>
      </section>

      {/* 3. Subprocesos Engine Activos (Table) */}
      <section className="bg-surface-container-low border border-outline-variant p-4 space-y-3">
        <div className="flex items-center justify-between border-b border-outline-variant pb-2">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-on-surface text-[18px]">terminal</span>
            <h2 className="font-headline font-bold text-sm text-on-surface">Subprocesos Engine Activos</h2>
          </div>
          <span className="font-label text-xs text-on-surface-variant font-mono">4 HILOS KERNEL</span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left font-label text-xs">
            <thead>
              <tr className="border-b border-outline-variant text-on-surface-variant font-mono uppercase text-[10px]">
                <th scope="col" className="py-1.5 px-2">Proceso / Engine</th>
                <th scope="col" className="py-1.5 px-2">PID</th>
                <th scope="col" className="py-1.5 px-2">Asignación Bus</th>
                <th scope="col" className="py-1.5 px-2 text-right">CPU</th>
                <th scope="col" className="py-1.5 px-2 text-right">Tick Rate</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline-variant font-mono">
              <tr className="hover:bg-surface-container transition-colors">
                <td className="py-2 px-2 text-on-surface font-semibold flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 bg-on-surface"></span>
                  novora_linkd
                </td>
                <td className="py-2 px-2 text-on-surface-variant">1842</td>
                <td className="py-2 px-2 text-on-surface">Bus USB DMA Async</td>
                <td className="py-2 px-2 text-right text-on-surface font-semibold">1.2%</td>
                <td className="py-2 px-2 text-right text-on-surface">0.4ms</td>
              </tr>
              <tr className="hover:bg-surface-container transition-colors">
                <td className="py-2 px-2 text-on-surface font-semibold flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 bg-on-surface"></span>
                  novora_vision_codec
                </td>
                <td className="py-2 px-2 text-on-surface-variant">1845</td>
                <td className="py-2 px-2 text-on-surface">MediaCodec H265 HW</td>
                <td className="py-2 px-2 text-right text-on-surface font-semibold">4.1%</td>
                <td className="py-2 px-2 text-right text-on-surface-variant">1.0ms</td>
              </tr>
              <tr className="hover:bg-surface-container transition-colors">
                <td className="py-2 px-2 text-on-surface font-semibold flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 bg-on-surface"></span>
                  novora_exin_hid
                </td>
                <td className="py-2 px-2 text-on-surface-variant">1849</td>
                <td className="py-2 px-2 text-on-surface">DirectInput RAW Poller</td>
                <td className="py-2 px-2 text-right text-on-surface font-semibold">0.8%</td>
                <td className="py-2 px-2 text-right text-on-surface">1000Hz</td>
              </tr>
              <tr className="hover:bg-surface-container transition-colors">
                <td className="py-2 px-2 text-on-surface font-semibold flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 bg-on-surface"></span>
                  novora_st_pipe
                </td>
                <td className="py-2 px-2 text-on-surface-variant">1853</td>
                <td className="py-2 px-2 text-on-surface">IPC Shared Pipe Daemon</td>
                <td className="py-2 px-2 text-right text-on-surface font-semibold">0.2%</td>
                <td className="py-2 px-2 text-right text-on-surface-variant">5.0ms</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      {/* 4. Canal de E/S & Bus de Datos */}
      <section className="bg-surface-container-low border border-outline-variant p-4 space-y-3">
        <div className="flex items-center justify-between border-b border-outline-variant pb-2">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-on-surface text-[18px]">cable</span>
            <h2 className="font-headline font-bold text-sm text-on-surface">Canal de E/S &amp; Bus de Datos</h2>
          </div>
          <span className="font-label text-xs text-on-surface font-mono font-bold">USB 3.2 GEN 2</span>
        </div>

        {/* USB Endpoint Specification Grid */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 font-label text-xs">
          <div className="p-2.5 bg-surface-container border border-outline-variant">
            <div className="text-on-surface-variant text-[10px] mb-1">Endpoints Asignados</div>
            <div className="font-mono text-on-surface font-semibold">
              IN: <span className="text-on-surface font-bold">0x81</span> | OUT:{' '}
              <span className="text-on-surface font-bold">0x02</span>
            </div>
            <div className="text-[10px] text-on-surface-variant mt-0.5">Bulk Transfer Stream</div>
          </div>

          <div className="p-2.5 bg-surface-container border border-outline-variant">
            <div className="text-on-surface-variant text-[10px] mb-1">Rendimiento Físico Bus</div>
            <div className="font-mono text-on-surface font-bold text-sm">4.8 Gbps</div>
            <div className="text-[10px] text-on-surface-variant mt-0.5">Latencia de enlace: 0.12ms</div>
          </div>

          <div className="p-2.5 bg-surface-container border border-outline-variant">
            <div className="text-on-surface-variant text-[10px] mb-1">Control de Paquetes</div>
            <div className="font-mono text-on-surface font-semibold">MTU: 16384 B</div>
            <div className="text-[10px] text-on-surface font-semibold mt-0.5">Zero-Copy Direct Pipe</div>
          </div>
        </div>

        {/* Live Kernel / Daemon Debug Stream */}
        <div className="space-y-1.5 pt-1">
          <div className="flex justify-between items-center font-label text-xs text-on-surface-variant">
            <span className="flex items-center gap-1 font-mono uppercase">
              <span className="w-1.5 h-1.5 bg-on-surface"></span>
              Logs del Kernel novora_linkd
            </span>
            <span className="font-mono text-[10px]">TICK: REALTIME</span>
          </div>

          <div
            role="log"
            aria-live="off"
            className="p-3 bg-surface-container-lowest border border-outline-variant font-mono text-[11px] space-y-1 overflow-x-auto max-h-40"
          >
            {logs.map((line, idx) => (
              <div key={idx} className="flex gap-2 text-on-surface">
                <span className="text-on-surface-variant shrink-0">{line.slice(0, 12)}</span>
                <span className="text-on-surface font-semibold">
                  {line.includes('[SYS_MEM]')
                    ? '[SYS_MEM]'
                    : line.includes('[DMA]')
                    ? '[DMA]'
                    : line.includes('[USB_CORE]')
                    ? '[USB_CORE]'
                    : line.includes('[ENGINE]')
                    ? '[ENGINE]'
                    : line.includes('[PIPE]')
                    ? '[PIPE]'
                    : line.includes('[HID_POLL]')
                    ? '[HID_POLL]'
                    : line.includes('[CODEC]')
                    ? '[CODEC]'
                    : line.includes('[ST_SYNC]')
                    ? '[ST_SYNC]'
                    : '[LOG]'}
                </span>
                <span className="truncate">{line.replace(/^\d+:\d+:\d+\.\d+\s+\[.*?\]\s*/, '')}</span>
              </div>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
};
