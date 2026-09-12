param([switch]$Sample)
$projectRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $projectRoot 'artifacts\package\Desktop\AiUsage.Desktop.exe'
if (Test-Path $exe) {
    if ($Sample) { Start-Process $exe -ArgumentList '--sample' } else { Start-Process $exe }
} else {
    $argsList = @('run', '--project', (Join-Path $projectRoot 'src\AiUsage.Desktop'))
    if ($Sample) { $argsList += @('--', '--sample') }
    & dotnet @argsList
}
