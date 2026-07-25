[CmdletBinding()]
param(
    [int]$Seconds = 30,
    [switch]$Detailed,
    [string]$Target = 'any',
    [int]$TargetPid = 0
)

. (Join-Path $PSScriptRoot '_common.ps1')

Test-Tool dotnet-trace

$procId = Get-PatherPid -TargetPid $TargetPid -Target $Target
$outDir = New-OutputDir
$nettrace = Join-Path $outDir 'trace.nettrace'
$speedscope = Join-Path $outDir 'trace.speedscope.json'

$durationStr = [TimeSpan]::FromSeconds($Seconds).ToString('hh\:mm\:ss')

$collectArgs = @('collect', '-p', $procId, '--duration', $durationStr, '-o', $nettrace)
if ($Detailed) {
    $collectArgs += @('--providers', 'Microsoft-Windows-DotNETRuntime:0x4c14fccbd:5')
} else {
    $collectArgs += @('--profile', 'cpu-sampling')
}

Write-Host "[ppather-profile cpu] pid=$procId duration=$durationStr profile=$(if($Detailed){'detailed'}else{'cpu-sampling'})"
& dotnet-trace @collectArgs
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet-trace collect failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

Write-Host '[ppather-profile cpu] Converting to speedscope JSON...'
& dotnet-trace convert --format speedscope -o $speedscope $nettrace
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet-trace convert failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

Write-Host '[ppather-profile cpu] Done.' -ForegroundColor Green
Write-Host ("  nettrace   : {0}" -f $nettrace)
Write-Host ("  speedscope : {0}" -f $speedscope)
Write-Host 'Open the speedscope.json at https://www.speedscope.app (drag-drop, all client-side).'
