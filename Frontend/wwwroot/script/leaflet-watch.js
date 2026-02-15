// 512
const Configs = {
    'som': {
        'Azeroth': {
            resX: 10752,
            resY: 21504,
            maxZoom: 6,
            MapID: 0,
            offset: {
                min: { x: 20, y: 24 },
            }
        },
        'Kalimdor': {
            resX: 15360,
            resY: 24064,
            maxZoom: 6,
            MapID: 1,
            offset: {
                min: { x: 9, y: 19 },
            }
        }
    },
    'tbc': {
        'Azeroth': {
            resX: 10752,
            resY: 21504,
            maxZoom: 6,
            MapID: 0,
            offset: {
                min: { x: 20, y: 24 },
            }
        },
        'Kalimdor': {
            resX: 15360,
            resY: 24064,
            maxZoom: 6,
            MapID: 1,
            offset: {
                min: { x: 9, y: 19 },
            }
        },
        'Expansion01': {
            resX: 25088,
            resY: 19968,
            maxZoom: 6,
            MapID: 530,
            offset: {
                min: { x: 6, y: 12 },
            }
        }
    },
};

const aSize = 32;

const AreaIDOffset = 1000000;

var areaCache = {};
var WMADB = {};
var Zones = {};
var SubZones = {};
var creatures = {};
var spawnLocations = {};
let factionTemplates = {};

const clickableSprites = new Set();

let savedLayerVisibility = null;
const activatedPOICategories = new Set();

const skinnableExclude = {
    21: true,       // Ram
    721: true,      // Rabiit
    883: true,      // Deer
    890: true,      // Fawn
    1933: true,     // Sheep
    2442: true,     // Cow
    2620: true,     // Prairie dog  
    5951: true,     // Hare
    7846: true,     // Teremus the Devourer
    8759: true,     // Mosshoof Runner
    12298: true,    // Sickly Deer
};

const raceNames = {
    'Human': true,
    'Dwarf': true,
    'NightElf': true,
    'Gnome': true,
    'Orc': true,
    'Troll': true,
    'Tauren': true,
    'Undead': true,
    'Draenei': true,
    'BloodElf': true,
    'Goblin': true,
    'Worgen': true,
    'Pandaren': true
}

const creatureTypeNames = {
    1: 'Beast', 2: 'Dragonkin', 3: 'Demon', 4: 'Elemental',
    5: 'Giant', 6: 'Undead', 7: 'Humanoid', 8: 'Critter',
    9: 'Mechanical', 11: 'Totem'
};

const creatureFamilyNames = {
    1: 'Wolf', 2: 'Cat', 3: 'Spider', 4: 'Bear', 5: 'Boar',
    6: 'Crocolisk', 7: 'Carrion Bird', 8: 'Crab', 9: 'Gorilla',
    11: 'Raptor', 12: 'Tallstrider', 15: 'Felhunter', 16: 'Voidwalker',
    17: 'Succubus', 20: 'Scorpid', 21: 'Turtle', 24: 'Bat',
    25: 'Hyena', 26: 'Bird of Prey', 27: 'Wind Serpent'
};

const raceToZone = {
    'Human': 12,
    'Dwarf': 1,
    'NightElf': 141,
    'Gnome': 1,
    'Orc': 14,
    'Troll': 14,
    'Tauren': 215,
    'Undead': 85,

    // todo fix below
    'Draenei': 21,
    'BloodElf': 22,

    'Goblin': 23,
    'Worgen': 24,
    'Pandaren': 25
}

var maxZoom = 10;

var expansion = "som";
var continent = 'Azeroth';
var startZoom = 4;

var baseUrl = "https://www.wowhead.com/classic";

var config;

var enableUrlEdit = false;

var maxSize;
var mapSize;
var adtSize;
var blpSize = 512;
var tileSize = 256;

var ADTEnabled = false;
const ADTGridLayer = new L.LayerGroup();
const ADTGridTextLayer = new L.LayerGroup();

const editableLayers = new L.FeatureGroup();

var playerLayer;

var recordPlayerPath = false;
var currentRecordPlayerPath = '';

var layerNames = {};

var LeafletMap;
var pixiOverlay;
var pixiContainer;
var currentResizeObserver;

var currentArea;
var lastRenderArea;

var editablePathLayerControl;

var groupedLayerControls = [];
var groupedOverlays = {
    "Zones": {},
    "Paths": {},
};

let redrawScheduled = false;

function schedulePixiRedraw() {
    if (redrawScheduled || !pixiOverlay) return;

    redrawScheduled = true;
    requestAnimationFrame(() => {
        pixiOverlay.redraw();
        redrawScheduled = false;
    });
}

async function getDBC(database) {
    const url = `/dbc/${database}.json`;

    return fetch(url)
        .then(r => r.json())
        .catch(e => console.error(e));
}

async function getNpcSpawnLocations(mapId) {
    const url = `/npcspawnlocations/${mapId}.json`;

    return fetch(url)
        .then(r => r.json())
        .catch(e => console.error(e));
}

function filterContientsAndInvalid(db, mapID) {
    return db.filter(x => x.MapID == mapID &&
        x.AreaName != "Eastern Kingdoms" &&
        x.AreaName != "Azeroth" &&
        x.AreaName != "Kalimdor" &&
        x.AreaName != "Hyjal" &&
        x.AreaName != "Outland"
    );
}

function createZoneLookup(db) {
    const lookup = {};
    for (const area of db) {
        if (area.ParentAreaId === 0) {
            lookup[area.AreaID] = area;
        }
    }
    return lookup;
}

function createSubZoneLookup(db) {
    const lookup = {};
    for (const subArea of db) {
        if (subArea.ParentAreaId !== 0) {
            lookup[subArea.AreaID] = subArea;
        }
    }
    return lookup;
}

function getZone(areaId) {
    const subZone = SubZones[areaId];
    return subZone
        ? Zones[subZone.ParentAreaId] ?? null
        : Zones[areaId] ?? null;
}

function getSubZones(areaId) {
    const subZones = [];
    for (const subZone of Object.values(SubZones)) {
        if (subZone.ParentAreaId === areaId) {
            subZones.push(subZone);
        }
    }
    return subZones;
}

function createCreaturesLookup(db) {
    const lookup = {};
    for (const entry of db) {
        lookup[entry.Entry] = entry;
    }
    return lookup;
}

var npcFlags = {};

function getCreatureByFlag(flag, excludedFactions = []) {
    return Object.values(creatures).filter(c => {
        const hasFlag = (c.NpcFlag & flag) !== 0;
        const isExcluded = excludedFactions.includes(c.Faction);
        return hasFlag && !isExcluded;
    });
}

async function loadFactionTemplates() {
    const db = await getDBC("factiontemplates");
    if (!db) return;
    for (const entry of db) {
        factionTemplates[entry.Id] = entry.FriendGroup;
    }
}

function getCreatureByFlagFiltered(flag, factionFilter) {
    return getCreatureByFlag(flag, []).filter(c => {
        if (factionFilter === 'both') return true;
        const friendGroup = factionTemplates[c.Faction];
        if (friendGroup === undefined) return true;
        if (factionFilter === 'alliance') return (friendGroup & 2) !== 0;
        if (factionFilter === 'horde') return (friendGroup & 4) !== 0;
        return true;
    });
}

function searchCreaturesByName(query, factionFilter, zoneId, typeFamily = null) {
    const hasQuery = query && query.length >= 2;
    if (!hasQuery && !typeFamily) return [];

    const lowerQuery = hasQuery ? query.toLowerCase() : null;
    const results = [];
    const targetArea = zoneId ? Zones[zoneId] : null;
    const subZones = zoneId ? getSubZones(zoneId) : [];

    let filterKey = null;
    let filterValue = 0;
    if (typeFamily) {
        const parts = typeFamily.split(':');
        filterKey = parts[0];
        filterValue = parseInt(parts[1], 10);
    }

    for (const creature of Object.values(creatures)) {
        if (filterKey) {
            if (filterKey === 'type' && creature.Type !== filterValue) continue;
            if (filterKey === 'family' && creature.Family !== filterValue) continue;
        }

        if (lowerQuery) {
            if (!creature.Name || !creature.Name.toLowerCase().includes(lowerQuery)) continue;
        }

        if (factionFilter !== 'both') {
            const friendGroup = factionTemplates[creature.Faction];
            if (friendGroup !== undefined) {
                if (factionFilter === 'alliance' && (friendGroup & 2) === 0) continue;
                if (factionFilter === 'horde' && (friendGroup & 4) === 0) continue;
            }
        }

        if (targetArea) {
            const spawns = spawnLocations[creature.Entry];
            if (!spawns) continue;
            const worldPos = spawns[0];
            let inZone = contains(targetArea, worldPos);
            if (!inZone) {
                const hitboxArea = Zones[zoneId + AreaIDOffset];
                if (hitboxArea) inZone = contains(hitboxArea, worldPos);
            }
            if (!inZone) {
                for (const sub of subZones) {
                    if (contains(sub, worldPos)) { inZone = true; break; }
                }
            }
            if (!inZone) continue;
        }

        results.push(creature);
        if (results.length >= 50) break;
    }
    return results;
}

function copyClipboardOnClick(element) {
    element.onclick = (e) => {
        e.preventDefault();
        e.stopPropagation();

        const text = element.innerText;
        navigator.clipboard.writeText(text).then(() => {
            //console.log('Text copied to clipboard:', text);
            element.classList.add('flash');

            setTimeout(() => {
                element.classList.remove('flash');
            }, 300);

        }).catch(err => {
            console.error('Error copying text: ', err);
        });
    };
}

async function addCoordinates(worldPos) {
    const response = await getAreaIdAndZFromService(worldPos);
    const areaId = response.areaId;
    const zPos = worldPos.z ?? response.z;

    const map = worldToPercentage(worldPos, areaId);

    return `
    <br/><span class="copy-coords">${map.p.x} ${map.p.y}</span>
    <br/><span class="copy-coords">${worldPos.x.toFixed(2)} ${worldPos.y.toFixed(2)} ${zPos.toFixed(2)} ${config.MapID}</span>
    `;
}

function bindPopupEnrichCoordinates(marker, worldPos, text) {
    marker.on('click', async (e) => {
        const html = `${text}${await addCoordinates(worldPos)}`;
        marker.bindPopup(html).openPopup();

        setTimeout(() => {
            const popupEl = marker.getPopup().getElement();
            const spans = popupEl.querySelectorAll('.copy-coords');
            spans.forEach(span => {
                copyClipboardOnClick(span);
            });
        }, 10);
    });
}

async function showPixiPopup(worldPos, latlng, baseHtml, sprite = null) {
    // Prevent multiple popups on the same marker
    if (sprite && sprite.popup && LeafletMap.hasLayer(sprite.popup)) {
        sprite.popup.bringToFront();
        return;
    }

    const popupId = `popup-${Date.now()}-${Math.floor(Math.random() * 1000)}`;
    const loadingId = `${popupId}-loading`;

    const popup = L.popup({
        autoClose: false,
        closeOnClick: false,
        closeButton: true,
        className: 'pixi-tooltip'
    })
        .setLatLng(latlng)
        .setContent(`
            <div id="${popupId}">
                ${baseHtml}
                <br/><em id="${loadingId}">Loading coordinates...</em>
            </div>
        `)
        .openOn(LeafletMap);

    if (sprite) {
        sprite.popup = popup;
    }

    requestAnimationFrame(() => {
        const popupEl = document.getElementById(popupId);
        if (!popupEl) return;
        popupEl.querySelectorAll('.copy-coords').forEach(copyClipboardOnClick);
    });

    const enriched = await addCoordinates(worldPos);

    const popupEl = document.getElementById(popupId);
    if (popupEl) {
        const loadingEl = document.getElementById(loadingId);
        if (loadingEl) loadingEl.remove();
        popupEl.insertAdjacentHTML('beforeend', enriched);
        popupEl.querySelectorAll('.copy-coords').forEach(copyClipboardOnClick);
    }
}


function getFilePathFileName() {
    const currentAreaName = currentArea.AreaName;
    const currentDate = new Date().toISOString().replace(/[-:T]/g, '_').split('.')[0];
    return `${currentAreaName}_${currentDate}.json`;
}

function setBaseUrl(e) {
    switch (e) {
        case 'som':
            baseUrl = "https://classic.wowhead.com";
            break;
        case 'tbc':
            baseUrl = "https://tbc.wowhead.com";
            break;
        case 'wrath':
            baseUrl = "https://www.wowhead.com/wotlk";
            break;
        case 'cata':
            baseUrl = "https://www.wowhead.com/cata";
            break;
        default:
            baseUrl = "https://www.wowhead.com/";
            break;
    }
}

function initializeMap(x, y) {
    return new L.map('js-map', {
        center: [x, y],
        zoom: startZoom,
        minZoom: 2,
        maxZoom: maxZoom,
        crs: L.CRS.Simple,
        zoomControl: false,
        preferCanvas: true
    });
}

function disposeMap() {
    if (LeafletMap && Object.keys(layerNames).length > 0) {
        savedLayerVisibility = new Map();
        for (const name in layerNames) {
            savedLayerVisibility.set(name, LeafletMap.hasLayer(layerNames[name]));
        }
    }

    if (currentResizeObserver) {
        currentResizeObserver.disconnect();
        currentResizeObserver = null;
    }

    if (pixiOverlay) {
        if (LeafletMap && LeafletMap.hasLayer(pixiOverlay)) {
            pixiOverlay.remove();
        }
        pixiOverlay = null;
    }

    if (pixiContainer) {
        pixiContainer.removeChildren();
    }

    clickableSprites.clear();

    editableLayers.clearLayers();
    ADTGridLayer.clearLayers();
    ADTGridTextLayer.clearLayers();

    playerLayer = null;

    if (LeafletMap) {
        LeafletMap.off();
        LeafletMap.remove();
        LeafletMap = null;
    }

    for (const control of groupedLayerControls) {
        try { control.remove(); } catch (_) { }
    }
    groupedLayerControls = [];
    sidebarLayersColumn = null;

    groupedOverlays = {
        "Zones": {},
        "Paths": {},
    };

    layerNames = {};

    enableUrlEdit = false;
    ADTEnabled = false;
    recordPlayerPath = false;

    currentArea = undefined;
    lastRenderArea = undefined;
}

