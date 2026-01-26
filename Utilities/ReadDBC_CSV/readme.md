# ReadDBC_CSV

**som_game_build** = 1.15.4.56493
**tbc_game_build** = 2.5.4.44833
**wrath_game_build** = 3.4.3.52237
**cata_game_build** = 4.4.1.56464

# ReadDBC_CSV_Consumables - What it does
* It generates the available Food and Water consumables list based on the given DBC files.

## ReadDBC_CSV_Consumables - Required DBC files
* data/spell.csv
https://wago.tools/db2/spell/csv?&build=4.4.0.54647

* data/itemeffect.csv
https://wago.tools/db2/itemeffect/csv?&build=4.4.0.54647

## ReadDBC_CSV_Consumables - Produces
* data/foods.json
* data/waters.json


---
# ReadDBC_CSV_Spell - What it does
* It generates the available spell(id, name, level) list based on the given DBC file.

## ReadDBC_CSV_Spell - Required DBC files
* data/spellname.csv
https://wago.tools/db2/spellname/csv?&build=4.4.0.54647

* data/spelllevels.csv
https://wago.tools/db2/spelllevels/csv?&build=4.4.0.54647

## ReadDBC_CSV_Spell - Produces
* data/spells.json


---
# ReadDBC_CSV_Talents - What it does
* It generates the available talents based on the given DBC file.

## ReadDBC_CSV_Talents - Required DBC files
* data/talenttab.csv
https://wago.tools/db2/talenttab/csv?&build=4.4.0.54647

* data/talent.csv
https://wago.tools/db2/talent/csv?&build=4.4.0.54647

## ReadDBC_CSV_Talents - Produces
* data/talent.json
* data/talenttab.json


---
# ReadDBC_CSV_WorldMapArea - What it does
* It generates the WorldMapArea.json list based on the given DBC files.

## SubZoneArea
* Requires continent names (like `0.json`, `1.json`) which contains each tile what zone it bounds to.
* These files can be found under the `json\subzones\EXPANSION` copy these files into `Utilities\ReadDBC_CSV\data` folder.

## ReadDBC_CSV_WorldMapArea - Required DBC files
* data/uimap.csv
https://wago.tools/db2/uimap/csv?&build=4.4.0.54647

* data/uimapassignment.csv
https://wago.tools/db2/uimapassignment/csv?&build=4.4.0.54647

* data/map.csv
https://wago.tools/db2/map/csv?&build=4.4.0.54647

## ReadDBC_CSV_WorldMapArea - Produces
* data/WorldMapArea.json