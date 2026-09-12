$ErrorActionPreference = 'Stop'
$manifest = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\package\AppxManifest.xml'
if (-not (Test-Path $manifest)) { throw 'Run scripts\Build.ps1 first.' }
# Development registration needs Windows developer mode; does not trust a certificate.
try { Add-AppxPackage -Register $manifest }
catch {
    if ($_.Exception.Message -match '0x80073CFF') {
        throw 'Windows developer mode is disabled. Enable Developer Mode in Windows Settings, then run this script again. No system policy was changed.'
    }
    throw
}
Write-Host 'Registered Agent Usage. Press Win+W, open Add widgets, and pin Agent Usage.'