async function init(e, c, z, x, y, urlEdit, flags) {

    npcFlags = flags;

    disposeMap();

    expansion = e;
    enableUrlEdit = urlEdit;

    // currently only som is supported
    if (expansion !== 'som' && expansion !== 'tbc') {
        return;
    }

    setBaseUrl(expansion);

    continent = c;
    startZoom = z;

    config = Configs[expansion][continent];

    maxSize = Math.max(config.resX, config.resY);
    const multi = 17066.66666666667 / maxSize;
    maxSize = maxSize * multi;      //
    mapSize = maxSize * 2;          //34133,33333333333
    adtSize = mapSize / 64; 	    //533,3333333333333

    LeafletMap = initializeMap(x, y);

    if (!pixiContainer) {
        pixiContainer = new PIXI.Container();
    }
    pixiContainer.interactive = true; // important
    pixiContainer.interactiveChildren = true; // ensures its children (your sprites) are interactive

    let firstDraw = true;
    let prevZoom;

    //pixiContainer = new PIXI.Container();
    pixiOverlay = new L.PixiOverlay((utils) => {
        const zoom = utils.getMap().getZoom();
        const scaleFactor = utils.getMap().getZoomScale(6, zoom);
        const container = utils.getContainer();
        const renderer = utils.getRenderer();

        for (const sprite of container.children) {
            if (!sprite.visible) continue;

            const projected = utils.latLngToLayerPoint(sprite.latlng);
            sprite.x = projected.x;
            sprite.y = projected.y;

            if (sprite instanceof PIXI.Text) {
                const textZoomFactor = Math.min(1.5, Math.max(0.5, scaleFactor * 1.2));
                sprite.scale.set(textZoomFactor);
            } 
            if (sprite.interactive) {
                sprite.scale.set(aSize / sprite.texture.width * scaleFactor);
            }
            else {
                sprite.scale.set(aSize / sprite.texture.width * Math.min(scaleFactor, 3) * 3);
                // TODO: FIX bitmap rendered subzone texts as sprites
                //const textZoomFactor = Math.min(1.5, Math.max(0.5, scaleFactor * 1.2));
                //sprite.scale.set(textZoomFactor);
            }

            //if (sprite.interactive) {
            //    const size = aSize * scaleFactor;
            //    sprite.hitArea = new PIXI.Rectangle(-size/2, -size/2, size, size);
            //} 
        }


        if (firstDraw) {
            // set coordinates
        }

        if (firstDraw || prevZoom !== zoom) {
            // update by scale
        }

        firstDraw = false;
        prevZoom = zoom;
        renderer.render(pixiContainer);
    }, pixiContainer,
        {
            destroyInteractionManager: false,
            autoPreventDefault: false
        }
    );
    pixiOverlay.addTo(LeafletMap);

    const southWest = LeafletMap.unproject([0, config.resY], config.maxZoom);
    const northEast = LeafletMap.unproject([config.resX, 0], config.maxZoom);
    const mapBounds = new L.LatLngBounds(southWest, northEast);

    LeafletMap.setMaxBounds(mapBounds);

    LeafletMap.doubleClickZoom.disable();

    L.tileLayer('tiles/' + continent + '/z{z}x{x}y{y}.png', {
        maxZoom: maxZoom,
        maxNativeZoom: config.maxZoom,
        continuousWorld: true,
        tileSize: tileSize,
        bounds: mapBounds,
        zoomOffset: 0,
        noWrap: true
    }).addTo(LeafletMap);

    WMADB = await getDBC("WorldMapArea");
    WMADB = WMADB.sort((a, b) => a.AreaID - b.AreaID);
    WMADB = filterContientsAndInvalid(WMADB, config.MapID);
    Zones = createZoneLookup(WMADB);
    SubZones = createSubZoneLookup(WMADB);

    let creaturesFile = await getDBC("creatures");
    creatures = createCreaturesLookup(creaturesFile);

    spawnLocations = await getNpcSpawnLocations(config.MapID);

    await loadFactionTemplates();

    //setBoundToArea(12);

    await load();

    //editablePathLayerControl = L.control.layers(null, null, { collapsed: true, autoZIndex: false }).addTo(LeafletMap);

    if (enableUrlEdit) {
        LeafletMap.addLayer(editableLayers);
        LeafletMap.addControl(drawControl);
    }

    // Sidebar is always visible regardless of enableUrlEdit
    createSidebar().addTo(LeafletMap);

    LeafletMap.on(L.Draw.Event.CREATED, function (e) {
        var type = e.layerType;
        var layer = e.layer;

        layer.options.color = '#ff0000';

        // every drawn layer is added to the editableLayers
        editableLayers.addLayer(layer);
    });

    LeafletMap.on(L.Draw.Event.EDITED, function (e) {
        const layers = e.layers.getLayers();
        for (const layer of layers) {
            if (layer.groupName !== undefined && layer.PathName !== undefined) {
                editableLayers.removeLayer(layer);
                addGroupLayer('Paths', 'Paths');
                assignLayer(layer.groupName, layer.PathName, layer, true);

                flashLayer(layer);
            }
            else {

                // currently only polyline Path supported
                if (!(layer instanceof L.Polyline) || (layer instanceof L.Polygon)) {
                    console.warn("Layer is not a polyline or polygon");
                    continue;
                }

                editableLayers.removeLayer(layer);

                const path = getFilePathFileName();

                addGroupLayer('Paths', 'Paths');

                bindRightClick(layer, 'Paths', path);

                flashLayer(layer);

                addToggleLayer('Paths', layer.PathName, layer, true);
                scheduleGroupedLayerControlUpdate();
            }
        }
    });

    LeafletMap.on('moveend zoomend dragend', function () {

        if (enableUrlEdit) {
            synchronizeTitleAndURL();
        }

        refreshADTGrid();
    });

    LeafletMap.on('click', function (e) {
        if (!handleSpriteClick(e)) {
            processOffsetClick(e);
        }
    });


    const mapContainer = document.getElementById('js-map');

    currentResizeObserver = new ResizeObserver(() => {
        requestAnimationFrame(() => {
            if (!LeafletMap || !pixiOverlay) return;
            LeafletMap.invalidateSize();
            pixiOverlay.redraw();
        })
    });

    currentResizeObserver.observe(mapContainer);

    const adtClick = document.getElementById("adtClick");
    if (adtClick != null)
        copyClipboardOnClick(adtClick);

    const worldClick = document.getElementById("worldClick");
    if (worldClick != null)
        copyClipboardOnClick(worldClick);

    const mapClick = document.getElementById("mapClick");
    if (mapClick != null)
        copyClipboardOnClick(mapClick);

    scheduleGroupedLayerControlUpdate();

    //testdrawIconAtlasPixi();
}

async function updateArea(areaId) {
    if (!LeafletMap) return;

    const area = Zones[areaId];
    if (area == null) {
        return;
    }

    if (currentArea != area) {
        setBoundToArea(areaId);

        if (enableUrlEdit) {
            await loadMapPathByFilter(area.AreaName);
            requestAnimationFrame(() => {
                scheduleGroupedLayerControlUpdate();
            });
        }
    }

    currentArea = area;
    updateSidebarCurrentZone();

    await replayActivatedPOICategories();
}

async function replayActivatedPOICategories() {
    if (activatedPOICategories.size === 0) return;

    const zoneId = getSidebarZoneId();
    const factionFilter = getSidebarFactionFilter();
    if (!zoneId && !currentArea) return;
    const effectiveZoneId = zoneId ?? currentArea?.AreaID;

    for (const category of activatedPOICategories) {
        if (category.startsWith('npc:')) {
            await addNpc(category.substring(4), effectiveZoneId, factionFilter);
        } else if (category === 'skinnable') {
            await addSkinnableNpcsToArea(effectiveZoneId);
        } else if (category === 'node:vein') {
            await addNodeSpawnsToArea(effectiveZoneId, 'vein');
        } else if (category === 'node:herb') {
            await addNodeSpawnsToArea(effectiveZoneId, 'herb');
        } else if (category === 'mailbox') {
            await addMailboxes();
        } else if (category === 'elites') {
            await addEliteNpcsToArea(effectiveZoneId, factionFilter);
        }
    }

    scheduleGroupedLayerControlUpdate();
}

function removeActivatedPOICategory(groupName) {
    if (activatedPOICategories.delete(`npc:${groupName}`)) return;

    if (groupName === 'Elites') {
        activatedPOICategories.delete('elites');
        return;
    }

    const mapping = { 'Mailboxes': 'mailbox' };
    if (mapping[groupName]) {
        activatedPOICategories.delete(mapping[groupName]);
        return;
    }

    if (groupName.endsWith(' Skinning')) activatedPOICategories.delete('skinnable');
    else if (groupName.endsWith(' vein')) activatedPOICategories.delete('node:vein');
    else if (groupName.endsWith(' herb')) activatedPOICategories.delete('node:herb');
}

function angleDifference(a, b) {
    const diff = Math.abs(a - b) % 360;
    return diff > 180 ? 360 - diff : diff;
}

function getAngleBetweenVectors(a, b) {
    const dot = a.x * b.x + a.y * b.y;
    const magA = Math.sqrt(a.x ** 2 + a.y ** 2);
    const magB = Math.sqrt(b.x ** 2 + b.y ** 2);
    const cosTheta = dot / (magA * magB);
    return Math.acos(Math.min(Math.max(cosTheta, -1), 1)) * (180 / Math.PI);
}

function createPlayer(latlng) {
    requestAnimationFrame(() => {
        const playerIcon = L.divIcon({
            className: 'poiatlas player-icon',
            iconSize: [aSize, aSize],
            html: atlasImgName('player')
        });
        playerLayer = new L.marker(latlng, { icon: playerIcon })
        playerLayer.addTo(LeafletMap);
    });
}

function setPlayerLocation(x, y, dir) {
    if (!LeafletMap) return;

    const latlng = worldTolatLng(x, y);
    LeafletMap.setView(latlng, LeafletMap.getZoom());

    if (playerLayer === undefined || playerLayer === null) {
        createPlayer(latlng);
        return;
    }

    playerLayer.setLatLng(latlng);

    dir = -dir * (180 / Math.PI);
    dir = dir % 360;

    const arrow = document.querySelector('.player-icon');
    if (arrow == null) {
        createPlayer(latlng);
        return;
    }

    arrow.style.transformOrigin = 'center center';

    const currentTransform = arrow.style.transform || "";
    const cleaned = currentTransform.replace(/rotate\([^)]*\)/, '').trim();
    arrow.style.transform = `${cleaned} rotate(${dir}deg)`.trim();

    if (recordPlayerPath == false) {
        return;
    }

    const polyline = editableLayers.getLayers().find(layer => layer.PathName === currentRecordPlayerPath);
    if (polyline == null) {
        return;
    }

    const latlngs = polyline.getLatLngs();
    if (latlngs.length == 0) {
        polyline.addLatLng(latlng);
        return;
    }

    const lastLatLng = latlngs[latlngs.length - 1];
    const lastWorldPos = latLngToWorld(lastLatLng);
    const currentWorldPos = latLngToWorld(latlng);

    const dx = currentWorldPos.x - lastWorldPos.x;
    const dy = currentWorldPos.y - lastWorldPos.y;
    const distance = Math.sqrt(dx ** 2 + dy ** 2);

    const MIN_DISTANCE = 40;
    const MIN_ANGLE_CHANGE = 5;

    // Directional change detection (optional vector-based)
    let directionChanged = false;

    if (latlngs.length >= 2) {
        const prevLatLng = latlngs[latlngs.length - 2];
        const prevWorld = latLngToWorld(prevLatLng);

        const lastVector = {
            x: lastWorldPos.x - prevWorld.x,
            y: lastWorldPos.y - prevWorld.y,
        };

        const currentVector = {
            x: currentWorldPos.x - lastWorldPos.x,
            y: currentWorldPos.y - lastWorldPos.y,
        };

        const angle = getAngleBetweenVectors(lastVector, currentVector);
        directionChanged = angle > MIN_ANGLE_CHANGE;
    }

    if (distance > MIN_DISTANCE || directionChanged) {
        polyline.addLatLng(latlng);
    }
}

function setRecord(state) {

    if (playerLayer == null) {
        console.warn("Player layer not found");
        return;
    }

    if (recordPlayerPath == false && state == true) {

        const latlng = playerLayer.getLatLng();
        const polyline = new L.Polyline([latlng], { color: 'red', weight: 2, opacity: 1, smoothFactor: 1 });

        currentRecordPlayerPath = getFilePathFileName();

        polyline.PathName = currentRecordPlayerPath
        polyline.groupName = 'Paths';
        polyline.addTo(editableLayers);

        polyline.editing.enable();
    }
    else if (recordPlayerPath == true && state == false) {

        const polyline = editableLayers.getLayers().find(layer => layer.PathName === currentRecordPlayerPath);
        if (polyline) {
            polyline.editing.disable();
            editableLayers.removeLayer(polyline);

            currentRecordPlayerPath = '';

            addGroupLayer('Paths', 'Paths');
            assignLayer(polyline.groupName, polyline.PathName, polyline, true);
            bindRightClick(polyline, polyline.groupName, polyline.PathName);
            flashLayer(polyline);

            scheduleGroupedLayerControlUpdate();
        }
    }

    recordPlayerPath = state;
}


function isMap(p) {
    return p.x > 0 && p.x < 100;
}

