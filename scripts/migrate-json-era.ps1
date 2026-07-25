#Requires -Version 5.1
<#
.SYNOPSIS
  Migrate a local Json data folder to the era-partitioned layout introduced by
  the Navmesh Navigation change. Idempotent and safe to re-run.

.DESCRIPTION
  Geometry-derived assets are now grouped by client era (precata/cata/...) instead
  of sitting flat per continent, so pre-cata (MPQ) and cata (CASC) data never
  collide (see DataConfig.ClientEra). This moves your existing flat data down one
  level into `precata/`:

    road/<Continent>            -> road/precata/<Continent>
    PathInfo/navmesh/<Continent>-> PathInfo/navmesh/precata/<Continent>
    PathInfo/<Continent>        -> PathInfo/precata/<Continent>   (legacy V1 cache)

  Dirs already named for an era (precata, cata, ...) are left alone, so re-running
  is a no-op. Leaflet tiles were already era-partitioned - nothing to move there.

  Destination root is taken from DataConfig (data_config.json `Root`), falling
  back to the repo's Json folder, so a customized Root is honored.

.EXAMPLE
  .\migrate-json-era.ps1              # migrate
  .\migrate-json-era.ps1 -DryRun      # show what would move, change nothing
#>
[CmdletBinding()]
param(
    [string]$DataConfig = "",   # explicit data_config.json path
    [switch]$DryRun             # print planned moves only
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-JsonRoot {
    $candidates = @()
    if ($DataConfig) { $candidates += $DataConfig }
    $candidates += (Join-Path $repoRoot "BlazorServer\data_config.json")
    $candidates += (Join-Path $repoRoot "data_config.json")
    foreach ($cfgFile in $candidates) {
        if (Test-Path $cfgFile) {
            $root = (Get-Content $cfgFile -Raw | ConvertFrom-Json).Root
            if ($root) {
                if ([System.IO.Path]::IsPathRooted($root)) { return $root }
                $try = (Resolve-Path (Join-Path (Split-Path $cfgFile) $root) -ErrorAction SilentlyContinue)
                if ($try) { return $try.Path }
            }
        }
    }
    return (Join-Path $repoRoot "Json")
}

# Names that are already era folders (or map to one) - never treated as continents.
$eraNames = @('precata','cata','mop','retail','som','tbc','bcc','wrath','wotlk','vanilla','classic')

function Move-IntoEra {
    param([string]$Parent, [string]$Era = "precata", [string[]]$Keep = @())
    if (-not (Test-Path $Parent)) { return }
    $dst = Join-Path $Parent $Era
    Get-ChildItem -LiteralPath $Parent -Directory | ForEach-Object {
        $name = $_.Name
        if ($name -eq $Era -or $eraNames -contains $name -or $name -like 'legacy_*' -or $Keep -contains $name) {
            return  # already an era folder / reserved - skip
        }
        $target = Join-Path $dst $name
        if (Test-Path $target) { Write-Host "  skip (target exists): $Parent\$name"; return }
        Write-Host "  move: $Parent\$name -> $Era\$name"
        if (-not $DryRun) {
            New-Item -ItemType Directory -Force -Path $dst | Out-Null
            Move-Item -LiteralPath $_.FullName -Destination $target
        }
    }
}

$root = Resolve-JsonRoot
Write-Host "Json root: $root$(if ($DryRun) { '   (dry-run)' })"

Write-Host "road:"
Move-IntoEra -Parent (Join-Path $root "road")
Write-Host "PathInfo\navmesh:"
Move-IntoEra -Parent (Join-Path $root "PathInfo\navmesh")
Write-Host "PathInfo (legacy V1 cache):"
Move-IntoEra -Parent (Join-Path $root "PathInfo") -Keep @('navmesh')

Write-Host "done."
