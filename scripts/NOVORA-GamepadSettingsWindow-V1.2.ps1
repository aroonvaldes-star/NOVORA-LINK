#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RepoPath = "C:\Users\Aroon\Desktop\NOVORA-LINK"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# =====================================================================
# NOVORA-LINK
# VISIONENGINE - GAMEPAD SETTINGS WINDOW V1.2
#
# Compatible con:
#   - Windows PowerShell 5.1
#   - PowerShell 7+
#
# IMPLEMENTA:
#   - Ventana independiente GamepadSettingsWindow.
#   - Acceso desde SettingsWindow mediante un solo botón.
#   - Activar/desactivar GamepadVE.
#   - Selección de mando físico preferido por nombre SDL.
#   - Selección "Automático" para conservar el comportamiento actual.
#   - Diagnóstico SDL3 bajo demanda (SIN polling nuevo).
#   - Medición local del coste de lectura SDL (NO es latencia Android).
#   - Persistencia en el settings.json actual de NOVORA.
#   - ManagerGamepadVE respeta Enabled + mando preferido al iniciar.
#   - Pruebas RED -> GREEN específicas de esta función.
#   - Preflight Rebuild para descartar obj/WPF incremental obsoleto.
#   - Limpieza de obj antes de GREEN y después de rollback.
#   - Backup + rollback automático.
#   - Protección SHA-256 de LinkEngine y Android LinkEngine.
#
# NO IMPLEMENTA TODAVÍA:
#   - Keyboard + Mouse -> Gamepad (siguiente bloque).
#   - Rumble Android -> mando físico.
#   - LED DualSense/DualShock.
#   - Touchpad/Gyro/Acelerómetro.
#   - Adaptive Triggers.
#   - Eliminación del polling histórico de ManagerGamepadVE.
#
# IMPORTANTE:
#   Este V1.2 NO agrega timers ni polling al menú. La detección/diagnóstico
#   ocurre al abrir la ventana o cuando el usuario pulsa "Actualizar" o
#   "Probar SDL".
# =====================================================================

# =====================================================================
# RUTAS
# =====================================================================

$RepoPath = [System.IO.Path]::GetFullPath($RepoPath)

$SolutionPath = Join-Path $RepoPath "NOVORA.sln"
$ProjectPath = Join-Path $RepoPath "src\NOVORA\NOVORA.csproj"
$TestsProject = Join-Path $RepoPath "tests\NOVORA.Tests\NOVORA.Tests.csproj"

$SettingsWindowXaml = Join-Path $RepoPath "src\NOVORA\SettingsWindow.xaml"
$SettingsWindowCode = Join-Path $RepoPath "src\NOVORA\SettingsWindow.xaml.cs"
$SettingsServicePath = Join-Path $RepoPath "src\NOVORA\Services\SettingsService.cs"

$GamepadRoot = Join-Path $RepoPath "src\NOVORA\VisionEngine\Gamepad"
$ManagerGamepadPath = Join-Path $GamepadRoot "ManagerGamepadVE.cs"
$SdlGamepadPath = Join-Path $GamepadRoot "SdlGamepadVE.cs"
$ConfigGamepadPath = Join-Path $GamepadRoot "ConfigGamepadVE.cs"

$GamepadWindowXaml = Join-Path $RepoPath "src\NOVORA\GamepadSettingsWindow.xaml"
$GamepadWindowCode = Join-Path $RepoPath "src\NOVORA\GamepadSettingsWindow.xaml.cs"

$GamepadTestsPath = Join-Path $RepoPath "tests\NOVORA.Tests\GamepadSettingsTests.cs"

$LinkEngineRoot = Join-Path $RepoPath "src\NOVORA\LinkEngine"
$AndroidLinkEngineRoot = Join-Path $RepoPath "NOVORA.linkEngine.Android\LinkEngine"

$NovoraObjRoot = Join-Path $RepoPath "src\NOVORA\obj"
$TestsObjRoot = Join-Path $RepoPath "tests\NOVORA.Tests\obj"

# =====================================================================
# HELPERS
# =====================================================================

function Write-Section
{
    param(
        [Parameter(Mandatory)]
        [string]$Text
    )

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Assert-File
{
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        throw "No existe el archivo requerido:`n$Path"
    }
}

function Assert-Directory
{
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container))
    {
        throw "No existe el directorio requerido:`n$Path"
    }
}

function Read-Text
{
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
}

function Write-TextUtf8NoBom
{
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content
    )

    $directory = Split-Path -Parent $Path

    if (-not (Test-Path -LiteralPath $directory -PathType Container))
    {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content.Replace("`n", "`r`n"), $encoding)
}

function Replace-ExactlyOnce
{
    param(
        [Parameter(Mandatory)]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Old,

        [Parameter(Mandatory)]
        [string]$New,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $first = $Content.IndexOf($Old, [StringComparison]::Ordinal)

    if ($first -lt 0)
    {
        throw "No encontré el bloque esperado: $Description"
    }

    $second = $Content.IndexOf(
        $Old,
        $first + $Old.Length,
        [StringComparison]::Ordinal)

    if ($second -ge 0)
    {
        throw "El bloque aparece más de una vez: $Description"
    }

    return (
        $Content.Substring(0, $first) +
        $New +
        $Content.Substring($first + $Old.Length)
    )
}

function Backup-File
{
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$BackupRoot
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        return
    }

    $relative = $Path.Substring($RepoPath.Length).TrimStart("\")
    $destination = Join-Path $BackupRoot $relative
    $directory = Split-Path -Parent $destination

    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item -LiteralPath $Path -Destination $destination -Force
}

function Restore-Backup
{
    param(
        [Parameter(Mandatory)]
        [string]$BackupRoot,

        [Parameter(Mandatory)]
        [string[]]$NewFiles
    )

    Write-Host ""
    Write-Host "[ROLLBACK] Restaurando archivos..." -ForegroundColor Yellow

    foreach ($newFile in $NewFiles)
    {
        if (Test-Path -LiteralPath $newFile -PathType Leaf)
        {
            Remove-Item -LiteralPath $newFile -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path -LiteralPath $BackupRoot -PathType Container)
    {
        Get-ChildItem -LiteralPath $BackupRoot -File -Recurse | ForEach-Object {
            $relative = $_.FullName.Substring($BackupRoot.Length).TrimStart("\")
            $destination = Join-Path $RepoPath $relative
            $directory = Split-Path -Parent $destination

            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
    }

    Write-Host "[ROLLBACK] Completo." -ForegroundColor Yellow
}

function Get-TreeFingerprint
{
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container))
    {
        return "<MISSING>"
    }

    $rows = Get-ChildItem -LiteralPath $Path -File -Recurse -Force |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($Path.Length)
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$relative=$hash"
        }

    $payload = [string]::Join("`n", $rows)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $sha = [System.Security.Cryptography.SHA256]::Create()

    try
    {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "")
    }
    finally
    {
        $sha.Dispose()
    }
}