function setPolyPath(name, path) {
    if (!LeafletMap) return;

    let polyline = layerNames[name];

    path = JSON.parse(path);

    var worlds = path;
    if (path.length > 0 && isMap(path[0])) {
        worlds = path.map(p => localToWorld(currentArea, flipXYLower(p)));
    }

    const latlngs = worlds.map(w => worldTolatLng(w.x, w.y));

    if (polyline == null) {
        polyline = new L.Polyline([latlngs], { color: name === 'Route' ? 'white' : 'blue', weight: 2, opacity: 1, smoothFactor: 1 });
        polyline.PathName = name;
        polyline.groupName = 'Navigation';
        polyline.addTo(editableLayers);

        addGroupLayer('Navigation', name);
        assignLayer(polyline.groupName, polyline.PathName, polyline, true);

        polyline.bringToFront();

        schedulePixiRedraw();
        scheduleGroupedLayerControlUpdate();
    }
    else {
        if (polyline.getLatLngs().length != latlngs.length) {
            polyline.setLatLngs(latlngs);

            polyline.bringToFront();
        }
    }
}

function atlasImg(p) {
    return `<div class="poiatlas" style="background-position: ${p.x}px ${p.y}px;height:${aSize}px;">&nbsp</div>`
}

function eliteIcon(creature, fallbackIcon) {
    return creature.Rank == 4 ? 'RareElite' : creature.Rank > 0 ? 'Elite' : fallbackIcon;
}

function atlasImgName(name) {
    let iconPos = IconAtlas[name];

    if (iconPos == null) {
        console.warn(`Icon "${name}" not found in atlas using fallback icon.`);
        iconPos = IconAtlas['redcross'];
    }

    const leftPx = -(iconPos[0] * aSize);
    const topPx = -(iconPos[1] * aSize);
    return atlasImg({ x: leftPx, y: topPx });
}

/// test
function testdrawIconAtlasPixi() {
    const itemsPerRow = 16;
    const spacing = 0.1;

    let i = 0;
    for (const [name, [col, row]] of Object.entries(IconAtlas)) {
        const gridX = i % itemsPerRow;
        const gridY = Math.floor(i / itemsPerRow);

        const lat = -25 - (gridY * spacing);
        const lng = 25 + (gridX * spacing);
        const latlng = L.latLng(lat, lng);


        const texture = getPixiIconTexture(name);
        const sprite = createSprite(latlng, texture);

        addSpriteClickHandler(sprite, () => {
            const popup = L.popup({
                autoClose: false,
                closeOnClick: false,
                closeButton: true,
                className: 'pixi-tooltip'
            })
                .setLatLng(latlng)
                .setContent(name)
                .openOn(LeafletMap);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);
        i++;
    }

    schedulePixiRedraw();
}


/// test

const atlasImagePath = '_content/Frontend/img/atlas.png';
var baseAtlasTexture;

const atlasImage = new Image();
atlasImage.src = atlasImagePath;
atlasImage.onload = () => {
    baseAtlasTexture = PIXI.BaseTexture.from(atlasImage);
};


function getPixiIconTexture(name) {
    let iconPos = IconAtlas[name];

    if (!iconPos) {
        console.warn(`Icon "${name}" not found in atlas. Falling back to redcross.`);
        iconPos = IconAtlas['redcross'];
    }

    const frame = new PIXI.Rectangle(
        iconPos[0] * aSize,
        iconPos[1] * aSize,
        aSize,
        aSize
    );

    return new PIXI.Texture(baseAtlasTexture, frame);
}

function createSprite(latlng, texture) {
    const sprite = new PIXI.Sprite(texture);
    sprite.anchor.set(0.5);
    sprite.latlng = latlng;
    sprite.interactive = true;
    sprite.buttonMode = true;
    sprite.scale.set(1);

    return sprite;
}

// Helper function to properly add click handlers to sprites
function addSpriteClickHandler(sprite, handler) {
    sprite.interactive = true;
    sprite.buttonMode = true;
    sprite._leafletClickHandler = handler;
    clickableSprites.add(sprite);

    sprite.on('pointertap', (e) => {
        handler(e);
    });

    sprite.on('pointerover', (e) => {
    });
}


let controlUpdateScheduled = false;
function scheduleGroupedLayerControlUpdate() {
    if (controlUpdateScheduled) return;
    controlUpdateScheduled = true;
    requestAnimationFrame(() => {
        createGroupedLayerControl();
        controlUpdateScheduled = false;
    });
}

const layerGroupIcons = {
    'vendor': 'vendor', 'vendorammo': 'vendorammo', 'vendorfood': 'vendorfood',
    'vendorpoison': 'vendorpoison', 'vendorreagent': 'vendorreagent',
    'repair': 'repair', 'classtrainer': 'classtrainer',
    'professiontrainer': 'professiontrainer', 'flightmaster': 'flightmaster',
    'innkeeper': 'innkeeper', 'spirithealer': 'spirithealer',
    'stablemaster': 'stablemaster', 'Elites': 'Elite',
    'Search': 'redcross', 'POI': 'poi', 'Mailboxes': 'mailbox', 'Teleport': 'poi'
};

function getLayerGroupIcon(gName) {
    if (layerGroupIcons[gName]) return layerGroupIcons[gName];
    if (gName.includes('Skinning')) return 'skinnable';
    if (gName.includes('vein')) return 'mining';
    if (gName.includes('herb')) return 'Silverleaf';
    return null;
}

function applyLayerToggleIcon(control, gName) {
    const container = control.getContainer();
    if (!container) return;

    const toggle = container.querySelector('.leaflet-control-layers-toggle');
    if (!toggle) return;

    const iconName = getLayerGroupIcon(gName);
    if (!iconName) return;

    const iconPos = IconAtlas[iconName];
    if (!iconPos) return;

    const leftPx = -(iconPos[0] * aSize);
    const topPx = -(iconPos[1] * aSize);

    toggle.style.backgroundImage = `url('${atlasImagePath}')`;
    toggle.style.backgroundPosition = `${leftPx}px ${topPx}px`;
    toggle.style.backgroundSize = '512px auto';
    toggle.style.imageRendering = 'pixelated';
    toggle.style.width = `${aSize}px`;
    toggle.style.height = `${aSize}px`;
}

function createGroupedLayerControl() {
    const newGroupedControls = [];

    for (const gName in groupedOverlays) {
        const group = groupedOverlays[gName];
        if (!group || Object.keys(group).length === 0) continue;

        const sortedGroup = {};
        Object.keys(group)
            .sort((a, b) => a.localeCompare(b))
            .forEach(name => {
                sortedGroup[name] = group[name];
            });

        // Replace any existing control for this group
        const existing = groupedLayerControls.find(c => c._groupName === gName);
        if (existing) {
            LeafletMap.removeControl(existing);
        }

        const control = L.control.groupedLayers(null, { [gName]: sortedGroup }, { groupCheckboxes: true });
        control._groupName = gName;

        control.addTo(LeafletMap);

        // Move layer control into the sidebar layers column
        if (sidebarLayersColumn) {
            const container = control.getContainer();
            if (container) {
                sidebarLayersColumn.appendChild(container);
            }
        }

        requestAnimationFrame(() => {
            addZoomButtonsToLayerControl(control);
            addCloseButtonToGroupLayers(control);
            applyLayerToggleIcon(control, gName);
        });

        newGroupedControls.push(control);
    }

    for (const ctrl of groupedLayerControls) {
        if (!newGroupedControls.includes(ctrl)) {
            LeafletMap.removeControl(ctrl);
        }
    }

    groupedLayerControls.length = 0;
    groupedLayerControls.push(...newGroupedControls);
}




function addZoomButtonsToLayerControl(control) {
    const container = control.getContainer();
    const inputs = container.querySelectorAll('input.leaflet-control-layers-selector');

    inputs.forEach(input => {
        const label = input.closest('label');
        if (!label) return;

        if (label.querySelector('.zoom-to-btn')) return;

        const layerName = label.textContent.trim();

        label.style.position = 'relative';

        const btn = document.createElement('button');
        btn.textContent = '🔍';
        btn.className = 'zoom-to-btn';

        btn.style.position = 'absolute';
        btn.style.right = '4px';
        btn.style.top = '50%';
        btn.style.transform = 'translateY(-50%)';

        btn.style.cursor = 'pointer';
        btn.style.border = 'none';
        btn.style.background = 'transparent';
        btn.style.padding = '0';
        btn.style.fontSize = '14px';
        btn.title = 'Zoom to layer';

        btn.onclick = (e) => {
            e.preventDefault();
            e.stopPropagation();

            const layer = layerNames[layerName]; // or wherever you store the layers
            if (!layer) {
                return;
            }

            const zoom = LeafletMap.getZoom();

            if (layer.getLatLng && typeof layer.getLatLng === 'function') {
                LeafletMap.setView(layer.getLatLng(), zoom);
            }
            else if (layer.getBounds && typeof layer.getBounds === 'function') {
                LeafletMap.fitBounds(layer.getBounds(), { padding: [20, 20] });
            } else if (layer instanceof L.LayerGroup) {

                let found = false;
                const layerGroup = layer.getLayers ? layer.getLayers() : layer._layers || [];

                for (const sub of layerGroup) {
                    if (sub.getBounds instanceof Function) {
                        LeafletMap.fitBounds(sub.getBounds(), { padding: [20, 20] });
                        found = true;
                        break;
                    } else if (sub.getLatLng instanceof Function) {
                        LeafletMap.setView(sub.getLatLng(), zoom);
                        found = true;
                        break;
                    }
                }

                if (!found) {
                    console.warn("No zoomable sublayers found.");
                }
            } else {
                console.warn("Layer does not support zooming");
            }
        };

        label.appendChild(btn);
    });
}

// ❌ Group remove buttons
function addCloseButtonToGroupLayers(control) {
    const container = control.getContainer();

    const groupLabels = container.querySelectorAll('.leaflet-control-layers-group-label');
    groupLabels.forEach(span => {
        if (span.querySelector('.remove-group-btn')) return;

        span.style.position = 'relative';
        span.style.paddingRight = '20px'; // add space for button on the right

        const removeBtn = document.createElement('button');
        removeBtn.textContent = '❌';
        removeBtn.className = 'remove-group-btn';

        removeBtn.style.position = 'absolute';
        removeBtn.style.right = '6px';
        removeBtn.style.top = '50%';
        removeBtn.style.transform = 'translateY(-50%)';

        removeBtn.style.cursor = 'pointer';
        removeBtn.style.border = 'none';
        removeBtn.style.background = 'transparent';
        removeBtn.style.padding = '0';
        removeBtn.style.fontSize = '14px';
        removeBtn.style.color = 'red';

        const groupName = span.textContent.trim();
        removeBtn.title = `Remove group "${groupName}"`;

        removeBtn.onclick = (e) => {
            e.preventDefault();
            e.stopPropagation();

            if (!groupedOverlays[groupName]) return;

            //if (!confirm(`Remove all layers in group "${groupName}"?`)) return;

            const layers = groupedOverlays[groupName];
            for (const layerName in layers) {
                const layer = layers[layerName];
                if (LeafletMap.hasLayer(layer)) {
                    LeafletMap.removeLayer(layer);
                }

                // Properly cleanup PixiSpriteGroupLayer instances
                if (layer && typeof layer.destroy === 'function') {
                    layer.destroy();
                }

                if (layerNames[layerName]) {
                    delete layerNames[layerName];
                }
            }

            delete groupedOverlays[groupName];
            removeActivatedPOICategory(groupName);

            scheduleGroupedLayerControlUpdate();
        };

        span.appendChild(removeBtn);
    });
}

function setBoundToArea(areaId) {

    const area = Zones[areaId];
    if (area == null) {
        return;
    }

    currentArea = area;

    const bounds = getAreaBounds(area);

    const southWest = bounds[0];
    const northEast = bounds[1];
    const mapBounds = new L.LatLngBounds(southWest, northEast);

    LeafletMap.setMaxBounds(mapBounds);
}

function addToggleLayer(type, name, layer, visible = true) {
    if (layerNames[name] != null) {
        return;
    }

    if (savedLayerVisibility !== null && savedLayerVisibility.has(name)) {
        visible = savedLayerVisibility.get(name);
    }

    assignLayer(type, name, layer, visible);
}

function assignLayer(type, name, layer, visible) {

    groupedOverlays[type][name] = layer;
    layerNames[name] = layer;

    if (visible && !LeafletMap.hasLayer(layer)) {
        layer.addTo(LeafletMap);
    }
}

function addGroupLayer(type, name) {
    groupedOverlays[type] ??= {};
    return groupedOverlays[type][name] ??= new L.LayerGroup();
}

function getGroupLayer(type, name) {
    return groupedOverlays[type]?.[name] ?? null;
}

function createPixiSpriteGroupLayer(type, sprites) {
    groupedOverlays[type] ??= {};

    return new PixiSpriteGroupLayer(sprites);
}

const PixiSpriteGroupLayer = L.Layer.extend({
    initialize(sprites) {
        this._sprites = sprites;
        this._visible = true;
    },

    onAdd(map) {
        this._visible = true;
        for (const sprite of this._sprites) {
            sprite.visible = true;
        }
        schedulePixiRedraw();
    },

    onRemove(map) {
        this._visible = false;
        for (const sprite of this._sprites) {
            sprite.visible = false;
        }
        schedulePixiRedraw();
    },

    destroy() {
        for (const sprite of this._sprites) {
            clickableSprites.delete(sprite);
        }
    },

    getBounds: function () {
        const latlngs = this._sprites.map(s => s.latlng);
        return L.latLngBounds(latlngs);
    },

    getLatLng: function () {
        return this._sprites[0].latlng;
    }
});



async function load() {

    if (!enableUrlEdit) { return; }

    //addGroupLayer('SubZones', 'SubZones');
    //addSubZones();

    //await loadPathsByAreaNames();
    //await loadPathsByRaceNames();
}

async function loadPathsByAreaNames() {
    for (const area of Object.values(Zones)) {
        if (area.AreaID > AreaIDOffset) {
            continue;
        }

        await loadMapPathByFilter(area.AreaName);
    }
}

async function loadPathsByRaceNames() {
    for (const name of Object.keys(raceNames)) {

        await loadMapPathByFilter(name);
    }
}

function synchronizeTitleAndURL() {
    const latlng = LeafletMap.getCenter();
    const zoom = LeafletMap.getZoom();

    const current =
    {
        Zoom: zoom,
        LatLng: latlng
    };

    const title = "Leaflet"

    const url = '/Leaflet/' + expansion + '/' + continent + '/' + zoom + '/' + latlng.lat.toFixed(3) + '/' + latlng.lng.toFixed(3) + '/';

    window.history.replaceState(current, title, url);

    document.title = title;
}

//// layers

function setText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text;
}

