[CmdletBinding()]
param([string]$Target = 'any', [int]$TargetPid = 0)

. (Join-Path $PSScriptRoot '_common.ps1')

Test-Tool dotnet-gcdump

$procId = Get-PatherPid -TargetPid $TargetPid -Target $Target
$outDir = New-OutputDir
$dump = Join-Path $outDir 'trace.gcdump'
$txt = Join-Path $outDir 'trace.gcdump.txt'

Write-Host "[ppather-profile heap] pid=$procId"
& dotnet-gcdump collect -p $procId -o $dump
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet-gcdump collect failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }

& dotnet-gcdump report $dump | Out-File -FilePath $txt -Encoding utf8

Write-Host '[ppather-profile heap] Done.' -ForegroundColor Green
Write-Host ("  gcdump : {0}" -f $dump)
Write-Host ("  report : {0}" -f $txt)
Write-Host 'Expect RcSpan / RcCompactSpan / int[] near the top during a bake - that is the known layout gap vs C++ recast.'