function Invoke-DotnetCapture
{
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & dotnet @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Remove-GeneratedArtifacts
{
    foreach ($path in @(
        $NovoraObjRoot,
        $TestsObjRoot
    ))
    {
        if (
            Test-Path `
                -LiteralPath $path `
                -PathType Container
        )
        {
            Remove-Item `
                -LiteralPath $path `
                -Recurse `
                -Force `
                -ErrorAction Stop
        }
    }
}

function Write-DiagnosticFile
{
    param(
        [Parameter(Mandatory)]
        [string]$Prefix,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content
    )

    $path =
        Join-Path `
            $env:USERPROFILE `
            (
                "Desktop\" +
                $Prefix +
                "-" +
                (Get-Date -Format "yyyyMMdd-HHmmss") +
                ".txt"
            )

    [System.IO.File]::WriteAllText(
        $path,
        $Content,
        (New-Object System.Text.UTF8Encoding($false)))

    return $path
}

# =====================================================================
# VALIDACIÓN BASE
# =====================================================================

Write-Section "VALIDANDO BASE NOVORA-LINK"

Assert-File $SolutionPath
Assert-File $ProjectPath
Assert-File $TestsProject
Assert-File $SettingsWindowXaml
Assert-File $SettingsWindowCode
Assert-File $SettingsServicePath
Assert-File $ManagerGamepadPath
Assert-File $SdlGamepadPath
Assert-Directory $GamepadRoot

if (
    (Test-Path -LiteralPath $ConfigGamepadPath -PathType Leaf) -or
    (Test-Path -LiteralPath $GamepadWindowXaml -PathType Leaf) -or
    (Test-Path -LiteralPath $GamepadWindowCode -PathType Leaf)
)
{
    throw @"
La ventana/configuración de GamepadVE ya parece existir.

No voy a aplicar el integrador dos veces.
Revisa primero estos archivos:

$ConfigGamepadPath
$GamepadWindowXaml
$GamepadWindowCode
"@
}

$LinkBefore = Get-TreeFingerprint $LinkEngineRoot
$AndroidLinkBefore = Get-TreeFingerprint $AndroidLinkEngineRoot

Write-Host "[OK] Base encontrada." -ForegroundColor Green
Write-Host "[OK] LinkEngine protegido por fingerprint." -ForegroundColor Green
Write-Host "[OK] Android LinkEngine protegido por fingerprint." -ForegroundColor Green

# =====================================================================
# BASELINE LIMPIO
# =====================================================================

Write-Section "BASELINE LIMPIO - REGENERANDO WPF/MSBUILD"

# El diagnóstico del 2026-09-07 confirmó que SettingsWindow.g.cs podía
# quedar obsoleto dentro de obj aunque SettingsWindow.xaml ya estuviera
# limpio. Por eso V1.2 nunca confía en obj heredado.
Remove-GeneratedArtifacts

Push-Location $RepoPath
try
{
    $Baseline = Invoke-DotnetCapture -Arguments @(
        "build",
        $SolutionPath,
        "-t:Rebuild",
        "-c",
        "Release",
        "--nologo",
        "-v:minimal"
    )
}
finally
{
    Pop-Location
}

Write-Host $Baseline.Output

if ($Baseline.ExitCode -ne 0)
{
    $BaselineFailurePath =
        Write-DiagnosticFile `
            -Prefix "NOVORA-GAMEPADSETTINGS-BASELINE-FAIL" `
            -Content $Baseline.Output

    throw @"
El baseline NO compila después de limpiar obj y ejecutar Rebuild.

No se ha modificado ninguna fuente de Gamepad Settings.

Diagnóstico:
$BaselineFailurePath
"@
}

Write-Host "[OK] Baseline limpio: Rebuild Release pasó." -ForegroundColor Green

# =====================================================================
# BACKUP
# =====================================================================

Write-Section "CREANDO BACKUP TRANSACCIONAL"

$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$BackupRoot = Join-Path $env:TEMP "NOVORA-GAMEPAD-SETTINGS-V1.2-$Timestamp"
New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

$ExistingFiles = @(
    $SettingsWindowXaml,
    $SettingsWindowCode,
    $SettingsServicePath,
    $ManagerGamepadPath
)

foreach ($file in $ExistingFiles)
{
    Backup-File -Path $file -BackupRoot $BackupRoot
}

if (Test-Path -LiteralPath $GamepadTestsPath -PathType Leaf)
{
    Backup-File -Path $GamepadTestsPath -BackupRoot $BackupRoot
}

$NewFiles = @(
    $ConfigGamepadPath,
    $GamepadWindowXaml,
    $GamepadWindowCode,
    $GamepadTestsPath
)

Write-Host "Backup: $BackupRoot" -ForegroundColor DarkGray

try
{
    # =================================================================
    # TDD - RED
    # =================================================================

    Write-Section "TDD RED - PRUEBA ANTES DE IMPLEMENTAR"

    $GamepadTests = @'
using NOVORA.Services;
using System.Reflection;
using Xunit;

namespace NOVORA.Tests;

public sealed class GamepadSettingsTests
{
    private static Assembly NovoraAssemblyVE =>
        typeof(NovoraSettings).Assembly;

    [Fact]
    public void ConfigGamepadVE_type_exists()
    {
        Type? type =
            NovoraAssemblyVE.GetType(
                "NOVORA.VisionEngine.Gamepad.ConfigGamepadVE");

        Assert.NotNull(type);
    }

    [Fact]
    public void ConfigGamepadVE_selection_rules_work()
    {
        Type? type =
            NovoraAssemblyVE.GetType(
                "NOVORA.VisionEngine.Gamepad.ConfigGamepadVE");

        Assert.NotNull(type);

        MethodInfo? method =
            type!.GetMethod(
                "MatchesSelectionVE",
                BindingFlags.Public |
                BindingFlags.Static);

        Assert.NotNull(method);

        object? autoResult =
            method!.Invoke(
                null,
                new object?[]
                {
                    "__auto__",
                    "DualSense Wireless Controller"
                });

        object? specificResult =
            method.Invoke(
                null,
                new object?[]
                {
                    "DualSense Wireless Controller",
                    "Xbox Wireless Controller"
                });

        Assert.True(
            Assert.IsType<bool>(
                autoResult));

        Assert.False(
            Assert.IsType<bool>(
                specificResult));
    }

    [Fact]
    public void NovoraSettings_has_safe_gamepad_defaults()
    {
        PropertyInfo? enabled =
            typeof(NovoraSettings).GetProperty(
                "GamepadEnabled");

        PropertyInfo? selected =
            typeof(NovoraSettings).GetProperty(
                "SelectedGamepadName");

        Assert.NotNull(enabled);
        Assert.NotNull(selected);

        NovoraSettings settings =
            new();

        Assert.True(
            Assert.IsType<bool>(
                enabled!.GetValue(
                    settings)));

        Assert.Equal(
            "__auto__",
            Assert.IsType<string>(
                selected!.GetValue(
                    settings)));
    }

    [Fact]
    public void SettingsWindow_exposes_gamepad_window_launcher()
    {
        Type? settingsWindow =
            NovoraAssemblyVE.GetType(
                "NOVORA.SettingsWindow");

        Assert.NotNull(
            settingsWindow);

        MethodInfo? method =
            settingsWindow!.GetMethod(
                "GamepadSettings_Click",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        Assert.NotNull(
            method);
    }

    [Fact]
    public void GamepadSettingsWindow_is_a_separate_WPF_window()
    {
        Type? windowType =
            NovoraAssemblyVE.GetType(
                "NOVORA.GamepadSettingsWindow");

        Assert.NotNull(
            windowType);

        Assert.Equal(
            "System.Windows.Window",
            windowType!
                .BaseType?
                .FullName);
    }
}
'@

    Write-TextUtf8NoBom -Path $GamepadTestsPath -Content $GamepadTests

    Push-Location $RepoPath
    try
    {
        $Red = Invoke-DotnetCapture -Arguments @(
            "test",
            $TestsProject,
            "-c",
            "Release",
            "--filter",
            "FullyQualifiedName~GamepadSettingsTests",
            "--logger",
            "console;verbosity=normal",
            "--nologo"
        )
    }
    finally
    {
        Pop-Location
    }

    if ($Red.ExitCode -eq 0)
    {
        throw "TDD RED inválido: la prueba pasó antes de implementar la función."
    }

    if (
        $Red.Output -match "error CS\d+" -or
        $Red.Output -match "error MC\d+" -or
        $Red.Output -match "error NETSDK\d+"
    )
    {
        $RedFailurePath =
            Write-DiagnosticFile `
                -Prefix "NOVORA-GAMEPADSETTINGS-RED-INFRA-FAIL" `
                -Content $Red.Output

        throw @"
TDD RED no llegó a una aserción: hubo error de compilación/infraestructura.

Diagnóstico:
$RedFailurePath

Salida:
$($Red.Output)
"@
    }

    if ($Red.Output -notmatch "GamepadSettingsTests")
    {
        $RedFailurePath =
            Write-DiagnosticFile `
                -Prefix "NOVORA-GAMEPADSETTINGS-RED-UNKNOWN-FAIL" `
                -Content $Red.Output

        throw @"
TDD RED falló, pero la salida no demuestra que GamepadSettingsTests
haya sido quien falló.

Diagnóstico:
$RedFailurePath
"@
    }

    Write-Host "[OK] RED confirmado por aserción: la función todavía no existe." -ForegroundColor Green

    # =================================================================
    # 1. CONFIG GAMEPAD VE
    # =================================================================

    Write-Section "1/7 - CONFIG GAMEPAD VE"

    $ConfigGamepad = @'
namespace NOVORA.VisionEngine.Gamepad;

/// <summary>
/// Configuración persistente mínima de GamepadVE.
///
/// V1.2 sólo gobierna:
/// - habilitar/deshabilitar GamepadVE;
/// - seleccionar el mando físico preferido.
///
/// No almacena seriales, cuentas ni información personal.
/// </summary>
public sealed record ConfigGamepadVE
{
    public const string AutoSelectionVE = "__auto__";

    public bool EnabledVE { get; init; } = true;

    public string SelectedGamepadNameVE { get; init; } =
        AutoSelectionVE;

    public ConfigGamepadVE NormalizeVE()
        => this with
        {
            SelectedGamepadNameVE =
                NormalizeSelectedNameVE(
                    SelectedGamepadNameVE)
        };

    public bool MatchesPhysicalNameVE(
        string physicalName)
        => MatchesSelectionVE(
            SelectedGamepadNameVE,
            physicalName);

    public static string NormalizeSelectedNameVE(
        string? value)
        => string.IsNullOrWhiteSpace(value)
            ? AutoSelectionVE
            : value.Trim();

    public static bool MatchesSelectionVE(
        string? selectedName,
        string physicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            physicalName);

        string selected =
            NormalizeSelectedNameVE(
                selectedName);

        if (string.Equals(
                selected,
                AutoSelectionVE,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            selected,
            physicalName.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
'@

    Write-TextUtf8NoBom -Path $ConfigGamepadPath -Content $ConfigGamepad
    Write-Host "[OK] ConfigGamepadVE.cs" -ForegroundColor Green

    # =================================================================
    # 2. SETTINGS SERVICE
    # =================================================================

    Write-Section "2/7 - SETTINGS SERVICE"

    $SettingsService = Read-Text $SettingsServicePath

    $OldSettingsMarker = @'
    // ============================================================
    // APARIENCIA
    // ============================================================

    public string Theme { get; set; } = ThemeService.Dark;
'@

    $NewSettingsMarker = @'
    // ============================================================
    // GAMEPAD / INPUT
    // ============================================================

    /// <summary>
    /// Permite desactivar completamente la creación de gamepads UHID
    /// sin afectar teclado/mouse normal de ControlVE.
    /// </summary>
    public bool GamepadEnabled { get; set; } = true;

    /// <summary>
    /// Nombre SDL del mando físico preferido.
    /// "__auto__" conserva la detección automática actual.
    /// No se persiste InstanceId porque puede cambiar al reconectar.
    /// </summary>
    public string SelectedGamepadName { get; set; } = "__auto__";

    // ============================================================
    // APARIENCIA
    // ============================================================

    public string Theme { get; set; } = ThemeService.Dark;
'@

    $SettingsService = Replace-ExactlyOnce `
        -Content $SettingsService `
        -Old $OldSettingsMarker `
        -New $NewSettingsMarker `
        -Description "NovoraSettings Gamepad"

    Write-TextUtf8NoBom -Path $SettingsServicePath -Content $SettingsService
    Write-Host "[OK] settings.json soporta GamepadEnabled/SelectedGamepadName." -ForegroundColor Green

    # =================================================================
    # 3. GAMEPAD SETTINGS WINDOW XAML
    # =================================================================

    Write-Section "3/7 - GAMEPAD SETTINGS WINDOW XAML"

    $GamepadWindowXamlContent = @'
<Window
    x:Class="NOVORA.GamepadSettingsWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Mando e Input - NOVORA-LINK 1.4"
    Width="760"
    Height="720"
    MinWidth="700"
    MinHeight="620"
    WindowStartupLocation="CenterOwner"
    ResizeMode="CanResize"
    Background="{DynamicResource WindowBrush}"
    Loaded="GamepadSettingsWindow_Loaded">

    <Window.Resources>
        <Style x:Key="GamepadSectionLabel" TargetType="TextBlock">
            <Setter Property="FontSize" Value="11"/>
            <Setter Property="FontWeight" Value="Bold"/>
            <Setter Property="Foreground" Value="{DynamicResource BlueBrush}"/>
            <Setter Property="Margin" Value="2,0,0,8"/>
        </Style>

        <Style x:Key="GamepadTitle" TargetType="TextBlock">
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>

        <Style x:Key="GamepadCaption" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource GreenBrush}"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>

        <Style x:Key="GamepadCombo" TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
            <Setter Property="MinHeight" Value="40"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
    </Window.Resources>

    <Border
        Background="{DynamicResource WindowBrush}"
        BorderBrush="{DynamicResource WindowBorderBrush}"
        BorderThickness="1"
        CornerRadius="12"
        Padding="22">

        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <StackPanel Grid.Row="0" Margin="0,0,0,18">
                <TextBlock
                    Text="MANDO E INPUT"
                    FontSize="22"
                    FontWeight="Bold"
                    Foreground="{DynamicResource TextBrush}"/>

                <TextBlock
                    Text="Configura el gamepad de VisionEngine sin llenar la configuración general de NOVORA."
                    Margin="0,5,0,0"
                    FontSize="11"
                    Foreground="{DynamicResource MutedBrush}"/>
            </StackPanel>

            <ScrollViewer
                Grid.Row="1"
                VerticalScrollBarVisibility="Auto"
                HorizontalScrollBarVisibility="Disabled">

                <StackPanel>

                    <!-- MANDO -->
                    <TextBlock
                        Text="MANDO"
                        Style="{StaticResource GamepadSectionLabel}"/>

                    <Border
                        Background="{DynamicResource TitleBarBrush}"
                        BorderBrush="{DynamicResource WindowBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="10"
                        Padding="18"
                        Margin="0,0,0,18">

                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="220"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>

                            <StackPanel Grid.Row="0" Grid.Column="0" Margin="0,0,18,14">
                                <TextBlock Text="Usar mando" Style="{StaticResource GamepadTitle}"/>
                                <TextBlock
                                    Text="Crea el gamepad UHID en Android"
                                    Style="{StaticResource GamepadCaption}"
                                    Margin="0,3,0,0"/>
                            </StackPanel>

                            <CheckBox
                                x:Name="GamepadEnabledCheckBox"
                                Grid.Row="0"
                                Grid.Column="1"
                                VerticalAlignment="Center"
                                HorizontalAlignment="Left"
                                Content="Activado"
                                Foreground="White"
                                FontWeight="SemiBold"/>

                            <StackPanel Grid.Row="1" Grid.Column="0" Margin="0,0,18,14">
                                <TextBlock Text="Mando preferido" Style="{StaticResource GamepadTitle}"/>
                                <TextBlock
                                    Text="Selecciona un mando o deja automático"
                                    Style="{StaticResource GamepadCaption}"
                                    Margin="0,3,0,0"/>
                            </StackPanel>

                            <ComboBox
                                x:Name="GamepadComboBox"
                                Grid.Row="1"
                                Grid.Column="1"
                                Margin="0,0,0,14"
                                Style="{StaticResource GamepadCombo}"
                                DisplayMemberPath="LabelVE"
                                SelectedValuePath="ValueVE"
                                IsEnabled="{Binding ElementName=GamepadEnabledCheckBox, Path=IsChecked}"/>

                            <StackPanel Grid.Row="2" Grid.Column="0" Margin="0,0,18,0">
                                <TextBlock Text="Detección" Style="{StaticResource GamepadTitle}"/>
                                <TextBlock
                                    Text="Consulta SDL sólo cuando tú lo pides"
                                    Style="{StaticResource GamepadCaption}"
                                    Margin="0,3,0,0"/>
                            </StackPanel>

                            <Button
                                Grid.Row="2"
                                Grid.Column="1"
                                Content="Actualizar mandos"
                                Height="40"
                                HorizontalAlignment="Stretch"
                                Style="{StaticResource ActionButton}"
                                Click="RefreshGamepads_Click"/>
                        </Grid>
                    </Border>

                    <!-- DIAGNOSTICO -->
                    <TextBlock
                        Text="DIAGNÓSTICO"
                        Style="{StaticResource GamepadSectionLabel}"/>

                    <Border
                        Background="{DynamicResource TitleBarBrush}"
                        BorderBrush="{DynamicResource WindowBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="10"
                        Padding="18"
                        Margin="0,0,0,18">

                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <TextBlock
                                x:Name="DetectionStatusText"
                                Grid.Row="0"
                                Text="SDL3 todavía no comprobado."
                                Foreground="White"
                                FontSize="12"
                                TextWrapping="Wrap"/>

                            <TextBlock
                                x:Name="DiagnosticStatusText"
                                Grid.Row="1"
                                Margin="0,10,0,14"
                                Text="La prueba SDL es local. No representa la latencia completa PC → Android → juego."
                                Foreground="{DynamicResource MutedBrush}"
                                FontSize="11"
                                TextWrapping="Wrap"
                                LineHeight="17"/>

                            <Button
                                Grid.Row="2"
                                Content="Probar SDL"
                                Height="40"
                                Style="{StaticResource ActionButton}"
                                Click="TestSdl_Click"/>
                        </Grid>
                    </Border>

                    <!-- TECLADO + RATON -->
                    <TextBlock
                        Text="TECLADO + RATÓN → MANDO"
                        Style="{StaticResource GamepadSectionLabel}"/>

                    <Border
                        Background="{DynamicResource TitleBarBrush}"
                        BorderBrush="{DynamicResource WindowBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="10"
                        Padding="18"
                        Margin="0,0,0,18">

                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>

                            <StackPanel Grid.Column="0" Margin="0,0,18,0">
                                <TextBlock
                                    Text="Mapper de FPS"
                                    Style="{StaticResource GamepadTitle}"/>

                                <TextBlock
                                    Margin="0,5,0,0"
                                    Text="Aquí conectaremos WASD al stick izquierdo y movimiento relativo del ratón al stick derecho. Esta V1 sólo prepara la ventana; el mapper se implementa en el siguiente bloque para no mezclar dos cambios grandes."
                                    Foreground="{DynamicResource MutedBrush}"
                                    FontSize="11"
                                    TextWrapping="Wrap"
                                    LineHeight="17"/>
                            </StackPanel>

                            <Border
                                Grid.Column="1"
                                Background="#263844"
                                BorderBrush="#6C879A"
                                BorderThickness="1"
                                CornerRadius="8"
                                Padding="12,7"
                                VerticalAlignment="Center">
                                <TextBlock
                                    Text="SIGUIENTE FASE"
                                    Foreground="{DynamicResource GreenBrush}"
                                    FontSize="10"
                                    FontWeight="Bold"/>
                            </Border>
                        </Grid>
                    </Border>

                    <!-- PLAYSTATION -->
                    <TextBlock
                        Text="PLAYSTATION"
                        Style="{StaticResource GamepadSectionLabel}"/>

                    <Border
                        Background="{DynamicResource TitleBarBrush}"
                        BorderBrush="{DynamicResource WindowBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="10"
                        Padding="18">

                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <TextBlock
                                Grid.Row="0"
                                Text="DualShock / DualSense"
                                Style="{StaticResource GamepadTitle}"/>

                            <WrapPanel Grid.Row="1" Margin="0,12,0,0">
                                <CheckBox Content="Vibración" IsEnabled="False" IsChecked="True" Foreground="White" Margin="0,0,22,8"/>
                                <CheckBox Content="LED" IsEnabled="False" Foreground="White" Margin="0,0,22,8"/>
                                <CheckBox Content="Touchpad" IsEnabled="False" Foreground="White" Margin="0,0,22,8"/>
                                <CheckBox Content="Giroscopio" IsEnabled="False" Foreground="White" Margin="0,0,22,8"/>
                                <CheckBox Content="Acelerómetro" IsEnabled="False" Foreground="White" Margin="0,0,22,8"/>

                                <TextBlock
                                    Width="620"
                                    Margin="0,5,0,0"
                                    Text="Estas opciones se habilitarán cuando conectemos UhidOutputVE con las capacidades reales de SDL/DualSense. Se muestran desactivadas para no prometer funciones que todavía no ejecuta el backend."
                                    Foreground="{DynamicResource MutedBrush}"
                                    FontSize="10"
                                    TextWrapping="Wrap"/>
                            </WrapPanel>
                        </Grid>
                    </Border>
                </StackPanel>
            </ScrollViewer>

            <Grid Grid.Row="2" Margin="0,18,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="12"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <TextBlock
                    Grid.Column="0"
                    Text="Los cambios de mando se aplican al iniciar la siguiente sesión de VisionEngine."
                    FontSize="10"
                    Foreground="{DynamicResource MutedBrush}"
                    VerticalAlignment="Center"
                    TextWrapping="Wrap"/>

                <Button
                    Grid.Column="1"
                    Content="Cancelar"
                    Width="120"
                    Height="42"
                    Click="Cancel_Click"/>

                <Button
                    Grid.Column="3"
                    Content="Guardar"
                    Width="140"
                    Height="42"
                    Style="{StaticResource ActionButton}"
                    Click="Save_Click"/>
            </Grid>
        </Grid>
    </Border>
</Window>
'@

    Write-TextUtf8NoBom -Path $GamepadWindowXaml -Content $GamepadWindowXamlContent
    Write-Host "[OK] GamepadSettingsWindow.xaml" -ForegroundColor Green

    # =================================================================
    # 4. GAMEPAD SETTINGS WINDOW CODE
    # =================================================================

    Write-Section "4/7 - GAMEPAD SETTINGS WINDOW CODE"

    $GamepadWindowCodeContent = @'
using NOVORA.Services;
using NOVORA.VisionEngine.Gamepad;
using System.Diagnostics;
using System.Windows;

namespace NOVORA;

public partial class GamepadSettingsWindow : Window
{
    private readonly SettingsService _settingsServiceVE =
        new();

    private readonly NovoraPaths _pathsVE =
        new();

    private readonly List<OptionGamepadVE> _optionsVE =
        [];

    private SdlGamepadVE? _sdlVE;
    private string _savedSelectionVE =
        ConfigGamepadVE.AutoSelectionVE;

    private bool _loadedVE;

    private sealed record OptionGamepadVE(
        uint? InstanceIdVE,
        string ValueVE,
        string LabelVE);

    public GamepadSettingsWindow()
    {
        InitializeComponent();
    }

    private void GamepadSettingsWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_loadedVE)
        {
            return;
        }

        _loadedVE = true;

        LoadSavedSettingsVE();
        RefreshGamepadsVE();
    }

    private void LoadSavedSettingsVE()
    {
        NovoraSettings settings =
            _settingsServiceVE.Load();

        GamepadEnabledCheckBox.IsChecked =
            settings.GamepadEnabled;

        _savedSelectionVE =
            ConfigGamepadVE.NormalizeSelectedNameVE(
                settings.SelectedGamepadName);
    }

    private void RefreshGamepads_Click(
        object sender,
        RoutedEventArgs e)
        => RefreshGamepadsVE();

    private void RefreshGamepadsVE()
    {
        _optionsVE.Clear();

        _optionsVE.Add(
            new OptionGamepadVE(
                null,
                ConfigGamepadVE.AutoSelectionVE,
                "Automático — usar mandos detectados"));

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        try
        {
            _sdlVE ??=
                new SdlGamepadVE(
                    _pathsVE);

            _sdlVE.InitializeVE();

            IReadOnlyList<uint> ids =
                _sdlVE.GetDetectedIdsVE();

            HashSet<string> names =
                new(
                    StringComparer.OrdinalIgnoreCase);

            foreach (uint id in ids)
            {
                IntPtr handle =
                    _sdlVE.OpenVE(
                        id);

                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    string name =
                        _sdlVE.GetNameVE(
                            handle)
                        .Trim();

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name =
                            $"Gamepad SDL {id}";
                    }

                    if (!names.Add(name))
                    {
                        continue;
                    }

                    _optionsVE.Add(
                        new OptionGamepadVE(
                            id,
                            name,
                            name));
                }
                finally
                {
                    _sdlVE.CloseVE(
                        handle);
                }
            }

            stopwatch.Stop();

            int physicalCount =
                _optionsVE.Count - 1;

            DetectionStatusText.Text =
                physicalCount == 0
                    ? $"SDL3 OK · no hay mandos detectados · {stopwatch.Elapsed.TotalMilliseconds:F1} ms"
                    : $"SDL3 OK · {physicalCount} mando(s) detectado(s) · {stopwatch.Elapsed.TotalMilliseconds:F1} ms";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            DetectionStatusText.Text =
                $"SDL3 ERROR · {ex.Message}";
        }

        if (
            !string.Equals(
                _savedSelectionVE,
                ConfigGamepadVE.AutoSelectionVE,
                StringComparison.OrdinalIgnoreCase) &&
            !_optionsVE.Any(
                option =>
                    string.Equals(
                        option.ValueVE,
                        _savedSelectionVE,
                        StringComparison.OrdinalIgnoreCase)))
        {
            _optionsVE.Add(
                new OptionGamepadVE(
                    null,
                    _savedSelectionVE,
                    $"{_savedSelectionVE} — no conectado"));
        }

        GamepadComboBox.ItemsSource =
            null;

        GamepadComboBox.ItemsSource =
            _optionsVE.ToArray();

        GamepadComboBox.SelectedValue =
            _savedSelectionVE;

        if (GamepadComboBox.SelectedIndex < 0)
        {
            GamepadComboBox.SelectedValue =
                ConfigGamepadVE.AutoSelectionVE;
        }
    }

    private void TestSdl_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _sdlVE ??=
                new SdlGamepadVE(
                    _pathsVE);

            _sdlVE.InitializeVE();

            OptionGamepadVE? selected =
                GamepadComboBox.SelectedItem
                as OptionGamepadVE;

            if (
                selected is not null &&
                !string.Equals(
                    selected.ValueVE,
                    ConfigGamepadVE.AutoSelectionVE,
                    StringComparison.OrdinalIgnoreCase) &&
                selected.InstanceIdVE is null)
            {
                DiagnosticStatusText.Text =
                    $"El mando seleccionado no está conectado: {selected.LabelVE}";

                return;
            }

            OptionGamepadVE? target =
                selected?.InstanceIdVE is not null
                    ? selected
                    : _optionsVE.FirstOrDefault(
                        option =>
                            option.InstanceIdVE is not null);

            if (target?.InstanceIdVE is not uint id)
            {
                DiagnosticStatusText.Text =
                    "No hay un mando físico disponible para probar.";

                return;
            }

            IntPtr handle =
                _sdlVE.OpenVE(
                    id);

            if (handle == IntPtr.Zero)
            {
                DiagnosticStatusText.Text =
                    "SDL3 detectó el mando, pero no pudo abrirlo.";

                return;
            }

            try
            {
                const int SamplesVE = 128;

                StateGamepadVE lastState =
                    default;

                Stopwatch stopwatch =
                    Stopwatch.StartNew();

                for (int index = 0;
                     index < SamplesVE;
                     index++)
                {
                    lastState =
                        _sdlVE.ReadStateVE(
                            handle);
                }

                stopwatch.Stop();

                double averageMs =
                    stopwatch.Elapsed.TotalMilliseconds /
                    SamplesVE;

                string name =
                    _sdlVE.GetNameVE(
                        handle);

                DiagnosticStatusText.Text =
                    $"SDL3 OK · {name}\n" +
                    $"Lectura local promedio: {averageMs:F3} ms · {SamplesVE} muestras\n" +
                    $"Sticks L({lastState.LeftX}, {lastState.LeftY}) " +
                    $"R({lastState.RightX}, {lastState.RightY}) · " +
                    $"LT {lastState.LeftTrigger} · RT {lastState.RightTrigger} · " +
                    $"Botones {lastState.Buttons}\n\n" +
                    "Esta medición cubre SDL local en Windows; no es la latencia completa hasta Android/juego.";
            }
            finally
            {
                _sdlVE.CloseVE(
                    handle);
            }
        }
        catch (Exception ex)
        {
            DiagnosticStatusText.Text =
                $"Prueba SDL falló: {ex.Message}";
        }
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            NovoraSettings settings =
                _settingsServiceVE.Load();

            settings.GamepadEnabled =
                GamepadEnabledCheckBox.IsChecked == true;

            settings.SelectedGamepadName =
                ConfigGamepadVE.NormalizeSelectedNameVE(
                    GamepadComboBox.SelectedValue
                        as string);

            _settingsServiceVE.Save(
                settings);

            DialogResult =
                true;

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA — Mando e Input",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            false;

        Close();
    }

    protected override void OnClosed(
        EventArgs e)
    {
        try
        {
            _sdlVE?.Dispose();
        }
        catch
        {
        }

        _sdlVE =
            null;

        base.OnClosed(e);
    }
}
'@

    Write-TextUtf8NoBom -Path $GamepadWindowCode -Content $GamepadWindowCodeContent
    Write-Host "[OK] GamepadSettingsWindow.xaml.cs" -ForegroundColor Green

    # =================================================================
    # 5. SETTINGS WINDOW - BOTÓN DE ENTRADA
    # =================================================================

    Write-Section "5/7 - ACCESO DESDE SETTINGSWINDOW"

    $SettingsXaml = Read-Text $SettingsWindowXaml

    $DescriptionMarker = @'
                    <!-- DESCRIPCION -->

                    <TextBlock
                        Text="DESCRIPCIÓN"
                        Style="{StaticResource SettingsSectionLabel}"/>