function handleSpriteClick(e) {
    if (!LeafletMap || clickableSprites.size === 0) return false;

    const clickPoint = LeafletMap.latLngToContainerPoint(e.latlng);
    const hitRadius = aSize / 2;
    const hitRadiusSq = hitRadius * hitRadius;

    for (const sprite of clickableSprites) {
        if (!sprite.visible) continue;

        const spritePoint = LeafletMap.latLngToContainerPoint(sprite.latlng);
        const dx = clickPoint.x - spritePoint.x;
        const dy = clickPoint.y - spritePoint.y;

        if (dx * dx + dy * dy <= hitRadiusSq) {
            sprite._leafletClickHandler();
            return true;
        }
    }

    return false;
}

async function processOffsetClick(e) {

    const layerPoint = LeafletMap.project(e.latlng, config.maxZoom);

    const adt = screenToAdt(layerPoint);
    const worldPos = screenToWorld(layerPoint);

    const response = enableUrlEdit ? await getAreaIdAndZFromService(worldPos) : { areaId: 0, z: 0 };
    const areaId = response.areaId;
    const zPos = response.z.toFixed(0);

    //addGroupLayer('SubZones', 'SubZones');
    //addSubZones(areaId);
    //scheduleGroupedLayerControlUpdate();

    const map = worldToPercentage(worldPos, areaId);

    const adtClick = document.getElementById("adtClick");
    if (adtClick != null)
        adtClick.innerHTML = continent + '_' + adt.x + '_' + adt.y;

    const worldClick = document.getElementById("worldClick")
    if (worldClick != null)
        worldClick.innerHTML = worldPos.x.toFixed(2) + ' ' + worldPos.y.toFixed(2) + ' ' + zPos + ' ' + config.MapID;

    if (map != null) {
        currentArea = map.area;
        updateSidebarCurrentZone();

        const mapClick = document.getElementById("mapClick");
        if (mapClick != null)
            mapClick.innerHTML = map.p.x + ' ' + map.p.y;

        if (map.subZone != null) {
            const subZoneName = document.getElementById("subZoneName");

            const subName = map.subZone.AreaName != map.name ? map.subZone.AreaName : '';
            if (subZoneName != null)
                subZoneName.innerHTML = map.AreaID + ' ' + map.name + ' ' + subName;
        }
    }
}

///////////////////////////////////

function worldTolatLng(x, y) {
    const pxPerCoord = adtSize / blpSize;

    const offset = config.offset.min;

    const offsetX = (offset.y * adtSize) / pxPerCoord;
    const offsetY = (offset.x * adtSize) / pxPerCoord;

    const tx = y * -1;
    const xx = (mapSize / 2 + tx) / pxPerCoord - offsetX;

    const ty = x * -1;
    const yy = (mapSize / 2 + ty) / pxPerCoord - offsetY;

    return LeafletMap.unproject([xx, yy], config.maxZoom);
}

function latLngToWorld(latlng) {
    const pxPerCoord = adtSize / blpSize;

    const offset = config.offset.min;
    const offsetX = (offset.y * adtSize) / pxPerCoord;
    const offsetY = (offset.x * adtSize) / pxPerCoord;

    const point = LeafletMap.project(latlng, config.maxZoom);

    // Reverse xx and yy logic
    const xx = (point.x + offsetX) * pxPerCoord - mapSize / 2;
    const yy = (point.y + offsetY) * pxPerCoord - mapSize / 2;

    // Undo the earlier inversion
    const x = -yy;
    const y = -xx;

    return { x, y };
}

function screenToWorld(point) {
    const offset = config.offset.min;
    const tileSize = blpSize;

    const adtCenterX = ((point.y / tileSize) + offset.x) - 32;
    const adtCenterY = ((point.x / tileSize) + offset.y) - 32;

    const worldX = -(adtCenterX * adtSize);
    const worldY = -(adtCenterY * adtSize);

    return new L.Point(worldX, worldY);
}

function screenToAdt(point) {
    const offset = config.offset.min;
    const tileSize = blpSize;

    const adtX = Math.floor((point.x / tileSize) + offset.y);
    const adtY = Math.floor((point.y / tileSize) + offset.x);

    return new L.Point(adtX, adtY);
}

function worldToPercentage(p, areaId) {
    let bestMatch = null;

    let bestParentArea = null;
    let bestSubZone = null;

    const subzone = SubZones[areaId];
    if (subzone != null) {
        bestParentArea = Zones[subzone.ParentAreaId];
        bestSubZone = subzone;
    }
    else {
        const zone = Zones[areaId];
        if (zone != null) {
            bestParentArea = Zones[areaId];
            bestSubZone = bestParentArea;
        }
    }

    if (!bestParentArea) {
        return { p: new L.Point(0, 0), name: "not found", subZoneName: '' };
    }

    bestMatch = {
        p: new L.Point(toMapY(bestParentArea, p.y), toMapX(bestParentArea, p.x)),
        area: bestParentArea,
        AreaID: bestParentArea ? bestParentArea.AreaID : null,
        name: bestParentArea ? bestParentArea.AreaName : "not found",
        subZone: bestSubZone
    };

    return bestMatch;
}

const getAreaIdFromServiceCache = {};

async function getAreaIdAndZFromService(worldPos) {

    const key = `${worldPos.x.toFixed(2)}:${worldPos.y.toFixed(2)}`;

    if (key in getAreaIdFromServiceCache) {
        return getAreaIdFromServiceCache[key];
    }

    const url = `/api/Path/GetAreaIdAndZ?mapid=${config.MapID}&x=${worldPos.x}&y=${worldPos.y}`;
    const res = await fetch(url);
    return getAreaIdFromServiceCache[key] = await res.json();
}

async function savePath(fileName, mapPoints) {
    const url = `/api/Path/SavePath?fileName=${encodeURIComponent(fileName)}`;

    const res = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(mapPoints) // assuming mapPoints = { x: ..., y: ..., z: ... }
    });

    if (!res.ok) {
        console.error("SavePath failed:", await res.text());
        throw new Error("SavePath request failed");
    }

    //return await res.json(); // or just `return;` if your C# returns `Ok()` without data
}

function localToWorld(area, point) {
    const x = toWorldX(area, point.x);
    const y = toWorldY(area, point.y);

    return new L.Point(x, y);
}

function toMapX(area, value) {
    return (100 - (((value - area.LocBottom) * 100) / (area.LocTop - area.LocBottom))).toFixed(2);
}

function toMapY(area, value) {
    return (100 - (((value - area.LocRight) * 100) / (area.LocLeft - area.LocRight))).toFixed(2);
}

function toWorldX(area, value) {
    return ((area.LocBottom - area.LocTop) * value / 100) + area.LocTop;
}

function toWorldY(area, value) {
    return ((area.LocRight - area.LocLeft) * value / 100) + area.LocLeft;
}

function contains(area, point) {
    const minX = Math.min(area.LocTop, area.LocBottom);
    const maxX = Math.max(area.LocTop, area.LocBottom);
    const minY = Math.min(area.LocLeft, area.LocRight);
    const maxY = Math.max(area.LocLeft, area.LocRight);

    return point.x >= minX && point.x <= maxX && point.y >= minY && point.y <= maxY;
}

//////////////////////////////////////////////////////////////
//////////////////// ADT layer on off //////////////////////////////////////////
///////////////////////////////////////////////////////////////////////

function addADT() {

    ADTEnabled = true;

    ADTGridLayer.clearLayers();

    const debugCell = false;
    const subDiv = 16;
    const subSize = adtSize / subDiv;

    for (let x = 0; x < 64; x++) {
        for (let y = 0; y < 64; y++) {
            // ADT-level borders (red)
            let xStart = maxSize - (x * adtSize);
            let yStart = maxSize - (y * adtSize);

            let xEnd = xStart - adtSize;
            let yEnd = yStart - adtSize;

            // vertical ADT line
            ADTGridLayer.addLayer(new L.polyline([
                worldTolatLng(xStart, -maxSize),
                worldTolatLng(xStart, maxSize)
            ], { weight: 0.1, color: 'red' }));

            // horizontal ADT line
            ADTGridLayer.addLayer(new L.polyline([
                worldTolatLng(maxSize, yStart),
                worldTolatLng(-maxSize, yStart)
            ], { weight: 0.1, color: 'red' }));

            // Now draw 16x16 subdivisions (gray)
            for (let i = 1; debugCell && i < subDiv; i++) {
                // vertical subdivision lines
                const subX = xStart - (i * subSize);
                ADTGridLayer.addLayer(new L.polyline([
                    worldTolatLng(subX, yStart),
                    worldTolatLng(subX, yEnd)
                ], { weight: 1, color: 'green' }));

                // horizontal subdivision lines
                const subY = yStart - (i * subSize);
                ADTGridLayer.addLayer(new L.polyline([
                    worldTolatLng(xStart, subY),
                    worldTolatLng(xEnd, subY)
                ], { weight: 1, color: 'green' }));
            }
        }
    }

    refreshADTGrid();

    if (!LeafletMap.hasLayer(ADTGridLayer)) {
        LeafletMap.addLayer(ADTGridLayer);
    }
}

function removeADT() {
    ADTEnabled = false;
    LeafletMap.removeLayer(ADTGridLayer);
    LeafletMap.removeLayer(ADTGridTextLayer);
}

function refreshADTGrid() {

    if (!ADTEnabled) {
        return;
    }

    if (LeafletMap.getZoom() < 5) {
        if (LeafletMap.hasLayer(ADTGridTextLayer)) {
            ADTGridTextLayer.clearLayers();
        }
        return;
    }

    ADTGridTextLayer.clearLayers();

    for (let x = 0; x < 64; x++) {
        for (let y = 0; y < 64; y++) {
            const latlng = worldTolatLng(maxSize - (x * adtSize) - 25, maxSize - (y * adtSize) - 25);

            if (LeafletMap.getBounds().contains(latlng)) {
                const myIcon = L.divIcon({ className: 'adtcoordicon', html: '<div class="adtText">' + y + '_' + x + '</div>' });
                ADTGridTextLayer.addLayer(new L.marker(latlng, { icon: myIcon }));
            }
        }
    }

    // Ensure the layer is added after processing
    if (!LeafletMap.hasLayer(ADTGridTextLayer)) {
        LeafletMap.addLayer(ADTGridTextLayer);
    }
}

function createLeafletButtonControl({ className = '', iconHTML = '', setImageFn = null, onClick = null, npcType = null }) {
    return L.Control.extend({
        options: {
            position: 'topleft',
            npcType: npcType
        },

        initialize: function (npcType, options) {
            L.Util.setOptions(this, options);
            this.options.npcType = npcType ?? this.options.npcType;
        },

        onAdd: function (map) {
            const container = L.DomUtil.create('div', `leaflet-bar leaflet-control leaflet-control-custom ${className}`);

            if (setImageFn && this.options.npcType) {
                setImageFn(container.style, this.options.npcType);
            }

            if (iconHTML) {
                container.innerHTML = iconHTML;
            }

            container.style.backgroundColor = 'white';
            container.style.width = '30px';
            container.style.height = '30px';
            container.style.display = 'flex';
            container.style.alignItems = 'center';
            container.style.justifyContent = 'center';

            L.DomEvent.disableClickPropagation(container);

            container.onclick = () => {
                if (typeof onClick === 'function') {
                    onClick(this.options.npcType, map);
                }
            };

            return container;
        }
    });
}

// Coordinate Marker Control moved to sidebar

// Storage for custom coordinate markers
const customMarkerLayer = L.layerGroup();
let customMarkerCount = 0;

function addCustomMarker(x, y, z) {
    const latlng = worldTolatLng(x, y);

    customMarkerCount++;
    const markerName = `Marker ${customMarkerCount}`;

    const markerIcon = L.divIcon({
        className: 'poiatlas',
        iconSize: [aSize, aSize],
        html: `<div style="background-color: #ff4444; border: 2px solid white; border-radius: 50%; width: 16px; height: 16px; box-shadow: 0 0 4px rgba(0,0,0,0.5);"></div>`
    });

    const marker = L.marker(latlng, { icon: markerIcon });

    const popupContent = `
        <b>${markerName}</b><br>
        X: ${x.toFixed(2)}<br>
        Y: ${y.toFixed(2)}<br>
        Z: ${z.toFixed(2)}<br>
        <button onclick="removeCustomMarker(${customMarkerCount})" style="margin-top:5px;cursor:pointer;">Remove</button>
    `;

    marker.bindPopup(popupContent);
    marker.customMarkerId = customMarkerCount;

    addGroupLayer('Custom Markers', 'Custom Markers');
    addToggleLayer('Custom Markers', markerName, marker);
    scheduleGroupedLayerControlUpdate();

    // Pan to the marker
    LeafletMap.setView(latlng, LeafletMap.getZoom());
}

function removeCustomMarker(markerId) {
    // Find and remove the marker from layers
    for (const [name, layer] of Object.entries(layerNames)) {
        if (layer.customMarkerId === markerId) {
            LeafletMap.removeLayer(layer);
            delete layerNames[name];
            break;
        }
    }
    scheduleGroupedLayerControlUpdate();
    LeafletMap.closePopup();
}

// Expose to global scope for popup button
window.removeCustomMarker = removeCustomMarker;

///////////////////////////////////////////////////

