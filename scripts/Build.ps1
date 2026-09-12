param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'artifacts'
$stage = Join-Path $output 'package'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($name in @('Desktop', 'Provider')) {
    $project = Join-Path $projectRoot "src\AiUsage.$name\AiUsage.$name.csproj"
    $argsList = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', (Join-Path $stage $name))
    if ($SkipRestore) { $argsList += '--no-restore' }
    & dotnet @argsList
    if ($LASTEXITCODE -ne 0) { throw "$name publish failed" }
}
& (Join-Path $PSScriptRoot 'New-Assets.ps1') -Destination (Join-Path $stage 'Assets')
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\AppxManifest.xml') -Destination $stage
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Public'), (Join-Path $stage 'Provider\Assets') | Out-Null
Copy-Item -Path (Join-Path $stage 'Assets\toggle-*.png') -Destination (Join-Path $stage 'Provider\Assets')
$exe = Join-Path $stage 'Desktop\AiUsage.Desktop.exe'
$process = Start-Process -FilePath $exe -ArgumentList @('--sample', '--render', ('"' + (Join-Path $stage 'Assets') + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw 'Preview rendering failed' }
$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows SDK makeappx.exe is required.' }
& (Join-Path $sdk.FullName 'x64\makeappx.exe') pack /d $stage /p (Join-Path $output 'AiUsageWidget.msix') /o | Out-File (Join-Path $output 'packaging.log')
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed' }
Write-Host "Built: $output\AiUsageWidget.msix"