'@

    $ControlsCard = @'
                    <!-- CONTROLES -->

                    <TextBlock
                        Text="CONTROLES"
                        Style="{StaticResource SettingsSectionLabel}"/>

                    <Border
                        Background="{DynamicResource TitleBarBrush}"
                        BorderBrush="{DynamicResource WindowBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="10"
                        Padding="18"
                        Margin="0,0,0,18">

                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="180"/>
                            </Grid.ColumnDefinitions>

                            <StackPanel Grid.Column="0" Margin="0,0,18,0">
                                <TextBlock
                                    Text="Mando e input"
                                    Style="{StaticResource BlueCardTitle}"/>

                                <TextBlock
                                    Margin="0,5,0,0"
                                    Text="Selecciona el mando, comprueba SDL3 y prepara perfiles de teclado/ratón en una ventana independiente."
                                    Style="{StaticResource BlueCardCaption}"
                                    TextWrapping="Wrap"/>
                            </StackPanel>

                            <Button
                                Grid.Column="1"
                                Content="CONFIGURAR"
                                Height="42"
                                VerticalAlignment="Center"
                                Style="{StaticResource ActionButton}"
                                Click="GamepadSettings_Click"/>
                        </Grid>
                    </Border>

                    <!-- DESCRIPCION -->

                    <TextBlock
                        Text="DESCRIPCIÓN"
                        Style="{StaticResource SettingsSectionLabel}"/>