async function addPoi(areaId) {
    if (!currentArea) return;

    const response = await fetch('_content/Frontend/teleport_locations.txt');
    const text = await response.text();
    const lines = text.split('\n');

    const pois = lines.map(line => {
        const parts = line.split(' ');
        return {
            x: parseFloat(parts[1]),
            y: parseFloat(parts[2]),
            z: parseFloat(parts[3]),
            mapID: Number(parts[5]),
            name: parts.slice(6).join(' ').trim()
        };
    });

    const texture = getPixiIconTexture('poi');
    const targetArea = Zones[areaId];

    for (const poi of pois) {
        if (poi.mapID !== config.MapID) continue;
        if (!contains(targetArea, poi)) continue;

        const res = await getAreaIdAndZFromService(poi);
        const zone = getZone(res.areaId);
        if (!zone || zone.AreaID !== targetArea.AreaID) continue;

        const latlng = worldTolatLng(poi.x, poi.y);
        const worldPos = { x: poi.x, y: poi.y };

        const sprite = createSprite(latlng, texture);

        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, poi.name, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);

        const groupLayer = createPixiSpriteGroupLayer('POI', [sprite]);
        addToggleLayer('POI', poi.name, groupLayer, true);

        scheduleGroupedLayerControlUpdate();
    }

    schedulePixiRedraw();
}

async function addTeleportPoints() {
    const response = await fetch('_content/Frontend/teleport_locations.txt');
    const text = await response.text();
    const lines = text.split('\n');

    const texture = getPixiIconTexture('poi');

    for (const line of lines) {
        const parts = line.split(' ');
        if (parts.length < 7) continue;
        const x = parseFloat(parts[1]);
        const y = parseFloat(parts[2]);
        const z = parseFloat(parts[3]);
        const mapID = Number(parts[5]);
        const name = parts.slice(6).join(' ').trim();

        if (mapID !== config.MapID) continue;

        const latlng = worldTolatLng(x, y);
        const worldPos = { x, y, z };

        const sprite = createSprite(latlng, texture);
        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, name, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);
        const groupLayer = createPixiSpriteGroupLayer('Teleport', [sprite]);
        addToggleLayer('Teleport', `tp:${name}`, groupLayer, true);
    }

    scheduleGroupedLayerControlUpdate();
    schedulePixiRedraw();
}

async function addNpcSpawns(npcId, groupName = 'Spawn', iconName = 'red') {
    const spawns = spawnLocations[npcId];
    if (!spawns) return;

    const creature = creatures[npcId];
    const npcName = creature.Name || npcId;
    const toggleControlName = `${npcName} - ${npcId} (${spawns.length})`;
    //var groupLayer = addGroupLayer(groupName, toggleControlName);

    const texture = getPixiIconTexture(eliteIcon(creature, iconName));
    const sprites = [];

    for (const worldPos of spawns) {
        const latlng = worldTolatLng(worldPos.x, worldPos.y);

        const sprite = createSprite(latlng, texture);

        const html = `
            ${npcName}
            <br/>${creature.MinLevel}-${creature.MaxLevel}
            <br/>${npcId}
            <br/><div onclick="openInNewTab('${baseUrl}/npc=${npcId}');">wowhead link</div>
        `;

        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, html, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);
        sprites.push(sprite);
    }

    const groupLayer = createPixiSpriteGroupLayer(groupName, sprites);
    addToggleLayer(groupName, toggleControlName, groupLayer, true);

    scheduleGroupedLayerControlUpdate();
}

async function addNodeTypeSpawns(oreName, coords, area, groupName) {
    const toggleControlName = `${oreName} (${coords.length})`;
    const texture = getPixiIconTexture(oreName);

    const sprites = [];

    for (const mapPos of coords) {
        const map = { x: mapPos[1], y: mapPos[0] };
        const worldPos = localToWorld(area, map);
        const latlng = worldTolatLng(worldPos.x, worldPos.y);

        const sprite = createSprite(latlng, texture);

        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, oreName, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);
        sprites.push(sprite);
    }

    const groupLayer = createPixiSpriteGroupLayer(groupName, sprites);
    addToggleLayer(groupName, toggleControlName, groupLayer, true);

    scheduleGroupedLayerControlUpdate();

    schedulePixiRedraw();
}


function openInNewTab(url) {
    window.open(url, '_blank').focus();
}

async function addSkinnableNpcsToArea(areaId) {
    const areaDBC = await getAreaOrCache(areaId);
    if (areaDBC == null) {
        return;
    }

    const areaName = Zones[areaId].AreaName;

    var count = 0;
    const firstRow = 8;

    for (const id of areaDBC.skinnable) {
        if (skinnableExclude[id] === true) continue;

        addNpcSpawns(id, `${areaName} Skinning`, Object.values(Circles)[++count % firstRow]);
    }
}

async function addNodeSpawnsToArea(areaId, nodeType) {
    const areaDBC = await getAreaOrCache(areaId);
    if (areaDBC == null) {
        return;
    }

    const area = Zones[areaId];
    const areaName = area.AreaName;

    // vein or herb
    const db = areaDBC[nodeType];

    for (const name in db) {
        const data = db[name][0];
        addNodeTypeSpawns(name, data.coords, area, `${areaName} ${nodeType}`);
    }
}

//////////////////////////////////////////////////

function npcLoc(npc) {
    return {
        x: npc.coords[0][1],
        y: npc.coords[0][0]
    }
}

const Circles = [
    "darkblue",
    "blue",
    "red",
    "yellow",
    "green",
    "yred",
    "yyellow",
    "ygreen",
]

async function getAreaOrCache(areaId) {
    if (areaCache[areaId] != null) {
        return areaCache[areaId];
    }

    const response = await fetch(`/area/${areaId}.json`)
        .catch(e => console.error(e));

    return areaCache[areaId] = await response.json();
}

// Cache for mailbox data per map
const mailboxCache = {};

async function getMailboxLocations(mapId) {
    if (mailboxCache[mapId]) return mailboxCache[mapId];

    const url = `/mailboxlocations/${mapId}.json`;
    const response = await fetch(url);
    if (!response.ok) return mailboxCache[mapId] = [];
    return mailboxCache[mapId] = await response.json();
}

async function addMailboxes() {
    const groupName = 'Mailboxes';

    addGroupLayer(groupName, groupName);

    const locations = await getMailboxLocations(config.MapID);
    if (!locations || locations.length === 0) {
        console.log(`No mailbox locations found for map ${config.MapID}`);
        return;
    }

    const texture = getPixiIconTexture('mailbox');

    for (const loc of locations) {
        const latlng = worldTolatLng(loc.x, loc.y);
        const sprite = createSprite(latlng, texture);
        const worldPos = { x: loc.x, y: loc.y, z: loc.z };

        const popupHtml = `
            <b>Mailbox</b>
            <br>World: ${loc.x.toFixed(1)}, ${loc.y.toFixed(1)}, ${loc.z.toFixed(1)}
        `;

        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, popupHtml, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);

        const groupLayer = createPixiSpriteGroupLayer(groupName, [sprite]);
        addToggleLayer(groupName, `Mailbox ${loc.x.toFixed(0)}, ${loc.y.toFixed(0)}`, groupLayer, true);
    }

    scheduleGroupedLayerControlUpdate();
    schedulePixiRedraw();

    console.log(`Loaded ${locations.length} mailbox locations for map ${config.MapID}`);
}

async function addNpc(npcType, overrideAreaId = null, factionFilter = 'both') {
    const area = overrideAreaId ? Zones[overrideAreaId] : currentArea;
    if (!area) return;

    addGroupLayer(npcType, npcType);

    const flag = npcFlags[npcType];
    const matches = factionFilter !== 'both'
        ? getCreatureByFlagFiltered(flag, factionFilter)
        : getCreatureByFlag(flag, []);
    const hitboxArea = Zones[area.AreaID + AreaIDOffset] ?? area;
    const texture = getPixiIconTexture(npcType);

    for (const creature of matches) {
        const spawns = spawnLocations[creature.Entry];
        if (!spawns) continue;

        const worldPos = spawns[0];

        var contain = false
        for (const subzone of getSubZones(area.AreaID)) {
            if (contains(subzone, worldPos)) {
                contain = true
                break;
            }
        }

        if (!contain && !contains(hitboxArea, worldPos)) continue;

        const npcName = creature.Name || `${npcType} ${creature.Entry}`;
        const latlng = worldTolatLng(worldPos.x, worldPos.y);

        const sprite = createSprite(latlng, texture);

        const popupHtml = `
            ${npcName}
            <br>${creature.SubName ?? ''}
            <br><div onclick="openInNewTab('${baseUrl}/npc=${creature.Entry}')">wowhead link</div>
        `;

        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, popupHtml, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);

        const groupLayer = createPixiSpriteGroupLayer(npcType, [sprite]);
        addToggleLayer(npcType, `${npcName} - ${npcType}`, groupLayer, true);
    }

    scheduleGroupedLayerControlUpdate();
    schedulePixiRedraw();

    lastRenderArea = area;
}

async function addEliteNpcsToArea(areaId, factionFilter = 'both') {
    const area = areaId ? Zones[areaId] : currentArea;
    if (!area) return;

    const groupName = 'Elites';
    addGroupLayer(groupName, groupName);

    const hitboxArea = Zones[area.AreaID + AreaIDOffset] ?? area;

    for (const creature of Object.values(creatures)) {
        if (creature.Rank <= 0) continue;

        if (factionFilter !== 'both') {
            const friendGroup = factionTemplates[creature.Faction];
            if (friendGroup !== undefined) {
                if (factionFilter === 'alliance' && (friendGroup & 2) === 0) continue;
                if (factionFilter === 'horde' && (friendGroup & 4) === 0) continue;
            }
        }

        const spawns = spawnLocations[creature.Entry];
        if (!spawns) continue;

        const worldPos = spawns[0];

        var contain = false;
        for (const subzone of getSubZones(area.AreaID)) {
            if (contains(subzone, worldPos)) {
                contain = true;
                break;
            }
        }

        if (!contain && !contains(hitboxArea, worldPos)) continue;

        const npcName = creature.Name || `Elite ${creature.Entry}`;
        const latlng = worldTolatLng(worldPos.x, worldPos.y);

        const texture = getPixiIconTexture(eliteIcon(creature, 'Elite'));
        const sprite = createSprite(latlng, texture);

        const popupHtml = `
            ${npcName}
            <br>${creature.SubName ?? ''}
            <br>${creature.MinLevel}-${creature.MaxLevel}
            <br><div onclick="openInNewTab('${baseUrl}/npc=${creature.Entry}')">wowhead link</div>
        `;

        addSpriteClickHandler(sprite, async () => {
            await showPixiPopup(worldPos, latlng, popupHtml, sprite);
        });

        pixiOverlay.utils.getContainer().addChild(sprite);

        const groupLayer = createPixiSpriteGroupLayer(groupName, [sprite]);
        addToggleLayer(groupName, `${npcName} - Elite`, groupLayer, true);
    }

    scheduleGroupedLayerControlUpdate();
    schedulePixiRedraw();

    lastRenderArea = area;
}

// ===== Sidebar Control =====

function getSidebarZoneId() {
    const dropdown = document.getElementById('sidebar-zone-select');
    if (dropdown && dropdown.value !== 'current') {
        return parseInt(dropdown.value);
    }
    return currentArea?.AreaID ?? null;
}

function getSidebarFactionFilter() {
    const checked = document.querySelector('input[name="sidebar-faction"]:checked');
    return checked ? checked.value : 'both';
}

function getAtlasIconStyle(iconName, iconSize = aSize) {
    const iconPos = IconAtlas[iconName];
    if (!iconPos) return '';
    const leftPx = -(iconPos[0] * iconSize);
    const topPx = -(iconPos[1] * iconSize);
    if (iconSize === aSize) {
        return `background-position: ${leftPx}px ${topPx}px;`;
    }
    const bgWidth = 16 * iconSize;
    return `background-position: ${leftPx}px ${topPx}px; background-size: ${bgWidth}px auto;`;
}

let sidebarSearchTimeout = null;
var sidebarLayersColumn = null;

function clearSidebarNpcCategories() {
    const container = document.getElementById('sidebar-npc-categories');
    if (!container) return;
    const checkboxes = container.querySelectorAll('input[type="checkbox"]');
    checkboxes.forEach(cb => { cb.checked = false; });

    // Remove associated layers
    const npcTypes = ['vendor', 'vendorammo', 'vendorfood', 'vendorpoison',
        'vendorreagent', 'repair', 'classtrainer', 'professiontrainer',
        'flightmaster', 'innkeeper', 'spirithealer', 'stablemaster'];
    for (const npcType of npcTypes) {
        activatedPOICategories.delete(`npc:${npcType}`);
        const layers = groupedOverlays[npcType];
        if (layers) {
            for (const name in layers) {
                const layer = layers[name];
                if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                if (layer && typeof layer.destroy === 'function') layer.destroy();
                if (layerNames[name]) delete layerNames[name];
            }
            delete groupedOverlays[npcType];
        }
    }

    // Remove elites
    activatedPOICategories.delete('elites');
    const eliteLayers = groupedOverlays['Elites'];
    if (eliteLayers) {
        for (const name in eliteLayers) {
            const layer = eliteLayers[name];
            if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
            if (layer && typeof layer.destroy === 'function') layer.destroy();
            if (layerNames[name]) delete layerNames[name];
        }
        delete groupedOverlays['Elites'];
    }

    scheduleGroupedLayerControlUpdate();

    // Clear search
    const searchInput = document.getElementById('sidebar-npc-search');
    if (searchInput) searchInput.value = '';
    const searchResults = document.getElementById('sidebar-search-results');
    if (searchResults) searchResults.innerHTML = '';
}

