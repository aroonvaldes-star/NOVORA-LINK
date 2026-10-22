[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$paths = @(& git ls-files)
if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar el contenido versionado.' }
$forbidden = @($paths | Where-Object {
    $_ -match '(^|/)(bin|obj|target|node_modules|artifacts|Versiones)(/|$)' -or
    $_ -match '(?i)\.(pfx|p12|jks|keystore|pem|key|apk|aab)$' -or
    $_ -match '(^|/)\.env(?:\.|$)' -and $_ -notmatch '\.env\.example$'
})
if ($forbidden.Count) {
    throw "Archivos excluidos incluidos en Git: $($forbidden -join ', ')"
}
Write-Host "Contenido versionado comprobado: $($paths.Count) rutas; sin bin/obj ni material privado de firma."
