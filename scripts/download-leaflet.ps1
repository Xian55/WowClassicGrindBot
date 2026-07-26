#Requires -Version 5.1
<#
.SYNOPSIS
  Download pre-generated Leaflet minimap tiles from the CDN into the local cache -
  no WoW client or MPQ extraction needed. Zero dependencies (built-in PowerShell).

.DESCRIPTION
  Destination is taken from DataConfig: reads data_config.json (Root) and writes to
  <Root>/leaflet/<era>, so a customized Root is honored. Layout is era-partitioned
  (precata/cata/mop) exactly like DataConfig.ClientEra groups clients, so one era's
  art never overwrites another's.

  Unlike the navmesh bundles - one zip per continent+settings-hash - leaflet ships a
  single <era>-tiles.zip per era, because the tiles are pure art with no bake
  parameters to vary. That is also why there is no -Continent switch: the whole era
  is one object. Northrend and Outland are absent from the cata bundle on purpose;
  the frontend serves those from precata (see DataConfig.TileEra), so -Era cata
  needs -Era precata too for full coverage.

.EXAMPLE
  .\download-leaflet.ps1                 # every era published on the CDN
  .\download-leaflet.ps1 -Era mop
  .\download-leaflet.ps1 -Force          # re-download even if tiles are present
#>
[CmdletBinding()]
param(
    [string]$Era = "",           # e.g. mop; empty = all published eras
    [string]$DataConfig = "",    # explicit data_config.json path
    [switch]$Force               # re-download even if the cache looks populated
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # ~10x faster Invoke-WebRequest

$Base = "https://bot.tortoiseclothing.org"
$UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"

# Fallback when the CDN has no leaflet/index.json yet. Mirrors
# DataConfig.LeafletEras and the Configs blocks in leaflet-watch.js.
$KnownEras = @("precata", "cata", "mop")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-LeafletDest {
    # data_config.json -> Root -> <Root>/leaflet (honoring DataConfig).
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

    return (Join-Path $dataRoot "leaflet")
}

function Get-RemoteSize([string]$url) {
    # A range GET, NOT a HEAD: R2 answers HEAD on these zips with an empty status
    # in PowerShell, while a 0-0 range returns 206 + Content-Range "bytes 0-0/<total>".
    try {
        $r = Invoke-WebRequest -Uri $url -Method Get -UserAgent $UA `
            -Headers @{ Range = "bytes=0-0" } -ErrorAction Stop
        $cr = $r.Headers['Content-Range']
        if ($cr -and ($cr -join '') -match '/(\d+)\s*$') { return [int64]$Matches[1] }
    }
    catch { }
    return 0
}

$dest = Resolve-LeafletDest
Write-Host "leaflet destination: $dest"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

# Prefer a published index (era -> tiles/bytes) so "already complete" is exact;
# fall back to probing the known era bundles when it is absent.
$entries = @()
try {
    $index = Invoke-RestMethod -Uri "$Base/leaflet/index.json" -UserAgent $UA `
        -Headers @{ "Cache-Control" = "no-cache" } -ErrorAction Stop
    foreach ($e in $index.eras) {
        $entries += [pscustomobject]@{ era = $e.era; zip = $e.zip; tiles = $e.tiles; bytes = $e.bytes }
    }
    Write-Host ("index: {0} era(s) published" -f $entries.Count)
}
catch {
    Write-Host "no leaflet/index.json on the CDN - probing known era bundles"
    foreach ($e in $KnownEras) {
        $size = Get-RemoteSize "$Base/$e-tiles.zip"
        if ($size -gt 0) {
            $entries += [pscustomobject]@{ era = $e; zip = "$e-tiles.zip"; tiles = 0; bytes = $size }
        }
    }
}

if ($Era) { $entries = @($entries | Where-Object { $_.era -eq $Era }) }
if (-not $entries) { throw "no leaflet bundle for era '$Era'. Known: $($KnownEras -join ', ')" }

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($it in $entries) {
    $targetDir = Join-Path $dest $it.era

    $have = 0
    if (Test-Path $targetDir) {
        $have = @(Get-ChildItem $targetDir -Filter *.webp -File -Recurse -ErrorAction SilentlyContinue).Count
    }

    # With an index we can tell "complete" from "partial"; without one, any tiles
    # at all count as present, since re-downloading ~100 MB to top up a few files
    # is the worse default. -Force overrides either way.
    $complete = if ($it.tiles -gt 0) { $have -ge $it.tiles } else { $have -gt 0 }
    if (-not $Force -and $complete) {
        Write-Host ("  {0}: present ({1} tiles), skipping" -f $it.era, $have)
        continue
    }

    $mb = [math]::Round($it.bytes / 1MB)
    $zip = Join-Path $env:TEMP ("leaflet-{0}.zip" -f $it.era)
    Write-Host ("  {0}: downloading {1} MB ..." -f $it.era, $mb)
    Invoke-WebRequest -Uri "$Base/$($it.zip)" -OutFile $zip -UserAgent $UA

    # Extract cannot merge into existing files, so clear the era dir first. This
    # discards locally generated tiles for that era - which is the point of
    # -Force, and the skip above keeps it from happening unasked.
    if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }
    Write-Host "  extracting ..."
    # zip arcnames are <era>/<Continent>/*.webp, so extract at the leaflet root.
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $dest)
    Remove-Item $zip -Force

    $now = @(Get-ChildItem $targetDir -Filter *.webp -File -Recurse -ErrorAction SilentlyContinue).Count
    Write-Host ("  -> {0} ({1} tiles)" -f $targetDir, $now)
}

Write-Host "Done."