'@

    $SettingsXaml = Replace-ExactlyOnce `
        -Content $SettingsXaml `
        -Old $DescriptionMarker `
        -New $ControlsCard `
        -Description "Tarjeta Controles en SettingsWindow"

    Write-TextUtf8NoBom -Path $SettingsWindowXaml -Content $SettingsXaml

    $SettingsCode = Read-Text $SettingsWindowCode

    $SaveMarker = @'
    private void Save_Click(
        object sender,
        RoutedEventArgs e)
'@

    $GamepadLauncher = @'
    private void GamepadSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        GamepadSettingsWindow window =
            new()
            {
                Owner = this
            };

        window.ShowDialog();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
'@

    $SettingsCode = Replace-ExactlyOnce `
        -Content $SettingsCode `
        -Old $SaveMarker `
        -New $GamepadLauncher `
        -Description "GamepadSettings_Click"

    Write-TextUtf8NoBom -Path $SettingsWindowCode -Content $SettingsCode

    Write-Host "[OK] SettingsWindow abre la ventana independiente." -ForegroundColor Green

    # =================================================================
    # 6. MANAGER GAMEPAD - APLICAR CONFIG AL INICIO
    # =================================================================

    Write-Section "6/7 - MANAGER GAMEPAD VE"

    $ManagerGamepad = Read-Text $ManagerGamepadPath

    $OldFields = @'
    private long _reportsVE;
    private bool _disposedVE;
