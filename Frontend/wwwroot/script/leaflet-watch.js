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
    if (redrawScheduled) return;

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
        x.AreaName != "Hyjal"
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

const npcFlags = {
    // None
    "none": 0,

    // Interaction
    "gossip": 1 << 0,              // 0x00000001
    "questgiver": 1 << 1,          // 0x00000002
    "spellclick": 1 << 24,         // 0x01000000
    "playervehicle": 1 << 25,      // 0x02000000
    "mailbox": 1 << 26,            // 0x04000000

    // Trainers
    "trainer": 1 << 4,             // 0x00000010
    "classtrainer": 1 << 5,        // 0x00000020
    "professiontrainer": 1 << 6,   // 0x00000040

    // Vendors
    "vendor": 1 << 7,              // 0x00000080
    "vendorammo": 1 << 8,          // 0x00000100
    "vendorfood": 1 << 9,          // 0x00000200
    "vendorpoison": 1 << 10,       // 0x00000400
    "vendorreagent": 1 << 11,      // 0x00000800
    "repair": 1 << 12,             // 0x00001000

    // Services
    "flightmaster": 1 << 13,       // 0x00002000
    "spiritgealer": 1 << 14,       // 0x00004000
    "spiritguide": 1 << 15,        // 0x00008000
    "innkeeper": 1 << 16,          // 0x00010000
    "banker": 1 << 17,             // 0x00020000
    "petitioner": 1 << 18,         // 0x00040000
    "tabarddesigner": 1 << 19,     // 0x00080000
    "battlemaster": 1 << 20,       // 0x00100000
    "auctioneer": 1 << 21,         // 0x00200000
    "stablemaster": 1 << 22,       // 0x00400000
    "guildbanker": 1 << 23,        // 0x00800000

    // Special
    "artifactpowerrespec": 1 << 27, // 0x08000000
    "transmogrifier": 1 << 28,      // 0x10000000
    "vaultkeeper": 1 << 29,         // 0x20000000
    "wildbattlepet": 1 << 30,       // 0x40000000
    "blackbarket": 1 << 31          // 0x80000000
};

function getCreatureByFlag(flag, excludedFactions = []) {
    return Object.values(creatures).filter(c => {
        const hasFlag = (c.NpcFlag & flag) !== 0;
        const isExcluded = excludedFactions.includes(c.Faction);
        return hasFlag && !isExcluded;
    });
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
    if (LeafletMap) {
        LeafletMap.remove();
        LeafletMap = null;
    }

    if (pixiOverlay) {
        pixiOverlay.remove();
        pixiOverlay = null;
    }

    pixiContainer.removeChildren();

    groupedOverlays = {
        "Zones": {},
        "Paths": {},
        "Watch": {},
    };

    layerNames = {};
    groupedLayerControls = [];
}

async function init(e, c, z, x, y, urlEdit) {

    //disposeMap(); // clean previous state

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
                sprite.scale.set(aSize / sprite.texture.width * scaleFactor * 2);
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

    //setBoundToArea(12);

    await load();

    //editablePathLayerControl = L.control.layers(null, null, { collapsed: true, autoZIndex: false }).addTo(LeafletMap);

    if (enableUrlEdit) {
        LeafletMap.addLayer(editableLayers);
        LeafletMap.addControl(drawControl);

        new L.Control.buttonADT().addTo(LeafletMap);
        new L.Control.buttonPOI().addTo(LeafletMap);
        new L.Control.buttonPOIPathfinder().addTo(LeafletMap);

        L.Control.buttonRecord = createLeafletButtonControl({
            className: 'poiatlas',
            setImageFn: setImage,
            onClick: () => {
                const isActive = recordPlayerPath;
                setRecord(!isActive);
            }
        });

        new L.Control.buttonRecord('redcross').addTo(LeafletMap);

        new L.Control.buttonNpcPOI("vendor").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("vendorammo").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("vendorfood").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("vendorpoison").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("vendorreagent").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("repair").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("classtrainer").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("professiontrainer").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("flightmaster").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("innkeeper").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("spirithealer").addTo(LeafletMap);
        new L.Control.buttonNpcPOI("stablemaster").addTo(LeafletMap);

        new L.Control.buttonSkinnablePOI('skinnable').addTo(LeafletMap);
        new L.Control.buttonMineablePOI('Copper Vein').addTo(LeafletMap);
        new L.Control.buttonHerbPOI('Silverleaf').addTo(LeafletMap);
    }

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
        processOffsetClick(e);
    });


    const mapContainer = document.getElementById('js-map');

    const resizeObserver = new ResizeObserver(() => {
        requestAnimationFrame(() => {
            LeafletMap.invalidateSize();
            pixiOverlay.redraw();
        })
    });

    resizeObserver.observe(mapContainer);

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

    toggleSearchControls()

    //testdrawIconAtlasPixi();
}

