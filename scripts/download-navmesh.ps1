#Requires -Version 5.1
<#
.SYNOPSIS
  Download pre-baked navmesh tiles from the CDN into the local cache - no WoW
  client or bake needed. Zero dependencies (built-in PowerShell).

.DESCRIPTION
  Destination is taken from DataConfig: reads data_config.json (Root) and writes
  to <Root>/PathInfo/navmesh/<era>/<continent>/<hash>, so a customized Root is
  honored. Layout is era-partitioned (precata/cata/...) - the era comes from the
  CDN index.json, as does the per-continent settings-hash, i.e. it matches the
  DEFAULT agent config. If you run a custom agent config (different hash) you must
  bake instead.

.EXAMPLE
  .\download-navmesh.ps1                 # all continents, all eras
  .\download-navmesh.ps1 -Continent Northrend
  .\download-navmesh.ps1 -Era precata
  .\download-navmesh.ps1 -Force          # re-download even if present
#>
[CmdletBinding()]
param(
    [string]$Continent = "",     # e.g. Northrend; empty = all
    [string]$Era = "",           # e.g. precata; empty = all
    [string]$DataConfig = "",    # explicit data_config.json path
    [switch]$Force               # re-download even if the cache looks complete
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # ~10x faster Invoke-WebRequest

$Base = "https://bot.tortoiseclothing.org"
$UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-NavmeshDest {
    # data_config.json -> Root -> <Root>/PathInfo/navmesh (honoring DataConfig).
    $candidates = @()
    if ($DataConfig) { $candidates += $DataConfig }
    $candidates += (Join-Path (Get-Location) "data_config.json")
    $candidates += (Join-Path $repoRoot "data_config.json")

    $dataRoot = $null
    $cfgFile = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($cfgFile) {
        $root = (Get-Content $cfgFile -Raw | ConvertFrom-Json).Root
        if ($root) {
            if ([System.IO.Path]::IsPathRooted($root)) {
                $dataRoot = $root
            }
            else {
                $cfgDir = Split-Path -Parent (Resolve-Path $cfgFile)
                $try = [System.IO.Path]::GetFullPath((Join-Path $cfgDir $root))
                if (Test-Path $try) { $dataRoot = $try }
            }
        }
    }

    # Fallback: the repo's Json dir (where the relative "..\json" points in dev).
    if (-not $dataRoot) { $dataRoot = Join-Path $repoRoot "Json" }

    return (Join-Path $dataRoot "PathInfo\navmesh")
}

$dest = Resolve-NavmeshDest
Write-Host "navmesh destination: $dest"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Host "fetching index from $Base/navmesh/index.json ..."
$index = Invoke-RestMethod -Uri "$Base/navmesh/index.json" -UserAgent $UA -Headers @{ "Cache-Control" = "no-cache" }

$items = $index.continents
if ($Continent) { $items = @($items | Where-Object { $_.name -eq $Continent }) }
if ($Era) { $items = @($items | Where-Object { $_.era -eq $Era }) }
if (-not $items) { throw "no bundle for '$Continent'/'$Era'. Available: $($index.continents.name -join ', ')" }

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($it in $items) {
    # index entries carry era (precata/cata/...); default precata for an older index.
    $era = if ($it.era) { $it.era } else { "precata" }
    $targetDir = Join-Path $dest ("{0}\{1}\{2}" -f $era, $it.name, $it.hash)

    $have = 0
    if (Test-Path $targetDir) {
        $have = @(Get-ChildItem $targetDir -Filter *.dnm -File -ErrorAction SilentlyContinue).Count
    }
    if (-not $Force -and $have -ge $it.tiles) {
        Write-Host ("  {0}/{1}/{2}: present ({3} tiles), skipping" -f $era, $it.name, $it.hash, $have)
        continue
    }

    $mb = [math]::Round($it.bytes / 1MB)
    $zip = Join-Path $env:TEMP ("navmesh-{0}-{1}-{2}.zip" -f $era, $it.name, $it.hash)
    Write-Host ("  {0}/{1}/{2}: downloading {3} MB ..." -f $era, $it.name, $it.hash, $mb)
    Invoke-WebRequest -Uri "$Base/$($it.zip)" -OutFile $zip -UserAgent $UA

    if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }
    Write-Host "  extracting ..."
    # zip arcnames are <era>/<continent>/<hash>/tile_*.dnm, so extract at the navmesh root.
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $dest)
    Remove-Item $zip -Force
    Write-Host ("  -> {0} ({1} tiles)" -f $targetDir, $it.tiles)
}

Write-Host "Done."
