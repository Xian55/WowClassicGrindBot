<#
    Runs the Benchmarks project, guarding the stale-DotRecast trap first.

    Modes:
      -Bake                 the 6-tile bake corpus profile (stage table + tile hashes)
      -Throughput           40-tile virgin Barrens corridor
      -Engines <list>       pathing A/B suite against a running PathingAPI
      -Filter <pattern>     a BenchmarkDotNet filter
#>
[CmdletBinding()]
param(
    [switch]$Bake,
    [switch]$Throughput,
    [string]$Label = 'run',
    [int]$Iterations = 2,
    [string]$Engines,
    [string]$BaseUrl = 'http://localhost:5001',
    [string]$Filter,
    [switch]$SkipDllCheck,
    [switch]$Rebuild
)

. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-ProjectRoot
Push-Location $root
try {
    if ($Rebuild) {
        Write-Host '[ppather-profile bench] Rebuilding Benchmarks with a clean DotRecast...'
        Get-ChildItem -Path 'Benchmarks\bin\Release' -Filter 'DotRecast.*.dll' -Recurse -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        & dotnet build Benchmarks -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { Write-Error 'Benchmarks build failed.'; exit $LASTEXITCODE }
    }

    if (-not $SkipDllCheck) {
        if (-not (Test-ReleaseDotRecast)) {
            Write-Error 'Refusing to benchmark against a Debug DotRecast. Re-run with -Rebuild.'
            exit 8
        }
    }

    $args = @()
    if ($Bake) {
        $args = @('--bake-profile', $Label, $Iterations)
        if ($Throughput) { $args += '--throughput' }
    }
    elseif ($Throughput) {
        $args = @('--bake-profile', $Label, 1, '--throughput')
    }
    elseif ($Engines) {
        $args = @('--pather-benchmark', $BaseUrl, $Iterations, $Label, '--engines', $Engines)
    }
    elseif ($Filter) {
        $args = @('--filter', $Filter)
    }
    else {
        Write-Error 'Pick a mode: -Bake, -Throughput, -Engines <list>, or -Filter <pattern>.'
        exit 2
    }

    Write-Host "[ppather-profile bench] dotnet run --project Benchmarks -c Release --no-build -- $($args -join ' ')"
    & dotnet run --project Benchmarks -c Release --no-build -- @args
    $code = $LASTEXITCODE

    Write-Host ''
    Write-Host 'Reports land in local/benchmark_results/. Compare tile hashes across runs:' -ForegroundColor Cyan
    Write-Host '  any optimisation that is not deliberately geometry-changing must keep all 6 identical.'
    exit $code
}
finally {
    Pop-Location
}
