$ErrorActionPreference = 'Stop'

function Get-ProjectRoot {
    $root = & git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
    if (-not $root) {
        return (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
    }
    return $root.Trim()
}

<#
    Finds the process to attach to. Targets are the two that actually do work:
    PathingAPI (interactive pathing, bulk bakes) and Benchmarks (the bake
    profiler / pathing A/B harness).
#>
function Get-PatherPid {
    param(
        [int]$TargetPid = 0,
        [string]$Target = 'any'   # any | PathingAPI | Benchmarks | BlazorServer
    )

    if ($TargetPid -gt 0) { return $TargetPid }

    Test-Tool dotnet-trace

    $lines = & dotnet-trace ps 2>$null
    if (-not $lines) {
        Write-Error 'dotnet-trace ps returned no output - no .NET processes are running.'
        exit 4
    }

    $names = switch ($Target) {
        'PathingAPI'   { @('PathingAPI') }
        'Benchmarks'   { @('Benchmarks') }
        'BlazorServer' { @('BlazorServer') }
        default        { @('PathingAPI', 'Benchmarks', 'BlazorServer') }
    }

    $found = @()
    foreach ($line in $lines) {
        $trim = $line.Trim()
        if (-not $trim) { continue }
        if ($trim -notmatch '^\s*(\d+)\s+(\S+)\s+(.*)$') { continue }

        $procPid = [int]$Matches[1]
        $procName = $Matches[2]
        $cmd = $Matches[3]

        foreach ($n in $names) {
            # Accept the apphost or `dotnet Foo.dll`, but not a shell whose cwd
            # merely happens to sit inside a folder with that name.
            if ($procName -notmatch "^(dotnet|$n)(\.exe)?$") { continue }
            if ($cmd -notmatch "(?i)[\\/\s`"]$n\.(dll|exe)(\s|`"|$)") { continue }

            $found += [pscustomobject]@{ ProcessId = $procPid; Name = $procName; Cmd = $cmd }
            break
        }
    }

    if ($found.Count -eq 0) {
        Write-Error "No $Target process found. Start one first (e.g. `dotnet run --project PathingAPI -c Release`)."
        exit 5
    }
    if ($found.Count -gt 1) {
        Write-Host '[ppather-profile] Multiple candidates found:' -ForegroundColor Yellow
        foreach ($m in $found) { Write-Host ("  pid={0}  name={1}  cmd={2}" -f $m.ProcessId, $m.Name, $m.Cmd) }
        Write-Error 'Pass -TargetPid <int> explicitly to disambiguate.'
        exit 6
    }

    return [int]$found[0].ProcessId
}

function New-OutputDir {
    $root = Get-ProjectRoot
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss')
    $dir = Join-Path $root ("artifacts\profile\" + $stamp)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Test-Tool {
    param([Parameter(Mandatory)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Error "$Name is not installed or not on PATH. Run '/ppather-profile install' first."
        exit 4
    }
}

<#
    Guards the trap that silently invalidates every bake measurement.

    DotRecast is a submodule and is NOT in MasterOfPuppets.sln, so
    `dotnet build MasterOfPuppets.sln -c Release` copies the *Debug* DotRecast
    assemblies into the Benchmarks output. Everything then measures 3-5x slower,
    including stages that were never touched, and the numbers look like a
    catastrophic regression rather than a build problem.

    Compares the DLL in the Benchmarks output against the submodule's own
    Release build. Returns $true when the output is trustworthy.
#>
function Test-ReleaseDotRecast {
    param([switch]$Quiet)

    $root = Get-ProjectRoot
    $out = Get-ChildItem -Path (Join-Path $root 'Benchmarks\bin\Release') -Filter 'DotRecast.Recast.dll' `
        -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $out) {
        if (-not $Quiet) { Write-Host '[ppather-profile] No Benchmarks output yet - build it first.' -ForegroundColor Yellow }
        return $false
    }

    $rel = Join-Path $root 'external\DotRecast\src\DotRecast.Recast\bin\Release\net10.0\DotRecast.Recast.dll'
    $dbg = Join-Path $root 'external\DotRecast\src\DotRecast.Recast\bin\Debug\net10.0\DotRecast.Recast.dll'

    $outLen = $out.Length
    $relLen = if (Test-Path -LiteralPath $rel) { (Get-Item -LiteralPath $rel).Length } else { 0 }
    $dbgLen = if (Test-Path -LiteralPath $dbg) { (Get-Item -LiteralPath $dbg).Length } else { 0 }

    if ($dbgLen -gt 0 -and $outLen -eq $dbgLen -and $relLen -ne $dbgLen) {
        Write-Host ''
        Write-Host '  STALE DEBUG DotRecast IN BENCHMARKS OUTPUT - measurements would be meaningless.' -ForegroundColor Red
        Write-Host ("  output={0} bytes matches the Debug build ({1}), not Release ({2})." -f $outLen, $dbgLen, $relLen)
        Write-Host '  Fix:' -ForegroundColor Yellow
        Write-Host '    rm -f Benchmarks/bin/Release/net10.0*/DotRecast.*.dll'
        Write-Host '    dotnet build Benchmarks -c Release'
        Write-Host ''
        return $false
    }

    if (-not $Quiet) {
        Write-Host ("[ppather-profile] DotRecast.Recast.dll in Benchmarks output: {0:N0} bytes (Release {1:N0}) - OK" -f $outLen, $relLen) -ForegroundColor Green
    }
    return $true
}