function clearSidebarResourceCategories() {
    const resBody = document.getElementById('sidebar-resource-body');
    if (!resBody) return;
    const checkboxes = resBody.querySelectorAll('input[type="checkbox"]');
    checkboxes.forEach(cb => { cb.checked = false; });

    const toRemove = ['skinnable', 'node:vein', 'node:herb', 'mailbox'];
    for (const cat of toRemove) {
        activatedPOICategories.delete(cat);
    }

    // Remove layers by group name patterns
    const groupPatterns = ['Skinning', 'vein', 'herb', 'Mailboxes'];
    for (const gName in groupedOverlays) {
        const shouldRemove = groupPatterns.some(p => gName.includes(p));
        if (!shouldRemove) continue;
        const layers = groupedOverlays[gName];
        for (const name in layers) {
            const layer = layers[name];
            if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
            if (layer && typeof layer.destroy === 'function') layer.destroy();
            if (layerNames[name]) delete layerNames[name];
        }
        delete groupedOverlays[gName];
    }
    scheduleGroupedLayerControlUpdate();
}

function clearSidebarUtilityCategories() {
    const utilBody = document.getElementById('sidebar-utility-body');
    if (!utilBody) return;
    const checkboxes = utilBody.querySelectorAll('input[type="checkbox"]');
    checkboxes.forEach(cb => { cb.checked = false; });

    if (ADTEnabled) removeADT();

    // Remove POI layers
    const layers = groupedOverlays['POI'];
    if (layers) {
        for (const name in layers) {
            const layer = layers[name];
            if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
            if (layer && typeof layer.destroy === 'function') layer.destroy();
            if (layerNames[name]) delete layerNames[name];
        }
        delete groupedOverlays['POI'];
    }

    if (recordPlayerPath) setRecord(false);
    scheduleGroupedLayerControlUpdate();
}

function clearSidebarLocationCategories() {
    const locBody = document.getElementById('sidebar-location-body');
    if (!locBody) return;
    const checkboxes = locBody.querySelectorAll('input[type="checkbox"]');
    checkboxes.forEach(cb => { cb.checked = false; });

    const toRemove = ['location:zones', 'location:subzones', 'location:teleport'];
    for (const cat of toRemove) {
        activatedPOICategories.delete(cat);
    }

    const groupKeys = ['Zones', 'SubZoneTexts', 'Teleport'];
    for (const gName of groupKeys) {
        const layers = groupedOverlays[gName];
        if (!layers) continue;
        for (const name in layers) {
            const layer = layers[name];
            if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
            if (layer && typeof layer.destroy === 'function') layer.destroy();
            if (layerNames[name]) delete layerNames[name];
        }
        delete groupedOverlays[gName];
    }
    scheduleGroupedLayerControlUpdate();
}

async function handleSidebarLocationChange(checkbox, locationType) {
    if (checkbox.checked) {
        activatedPOICategories.add(`location:${locationType}`);
        if (locationType === 'zones') addZones();
        else if (locationType === 'subzones') addSubZonesTexts();
        else if (locationType === 'teleport') await addTeleportPoints();
    } else {
        activatedPOICategories.delete(`location:${locationType}`);
        const groupKey = locationType === 'zones' ? 'Zones'
            : locationType === 'subzones' ? 'SubZoneTexts'
            : 'Teleport';
        const layers = groupedOverlays[groupKey];
        if (layers) {
            for (const name in layers) {
                const layer = layers[name];
                if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                if (layer && typeof layer.destroy === 'function') layer.destroy();
                if (layerNames[name]) delete layerNames[name];
            }
            delete groupedOverlays[groupKey];
        }
        scheduleGroupedLayerControlUpdate();
    }
}

function updateSidebarResourceState() {
    const zoneId = getSidebarZoneId();
    const resBody = document.getElementById('sidebar-resource-body');
    if (!resBody) return;
    const notice = document.getElementById('sidebar-zone-notice');
    const checkboxes = resBody.querySelectorAll('.sidebar-checkbox-row');

    if (!zoneId) {
        if (notice) notice.style.display = 'block';
        checkboxes.forEach(row => row.classList.add('disabled'));
    } else {
        if (notice) notice.style.display = 'none';
        checkboxes.forEach(row => row.classList.remove('disabled'));
    }
}

function updateSidebarCurrentZone() {
    const dropdown = document.getElementById('sidebar-zone-select');
    if (!dropdown) return;
    if (dropdown.value === 'current') {
        // Update the label to show current zone name
        const opt = dropdown.options[0];
        if (currentArea) {
            opt.textContent = `Current Zone (${currentArea.AreaName})`;
        } else {
            opt.textContent = 'Current Zone (none)';
        }
    }
    updateSidebarResourceState();
}

async function handleSidebarNpcCategoryChange(checkbox, npcType) {
    const zoneId = getSidebarZoneId();
    const factionFilter = getSidebarFactionFilter();

    if (checkbox.checked) {
        activatedPOICategories.add(`npc:${npcType}`);
        await addNpc(npcType, zoneId, factionFilter);
    } else {
        activatedPOICategories.delete(`npc:${npcType}`);
        // Remove layers for this npcType
        const layers = groupedOverlays[npcType];
        if (layers) {
            for (const name in layers) {
                const layer = layers[name];
                if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                if (layer && typeof layer.destroy === 'function') layer.destroy();
                if (layerNames[name]) delete layerNames[name];
            }
            delete groupedOverlays[npcType];
        }
        scheduleGroupedLayerControlUpdate();
    }
}

async function handleSidebarEliteChange(checkbox) {
    const zoneId = getSidebarZoneId();
    const factionFilter = getSidebarFactionFilter();

    if (checkbox.checked) {
        activatedPOICategories.add('elites');
        await addEliteNpcsToArea(zoneId, factionFilter);
    } else {
        activatedPOICategories.delete('elites');
        const layers = groupedOverlays['Elites'];
        if (layers) {
            for (const name in layers) {
                const layer = layers[name];
                if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                if (layer && typeof layer.destroy === 'function') layer.destroy();
                if (layerNames[name]) delete layerNames[name];
            }
            delete groupedOverlays['Elites'];
        }
        scheduleGroupedLayerControlUpdate();
    }
}

async function handleSidebarResourceChange(checkbox, resourceType) {
    const zoneId = getSidebarZoneId();
    if (!zoneId) return;

    if (checkbox.checked) {
        activatedPOICategories.add(resourceType);
        if (resourceType === 'skinnable') {
            await addSkinnableNpcsToArea(zoneId);
        } else if (resourceType === 'node:vein') {
            await addNodeSpawnsToArea(zoneId, 'vein');
        } else if (resourceType === 'node:herb') {
            await addNodeSpawnsToArea(zoneId, 'herb');
        } else if (resourceType === 'mailbox') {
            await addMailboxes();
        }
        scheduleGroupedLayerControlUpdate();
    } else {
        activatedPOICategories.delete(resourceType);
        // Find and remove the associated group layers
        const groupPatterns = {
            'skinnable': 'Skinning',
            'node:vein': 'vein',
            'node:herb': 'herb',
            'mailbox': 'Mailboxes'
        };
        const pattern = groupPatterns[resourceType];
        for (const gName in groupedOverlays) {
            if (resourceType === 'mailbox' ? gName === 'Mailboxes' : gName.includes(pattern)) {
                const layers = groupedOverlays[gName];
                for (const name in layers) {
                    const layer = layers[name];
                    if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                    if (layer && typeof layer.destroy === 'function') layer.destroy();
                    if (layerNames[name]) delete layerNames[name];
                }
                delete groupedOverlays[gName];
            }
        }
        scheduleGroupedLayerControlUpdate();
    }
}

function handleSidebarSearch() {
    const input = document.getElementById('sidebar-npc-search');
    const resultsContainer = document.getElementById('sidebar-search-results');
    if (!input || !resultsContainer) return;

    if (sidebarSearchTimeout) clearTimeout(sidebarSearchTimeout);

    sidebarSearchTimeout = setTimeout(() => {
        const query = input.value.trim();
        const factionFilter = getSidebarFactionFilter();
        const mapWide = document.getElementById('sidebar-map-wide')?.checked ?? false;
        const zoneId = mapWide ? null : getSidebarZoneId();
        const typeFamilySelect = document.getElementById('sidebar-type-family');
        const typeFamily = typeFamilySelect?.value || null;

        const results = searchCreaturesByName(query, factionFilter, zoneId, typeFamily);
        renderSearchResults(results, resultsContainer);
    }, 300);
}

function renderSearchResults(results, container) {
    container.innerHTML = '';
    if (results.length === 0) return;

    let iconIndex = 0;

    for (const creature of results) {
        const item = document.createElement('div');
        item.className = 'npc-search-result-item';

        const level = creature.MinLevel === creature.MaxLevel
            ? `[${creature.MinLevel}]`
            : `[${creature.MinLevel}-${creature.MaxLevel}]`;

        const typeName = creatureTypeNames[creature.Type] || '';
        const familyName = creatureFamilyNames[creature.Family] || '';
        const typeInfo = familyName
            ? ` - <span class="result-subname">${typeName} &gt; ${familyName}</span>`
            : typeName
                ? ` - <span class="result-subname">${typeName}</span>`
                : '';

        const iconDiv = document.createElement('div');
        iconDiv.className = 'result-icon';
        const iconName = Circles[iconIndex++ % Circles.length];
        iconDiv.style.cssText = getAtlasIconStyle(iconName, 16);
        item.appendChild(iconDiv);

        const textSpan = document.createElement('span');
        textSpan.innerHTML = `<span class="result-level">${level}</span> ${creature.Name}${typeInfo}`;
        item.appendChild(textSpan);

        item.onclick = () => {
            addNpcSpawns(creature.Entry, 'Search', iconName);
        };
        container.appendChild(item);
    }
}

async function replayActiveSidebarCategories() {
    const zoneId = getSidebarZoneId();
    const factionFilter = getSidebarFactionFilter();

    // Remove existing NPC category layers before replaying
    const npcTypes = ['vendor', 'vendorammo', 'vendorfood', 'vendorpoison',
        'vendorreagent', 'repair', 'classtrainer', 'professiontrainer',
        'flightmaster', 'innkeeper', 'spirithealer', 'stablemaster'];
    for (const npcType of npcTypes) {
        if (!activatedPOICategories.has(`npc:${npcType}`)) continue;
        const layers = groupedOverlays[npcType];
        if (layers) {
            for (const name in layers) {
                const layer = layers[name];
                if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                if (layer && typeof layer.destroy === 'function') layer.destroy();
                if (layerNames[name]) delete layerNames[name];
            }
            delete groupedOverlays[npcType];
        }
        await addNpc(npcType, zoneId, factionFilter);
    }

    // Remove and replay elites
    if (activatedPOICategories.has('elites')) {
        const eliteLayers = groupedOverlays['Elites'];
        if (eliteLayers) {
            for (const name in eliteLayers) {
                const layer = eliteLayers[name];
                if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                if (layer && typeof layer.destroy === 'function') layer.destroy();
                if (layerNames[name]) delete layerNames[name];
            }
            delete groupedOverlays['Elites'];
        }
        await addEliteNpcsToArea(zoneId, factionFilter);
    }

    // Remove and replay resource categories
    for (const cat of ['skinnable', 'node:vein', 'node:herb']) {
        if (!activatedPOICategories.has(cat)) continue;
        // Remove old layers
        const groupPatterns = { 'skinnable': 'Skinning', 'node:vein': 'vein', 'node:herb': 'herb' };
        const pattern = groupPatterns[cat];
        for (const gName in groupedOverlays) {
            if (gName.includes(pattern)) {
                const layers = groupedOverlays[gName];
                for (const name in layers) {
                    const layer = layers[name];
                    if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                    if (layer && typeof layer.destroy === 'function') layer.destroy();
                    if (layerNames[name]) delete layerNames[name];
                }
                delete groupedOverlays[gName];
            }
        }
        if (zoneId) {
            if (cat === 'skinnable') await addSkinnableNpcsToArea(zoneId);
            else if (cat === 'node:vein') await addNodeSpawnsToArea(zoneId, 'vein');
            else if (cat === 'node:herb') await addNodeSpawnsToArea(zoneId, 'herb');
        }
    }

    scheduleGroupedLayerControlUpdate();
}

