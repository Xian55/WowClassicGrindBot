[CmdletBinding()]
param(
    [int]$Seconds = 60,
    [string]$Target = 'any',
    [int]$TargetPid = 0,
    [string]$Counters = 'System.Runtime'
)

. (Join-Path $PSScriptRoot '_common.ps1')

Test-Tool dotnet-counters

$procId = Get-PatherPid -TargetPid $TargetPid -Target $Target
$outDir = New-OutputDir
$csv = Join-Path $outDir 'counters.csv'

Write-Host "[ppather-profile counters] pid=$procId seconds=$Seconds counters=$Counters"
Write-Host '  watch: alloc-rate, gc-heap-size, gen-0-gc-count, cpu-usage, threadpool-queue-length'

$job = Start-Job -ScriptBlock {
    param($p, $c, $o)
    & dotnet-counters collect -p $p --counters $c --refresh-interval 1 --format csv -o $o
} -ArgumentList $procId, $Counters, $csv

Start-Sleep -Seconds $Seconds
Stop-Job $job -ErrorAction SilentlyContinue | Out-Null
Receive-Job $job -ErrorAction SilentlyContinue | Out-Null
Remove-Job $job -Force -ErrorAction SilentlyContinue | Out-Null

if (Test-Path -LiteralPath $csv) {
    Write-Host '[ppather-profile counters] Done.' -ForegroundColor Green
    Write-Host ("  csv : {0}" -f $csv)
} else {
    Write-Warning "No CSV produced at $csv - dotnet-counters may need a longer window."
}
