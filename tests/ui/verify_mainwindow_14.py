from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
xaml = (root / 'src/NOVORA/MainWindow.xaml').read_text(encoding='utf-8-sig')
ve = (root / 'src/NOVORA/MainWindow.VisionEngineVE.cs').read_text(encoding='utf-8-sig')
main = (root / 'src/NOVORA/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')

checks = {
    'no brand image/title in titlebar': 'NovoraBrandImageSource' not in xaml and '<Image' not in xaml,
    'matte novora blue palette': '#00AEEF' in xaml and '#ECEFF2' in xaml,
    'no shadows': 'DropShadowEffect' not in xaml and 'Effect=' not in xaml,
    'device selector': 'x:Name="DeviceComboBox14"' in xaml and 'ItemsSource="{Binding Devices}"' in xaml,
    'monitor selector only': 'x:Name="MonitorComboBox"' in xaml and 'Text="MONITOR PRINCIPAL"' not in xaml,
    'red/video/performance headers': all(s in xaml for s in ['Text="RED"', 'Text="VIDEO"', 'Text="PERFORMANCE"']),
    'video settings controls': all(s in xaml for s in ['BitrateOptions', 'FpsOptions', 'ResolutionOptions']),
    'renderer container': 'x:Name="VideoRendererContainer"' in xaml and 'x:Name="VideoSettingsPanel"' in xaml,
    'battery one value': 'x:Name="BatteryPercentText14"' in xaml and 'x:Name="BatteryProgress14"' in xaml,
    'performance individual metrics': all(s in xaml for s in ['CpuValueText14', 'RamValueText14', 'TemperatureValueText14']),
    'linkengine fixed label': 'Text="LinkEngine"' in xaml,
    'no footer': 'NOVORA-LINK  v' not in xaml and 'Listo para crear' not in xaml,
    'renderer created dynamically': 'EnsureVisionRendererHostVE' in ve and 'VideoRendererContainer.Children.Add' in ve,
    'performance surface wired': 'ApplyPerformanceSurface14(metrics)' in main,
}

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ' - ' + name)

if failed:
    print(f'\n{len(failed)} contract checks failed.')
    sys.exit(1)
print('\nMAINWINDOW 1.4 CONTRACT PASS')