'@

    $NewFields = @'
    private long _reportsVE;
    private bool _disposedVE;

    /*
     * Configuración leída UNA sola vez al iniciar GamepadVE.
     * No genera polling ni lecturas repetidas de settings.json.
     */
    private bool _enabledBySettingsVE = true;
    private string _preferredPhysicalNameVE =
        ConfigGamepadVE.AutoSelectionVE;
'@

    $ManagerGamepad = Replace-ExactlyOnce `
        -Content $ManagerGamepad `
        -Old $OldFields `
        -New $NewFields `
        -Description "Campos de configuración GamepadVE"

    $OldStart = @'
        _sdlVE.InitializeVE();
        cancellationToken.ThrowIfCancellationRequested();

        _pumpCtsVE =
'@

    $NewStart = @'
        NovoraSettings settingsVE =
            new SettingsService().Load();

        _enabledBySettingsVE =
            settingsVE.GamepadEnabled;

        _preferredPhysicalNameVE =
            ConfigGamepadVE.NormalizeSelectedNameVE(
                settingsVE.SelectedGamepadName);

        if (!_enabledBySettingsVE)
        {
            PublishVE(
                StatesGamepadVE.Ready,
                null);

            return Task.CompletedTask;
        }

        _sdlVE.InitializeVE();
        cancellationToken.ThrowIfCancellationRequested();

        _pumpCtsVE =
