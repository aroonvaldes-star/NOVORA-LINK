from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
ve = root / 'src' / 'NOVORA' / 'VisionEngine'
required = [
    'Recovery/StatesRecoveryVE.cs', 'Recovery/ScopeRecoveryVE.cs',
    'Recovery/PolicyRecoveryVE.cs', 'Recovery/HealthRecoveryVE.cs',
    'Recovery/ResultRecoveryVE.cs', 'Recovery/MonitorRecoveryVE.cs',
    'Recovery/ManagerRecoveryVE.cs',
    'Metrics/VideoMetricsVE.cs', 'Metrics/AudioMetricsVE.cs',
    'Metrics/ControlMetricsVE.cs', 'Metrics/TransportMetricsVE.cs',
    'Metrics/SnapshotMetricsVE.cs', 'Metrics/CollectorMetricsVE.cs',
    'Performance/PriorityPerformanceVE.cs',
    'Performance/BufferPerformanceVE.cs', 'Performance/QueuePerformanceVE.cs',
    'Performance/BackpressurePerformanceVE.cs',
    'Performance/BitratePerformanceVE.cs',
    'Performance/CongestionPerformanceVE.cs',
    'Performance/SnapshotPerformanceVE.cs',
    'Performance/ManagerPerformanceVE.cs',
    'Stress/ResultStressVE.cs', 'Stress/SessionStressVE.cs',
    'Stress/TransportStressVE.cs', 'Stress/DecoderStressVE.cs',
    'Stress/RecoveryStressVE.cs', 'Stress/ManagerStressVE.cs',
]
errors = []
for rel in required:
    path = ve / rel
    if not path.is_file():
        errors.append(f'missing: {rel}')
        continue
    text = path.read_text(encoding='utf-8')
    stem = path.stem
    if not re.search(rf'\b(class|record|enum|struct)\s+{re.escape(stem)}(?:\b|<)', text):
        errors.append(f'principal type mismatch: {rel}')
    if 'TODO' in text or 'TBD' in text:
        errors.append(f'placeholder: {rel}')

runtime = (ve / 'Core' / 'RuntimeCoreVE.cs').read_text(encoding='utf-8')
if 'TransportSessionVE => _transportSessionVE' not in runtime:
    errors.append('RuntimeCoreVE does not expose TransportSessionVE')

for path in [ve / 'Recovery', ve / 'Metrics', ve / 'Performance', ve / 'Stress']:
    for cs in path.rglob('*.cs'):
        text = cs.read_text(encoding='utf-8')
        for token in ('RendererVideoVE', 'Direct3D', 'D3D11', 'WriteableBitmap', 'BitmapSource'):
            if token in text:
                errors.append(f'visual token {token}: {cs.relative_to(ve)}')

if errors:
    print('BLOCK C STATIC CONTRACT: FAIL')
    for error in errors:
        print(' -', error)
    sys.exit(1)

print('BLOCK C STATIC CONTRACT: PASS')
print(f' Block C production files checked: {len(required)}')
print(' Renderer implementation: NONE')
print(' Recovery/Metrics/Performance/Stress: PRESENT')
