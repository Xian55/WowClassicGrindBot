[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot '_common.ps1')

$tools = @('dotnet-trace', 'dotnet-counters', 'dotnet-gcdump', 'dotnet-dump')

$installed = & dotnet tool list -g 2>$null

foreach ($t in $tools) {
    if ($installed -match "(?m)^\s*$([regex]::Escape($t))\s") {
        Write-Host "[ppather-profile install] $t already installed" -ForegroundColor DarkGray
        continue
    }

    Write-Host "[ppather-profile install] installing $t ..."
    & dotnet tool install -g $t
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet tool install $t failed (exit $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

Write-Host '[ppather-profile install] Done.' -ForegroundColor Green
Write-Host 'Tools live in ~/.dotnet/tools - make sure that is on PATH (reopen the shell if it was open before installing).'