'@

    $ManagerGamepad = Replace-ExactlyOnce `
        -Content $ManagerGamepad `
        -Old $OldStart `
        -New $NewStart `
        -Description "Carga settings una vez en StartAsync"

    $OldPhysicalName = @'
            string physicalName =
                _sdlVE.GetNameVE(
                    handle);

            var device =
'@

    $NewPhysicalName = @'
            string physicalName =
                _sdlVE.GetNameVE(
                    handle);

            if (!ConfigGamepadVE.MatchesSelectionVE(
                    _preferredPhysicalNameVE,
                    physicalName))
            {
                _sdlVE.CloseVE(
                    handle);

                continue;
            }

            var device =
'@

    $ManagerGamepad = Replace-ExactlyOnce `
        -Content $ManagerGamepad `
        -Old $OldPhysicalName `
        -New $NewPhysicalName `
        -Description "Filtro de mando preferido"

    $OldPublish = @'
                    StatesGamepadVE.Stopped =>
                        "Gamepad VisionEngine detenido.",

                    _ when _slotsVE.Count == 0 =>
                        "Gamepad VisionEngine activo; esperando control físico.",
'@

    $NewPublish = @'
                    StatesGamepadVE.Stopped =>
                        "Gamepad VisionEngine detenido.",

                    _ when !_enabledBySettingsVE =>
                        "Gamepad VisionEngine desactivado desde Configuración.",

                    _ when _slotsVE.Count == 0 =>
                        string.Equals(
                            _preferredPhysicalNameVE,
                            ConfigGamepadVE.AutoSelectionVE,
                            StringComparison.OrdinalIgnoreCase)
                            ? "Gamepad VisionEngine activo; esperando control físico."
                            : $"Gamepad VisionEngine activo; esperando {_preferredPhysicalNameVE}.",