async function updateArea(areaId) {
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

    // Use 'click' instead of 'pointertap' as it's more reliable
    sprite.on('pointertap', (e) => {
        //console.log('PIXI Sprite clicked!', sprite.latlng, window.location.pathname);
        handler(e);
    });

    //sprite.on('click', (e) => {
    //console.log('PIXI Sprite clicked!', sprite.latlng, window.location.pathname);
    //handler(e);
    //});

    // hoover
    sprite.on('pointerover', (e) => {
        //console.log('PIXI Sprite hovered!', sprite.latlng);
        //console.log(sprite.worldTransform, sprite.getBounds(), sprite.interactive);
        // Optionally change cursor or style here
    });

    //sprite.on('pointerout', (e) => {
    //console.log('PIXI Sprite unhovered!', sprite.latlng);
    //});
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

        requestAnimationFrame(() => {
            addZoomButtonsToLayerControl(control);
            addCloseButtonToGroupLayers(control);
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
        const container = pixiContainer;
        for (const sprite of this._sprites) {
            //if (container.children.includes(sprite)) {
            //    container.removeChild(sprite);
            //}
            //sprite.destroy();
        }
        //this._sprites = [];
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

    addZones();

    //addGroupLayer('SubZones', 'SubZones');
    //addSubZones();

    addGroupLayer('SubZoneTexts', 'SubZoneTexts');
    addSubZonesTexts();

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

async function addNpc(npcType) {
    if (!currentArea) return;

    addGroupLayer(npcType, npcType);

    const flag = npcFlags[npcType];
    const matches = getCreatureByFlag(flag, []);
    const hitboxArea = Zones[currentArea.AreaID + AreaIDOffset] ?? currentArea;
    const texture = getPixiIconTexture(npcType);

    for (const creature of matches) {
        const spawns = spawnLocations[creature.Entry];
        if (!spawns) continue;

        const worldPos = spawns[0];

        var contain = false
        for (const subzone of getSubZones(currentArea.AreaID)) {
            //const subzoneArea = Zones[subzone.AreaID];
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

    lastRenderArea = currentArea;
}



L.Control.buttonNpcPOI = createLeafletButtonControl({
    className: 'poiatlas',
    setImageFn: setImage,
    onClick: (npcType) => addNpc(npcType),
});

L.Control.buttonADT = createLeafletButtonControl({
    iconHTML: `
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
            <rect x="1" y="1" width="6" height="6" stroke="black" stroke-width="2"/>
            <rect x="1" y="11" width="6" height="6" stroke="black" stroke-width="2"/>
            <rect x="11" y="1" width="6" height="6" stroke="black" stroke-width="2"/>
            <rect x="11" y="11" width="6" height="6" stroke="black" stroke-width="2"/>
        </svg>`,
    onClick: () => {
        if (LeafletMap.hasLayer(ADTGridLayer)) removeADT();
        else addADT();
    }
});


L.Control.buttonPOI = createLeafletButtonControl({
    iconHTML: `
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M10 18C10 18 4 12.5 4 8C4 4.68629 6.68629 2 10 2C13.3137 2 16 4.68629 16 8C16 12.5 10 18 10 18Z" stroke="black" stroke-width="2" fill="none"/>
            <circle cx="10" cy="8" r="2.5" fill="black"/>
        </svg>`,
    onClick: () => addPoi(currentArea?.AreaID)
});

L.Control.buttonPOIPathfinder = createLeafletButtonControl({
    iconHTML: `
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
            <!-- Circle lens -->
            <circle cx="8" cy="8" r="5" stroke="black" stroke-width="2" fill="none" />
            <!-- Handle -->
            <line x1="11.5" y1="11.5" x2="17" y2="17" stroke="black" stroke-width="2" stroke-linecap="round" />
        </svg>`,
    onClick: () => toggleSearchControls()
});


L.Control.buttonSkinnablePOI = createLeafletButtonControl({
    className: 'poiatlas',
    setImageFn: setImage,
    onClick: async () => {
        if (!currentArea) return;
        await addSkinnableNpcsToArea(currentArea.AreaID);
        scheduleGroupedLayerControlUpdate();
    },
});

L.Control.buttonMineablePOI = createLeafletButtonControl({
    className: 'poiatlas',
    setImageFn: setImage,
    onClick: async () => {
        if (!currentArea) return;
        await addNodeSpawnsToArea(currentArea.AreaID, 'vein');
        scheduleGroupedLayerControlUpdate();
    },
});

L.Control.buttonHerbPOI = createLeafletButtonControl({
    className: 'poiatlas',
    setImageFn: setImage,
    onClick: async () => {
        if (!currentArea) return;
        await addNodeSpawnsToArea(currentArea.AreaID, 'herb');
        scheduleGroupedLayerControlUpdate();
    },
});


function toggleSearchControls() {
    const searchParam = document.getElementById('searchParam');
    const watchtopbar = document.getElementById('watchtopbar');

    if (searchParam != null) {
        searchParam.style.display = searchParam.style.display === 'none' ? 'block' : 'none';
    }
    if (watchtopbar != null) {
        watchtopbar.style.display = watchtopbar.style.display === 'none' ? 'block' : 'none';
    }
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