/*
    Navmesh coverage overlay: which ground actually has a baked navmesh.

    Tiles come from the bake cache on disk (133.33yd each, so 4x4 per 533.33yd
    ADT), which is why the layer is populated even before the engine has run a
    query. Tiles also stitched into the live mesh right now are drawn brighter.

    Two layers so the ADT question can be answered at either resolution:
      "Navmesh tiles" - one square per baked navmesh tile
      "Navmesh ADT"   - one square per ADT that has any navmesh, labelled with
                        how many of its 16 tiles are baked
*/

const NAVMESH_GROUP = 'Navmesh';
const NAVMESH_TILE_LAYER = 'Navmesh tiles';
const NAVMESH_ADT_LAYER = 'Navmesh ADT';

const NAVMESH_TILES_PER_ADT = 4;

let navmeshTileGroup = null;
let navmeshAdtGroup = null;

function navmeshRect(minX, minY, maxX, maxY, style) {
    // World bounds -> map. worldTolatLng takes (wowX, wowY).
    return L.rectangle([worldTolatLng(minX, minY), worldTolatLng(maxX, maxY)], style);
}

async function navmeshCoverageInit(continent) {
    navmeshTileGroup = addGroupLayer(NAVMESH_GROUP, NAVMESH_TILE_LAYER);
    navmeshAdtGroup = addGroupLayer(NAVMESH_GROUP, NAVMESH_ADT_LAYER);

    // Off by default - this is a diagnostic overlay, not part of the map.
    addToggleLayer(NAVMESH_GROUP, NAVMESH_TILE_LAYER, navmeshTileGroup, false);
    addToggleLayer(NAVMESH_GROUP, NAVMESH_ADT_LAYER, navmeshAdtGroup, false);

    try {
        await navmeshCoverageRefresh(continent);
    } catch (err) {
        console.error('Failed to load navmesh coverage', err);
    }

    scheduleGroupedLayerControlUpdate();
}

function setNavmeshLayerVisible(group, visible) {
    if (!group || !LeafletMap) {
        return;
    }

    if (visible) {
        if (!LeafletMap.hasLayer(group)) {
            group.addTo(LeafletMap);
        }
    } else if (LeafletMap.hasLayer(group)) {
        LeafletMap.removeLayer(group);
    }
}

function setNavmeshTilesVisible(visible) {
    setNavmeshLayerVisible(navmeshTileGroup, visible);
}

function setNavmeshAdtVisible(visible) {
    setNavmeshLayerVisible(navmeshAdtGroup, visible);
}

async function navmeshCoverageRefresh(continent) {
    const tiles = await fetch(`/api/PPather/NavmeshTiles?continent=${encodeURIComponent(continent)}`)
        .then(r => r.ok ? r.json() : []);

    navmeshTileGroup.clearLayers();
    navmeshAdtGroup.clearLayers();

    if (!tiles.length) {
        console.log('navmesh coverage: no baked tiles for ' + continent);
        return;
    }

    // Per-ADT rollup. A tile index divided by 4 is its ADT, but the indices go
    // negative-ward from the origin, so floor rather than truncate.
    const adts = new Map();

    for (const t of tiles) {
        navmeshTileGroup.addLayer(navmeshRect(t.minX, t.minY, t.maxX, t.maxY, {
            color: t.resident ? '#31d65a' : '#2f7d3f',
            weight: 1,
            fillColor: t.resident ? '#31d65a' : '#2f7d3f',
            fillOpacity: t.resident ? 0.35 : 0.15,
            interactive: false
        }));

        const ax = Math.floor(t.x / NAVMESH_TILES_PER_ADT);
        const az = Math.floor(t.z / NAVMESH_TILES_PER_ADT);
        const key = ax + '_' + az;

        let adt = adts.get(key);
        if (adt === undefined) {
            adt = { ax, az, count: 0, minX: t.minX, minY: t.minY, maxX: t.maxX, maxY: t.maxY };
            adts.set(key, adt);
        }

        adt.count++;
        adt.minX = Math.min(adt.minX, t.minX);
        adt.minY = Math.min(adt.minY, t.minY);
        adt.maxX = Math.max(adt.maxX, t.maxX);
        adt.maxY = Math.max(adt.maxY, t.maxY);
    }

    const full = NAVMESH_TILES_PER_ADT * NAVMESH_TILES_PER_ADT;

    for (const adt of adts.values()) {
        const complete = adt.count >= full;

        const rect = navmeshRect(adt.minX, adt.minY, adt.maxX, adt.maxY, {
            color: complete ? '#31d65a' : '#d6a531',
            weight: 2,
            fill: false,
            dashArray: complete ? null : '6,6'
        });

        rect.bindTooltip(`ADT ${adt.ax},${adt.az} - ${adt.count}/${full} tiles baked`,
            { direction: 'center' });

        navmeshAdtGroup.addLayer(rect);
    }

    console.log(`navmesh coverage: ${tiles.length} tiles across ${adts.size} ADTs`);
}