'@

    $ManagerGamepad = Replace-ExactlyOnce `
        -Content $ManagerGamepad `
        -Old $OldPublish `
        -New $NewPublish `
        -Description "Estado de selección GamepadVE"

    Write-TextUtf8NoBom -Path $ManagerGamepadPath -Content $ManagerGamepad
    Write-Host "[OK] ManagerGamepadVE respeta configuración al iniciar." -ForegroundColor Green

    # =================================================================
    # 7. VALIDACIÓN XAML + TDD GREEN + BUILD
    # =================================================================

    Write-Section "7/7 - VALIDACIÓN FINAL"

    try
    {
        [xml](Get-Content -LiteralPath $SettingsWindowXaml -Raw) | Out-Null
        [xml](Get-Content -LiteralPath $GamepadWindowXaml -Raw) | Out-Null
        Write-Host "[OK] XAML válido como XML." -ForegroundColor Green
    }
    catch
    {
        throw "XAML inválido: $($_.Exception.Message)"
    }

    Push-Location $RepoPath
    try
    {
        Write-Host ""
        Write-Host "=== LIMPIEZA GENERATED ANTES DE GREEN ===" -ForegroundColor Cyan

        Remove-GeneratedArtifacts

        Write-Host "[OK] obj de NOVORA/tests eliminado antes de GREEN." -ForegroundColor Green

        Write-Host ""
        Write-Host "=== TDD GREEN ===" -ForegroundColor Cyan

        $Green = Invoke-DotnetCapture -Arguments @(
            "test",
            $TestsProject,
            "-c",
            "Release",
            "--filter",
            "FullyQualifiedName~GamepadSettingsTests",
            "--logger",
            "console;verbosity=normal",
            "--nologo"
        )

        Write-Host $Green.Output

        if ($Green.ExitCode -ne 0)
        {
            $GreenFailurePath =
                Join-Path `
                    $env:USERPROFILE `
                    (
                        "Desktop\NOVORA-GAMEPADSETTINGS-GREEN-FAIL-" +
                        (Get-Date -Format "yyyyMMdd-HHmmss") +
                        ".txt"
                    )

            [System.IO.File]::WriteAllText(
                $GreenFailurePath,
                $Green.Output,
                (New-Object System.Text.UTF8Encoding($false)))

            throw @"
Las pruebas GamepadSettingsTests no pasaron.

La salida completa quedó guardada en:
$GreenFailurePath
"@
        }

        Write-Host "[OK] GamepadSettingsTests GREEN." -ForegroundColor Green

        Write-Host ""
        Write-Host "=== DOTNET BUILD RELEASE ===" -ForegroundColor Cyan

        $Build = Invoke-DotnetCapture -Arguments @(
            "build",
            $SolutionPath,
            "-t:Rebuild",
            "-c",
            "Release",
            "--nologo",
            "-v:minimal"
        )

        Write-Host $Build.Output

        if ($Build.ExitCode -ne 0)
        {
            throw "NOVORA.sln no compiló en Release."
        }
    }
    finally
    {
        Pop-Location
    }

    $LinkAfter = Get-TreeFingerprint $LinkEngineRoot
    $AndroidLinkAfter = Get-TreeFingerprint $AndroidLinkEngineRoot

    if ($LinkAfter -ne $LinkBefore)
    {
        throw "INTEGRIDAD: LinkEngine cambió durante una integración exclusiva de VisionEngine/Gamepad."
    }

    if ($AndroidLinkAfter -ne $AndroidLinkBefore)
    {
        throw "INTEGRIDAD: Android LinkEngine cambió durante una integración exclusiva de VisionEngine/Gamepad."
    }

    Write-Host "[OK] LinkEngine intacto." -ForegroundColor Green
    Write-Host "[OK] Android LinkEngine intacto." -ForegroundColor Green

    Write-Section "GAMEPAD SETTINGS WINDOW V1.2 INTEGRADO"

    Write-Host @"
