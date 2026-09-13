#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$BuildOnly,
    [switch]$Msix,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath($PSScriptRoot)
$artifacts = Join-Path $repo 'artifacts'
$build = Join-Path $artifacts 'publish'
$stage = Join-Path $build 'package'
$package = Join-Path $artifacts 'package'
$backup = Join-Path $build ('previous-package-' + [Guid]::NewGuid().ToString('N'))
$install = !$BuildOnly -and !$Msix
$buildLock = $null

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE)." }
}

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
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Where-Object { (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) -and (Test-Path (Join-Path $_.FullName 'x64\makepri.exe')) } |
        Select-Object -First 1
    if (!$sdk) { throw 'Install the Windows SDK with makeappx.exe and makepri.exe (x64).' }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $repo 'packaging\AppxManifest.xml') -Raw
    $identity = $manifest.Package.Identity.Name
    $activation = $manifest.SelectSingleNode("//*[local-name()='WidgetProvider']/*[local-name()='Activation']/*[local-name()='CreateInstance']")
    $classId = [Guid]$activation.ClassId
    Assert-ArtifactPath $stage
    Assert-ArtifactPath $package
    Assert-ArtifactPath $backup
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $lockPath = Join-Path $artifacts 'build-install.lock'
    Assert-ArtifactPath $lockPath
    try { $buildLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw "Cannot acquire build lock. Another build may be running: $lockPath. $($_.Exception.Message)" }

    # Recreate only staging; previous installation backups must survive later builds.
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    $installed = $null
    if ($install) {
        $developerMode = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
        if (!$developerMode -or !$developerMode.PSObject.Properties['AllowDevelopmentWithoutDevLicense'] -or $developerMode.AllowDevelopmentWithoutDevLicense -ne 1) {
            throw 'Enable Windows Developer Mode in Settings, then run this script again.'
        }
        $installed = Get-AppxPackage -Name $identity
        if ($installed -and !$installed.IsDevelopmentMode) {
            throw 'A signed installation already exists. This script only replaces development registrations.'
        }
        if ($installed -and $installed.InstallLocation -ne $package) {
            # Older builds used versioned folders within this checkout's artifacts.
            Assert-ArtifactPath $installed.InstallLocation
            if ($installed.InstallLocation.StartsWith($package + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'The existing installation is nested inside artifacts/package. Move its registration before running this script.'
            }
        }
    }

    Write-Host "Building and checking AI Usage ($Configuration)..."
    Invoke-Checked $dotnet @('build', 'AiUsage.slnx', '-c', $Configuration, '--nologo')
    Invoke-Checked $dotnet @('run', '--project', 'tests/AiUsage.Checks', '-c', $Configuration, '--no-build')
    foreach ($app in @('Desktop', 'Provider')) {
        Invoke-Checked $dotnet @('publish', "src/AiUsage.$app/AiUsage.$app.csproj", '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', (Join-Path $stage $app), '--nologo')
    }
    Copy-Item -LiteralPath (Join-Path $repo 'packaging\AppxManifest.xml') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $repo 'packaging\Assets'), (Join-Path $repo 'packaging\Strings') -Destination $stage -Recurse
    $providerAssets = Join-Path $stage 'Provider\Assets'
    New-Item -ItemType Directory -Path $providerAssets -Force | Out-Null
    foreach ($name in @('toggle-on.png', 'toggle-off.png')) {
        $source = Join-Path $repo "packaging\Assets\$name"
        if ((Get-Item -LiteralPath $source).Length -eq 0) { throw "Empty provider asset: $name" }
        Copy-Item -LiteralPath $source -Destination $providerAssets
    }
    # Out-of-process WinRT marshaling needs this metadata at the package root.
    Copy-Item -LiteralPath (Join-Path $stage 'Provider\Microsoft.Windows.Widgets.winmd') -Destination $stage
    New-Item -ItemType Directory -Path (Join-Path $stage 'Public') -Force | Out-Null
    $preview = Start-Process -FilePath (Join-Path $stage 'Desktop\AiUsage.Desktop.exe') -ArgumentList @('--sample', '--render', ('"' + (Join-Path $stage 'Assets') + '"')) -WindowStyle Hidden -PassThru
    try {
        if (!$preview.WaitForExit(30000)) { $preview.Kill(); $preview.WaitForExit(); throw 'Preview rendering timed out.' }
        if ($preview.ExitCode -ne 0) { throw "Preview rendering failed: $($preview.ExitCode)" }
    }
    finally { $preview.Dispose() }
    foreach ($name in @('status-light.png', 'status-dark.png', 'setting-light.png', 'setting-dark.png', 'Logo.png', 'SmallLogo.png', 'StoreLogo.png')) {
        $file = Get-Item -LiteralPath (Join-Path $stage "Assets\$name")
        if ($file.Length -eq 0) { throw "Empty package asset: $name" }
    }
    Write-Host "Packaging (logs: $build)..."
    Invoke-Checked (Join-Path $sdk.FullName 'x64\makepri.exe') @('new', '/pr', $stage, '/cf', (Join-Path $repo 'packaging\priconfig.xml'), '/of', (Join-Path $stage 'resources.pri'), '/o') > (Join-Path $build 'resources.log')
    $stagedMsix = Join-Path $build 'AiUsageWidget.msix'
    Assert-ArtifactPath $stagedMsix
    Invoke-Checked (Join-Path $sdk.FullName 'x64\makeappx.exe') @('pack', '/d', $stage, '/p', $stagedMsix, '/o') > (Join-Path $build 'packaging.log')
    $msixName = '{0}_{1}_{2}.msix' -f $identity, $manifest.Package.Identity.Version, $manifest.Package.Identity.ProcessorArchitecture
    $msixDirectory = Join-Path $artifacts 'msix'
    $msixPath = Join-Path $msixDirectory $msixName
    $latestMsix = Join-Path $artifacts 'AiUsageWidget.msix'
    Assert-ArtifactPath $msixPath
    Assert-ArtifactPath $latestMsix
    New-Item -ItemType Directory -Path $msixDirectory -Force | Out-Null
    Copy-Item -LiteralPath $stagedMsix -Destination $msixPath -Force
    Copy-Item -LiteralPath $stagedMsix -Destination $latestMsix -Force
    Write-Host "Unsigned MSIX package: $msixPath"
    Write-Host "Latest package: $latestMsix"
    if (!$install) {
        Write-Host "Build and checks succeeded: $msixPath"
        return
    }

    Write-Host 'Installing AI Usage for the current Windows user...'
    Assert-ArtifactPath $package
    Assert-ArtifactPath $stage
    Assert-ArtifactPath $backup
    Stop-WidgetProcesses
    $oldMoved = $false
    $newMoved = $false
    try {
        if (Test-Path -LiteralPath $package) {
            Move-Item -LiteralPath $package -Destination $backup
            $oldMoved = $true
        }
        Move-Item -LiteralPath $stage -Destination $package
        $newMoved = $true
        # Windows can keep the old path when the same version is registered elsewhere.
        # The old directory remains intact and can be registered again on failure.
        if ($installed -and $installed.InstallLocation -ne $package) {
            Remove-AppxPackage -Package $installed.PackageFullName
        }
        Add-AppxPackage -Register (Join-Path $package 'AppxManifest.xml') -ForceApplicationShutdown
        $registered = Get-AppxPackage -Name $identity
        if (!$registered -or $registered.Status -ne 'Ok' -or !$registered.IsDevelopmentMode -or $registered.InstallLocation -ne $package) {
            throw 'Package registration verification failed.'
        }
        $provider = $null
        try {
            $provider = [Activator]::CreateInstance([Type]::GetTypeFromCLSID($classId))
            if (!$provider) { throw 'Widget provider activation failed.' }
            Write-Host 'Widget provider activation verified.'
        }
        finally {
            if ($provider -and [Runtime.InteropServices.Marshal]::IsComObject($provider)) {
                [Runtime.InteropServices.Marshal]::ReleaseComObject($provider) | Out-Null
            }
        }
    }
    catch {
        $failure = $_
        try {
            Stop-WidgetProcesses
            Assert-ArtifactPath $package
            Assert-ArtifactPath $stage
            Assert-ArtifactPath $backup
            # Remove a new registration before restoring an earlier registration at another path.
            if ($newMoved -and (!$installed -or $installed.InstallLocation -ne $package)) {
                $partial = Get-AppxPackage -Name $identity
                if ($partial -and $partial.IsDevelopmentMode -and $partial.InstallLocation -eq $package) {
                    Remove-AppxPackage -Package $partial.PackageFullName
                }
            }
            if ($newMoved) { Move-Item -LiteralPath $package -Destination $stage }
            if ($oldMoved) { Move-Item -LiteralPath $backup -Destination $package }
            if ($installed -and $newMoved) {
                Add-AppxPackage -Register (Join-Path $installed.InstallLocation 'AppxManifest.xml') -ForceApplicationShutdown
            }
        }
        catch { Write-Warning "Rollback failed: $($_.Exception.Message). Previous files: $backup. Build files: $build" }
        throw $failure
    }
    Write-Host "Installed successfully: $($registered.PackageFullName)"
    Write-Host 'Open Win + W and add the AI Usage widget if needed.'
    if ($oldMoved) { Write-Host "Previous files: $backup" }
}
finally {
    if ($buildLock) { $buildLock.Dispose() }
    Pop-Location
}
