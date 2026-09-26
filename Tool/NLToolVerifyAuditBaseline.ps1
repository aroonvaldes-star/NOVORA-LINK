[CmdletBinding()]
param(
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [string]$AuditPath = "Documentation/Audit/2026-09-23"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path -LiteralPath $RepositoryPath).Path
$audit = (Resolve-Path -LiteralPath (Join-Path $repo $AuditPath)).Path
$required = @(
    "NLDocumentationAuditAndroidRuntime.json",
    "NLDocumentationAuditBaseline.md",
    "NLDocumentationAuditDetections.json",
    "NLDocumentationAuditEnvironment.json",
    "NLDocumentationAuditGitState.json",
    "NLDocumentationAuditManifest.json",
    "NLDocumentationAuditProjectGraph.json",
    "NLDocumentationAuditProjectReferences.json",
    "NLDocumentationAuditRuntimeSnapshot.json",
    "NLDocumentationAuditToolManifest.json",
    "index.diff",
    "untracked-files.txt",
    "working-tree.diff"
)

$errors = [Collections.Generic.List[string]]::new()
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $audit $name))) {
        $errors.Add("Missing required artifact: $name")
    }
}

Get-ChildItem -LiteralPath $audit -Filter *.json -File | ForEach-Object {
    try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json | Out-Null }
    catch { $errors.Add("Invalid JSON: $($_.Name): $($_.Exception.Message)") }
}

$manifest = Get-Content -LiteralPath (Join-Path $audit "NLDocumentationAuditManifest.json") -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = Join-Path $audit $entry.file
    if (-not (Test-Path -LiteralPath $path)) {
        $errors.Add("Manifest file missing: $($entry.file)")
        continue
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $entry.sha256) {
        $errors.Add("Manifest hash mismatch: $($entry.file)")
    }
}

$gitState = Get-Content -LiteralPath (Join-Path $audit "NLDocumentationAuditGitState.json") -Raw | ConvertFrom-Json
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($gitState.head -ne $head) {
    $errors.Add("Baseline HEAD $($gitState.head) does not match current HEAD $head")
}

$detections = Get-Content -LiteralPath (Join-Path $audit "NLDocumentationAuditDetections.json") -Raw | ConvertFrom-Json
foreach ($category in @("process", "adb", "scrcpy", "storage", "engineDependencies")) {
    foreach ($item in $detections.$category) {
        if ($item.state -ne "DETECTED") {
            $errors.Add("Detection in $category is not DETECTED: $($item.file):$($item.line)")
        }
    }
}

$baselineText = Get-Content -LiteralPath (Join-Path $audit "NLDocumentationAuditBaseline.md") -Raw
if ($baselineText -notmatch "does not prove Android installation") {
    $errors.Add("Evidence boundary is missing from baseline summary")
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

[pscustomobject]@{
    Result = "PASS"
    Head = $head
    RequiredArtifacts = $required.Count
    ManifestEntries = $manifest.files.Count
    ProcessDetections = $detections.process.Count
    AdbDetections = $detections.adb.Count
    ScrcpyDetections = $detections.scrcpy.Count
    StorageDetections = $detections.storage.Count
    EngineDependencyDetections = $detections.engineDependencies.Count
}
