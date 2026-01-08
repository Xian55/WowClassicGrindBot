$luaFile = "UIMapIDToWorldMapAreaID.lua"    # https://www.townlong-yak.com/framexml/8.1.5/Blizzard_Deprecated/UIMapIDToWorldMapAreaID.lua
$csvFile = "UiMap.8.1.0.27826.csv"          # https://wago.tools/db2/UiMap?build=8.1.0.27826&page=1
$outFile = "WorldMapAreaIDToUiMapID.lua"

# Build UiMapID → Name lookup (if CSV available)
$names = @{}
if (Test-Path $csvFile) {
    Import-Csv $csvFile | ForEach-Object {
        if ($_.Name_lang -and $_.ID) {
            $names[[int]$_.ID] = $_.Name_lang
        }
    }
}

# Parse UiMapID → WorldMapAreaID
$mapping = @{}
Select-String -Path $luaFile -Pattern '^\d+,' | ForEach-Object {
    $cols = $_.Line -split ','
    if ($cols.Count -ge 2) {
        $uiMap = [int]$cols[0]
        $worldMap = [int]$cols[1]
        if ($uiMap -gt 0 -and $worldMap -gt 0) {
            if (-not $mapping.ContainsKey($worldMap) -or $uiMap -lt $mapping[$worldMap]) {
                $mapping[$worldMap] = $uiMap
            }
        }
    }
}

# Write output
@(
    "-- Auto-generated mapping WorldMapAreaID → UiMapID"
    "DataToColor = DataToColor or {}"
    "DataToColor.WorldMapAreaIDToUiMapID = {"
) + ($mapping.GetEnumerator() | Sort-Object Name | ForEach-Object {
    $comment = ""
    if ($names.ContainsKey($_.Value)) {
        $comment = " -- $($names[$_.Value])"
    }
    "    [$($_.Name)] = $($_.Value),$comment"
}) + "}" | Set-Content $outFile -Encoding UTF8

Write-Host "✅ Generated $outFile with $($mapping.Count) entries"