function createSidebar() {
    const SidebarControl = L.Control.extend({
        options: { position: 'topright' },

        onAdd: function (map) {
            // Wrapper holds layers column (left) + sidebar (right)
            const wrapper = L.DomUtil.create('div', 'sidebar-wrapper');
            L.DomEvent.disableClickPropagation(wrapper);
            L.DomEvent.disableScrollPropagation(wrapper);

            // Left column: layer controls
            const layersCol = L.DomUtil.create('div', 'sidebar-layers-column', wrapper);
            sidebarLayersColumn = layersCol;

            // Right column: map filters sidebar
            const sidebar = L.DomUtil.create('div', 'map-filter-sidebar', wrapper);

            // Toggle button
            const toggleBtn = L.DomUtil.create('button', 'sidebar-toggle-btn', sidebar);
            toggleBtn.innerHTML = '◀';
            toggleBtn.title = 'Toggle sidebar';

            // Content wrapper
            const content = L.DomUtil.create('div', 'sidebar-content', sidebar);

            // Header
            const header = L.DomUtil.create('div', 'sidebar-header', content);
            header.textContent = 'Map Filters';

            let isCollapsed = false;
            const collapseFilters = (collapsed) => {
                isCollapsed = collapsed;
                content.style.display = isCollapsed ? 'none' : '';
                toggleBtn.innerHTML = isCollapsed ? '▶' : '◀';
                sidebar.style.width = isCollapsed ? 'auto' : '';
            };
            toggleBtn.onclick = () => collapseFilters(!isCollapsed);

            // ===== NPCs Section =====
            this._buildNpcSection(content);

            // ===== Resources Section =====
            this._buildResourceSection(content);

            // ===== Locations Section =====
            this._buildLocationSection(content);

            // ===== Utilities Section =====
            this._buildUtilitySection(content);

            // Auto-collapse on small screens or when not in edit mode
            if (window.innerWidth <= 768 || !enableUrlEdit) {
                collapseFilters(true);
            }

            return wrapper;
        },

        _buildSectionHeader: function (parent, title, clearFn) {
            const section = L.DomUtil.create('div', 'sidebar-section', parent);

            const header = L.DomUtil.create('div', 'section-header', section);
            const headerLeft = L.DomUtil.create('div', 'section-header-left', header);
            const chevron = L.DomUtil.create('span', 'section-chevron', headerLeft);
            chevron.textContent = '▼';
            const titleSpan = L.DomUtil.create('span', '', headerLeft);
            titleSpan.textContent = title;

            const clearBtn = L.DomUtil.create('button', 'section-clear-btn', header);
            clearBtn.textContent = 'Clear';
            clearBtn.onclick = (e) => {
                e.stopPropagation();
                if (clearFn) clearFn();
            };

            const body = L.DomUtil.create('div', 'section-body', section);

            header.onclick = (e) => {
                if (e.target === clearBtn) return;
                const isCollapsed = body.classList.toggle('collapsed');
                chevron.classList.toggle('collapsed', isCollapsed);
            };

            return body;
        },

        _buildNpcSection: function (parent) {
            const body = this._buildSectionHeader(parent, 'NPCs', clearSidebarNpcCategories);

            // Zone dropdown
            const zoneLabel = L.DomUtil.create('div', 'section-label', body);
            zoneLabel.textContent = 'Zone';
            const zoneSelect = L.DomUtil.create('select', 'sidebar-select', body);
            zoneSelect.id = 'sidebar-zone-select';

            const currentOpt = document.createElement('option');
            currentOpt.value = 'current';
            currentOpt.textContent = currentArea
                ? `Current Zone (${currentArea.AreaName})`
                : 'Current Zone (none)';
            zoneSelect.appendChild(currentOpt);

            // Populate with parent zones sorted by name
            const parentZones = Object.values(Zones)
                .filter(z => z.ParentAreaId === 0 && z.AreaID < AreaIDOffset)
                .sort((a, b) => a.AreaName.localeCompare(b.AreaName));

            for (const zone of parentZones) {
                const opt = document.createElement('option');
                opt.value = zone.AreaID;
                opt.textContent = zone.AreaName;
                zoneSelect.appendChild(opt);
            }

            zoneSelect.onchange = () => {
                updateSidebarResourceState();
                replayActiveSidebarCategories();
            };

            // Faction filter
            const factionLabel = L.DomUtil.create('div', 'section-label', body);
            factionLabel.textContent = 'Faction';
            const factionGroup = L.DomUtil.create('div', 'sidebar-radio-group', body);

            const factions = [
                { value: 'both', label: 'Both' },
                { value: 'alliance', label: 'Alliance' },
                { value: 'horde', label: 'Horde' }
            ];
            for (const f of factions) {
                const lbl = L.DomUtil.create('label', '', factionGroup);
                const radio = document.createElement('input');
                radio.type = 'radio';
                radio.name = 'sidebar-faction';
                radio.value = f.value;
                if (f.value === 'both') radio.checked = true;
                radio.onchange = () => replayActiveSidebarCategories();
                lbl.appendChild(radio);
                const span = document.createElement('span');
                span.textContent = f.label;
                lbl.appendChild(span);
            }

            // NPC category checkboxes
            const categoryList = L.DomUtil.create('div', '', body);
            categoryList.id = 'sidebar-npc-categories';

            const npcCategories = [
                { type: 'vendor', label: 'Vendor', icon: 'vendor' },
                { type: 'vendorammo', label: 'Vendor Ammo', icon: 'vendorammo' },
                { type: 'vendorfood', label: 'Vendor Food', icon: 'vendorfood' },
                { type: 'vendorpoison', label: 'Vendor Poison', icon: 'vendorpoison' },
                { type: 'vendorreagent', label: 'Vendor Reagent', icon: 'vendorreagent' },
                { type: 'repair', label: 'Repair', icon: 'repair' },
                { type: 'classtrainer', label: 'Class Trainer', icon: 'classtrainer' },
                { type: 'professiontrainer', label: 'Profession Trainer', icon: 'professiontrainer' },
                { type: 'flightmaster', label: 'Flight Master', icon: 'flightmaster' },
                { type: 'innkeeper', label: 'Innkeeper', icon: 'innkeeper' },
                { type: 'spirithealer', label: 'Spirit Healer', icon: 'spirithealer' },
                { type: 'stablemaster', label: 'Stable Master', icon: 'stablemaster' },
            ];

            for (const cat of npcCategories) {
                const row = L.DomUtil.create('label', 'sidebar-checkbox-row', categoryList);
                const cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.dataset.npcType = cat.type;
                cb.onchange = () => handleSidebarNpcCategoryChange(cb, cat.type);
                row.appendChild(cb);

                const iconDiv = L.DomUtil.create('div', 'cb-icon', row);
                iconDiv.style.cssText = getAtlasIconStyle(cat.icon, 20);

                const label = L.DomUtil.create('span', 'cb-label', row);
                label.textContent = cat.label;
            }

            // Elites checkbox
            const eliteRow = L.DomUtil.create('label', 'sidebar-checkbox-row', categoryList);
            const eliteCb = document.createElement('input');
            eliteCb.type = 'checkbox';
            eliteCb.dataset.npcType = 'elites';
            eliteCb.onchange = () => handleSidebarEliteChange(eliteCb);
            eliteRow.appendChild(eliteCb);

            const eliteIconDiv = L.DomUtil.create('div', 'cb-icon', eliteRow);
            eliteIconDiv.style.cssText = getAtlasIconStyle('Elite', 20);

            const eliteLabel = L.DomUtil.create('span', 'cb-label', eliteRow);
            eliteLabel.textContent = 'Elites';

            // Type / Family dropdown
            const typeFamilyLabel = L.DomUtil.create('div', 'section-label', body);
            typeFamilyLabel.textContent = 'Type / Family';
            const typeFamilySelect = L.DomUtil.create('select', 'sidebar-select', body);
            typeFamilySelect.id = 'sidebar-type-family';

            const allOpt = document.createElement('option');
            allOpt.value = '';
            allOpt.textContent = 'All';
            typeFamilySelect.appendChild(allOpt);

            const typeGroup = document.createElement('optgroup');
            typeGroup.label = 'Creature Type';
            for (const [id, name] of Object.entries(creatureTypeNames)) {
                const opt = document.createElement('option');
                opt.value = `type:${id}`;
                opt.textContent = name;
                typeGroup.appendChild(opt);
            }
            typeFamilySelect.appendChild(typeGroup);

            const familyGroup = document.createElement('optgroup');
            familyGroup.label = 'Beast Family';
            for (const [id, name] of Object.entries(creatureFamilyNames)) {
                const opt = document.createElement('option');
                opt.value = `family:${id}`;
                opt.textContent = name;
                familyGroup.appendChild(opt);
            }
            typeFamilySelect.appendChild(familyGroup);

            typeFamilySelect.onchange = handleSidebarSearch;

            // Name search
            const searchLabel = L.DomUtil.create('div', 'section-label', body);
            searchLabel.textContent = 'Search NPC';
            const searchInput = L.DomUtil.create('input', 'sidebar-input', body);
            searchInput.id = 'sidebar-npc-search';
            searchInput.type = 'text';
            searchInput.placeholder = 'Type NPC name...';
            searchInput.onkeyup = handleSidebarSearch;

            // Map-wide toggle
            const toggleRow = L.DomUtil.create('div', 'sidebar-toggle-row', body);
            const mapWideCheck = document.createElement('input');
            mapWideCheck.type = 'checkbox';
            mapWideCheck.id = 'sidebar-map-wide';
            toggleRow.appendChild(mapWideCheck);
            const toggleLabel = L.DomUtil.create('label', '', toggleRow);
            toggleLabel.textContent = 'Search map-wide';
            toggleLabel.htmlFor = 'sidebar-map-wide';

            // Search results container
            const searchResults = L.DomUtil.create('div', 'npc-search-results', body);
            searchResults.id = 'sidebar-search-results';
        },

        _buildResourceSection: function (parent) {
            const body = this._buildSectionHeader(parent, 'Resources', clearSidebarResourceCategories);
            body.id = 'sidebar-resource-body';

            const notice = L.DomUtil.create('div', 'zone-required-notice', body);
            notice.id = 'sidebar-zone-notice';
            notice.textContent = 'Select a zone to show resources';

            const resourceCategories = [
                { type: 'skinnable', label: 'Skinnable', icon: 'skinnable' },
                { type: 'node:vein', label: 'Mining Veins', icon: 'mining' },
                { type: 'node:herb', label: 'Herbs', icon: 'Silverleaf' },
                { type: 'mailbox', label: 'Mailboxes', icon: 'mailbox' },
            ];

            for (const cat of resourceCategories) {
                const row = L.DomUtil.create('label', 'sidebar-checkbox-row', body);
                const cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.dataset.resourceType = cat.type;
                cb.onchange = () => handleSidebarResourceChange(cb, cat.type);
                row.appendChild(cb);

                const iconDiv = L.DomUtil.create('div', 'cb-icon', body);
                iconDiv.style.cssText = getAtlasIconStyle(cat.icon, 20);
                row.appendChild(iconDiv);

                const label = L.DomUtil.create('span', 'cb-label', row);
                label.textContent = cat.label;
            }

            // Initial state
            requestAnimationFrame(() => updateSidebarResourceState());
        },

        _buildLocationSection: function (parent) {
            const body = this._buildSectionHeader(parent, 'Locations', clearSidebarLocationCategories);
            body.id = 'sidebar-location-body';

            const locationCategories = [
                { type: 'zones', label: 'Zones', icon: 'darkblue' },
                { type: 'subzones', label: 'Sub Zones', icon: 'green' },
                { type: 'teleport', label: 'Teleport Points', icon: 'poi' },
            ];

            for (const cat of locationCategories) {
                const row = L.DomUtil.create('label', 'sidebar-checkbox-row', body);
                const cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.dataset.locationType = cat.type;
                cb.onchange = () => handleSidebarLocationChange(cb, cat.type);
                row.appendChild(cb);

                const iconDiv = L.DomUtil.create('div', 'cb-icon', row);
                iconDiv.style.cssText = getAtlasIconStyle(cat.icon, 20);

                const label = L.DomUtil.create('span', 'cb-label', row);
                label.textContent = cat.label;
            }
        },

        _buildUtilitySection: function (parent) {
            const body = this._buildSectionHeader(parent, 'Utilities', clearSidebarUtilityCategories);
            body.id = 'sidebar-utility-body';

            // ADT Grid
            const adtRow = L.DomUtil.create('label', 'sidebar-checkbox-row', body);
            const adtCb = document.createElement('input');
            adtCb.type = 'checkbox';
            adtCb.onchange = () => {
                if (adtCb.checked) addADT();
                else removeADT();
            };
            adtRow.appendChild(adtCb);
            const adtLabel = L.DomUtil.create('span', 'cb-label', adtRow);
            adtLabel.textContent = 'ADT Grid';

            // POI Points
            const poiRow = L.DomUtil.create('label', 'sidebar-checkbox-row', body);
            const poiCb = document.createElement('input');
            poiCb.type = 'checkbox';
            poiCb.onchange = () => {
                if (poiCb.checked) addPoi(getSidebarZoneId() ?? currentArea?.AreaID);
                else {
                    const layers = groupedOverlays['POI'];
                    if (layers) {
                        for (const name in layers) {
                            const layer = layers[name];
                            if (LeafletMap.hasLayer(layer)) LeafletMap.removeLayer(layer);
                            if (layer && typeof layer.destroy === 'function') layer.destroy();
                            if (layerNames[name]) delete layerNames[name];
                        }
                        delete groupedOverlays['POI'];
                    }
                    scheduleGroupedLayerControlUpdate();
                }
            };
            poiRow.appendChild(poiCb);
            const poiIconDiv = L.DomUtil.create('div', 'cb-icon', poiRow);
            poiIconDiv.style.cssText = getAtlasIconStyle('poi', 20);
            const poiLabel = L.DomUtil.create('span', 'cb-label', poiRow);
            poiLabel.textContent = 'POI Points';

            // Record Path (only when enableUrlEdit)
            if (enableUrlEdit) {
                const recRow = L.DomUtil.create('label', 'sidebar-checkbox-row', body);
                const recCb = document.createElement('input');
                recCb.type = 'checkbox';
                recCb.onchange = () => setRecord(recCb.checked);
                recRow.appendChild(recCb);
                const recIconDiv = L.DomUtil.create('div', 'cb-icon', recRow);
                recIconDiv.style.cssText = getAtlasIconStyle('redcross', 20);
                const recLabel = L.DomUtil.create('span', 'cb-label', recRow);
                recLabel.textContent = 'Record Path';
            }

            // Coordinate Marker
            const coordLabel = L.DomUtil.create('div', 'section-label', body);
            coordLabel.textContent = 'Coordinate Marker';
            coordLabel.style.marginTop = '8px';

            const coordRow = L.DomUtil.create('div', 'sidebar-coord-row', body);
            const coordInput = L.DomUtil.create('input', 'sidebar-input', coordRow);
            coordInput.type = 'text';
            coordInput.placeholder = 'x y z [mapId]';
            coordInput.style.flex = '1';
            coordInput.style.marginBottom = '0';

            const coordBtn = L.DomUtil.create('button', 'sidebar-coord-btn', coordRow);
            coordBtn.textContent = '📍';
            coordBtn.title = 'Add marker at coordinates';

            const addCoord = () => {
                const text = coordInput.value.trim();
                if (!text) return;
                const parts = text.split(/\s+/);
                if (parts.length < 3) { alert('Format: x y z [mapId]'); return; }
                const x = parseFloat(parts[0]);
                const y = parseFloat(parts[1]);
                const z = parseFloat(parts[2]);
                const mapId = parts.length >= 4 ? parseInt(parts[3]) : null;
                if (isNaN(x) || isNaN(y) || isNaN(z)) { alert('Invalid coordinates'); return; }
                if (mapId !== null && mapId !== config.MapID) {
                    alert(`Map ${mapId} does not match current map ${config.MapID}`);
                    return;
                }
                addCustomMarker(x, y, z);
                coordInput.value = '';
            };

            coordBtn.onclick = addCoord;
            coordInput.onkeydown = (e) => { if (e.key === 'Enter') addCoord(); };
        }
    });

    return new SidebarControl();
}

