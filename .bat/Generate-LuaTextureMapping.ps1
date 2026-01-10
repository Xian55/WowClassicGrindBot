param(
    [Parameter(Mandatory = $true)]
    [string]$InputCsvPath = "community-listfile.csv",   # https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv
    [string]$OutputLuaPath = "LegacyTextureToFileID.lua",
    [switch]$OnlyIcons = $true                          # keep only interface/icons/*
)

Write-Host "Reading $InputCsvPath ..."
$rows = Get-Content -Raw -Path $InputCsvPath -ErrorAction Stop |
        ConvertFrom-Csv -Delimiter ';' -Header 'FileID','Path'

# Filter: remove .meta and optionally only icons
$filtered = $rows | Where-Object {
    $_.Path -and
    $_.Path -notmatch '\.meta$' -and
    (-not $OnlyIcons -or $_.Path -match '^interface/icons/')
}

Write-Host "Found $($filtered.Count) candidate rows."

# Start Lua file
@(
    "-- Auto-generated from wowdev community-listfile.csv",
    "-- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "",
    "DataToColor = DataToColor or {}",
    "DataToColor.LegacyTextureToFileID = {"
) | Set-Content -Path $OutputLuaPath -Encoding UTF8

$seen = [System.Collections.Generic.HashSet[string]]::new()

foreach ($row in $filtered) {
    $fileID = [int]$row.FileID
    if ($fileID -le 0) { continue }

    # normalize
    $p = $row.Path.Trim().ToLower()
    $p = $p -replace '\.blp$', ''
    $p = $p -replace '/', '\\'

    # only keep ones starting with interface\
    if ($p -notmatch '^interface\\') { continue }

    # ensure consistent key
    $key = $p

    # avoid duplicates
    if ($seen.Add($key)) {
        $line = '    ["{0}"] = {1},' -f $key, $fileID
        Add-Content -Path $OutputLuaPath -Value $line -Encoding UTF8
    }
}

Add-Content -Path $OutputLuaPath -Value "}" -Encoding UTF8
Write-Host "✅ Done! Wrote $OutputLuaPath"
