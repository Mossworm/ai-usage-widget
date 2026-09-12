param([switch]$RemoveSettings)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$manifest = Join-Path $projectRoot 'packaging\AppxManifest.xml'

if (-not (Test-Path -LiteralPath $manifest)) {
    throw "Package manifest not found: $manifest"
}

[xml]$manifestXml = Get-Content -LiteralPath $manifest -Raw
$identityName = $manifestXml.Package.Identity.Name
if ([string]::IsNullOrWhiteSpace($identityName)) {
    throw "Package identity is missing from: $manifest"
}

$packages = @(Get-AppxPackage -Name $identityName)
if ($packages.Count -eq 0) {
    Write-Host "AI Usage is not registered for the current user."
} else {
    foreach ($package in $packages) {
        Remove-AppxPackage -Package $package.PackageFullName
        Write-Host "Removed: $($package.PackageFullName)"
    }
}

if ($RemoveSettings) {
    $settingsDirectory = Join-Path $env:LOCALAPPDATA 'AiUsageWidget'
    if (Test-Path -LiteralPath $settingsDirectory) {
        Remove-Item -LiteralPath $settingsDirectory -Recurse -Force
        Write-Host "Removed settings: $settingsDirectory"
    } else {
        Write-Host 'No saved AI Usage settings were found.'
    }
}