Resultado:

  SettingsWindow
      -> CONTROLES
      -> CONFIGURAR
      -> GamepadSettingsWindow

Funcional ahora:
  [OK] Activar/desactivar mando
  [OK] Seleccionar mando físico preferido
  [OK] Automático
  [OK] Detección SDL bajo demanda
  [OK] Diagnóstico SDL local bajo demanda
  [OK] Persistencia settings.json
  [OK] ManagerGamepadVE respeta la selección al iniciar
  [OK] Cero polling nuevo

Preparado para siguiente bloque:
  [ ] Keyboard + Mouse -> Gamepad
  [ ] Latencia end-to-end con ACK Android
  [ ] Rumble físico
  [ ] LED PlayStation
  [ ] Touchpad
  [ ] Gyro / acelerómetro
  [ ] Adaptive Triggers
  [ ] GamepadVE completamente event-driven

Backup:
$BackupRoot
"@ -ForegroundColor Green
}
catch
{
    $message = $_.Exception.Message

    Restore-Backup `
        -BackupRoot $BackupRoot `
        -NewFiles $NewFiles

    # Nunca dejamos .g.cs/.baml generados por una integración fallida.
    try
    {
        Remove-GeneratedArtifacts
    }
    catch
    {
    }

    Write-Host ""
    Write-Host "[ROLLBACK] Verificando baseline restaurado con Rebuild..." -ForegroundColor Yellow

    try
    {
        Push-Location $RepoPath
        try
        {
            $RollbackBuild =
                Invoke-DotnetCapture -Arguments @(
                    "build",
                    $SolutionPath,
                    "-t:Rebuild",
                    "-c",
                    "Release",
                    "--nologo",
                    "-v:minimal"
                )
        }
        finally
        {
            Pop-Location
        }

        if ($RollbackBuild.ExitCode -eq 0)
        {
            Write-Host "[ROLLBACK] Baseline restaurado y Rebuild correcto." -ForegroundColor Green
        }
        else
        {
            $RollbackFailurePath =
                Write-DiagnosticFile `
                    -Prefix "NOVORA-GAMEPADSETTINGS-ROLLBACK-BUILD-FAIL" `
                    -Content $RollbackBuild.Output

            Write-Host "[ROLLBACK] El Rebuild del baseline restaurado falló." -ForegroundColor Red
            Write-Host "[ROLLBACK] Diagnóstico: $RollbackFailurePath" -ForegroundColor Red
        }
    }
    catch
    {
        Write-Host "[ROLLBACK] No fue posible verificar el Rebuild: $($_.Exception.Message)" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "INTEGRACIÓN CANCELADA:" -ForegroundColor Red
    Write-Host $message -ForegroundColor Red

    throw
}
