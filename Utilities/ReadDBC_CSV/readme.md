# ReadDBC_CSV

A CLI tool to extract WoW DBC data from [wago.tools](https://wago.tools) and generate JSON files for WowClassicGrindBot.

## Usage

```
ReadDBC_CSV [options] [extractors...]
```

### Options

| Option | Description |
|--------|-------------|
| `-v, --version <version>` | Game version (default: som) |
| `-b, --build <build>` | Override build version (e.g., 3.4.3.54261) |
| `-c, --clean` | Clean downloaded CSV files before running |
| `-h, --help` | Show help |

### Supported Versions

| Version | Build | Description |
|---------|-------|-------------|
| `som` | 1.15.8.63829 | Season of Discovery / Classic Era (default) |
| `tbc` | 2.5.4.44833 | TBC Classic |
| `wrath` | 3.4.5.63697 | WotLK Classic |
| `cata` | 4.4.2.60895 | Cataclysm Classic |
| `mop` | 5.5.4.68806 | MoP Classic |
| `legacy_cata` | 8.1.0.27826 | Legacy Cataclysm data |
| `legacy_mop` | 5.5.4.68806 | Legacy MoP data (sourced from MoP Classic) |

### Why the `legacy_*` builds point at a re-release

wago.tools does not carry the original 2010/2012 clients. The addons are written against the
modern Classic clients and bridge the gap for legacy ones through
`Addons/DataToColor/Legacy/WorldMapAreaIDToUiMapID.lua`, which maps a legacy client's
`WorldMapAreaID` onto a modern `UiMapID`. So the generated `worldmaparea.json` has to be keyed
by **UiMapIDs that agree with that table**, which means sourcing it from a build that has
`UiMap` at all.

`legacy_mop` uses the MoP Classic build rather than following `legacy_cata`'s 8.1.0: it is the
same expansion's content, and it verifies clean against the mapping table (261 zone names
match, 2 cosmetic differences - `Tol Barad`/`Battle for Tol Barad`,
`Scarlet Monastery`/`Forlorn Cloister`). All Pandaria zones come through on map 870
(`HawaiiMainLand`) with real bounds. Of the 179 mapped UiMapIDs it does not cover, 176 are
Warlords-or-later zones a 5.4.8 client cannot reach; the three that predate Warlords are
`Undercity` (90), `Outland` (101) and `Dire Maul` (234) - the last is missing from
`legacy_cata` as well.

> **`legacy_cata`'s 8.1.0.27826 no longer regenerates cleanly.** wago.tools now answers `200`
> with an **empty body** for `Map`, `SpellName` and `ManifestInterfaceData` at that build, and
> `404`s `TalentTab`, which silently guts the worldmap, spell, icon and talent output. The
> committed `Json/dbc/legacy_cata/` data predates that. If it ever needs refreshing, switch it
> to the Cata Classic build (`4.4.2.60895`) and re-check the ids against the mapping table.
> The downloader now refuses to cache an empty response instead of failing later with
> `The given key 'ID' was not present in the dictionary`.

### Extractors

| Name | Output Files | Description |
|------|--------------|-------------|
| `faction` | factiontemplate.json | Faction template data |
| `item` | items.json | Item data (id, name, quality, sell price, texture) |
| `consumable` | foods.json, waters.json | Food and drink item IDs |
| `spell` | spells.json | Spell data (id, name, level) |
| `icon` | spelliconmap.json, iconnames.json | Icon texture mappings (all icons from Interface\Icons) |
| `talent` | talents.json | Talent tree data |
| `worldmap` | worldmaparea.json | World map area data |

If no extractors are specified, all will run.

## Examples

```bash
# Run all extractors for default version (SoM)
ReadDBC_CSV

# Run all extractors for WotLK
ReadDBC_CSV -v wrath

# Run all extractors for WotLK with a specific build
ReadDBC_CSV -v wrath -b 3.4.3.54261

# Run only item and consumable extractors for SoM
ReadDBC_CSV item consumable

# Run spell and talent extractors for TBC
ReadDBC_CSV -v tbc spell talent

# Clean CSV cache and run item extractor
ReadDBC_CSV --clean item
```

## Output

Generated JSON files are automatically copied to:
```
WowClassicGrindBot/Json/dbc/{version}/
```

Downloaded CSV files are cached in:
```
Utilities/ReadDBC_CSV/data/
```

Use `--clean` to remove cached CSVs when switching between versions or builds.

---

## Extractor Details

### Item Extractor

Extracts item data including texture IDs for action bar detection.

**Required CSV files:**
- itemsparse.csv - Item details (name, quality, sell price)
- item.csv - Item icons (IconFileDataID)

**Produces:**
- items.json

### Consumables Extractor

Generates food and water item ID lists based on spell descriptions.

**Required CSV files:**
- spell.csv
- itemeffect.csv

**Produces:**
- foods.json
- waters.json

### Spell Extractor

Extracts spell data (id, name, level).

**Required CSV files:**
- spellname.csv
- spelllevels.csv

**Produces:**
- spells.json

### Icon Extractor

Extracts all icons from Interface\Icons and builds texture ID to spell ID mappings for action bar slot detection.

**Required CSV files:**
- spellmisc.csv
- spellname.csv
- manifestinterfacedata.csv

**Produces:**
- spelliconmap.json - Maps texture ID to spell IDs (for spell validation)
- iconnames.json - Maps texture ID to icon names (ALL icons from Interface\Icons, ~31,000+)

### Talent Extractor

Extracts talent tree data.

**Required CSV files:**
- talenttab.csv
- talent.csv

**Produces:**
- talents.json

### World Map Area Extractor

Extracts world map area data with zone boundaries for minimap click-to-move functionality.

**Required CSV files:**
- uimap.csv
- uimapassignment.csv
- map.csv
- areatable.csv

**Produces:**
- worldmaparea.json

#### Subzone Data (Optional but Recommended)

The extractor can extend zone data with subzone boundaries from pre-generated subzone files. These files contain tile-based zone boundaries used for precise click-to-move targeting.

**Subzone files location:**
```
WowClassicGrindBot/Json/subzones/{expansion}/
```

**File format:** Numeric filenames representing continent IDs:
- `0.json` - Eastern Kingdoms
- `1.json` - Kalimdor
- `530.json` - Outland (TBC)
- `571.json` - Northrend (WotLK)
- etc.

**To use subzones:**
1. Copy the appropriate subzone files from `Json/subzones/{expansion}/` to `Utilities/ReadDBC_CSV/data/`
2. Run the worldmap extractor
3. The extractor automatically detects and merges any `*.json` files with numeric-only names

**Example:**
```bash
# Copy subzone data for Classic
cp Json/subzones/vanilla/*.json Utilities/ReadDBC_CSV/data/

# Run the extractor
ReadDBC_CSV -v som worldmap
```

The subzone data adds clickable area entries (prefixed with `C_`) that provide more precise zone boundaries than the DBC data alone.

### Faction Template Extractor

Extracts faction template data.

**Required CSV files:**
- factiontemplate.csv

**Produces:**
- factiontemplate.json