function setImage(style, npcType) {

    const iconPos = IconAtlas[npcType];
    const leftPx = -(iconPos[0] * aSize);
    const topPx = -(iconPos[1] * aSize);

    style.backgroundPosition = `${leftPx}px ${topPx}px`;
    style.width = `${aSize}px`;
    style.height = `${aSize}px`;
}

//////////////////////////////////////////////////

function getAreaBounds(area) {
    return [
        [worldTolatLng(area.LocBottom, area.LocLeft)],
        [worldTolatLng(area.LocTop, area.LocRight)]
    ];
}



function addZones() {

    addGroupLayer('Zones', 'Zones');

    for (const area of Object.values(Zones)) {
        if (area.AreaID < AreaIDOffset) {
            continue;
        }

        if (area.AreaID == 0) {
            console.error(area);
            continue;
        }

        const bounds = getAreaBounds(area);
        const color = getRandomColor();

        const rect = L.rectangle(bounds, { color: color, weight: 0.5 });

        const originalArea = Zones[area.AreaID - AreaIDOffset];
        if (originalArea == null) {
            console.error(area);
            continue;
        }
        addToggleLayer('Zones', originalArea.AreaName, rect, false);
    }
}

function addSubZones(areaId) {

    //const visible = areaId != null;

    for (const area of Object.values(SubZones)
        .filter(area => areaId == null || area.AreaID == areaId)) {

        const color = getRandomColor();
        const bounds = getAreaBounds(area);

        const rect = L.rectangle(bounds, { color: color, weight: 1 })

        addToggleLayer("SubZones", area.AreaName, rect, false);
    }
}

function addSubZonesTexts(areaId) {
    const container = pixiOverlay.utils.getContainer();
    const groupName = "SubZoneTexts";
    const renderer = pixiOverlay.utils.getRenderer();

    // Step 1: Count duplicates
    const nameCounts = {};
    for (const area of Object.values(SubZones)) {
        nameCounts[area.AreaName] = (nameCounts[area.AreaName] ?? 0) + 1;
    }

    const textStyle = new PIXI.TextStyle({
        fontFamily: 'Arial',
        fontSize: 15,
        fill: '#ffffff',
        stroke: '#000000',
        strokeThickness: 4,
        align: 'center',
        wordWrap: true,
        wordWrapWidth: 150
    });

    for (const area of Object.values(SubZones)
        .filter(area => areaId == null || area.AreaID == areaId)) {

        const label = area.AreaName;

        // Unique toggle label if duplicates
        const toggleLabel = nameCounts[area.AreaName] > 1
            ? `${area.AreaName} - ${area.AreaID}`
            : area.AreaName;

        // Step 2: Create PIXI.Text
        const text = new PIXI.Text(label, textStyle);
        text.anchor.set(0.5);

        // Step 3: Convert to sprite via texture
        const texture = renderer.generateTexture(text);
        const sprite = new PIXI.Sprite(texture);
        sprite.anchor.set(0.5);
        sprite.scale.set(2);

        // Step 4: Positioning
        const bounds = getAreaBounds(area).flat();
        const boundsObj = L.latLngBounds(bounds);
        const northCenterLatLng = L.latLng(boundsObj.getNorth(), boundsObj.getCenter().lng);
        const offsetLat = northCenterLatLng.lat - 2;
        sprite.latlng = L.latLng(offsetLat, northCenterLatLng.lng);

        sprite.visible = true;
        sprite.label = toggleLabel;

        container.addChild(sprite);

        const groupLayer = createPixiSpriteGroupLayer(groupName, [sprite]);
        addToggleLayer(groupName, toggleLabel, groupLayer, true);
    }

    scheduleGroupedLayerControlUpdate();
    schedulePixiRedraw();
}

function flipXYLower(p) {
    return {
        x: p.Y || p.y,
        y: p.X || p.x
    };
}

function revertFlipXYUpper(p) {
    return {
        X: p.X,
        Y: p.Y
    };
}

async function loadMapPath(areaId, path, color = 'blue') {
    const area = Zones[areaId];
    if (area == null) {
        console.error("Area not found: " + areaId);
        return;
    }

    if (config.MapID != area.MapID) {
        console.error("Currently loaded MapID does not match with: " + area.MapID);
        return;
    }

    const response = await fetch(path)
        .catch(e => console.error(e));

    const mapPoints = await response.json();

    var latlngs = [];

    for (const point of mapPoints) {
        const flippedPoint = flipXYLower(point);
        const worldPos = localToWorld(area, flippedPoint);
        latlngs.push(worldTolatLng(worldPos.x, worldPos.y));
    }
    const polyline = L.polyline(latlngs, { color: color });

    polyline.bindPopup(path);

    //.addTo(editableLayers);
    //editablePathLayerControl.addOverlay(polyline, path, true);

    bindRightClick(polyline, 'Paths', path);

    addToggleLayer('Paths', path, polyline, true);
}

async function loadMapPathByFilter(filter, color = 'random') {

    const response = await fetch(`/api/Path?filter=${encodeURIComponent(filter)}`)
        .catch(e => console.error(e));

    const fileNames = await response.json();

    for (const name of fileNames) {
        if (name.includes("optimal") || name.includes("Herb") || name.includes("Vein")) {
            continue;
        }

        // split by "_"" or "\" or "." or " "
        const regex = /[\\_. ]/g;
        const tokens = name.split(regex).filter(Boolean);

        let area = null;

        // Try to find matching zone by AreaName
        for (const rawToken of tokens) {
            const token = rawToken.toLowerCase();
            area = Object.values(Zones).find(zone =>
                zone.AreaName.toLowerCase().includes(token)
            );

            if (area) break;
        }

        // If no match found, try to find by race starting zone
        if (area == null) {
            for (const rawToken of tokens) {
                const token = rawToken.toLowerCase();
                // Try raceToZone mapping
                const raceZoneId = raceToZone[rawToken];
                if (raceZoneId !== undefined) {
                    area = Zones[raceZoneId];
                    if (area) break;
                }
            }
        }

        if (area == null) {
            console.warn("Area not found: " + name + " -- " + filter);
            continue;
        }

        if (config.MapID != area.MapID) {
            console.warn("Currently loaded MapID does not match with: " + area.MapID);
            continue;
        }

        loadMapPath(area.AreaID, name, color == "random" ? getRandomColor() : color);
    }
}


function getRandomColor() {
    return '#' + Math.floor(Math.random() * 0xFFFFFF)
        .toString(16)
        .padStart(6, '0')
        .toUpperCase();
}

///////////////////////////////////////////////////////////////////////
//////////////////// drawing //////////////////////////////////////////
///////////////////////////////////////////////////////////////////////

const drawControl = new L.Control.Draw({
    position: 'topleft',
    draw: {
        polyline: {
            showLength: false,
            shapeOptions: {
                lazyMode: true,
            }
        },
        polygon: {
            allowIntersection: false,
            showArea: true,
            shapeOptions: {
                dashArray: '20,15',
                lineJoin: 'round',
            },
            drawError: {
                timeout: 1000
            },
            shapeOptions: {

            }
        },
        circle: {
            shapeOptions: {
            }
        },
        rectangle: {
            shapeOptions: {
                clickable: true
            }
        },
        marker: false
    },
    edit: {
        featureGroup: editableLayers,
        edit: true,
        remove: true
    }
});

function bindRightClick(layer, groupName, pathName) {
    layer.groupName = groupName;
    layer.PathName = pathName;

    layer.on('contextmenu', function (e) {

        // Middle click saves the path if editable
        layer.on('mousedown', async function (e) {
            if (e.originalEvent.button === 1 && editableLayers.hasLayer(layer)) {
                //e.preventDefault();
                //e.stopPropagation();

                const latlngs = layer.getLatLngs?.();
                if (!latlngs || latlngs.length === 0) {
                    console.warn("Layer has no coordinates to save.");
                    return;
                }

                try {
                    // Step 1: Convert to world coordinates
                    const worldPoints = latlngs.map(latLng => latLngToWorld(latLng));

                    // Step 2: Fetch areaId and Z for each world point
                    const areaDataList = await Promise.all(
                        worldPoints.map(pos => getAreaIdAndZFromService(pos))
                    );

                    // Step 3: Convert each point to map percentage coords
                    const mapPoints = areaDataList.map((areaData, index) => {
                        const world = worldPoints[index];
                        const areaId = areaData.areaId;

                        const map = worldToPercentage(world, areaId); // returns { p: {x, y, z}, ... }
                        return {
                            x: Number(map.p.x),
                            y: Number(map.p.y),
                            z: Number(0)//map.p.z
                        };
                    });

                    // Step 4: Save path
                    await savePath(pathName, mapPoints);
                    console.log(`${pathName} Path saved successfully!`);
                } catch (err) {
                    console.error("Failed to save path:", err);
                }

                // Disable editing and remove from editable group
                if (layer.editing?.disable) {
                    layer.editing.disable();
                }
                editableLayers.removeLayer(layer);
                assignLayer(layer.groupName, layer.PathName, layer, true);
            }
        });

        const isEditable = editableLayers.hasLayer(layer);
        if (isEditable) {
            // Disable editing and remove from editable group
            if (layer.editing?.disable) {
                layer.editing.disable();
            }
            editableLayers.removeLayer(layer);
            assignLayer(layer.groupName, layer.PathName, layer, true);
        } else {
            // Enable editing and add to editable group
            editableLayers.addLayer(layer);
            if (layer.editing?.enable) {
                layer.editing.enable();
            }
        }
    });
}

function flashLayer(layer, duration = 600) {
    const originalStyle = {
        color: layer.options.color,
        weight: layer.options.weight,
        opacity: layer.options.opacity,
        dashArray: layer.options.dashArray
    };

    layer.setStyle({
        color: '#ffff00',
        weight: 6,
        opacity: 1,
        dashArray: '5, 5'
    });

    setTimeout(() => {
        layer.setStyle(originalStyle);
    }, duration);
}

/////////////////////////////////////////////////
////////////// signalR ///////////////////////////
/////////////////////////////////////////////////

window.addEventListener('DOMContentLoaded', function () {

    if (typeof signalR === "undefined" || signalR == null) {
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/watchHub")
        .withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())
        .withAutomaticReconnect([0, 3000, 5000, 10000, 15000, 30000])
        .build();

    connection.start().then(function () {
        console.log("Connected to SignalR Hub");
    }).catch(function (err) {
        return console.error(err.toString());
    });

    removeMesh = function (name) {
        const layer = layerNames[name];
        if (!layer) return;

        if (LeafletMap.hasLayer(layer)) {
            requestAnimationFrame(() => {
                LeafletMap.removeLayer(layer);
            });
        }

        if (layerNames[name]) {
            delete layerNames[name];
        }

        scheduleGroupedLayerControlUpdate();
    }

    clear = function () {

        const groupName = 'Watch';

        const layers = groupedOverlays[groupName];
        for (const layerName in layers) {
            const layer = layers[layerName];
            if (LeafletMap.hasLayer(layer)) {
                requestAnimationFrame(() => {
                    LeafletMap.removeLayer(layer);
                });
            }

            // Properly cleanup PixiSpriteGroupLayer instances
            if (layer && typeof layer.destroy === 'function') {
                layer.destroy();
            }

            if (layerNames[layerName]) {
                delete layerNames[layerName];
            }
        }

        delete groupedOverlays[groupName];
        removeActivatedPOICategory(groupName);

        scheduleGroupedLayerControlUpdate();
    };

    getColor = function (color) {
        switch (color) {
            case 1: return "#FF0000"; // Red
            case 2: return "#008000"; // Green
            case 3: return "#0000FF"; // Blue
            case 4: return "#008080"; // Teal
            case 5: return "#008080"; // Teal again
            case 6: return "#FF9900"; // Orange
            case 7: return "#FFFF00"; // Yellow
            case 8: return "#000000"; // Black
            case 9: return "#FF00FF"; // Magenta
            case 10: return "#00FFFF"; // Cyan or something else if needed
            default: return "#FFFFFF"; // White
        }
    };

    connection.on("removeMesh", removeMesh);
    connection.on("clear", clear);

    connection.on("drawLine", (array, height, color, name) => {
        if (array.length === 0) return;
        if (LeafletMap == null) return;

        const existing = layerNames[name];
        if (existing && existing.setLatLng) {
            requestAnimationFrame(() => {
                const latlng = worldTolatLng(array[0], array[1]);
                existing.setLatLng(latlng);
            });
            return;
        }
        requestAnimationFrame(async () => {

            addGroupLayer('Watch', 'Watch');

            const markerIcon = L.divIcon({
                className: 'poiatlas',
                iconSize: [aSize, aSize],
                html: atlasImgName('poi')
            });

            const worldPos = { x: array[0], y: array[1] };

            const latlng = worldTolatLng(worldPos.x, worldPos.y);

            const marker = new L.marker(latlng, { icon: markerIcon });

            bindPopupEnrichCoordinates(marker, worldPos, name);

            addToggleLayer('Watch', name, marker);

            scheduleGroupedLayerControlUpdate();
        });
    });

    connection.on("drawPath", (arrays, color, name) => {
        if (arrays.length === 0) return;
        if (LeafletMap == null) return;

        const latlngs = arrays.map(([x, y]) => worldTolatLng(x, y));
        const existing = layerNames[name];
        if (existing && existing.setLatLngs) {
            requestAnimationFrame(() => {
                existing.setLatLngs(latlngs);
                existing.setStyle({ color: getColor(color) });
            });
            return;
        }

        requestAnimationFrame(() => {

            addGroupLayer('Watch', 'Watch');

            const resolvedColor = getColor(color);

            const polyline = L.polyline(latlngs, {
                color: resolvedColor,
                weight: 1,
                opacity: 1
            }).bindPopup(name);

            addToggleLayer('Watch', name, polyline);
            scheduleGroupedLayerControlUpdate();
        });
    });
});