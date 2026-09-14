#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$RemoveAppData,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath($PSScriptRoot)
$artifacts = Join-Path $repo 'artifacts'
$package = Join-Path $artifacts 'package'
$buildInstall = Join-Path $repo 'build-install.ps1'
$backup = Join-Path $artifacts ('removed-package-' + [Guid]::NewGuid().ToString('N'))
$uninstallLock = $null

function Assert-ArtifactPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $boundary = $artifacts + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside artifacts: $resolved"
    }
    # Check ancestors too: artifacts or a parent of the target may be a junction.
    for ($current = $resolved; $current -ne $repo; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to modify a linked path: $current"
        }
    }
}

function Stop-WidgetProcesses {
    foreach ($process in @(Get-Process -Name 'AiUsage.Provider', 'AiUsage.Desktop' -ErrorAction SilentlyContinue)) {
        if ($process.Path -and $process.Path.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $process.Id -Force
            if (!$process.WaitForExit(10000)) { throw "Process $($process.Id) did not stop." }
        }
    }
}

Push-Location $repo
try {
    if (![Environment]::Is64BitProcess) { throw 'Run this script in 64-bit Windows PowerShell.' }
    if (!(Test-Path -LiteralPath $buildInstall)) { throw "Missing build script: $buildInstall" }
    $developerMode = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
    if (!$developerMode -or !$developerMode.PSObject.Properties['AllowDevelopmentWithoutDevLicense'] -or $developerMode.AllowDevelopmentWithoutDevLicense -ne 1) {
        throw 'Enable Windows Developer Mode in Settings, then run this script again.'
    }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $repo 'packaging\AppxManifest.xml') -Raw
    $identity = $manifest.Package.Identity.Name
    Assert-ArtifactPath $package
    Assert-ArtifactPath $backup
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $lockPath = Join-Path $artifacts 'build-install.lock'
    Assert-ArtifactPath $lockPath
    try { $uninstallLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw "Cannot acquire build lock. Another build may be running: $lockPath. $($_.Exception.Message)" }

    $installed = Get-AppxPackage -Name $identity
    if ($installed) {
        if (!$installed.IsDevelopmentMode) {
            throw 'A signed installation already exists. This script only removes development registrations.'
        }
        # Registrations pointing outside this checkout belong to another clone; leave them alone.
        Assert-ArtifactPath $installed.InstallLocation
        Write-Host "Removing the development registration: $($installed.PackageFullName)"
        if ($RemoveAppData) { Write-Warning 'Local app data for this package will be deleted, including saved settings and sign-ins.' }
        Stop-WidgetProcesses
        if ($RemoveAppData) { Remove-AppxPackage -Package $installed.PackageFullName }
        else { Remove-AppxPackage -Package $installed.PackageFullName -PreserveApplicationData }
        if (Get-AppxPackage -Name $identity) { throw 'Package removal verification failed.' }
        Write-Host 'Removed.'
    }
    else {
        Write-Host 'No development registration to remove.'
    }

    # Keep the previous files recoverable instead of deleting them outright.
    if (Test-Path -LiteralPath $package) {
        Stop-WidgetProcesses
        Assert-ArtifactPath $package
        Assert-ArtifactPath $backup
        Move-Item -LiteralPath $package -Destination $backup
        Write-Host "Previous files: $backup"
    }
}
finally {
    if ($uninstallLock) { $uninstallLock.Dispose() }
    Pop-Location
}

Write-Host "Rebuilding and installing ($Configuration)..."
& $buildInstall -Configuration $Configuration
