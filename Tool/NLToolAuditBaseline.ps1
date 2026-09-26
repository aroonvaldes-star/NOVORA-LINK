[CmdletBinding()]
param(
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = "Documentation/Audit/2026-09-23"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path -LiteralPath $RepositoryPath).Path
$output = Join-Path $repo $OutputPath

if (-not (Test-Path -LiteralPath (Join-Path $repo ".git"))) {
    throw "La ruta no es la raiz del repositorio NOVORA-LINK: $repo"
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

function Write-JsonFile {
    param([Parameter(Mandatory)]$Value, [Parameter(Mandatory)][string]$Path)
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-GitText {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $result = & git -C $repo @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') fallo: $result"
    }
    return ($result -join [Environment]::NewLine)
}

function Get-CommandVersion {
    param([Parameter(Mandatory)][string]$Name, [string[]]$Arguments = @("--version"))
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return [ordered]@{ name = $Name; available = $false; path = $null; version = $null }
    }

    $version = try { (& $command.Source @Arguments 2>&1 | Select-Object -First 1).ToString() } catch { $_.Exception.Message }
    return [ordered]@{ name = $Name; available = $true; path = $command.Source; version = $version }
}

function Get-TextDetections {
    param([Parameter(Mandatory)][string]$Pattern, [Parameter(Mandatory)][string[]]$Roots)
    $arguments = @(
        "-n", "--no-heading", "--color", "never",
        "--glob", "!**/bin/**",
        "--glob", "!**/obj/**",
        "--glob", "!**/target/**",
        "--glob", "!Documentation/Audit/**",
        $Pattern
    ) + $Roots
    $lines = & rg @arguments 2>$null
    if ($LASTEXITCODE -notin @(0, 1)) {
        throw "rg fallo para patron $Pattern"
    }

    return @($lines | ForEach-Object {
        if ($_ -match "^(.*?):(\d+):(.*)$") {
            [ordered]@{
                file = $Matches[1].Replace("\", "/")
                line = [int]$Matches[2]
                text = $Matches[3].Trim()
                state = "DETECTED"
            }
        }
    })
}

$head = Invoke-GitText -Arguments @("rev-parse", "HEAD")
$branch = Invoke-GitText -Arguments @("branch", "--show-current")
$status = Invoke-GitText -Arguments @("status", "--porcelain=v1")
$trackedDiff = Invoke-GitText -Arguments @("diff", "--binary", "--no-ext-diff")
$cachedDiff = Invoke-GitText -Arguments @("diff", "--cached", "--binary", "--no-ext-diff")
$untracked = Invoke-GitText -Arguments @("ls-files", "--others", "--exclude-standard")

$trackedDiff | Set-Content -LiteralPath (Join-Path $output "working-tree.diff") -Encoding utf8
$cachedDiff | Set-Content -LiteralPath (Join-Path $output "index.diff") -Encoding utf8
$untracked | Set-Content -LiteralPath (Join-Path $output "untracked-files.txt") -Encoding utf8

$gitState = [ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow
    repository = $repo
    head = $head.Trim()
    branch = $branch.Trim()
    clean = [string]::IsNullOrWhiteSpace($status)
    porcelain = @($status -split "`r?`n" | Where-Object { $_ })
    commands = @(
        "git rev-parse HEAD",
        "git branch --show-current",
        "git status --porcelain=v1",
        "git diff --binary --no-ext-diff",
        "git diff --cached --binary --no-ext-diff",
        "git ls-files --others --exclude-standard"
    )
}
Write-JsonFile $gitState (Join-Path $output "NLDocumentationAuditGitState.json")

$cimAvailable = $true
try {
    $computer = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
    $gpus = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object {
        [ordered]@{ name = $_.Name; driverVersion = $_.DriverVersion; adapterRam = $_.AdapterRAM }
    })
}
catch {
    $cimAvailable = $false
    $computer = $null
    $os = $null
    $cpu = $null
    $gpus = @()
}
$environment = [ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow
    machine = $env:COMPUTERNAME
    cimAvailable = $cimAvailable
    cimLimitation = if ($cimAvailable) { $null } else { "CIM no disponible en el entorno actual; completar en baseline fisico autorizado." }
    windows = [ordered]@{
        caption = if ($os) { $os.Caption } else { [System.Runtime.InteropServices.RuntimeInformation]::OSDescription }
        version = if ($os) { $os.Version } else { [Environment]::OSVersion.Version.ToString() }
        build = if ($os) { $os.BuildNumber } else { [Environment]::OSVersion.Version.Build }
        architecture = if ($os) { $os.OSArchitecture } else { [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() }
    }
    hardware = [ordered]@{
        manufacturer = if ($computer) { $computer.Manufacturer } else { $null }
        model = if ($computer) { $computer.Model } else { $null }
        ramBytes = if ($computer) { [long]$computer.TotalPhysicalMemory } else { $null }
        cpu = if ($cpu) { $cpu.Name } else { $env:PROCESSOR_IDENTIFIER }
        gpu = $gpus
    }
    toolchain = @(
        Get-CommandVersion "git"
        Get-CommandVersion "dotnet" @("--version")
        Get-CommandVersion "cargo" @("--version")
        Get-CommandVersion "rustc" @("--version")
        Get-CommandVersion "pwsh" @("--version")
        Get-CommandVersion "adb" @("version")
    )
}
Write-JsonFile $environment (Join-Path $output "NLDocumentationAuditEnvironment.json")

$projects = @()
Get-ChildItem -LiteralPath $repo -Recurse -File -Include *.csproj,*.sln,Cargo.toml |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj|target)[\\/]" } |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($repo, $_.FullName).Replace("\", "/")
        $projects += [ordered]@{ path = $relative; kind = $_.Extension.TrimStart("."); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
Write-JsonFile $projects (Join-Path $output "NLDocumentationAuditProjectGraph.json")

$projectReferences = @()
Get-ChildItem -LiteralPath $repo -Recurse -File -Filter *.csproj |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
    ForEach-Object {
        [xml]$xml = Get-Content -LiteralPath $_.FullName -Raw
        $from = [IO.Path]::GetRelativePath($repo, $_.FullName).Replace("\", "/")
        foreach ($node in $xml.Project.ItemGroup.ProjectReference) {
            if ($node.Include) { $projectReferences += [ordered]@{ from = $from; type = "ProjectReference"; to = [string]$node.Include } }
        }
        foreach ($node in $xml.Project.ItemGroup.PackageReference) {
            if ($node.Include) { $projectReferences += [ordered]@{ from = $from; type = "PackageReference"; to = [string]$node.Include; version = [string]$node.Version } }
        }
    }
Write-JsonFile $projectReferences (Join-Path $output "NLDocumentationAuditProjectReferences.json")

$maps = [ordered]@{
    process = Get-TextDetections "Process\.Start|ProcessStartInfo|Start-Process" @("src", "Tool", "scripts")
    adb = Get-TextDetections "NLServiceADB|adb\.exe|adb reverse|adb forward|track-devices|wait-for-device" @("src", "tests", "Tool")
    scrcpy = Get-TextDetections "scrcpy|app_process|SCID|Scrcpy41Compatibility" @("src", "tests", "Documentation", "docs", "third_party")
    storage = Get-TextDetections "LocalApplicationData|ApplicationData|DesktopDirectory|NOVORA-Files|/data/local/tmp|GetFolderPath|File\.|Directory\." @("src")
    engineDependencies = Get-TextDetections "using NOVORA\.(VisionEngine|LinkEngine|STEngine|ExInEngine)" @("src/NOVORA")
}
Write-JsonFile $maps (Join-Path $output "NLDocumentationAuditDetections.json")

$toolFiles = @(
    "src/NOVORA/Tools/adb.exe",
    "src/NOVORA/Tools/scrcpy.exe",
    "src/NOVORA/Tools/scrcpy-server",
    "src/NOVORA/Tools/SDL3.dll",
    "src/NOVORA/Tools/avcodec-62.dll",
    "src/NOVORA/Tools/avformat-62.dll",
    "src/NOVORA/Tools/avutil-60.dll",
    "src/NOVORA/Tools/swresample-6.dll"
)
$toolManifest = @($toolFiles | ForEach-Object {
    $path = Join-Path $repo $_
    [ordered]@{
        path = $_
        exists = Test-Path -LiteralPath $path
        length = if (Test-Path -LiteralPath $path) { (Get-Item -LiteralPath $path).Length } else { $null }
        sha256 = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { $null }
    }
})
Write-JsonFile $toolManifest (Join-Path $output "NLDocumentationAuditToolManifest.json")

$runtimeProcesses = @(Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match "NOVORA|Relay|adb|scrcpy" } |
    ForEach-Object {
        [ordered]@{ name = $_.ProcessName; id = $_.Id; responding = $_.Responding; path = try { $_.Path } catch { $null } }
    })
$runtimePorts = @()
if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
    $runtimePorts = @(Get-NetTCPConnection -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in @(27182, 27183, 27184, 27186, 27187, 27214) } |
        ForEach-Object {
            [ordered]@{ localAddress = $_.LocalAddress; localPort = $_.LocalPort; remoteAddress = $_.RemoteAddress; remotePort = $_.RemotePort; state = [string]$_.State; owningProcess = $_.OwningProcess }
        })
}
$runtime = [ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow
    note = "Snapshot local de solo lectura. No inicia motores ni ADB."
    processes = $runtimeProcesses
    ports = $runtimePorts
}
Write-JsonFile $runtime (Join-Path $output "NLDocumentationAuditRuntimeSnapshot.json")

