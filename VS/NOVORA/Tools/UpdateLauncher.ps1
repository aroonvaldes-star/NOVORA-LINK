param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Target,
    [Parameter(Mandatory=$true)][int]$ProcessId
)

$ErrorActionPreference = 'Stop'

try {
    Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    # Releases must contain compiled NOVORA files, preferably with NOVORA.exe at the root.
    $root = $Source
    $exe = Get-ChildItem -Path $Source -Filter 'NOVORA.exe' -Recurse -File | Select-Object -First 1
    if ($exe) { $root = $exe.Directory.FullName }

    $files = Get-ChildItem -Path $root -Recurse -File
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('','/')
        $destination = Join-Path $Target $relative
        $parent = Split-Path $destination -Parent
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        Copy-Item $file.FullName $destination -Force
    }

    Start-Process (Join-Path $Target 'NOVORA.exe')
}
catch {
    # Leave the current installation intact if anything fails.
}