$summary = @"
# NOVORA-LINK Audit Baseline A/B

- Captured UTC: $([DateTimeOffset]::UtcNow.ToString("O"))
- Repository: $repo
- Branch: $($branch.Trim())
- HEAD: $($head.Trim())
- Working tree clean: $([string]::IsNullOrWhiteSpace($status))
- Project entries: $($projects.Count)
- Project/package references: $($projectReferences.Count)
- Process detections: $($maps.process.Count)
- ADB detections: $($maps.adb.Count)
- scrcpy detections: $($maps.scrcpy.Count)
- Storage detections: $($maps.storage.Count)
- Cross-engine using detections: $($maps.engineDependencies.Count)
- Runtime processes observed: $($runtimeProcesses.Count)
- Relevant TCP endpoints observed: $($runtimePorts.Count)

## Evidence boundary

This package preserves and inventories the current checkout. Text detections remain `DETECTED`; they are not findings until caller, owner, lifecycle and runtime impact are triaged. This capture does not prove Android installation, physical connectivity, engine operation, performance, security or publication.
"@
$summary | Set-Content -LiteralPath (Join-Path $output "NLDocumentationAuditBaseline.md") -Encoding utf8

$artifactHashes = @()
Get-ChildItem -LiteralPath $output -File |
    Where-Object { $_.Name -ne "NLDocumentationAuditManifest.json" } |
    Sort-Object Name |
    ForEach-Object {
        $artifactHashes += [ordered]@{ file = $_.Name; length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
Write-JsonFile ([ordered]@{ generatedAtUtc = [DateTimeOffset]::UtcNow; files = $artifactHashes }) (Join-Path $output "NLDocumentationAuditManifest.json")

Write-Host "NOVORA audit baseline written to $output"
