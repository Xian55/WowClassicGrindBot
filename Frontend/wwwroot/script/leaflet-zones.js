/*
    Cost-zone authoring: roads (attract routes) and danger zones (avoid / block).

    Shapes are stored in WORLD coordinates - the same space PPather's CostZones
    rasterizes onto the MCNK grid - so latLngToWorld/worldTolatLng from
    leaflet-watch.js convert straight to and from the on-disk format.

    Files are keyed by UIMapId, one per zone, so a shape is filed under whichever
    WorldMapArea contains its anchor point. Saving rewrites that whole file, then
    asks the pather to reload; there is no rebake and no navmesh invalidation
    because these are cost changes, not geometry changes.

    Drawing:
      circle / rectangle -> danger zone
      polyline           -> road, but only while "Draw roads" is toggled on,
                            since polylines otherwise mean bot Paths.
*/

const COSTZONE_GROUP = 'Cost Zones';
const ROAD_LAYER = 'Roads';
const DANGER_LAYER = 'Danger Zones';

const ROAD_COLOR = '#39b54a';
const DANGER_AVOID_COLOR = '#e8a33d';
const DANGER_BLOCK_COLOR = '#d9453d';

// Authored roads are 2 MCNK chunks per side; penalty 500 -> ~6x cost factor.
const DEFAULT_ROAD_WIDTH = 2;
const DEFAULT_PENALTY = 500;

const MODE_AVOID = 0;
const MODE_BLOCK = 1;

let costZoneContinent = 'Azeroth';
let roadDrawMode = false;

let roadGroup = null;
let dangerGroup = null;

// Every authored shape, with the metadata that is not recoverable from geometry.
let costZoneShapes = [];

function costZoneWorldToUiMapId(world) {
    // Mirrors WorldMapAreaDB.GetWorldMapArea: WMADB is already filtered to this
    // continent's MapID by filterContientsAndInvalid.
    const hits = WMADB.filter(a =>
        world.x <= a.LocTop && world.x >= a.LocBottom &&
        world.y <= a.LocLeft && world.y >= a.LocRight);

    if (hits.length === 0) {
        return -1;
    }

    // Overlapping areas (Silithus/Feralas) - prefer the smallest, it is the
    // more specific zone.
    hits.sort((a, b) =>
        Math.abs((a.LocTop - a.LocBottom) * (a.LocLeft - a.LocRight)) -
        Math.abs((b.LocTop - b.LocBottom) * (b.LocLeft - b.LocRight)));

    return hits[0].UIMapId;
}

function dangerColor(mode) {
    return mode === MODE_BLOCK ? DANGER_BLOCK_COLOR : DANGER_AVOID_COLOR;
}

async function costZoneInit(continent) {
    costZoneContinent = continent;
    costZoneShapes = [];

    roadGroup = addGroupLayer(COSTZONE_GROUP, ROAD_LAYER);
    dangerGroup = addGroupLayer(COSTZONE_GROUP, DANGER_LAYER);

    addToggleLayer(COSTZONE_GROUP, ROAD_LAYER, roadGroup, true);
    addToggleLayer(COSTZONE_GROUP, DANGER_LAYER, dangerGroup, true);

    try {
        await costZoneLoad();
    } catch (err) {
        console.error('Failed to load cost zones', err);
    }

    scheduleGroupedLayerControlUpdate();
}

async function costZoneLoad() {
    const roads = await fetch(`/api/Road?continent=${encodeURIComponent(costZoneContinent)}`)
        .then(r => r.ok ? r.json() : {});

    for (const [uiMapId, data] of Object.entries(roads)) {
        for (const segment of (data.roads ?? [])) {
            costZoneAddRoad(
                segment.points.map(p => worldTolatLng(p.x, p.y)),
                { uiMapId: +uiMapId, name: segment.name, width: segment.width });
        }
    }

    const danger = await fetch(`/api/DangerZone?continent=${encodeURIComponent(costZoneContinent)}`)
        .then(r => r.ok ? r.json() : {});

    for (const [uiMapId, data] of Object.entries(danger)) {
        for (const c of (data.circles ?? [])) {
            const layer = L.circle(worldTolatLng(c.centerX, c.centerY), { radius: 0 });
            // Radius is a world distance; Leaflet wants it in layer units, so
            // derive it from two converted points rather than guessing a scale.
            layer.setRadius(costZoneWorldRadiusToLayer(c.centerX, c.centerY, c.radius));
            costZoneRegister(layer, 'circle', {
                uiMapId: +uiMapId, name: c.name, penalty: c.penalty, mode: c.mode ?? MODE_AVOID
            });
        }

        for (const r of (data.rectangles ?? [])) {
            const layer = L.rectangle([
                worldTolatLng(r.minX, r.minY),
                worldTolatLng(r.maxX, r.maxY)]);
            costZoneRegister(layer, 'rect', {
                uiMapId: +uiMapId, name: r.name, penalty: r.penalty, mode: r.mode ?? MODE_AVOID
            });
        }
    }
}

/*
    L.Circle's radius is in PROJECTED CRS units for a non-Earth CRS - Leaflet's
    Circle._project subtracts the radius from crs.project(latlng) directly. This
    map uses L.CRS.Simple, so it must not be a layer-pixel distance: those scale
    with zoom, which would both draw the circle at the wrong size and corrupt the
    stored radius whenever it was saved at a different zoom than it was loaded.
*/
function costZoneWorldRadiusToLayer(centerX, centerY, worldRadius) {
    const crs = LeafletMap.options.crs;
    const center = crs.project(worldTolatLng(centerX, centerY));
    const edge = crs.project(worldTolatLng(centerX + worldRadius, centerY));
    return center.distanceTo(edge);
}

function costZoneLayerRadiusToWorld(layer) {
    const crs = LeafletMap.options.crs;
    const center = layer.getLatLng();
    const projected = crs.project(center);
    const edge = crs.unproject(L.point(projected.x + layer.getRadius(), projected.y));

    const wc = latLngToWorld(center);
    const we = latLngToWorld(edge);
    return Math.hypot(we.x - wc.x, we.y - wc.y);
}

function costZoneAddRoad(latlngs, meta) {
    const layer = L.polyline(latlngs, { color: ROAD_COLOR, weight: 4, opacity: 0.9 });
    costZoneRegister(layer, 'road', meta);
}

function costZoneRegister(layer, kind, meta) {
    const shape = {
        layer,
        kind,
        uiMapId: meta.uiMapId,
        name: meta.name || (kind === 'road' ? 'road' : 'zone'),
        width: meta.width ?? DEFAULT_ROAD_WIDTH,
        penalty: meta.penalty ?? DEFAULT_PENALTY,
        mode: meta.mode ?? MODE_AVOID
    };

    if (kind !== 'road') {
        layer.setStyle({ color: dangerColor(shape.mode), fillOpacity: 0.25 });
    }

    layer.costZone = shape;
    costZoneShapes.push(shape);

    layer.addTo(kind === 'road' ? roadGroup : dangerGroup);
    costZoneBindPopup(shape);

    // The Pixi overlay canvas sits above the vector renderer and takes pointer
    // events, and the map has a global click handler. Claim the click on the
    // shape itself so the property popup always opens.
    layer.options.interactive = true;
    layer.on('click', (e) => {
        L.DomEvent.stopPropagation(e);
        layer.openPopup(e.latlng);
    });

    if (layer.bringToFront) {
        layer.bringToFront();
    }

    return shape;
}

// The shape whose popup is currently open. The popup buttons are handled by
// ONE delegated document-level listener rather than listeners on the buttons
// themselves, because the buttons do not survive: the popup content is a
// function, and Leaflet re-evaluates it AFTER 'popupopen' (its built-in
// bindPopup click handler calls popup.update() alongside our own
// openPopup(e.latlng), replacing every child of .leaflet-popup-content).
// Listeners attached at popupopen die with those children - verified live with
// a MutationObserver: tag at popupopen, then +7/-7 childList mutations, twice.
// Delegation at the document level survives any re-render, whoever causes it.
//
// CAPTURE phase, deliberately: disableClickPropagation on the popup root stops
// the bubble phase there, so a bubbling document listener would never hear
// these clicks. Capture runs on the way DOWN, before the root can stop it.
let costZoneActiveShape = null;

document.addEventListener('click', async (e) => {
    const shape = costZoneActiveShape;
    if (!shape || !e.target.closest('.leaflet-popup')) return;

    const root = e.target.closest('.leaflet-popup');

    if (e.target.closest('.cz-save')) {
        shape.name = root.querySelector('.cz-name').value.trim() || shape.name;

        if (shape.kind === 'road') {
            shape.width = Math.max(1, parseInt(root.querySelector('.cz-width').value, 10) || DEFAULT_ROAD_WIDTH);
        } else {
            shape.penalty = parseFloat(root.querySelector('.cz-penalty').value) || 0;
            shape.mode = parseInt(root.querySelector('.cz-mode').value, 10);
            shape.layer.setStyle({ color: dangerColor(shape.mode) });
        }

        shape.layer.closePopup();
        await costZoneSave(shape.uiMapId);
    }
    // Zone shapes deliberately live in their own display groups rather than
    // in editableLayers, so the draw toolbar's edit/delete never sees them.
    // Drive geometry editing from here instead.
    else if (e.target.closest('.cz-edit')) {
        shape.layer.closePopup();

        if (shape.editing) {
            shape.layer.editing?.disable();
            shape.editing = false;
            await costZoneSave(shape.uiMapId);
        } else {
            shape.layer.editing?.enable();
            shape.editing = true;
        }
    }
    else if (e.target.closest('.cz-delete')) {
        const uiMapId = shape.uiMapId;
        costZoneRemove(shape);
        await costZoneSave(uiMapId);
    }
}, true);

function costZoneBindPopup(shape) {
    shape.layer.bindPopup(() => costZonePopupHtml(shape), { minWidth: 220 });

    shape.layer.on('popupopen', () => {
        costZoneActiveShape = shape;

        const root = shape.layer.getPopup().getElement();
        if (!root) return;

        // Without this the map keeps handling clicks and wheel events that land
        // on the form, so focusing a field or scrolling a number input fights
        // the map instead of editing the value. Attached to the popup ROOT,
        // which - unlike the content children - does survive the re-render.
        L.DomEvent.disableClickPropagation(root);
        L.DomEvent.disableScrollPropagation(root);
    });

    shape.layer.on('popupclose', () => {
        if (costZoneActiveShape === shape) {
            costZoneActiveShape = null;
        }
    });
}

function costZonePopupHtml(shape) {
    const head = `<div style="font-weight:600;margin-bottom:6px">`
        + `${shape.kind === 'road' ? 'Road' : 'Danger zone'} - map ${shape.uiMapId}</div>`;

    const name = `<label>Name<br><input class="cz-name" value="${shape.name}" style="width:100%"></label><br>`;

    const body = shape.kind === 'road'
        ? `<label>Width (MCNK chunks per side)<br>`
        + `<input class="cz-width" type="number" min="1" step="1" value="${shape.width}" style="width:100%"></label>`
        : `<label>Penalty<br>`
        + `<input class="cz-penalty" type="number" min="0" step="10" value="${shape.penalty}" style="width:100%"></label><br>`
        + `<label>Mode<br><select class="cz-mode" style="width:100%">`
        + `<option value="0"${shape.mode === MODE_AVOID ? ' selected' : ''}>Avoid (soft cost)</option>`
        + `<option value="1"${shape.mode === MODE_BLOCK ? ' selected' : ''}>Block (impassable)</option>`
        + `</select></label>`;

    const buttons = `<div style="margin-top:8px;display:flex;gap:6px">`
        + `<button class="cz-save" type="button">Save</button>`
        + `<button class="cz-edit" type="button">${shape.editing ? 'Finish shape' : 'Edit shape'}</button>`
        + `<button class="cz-delete" type="button">Delete</button></div>`;

    return head + name + body + buttons;
}

function costZoneRemove(shape) {
    roadGroup.removeLayer(shape.layer);
    dangerGroup.removeLayer(shape.layer);
    editableLayers.removeLayer(shape.layer);

    const i = costZoneShapes.indexOf(shape);
    if (i >= 0) {
        costZoneShapes.splice(i, 1);
    }
}

/// Rewrites both files for one UIMapId, then reloads the pather's zones.
async function costZoneSave(uiMapId) {
    if (uiMapId < 0) {
        console.warn('Shape is outside every known map area - not saved');
        return;
    }

    const mine = costZoneShapes.filter(s => s.uiMapId === uiMapId);

    const roadData = {
        Roads: mine.filter(s => s.kind === 'road').map(s => ({
            Name: s.name,
            Width: s.width,
            Points: s.layer.getLatLngs().map(ll => {
                const w = latLngToWorld(ll);
                return { X: w.x, Y: w.y };
            })
        }))
    };

    const dangerData = {
        Circles: mine.filter(s => s.kind === 'circle').map(s => {
            const w = latLngToWorld(s.layer.getLatLng());
            return {
                Name: s.name,
                CenterX: w.x,
                CenterY: w.y,
                Radius: costZoneLayerRadiusToWorld(s.layer),
                Penalty: s.penalty,
                Mode: s.mode
            };
        }),
        Rectangles: mine.filter(s => s.kind === 'rect').map(s => {
            const b = s.layer.getBounds();
            const a = latLngToWorld(b.getSouthWest());
            const c = latLngToWorld(b.getNorthEast());
            return {
                Name: s.name,
                MinX: Math.min(a.x, c.x),
                MinY: Math.min(a.y, c.y),
                MaxX: Math.max(a.x, c.x),
                MaxY: Math.max(a.y, c.y),
                Penalty: s.penalty,
                Mode: s.mode
            };
        })
    };

    const query = `continent=${encodeURIComponent(costZoneContinent)}&uiMapId=${uiMapId}`;

    const okRoad = await costZonePost(`/api/Road?${query}`, roadData);
    const okDanger = await costZonePost(`/api/DangerZone?${query}`, dangerData);

    if (!okRoad || !okDanger) {
        console.error(`Cost zones for map ${uiMapId} did NOT fully save `
            + `(roads ${okRoad ? 'ok' : 'FAILED'}, danger ${okDanger ? 'ok' : 'FAILED'}) `
            + `- the map and the files are now out of sync, reload to see the truth.`);
        return;
    }

    // Cost zones are query-time only, so this takes effect on the next path
    // without a rebake.
    try {
        await fetch('/api/Road/reload', { method: 'POST' });
    } catch (err) {
        console.error('cost zones: saved, but the pather reload failed', err);
    }

    console.log(`Cost zones saved for map ${uiMapId}`);
}

/// Never throws: the road and danger files are written back independently, and
/// one failing must not abandon the other half-saved - that leaves a shape gone
/// from the map but still on disk, which looks exactly like "delete did nothing".
async function costZonePost(url, body) {
    try {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });

        if (!res.ok) {
            console.error(`cost zones: ${url} -> ${res.status} ${await res.text()}`);
            return false;
        }

        return true;
    } catch (err) {
        console.error(`cost zones: ${url} failed`, err);
        return false;
    }
}

/// Returns true when the drawn layer was claimed as a cost zone.
function costZoneHandleCreated(e) {
    const layer = e.layer;

    let kind = null;
    if (e.layerType === 'circle') {
        kind = 'circle';
    } else if (e.layerType === 'rectangle') {
        kind = 'rect';
    } else if (e.layerType === 'polyline' && roadDrawMode) {
        kind = 'road';
    }

    if (kind === null) {
        return false;
    }

    const anchor = kind === 'circle'
        ? latLngToWorld(layer.getLatLng())
        : latLngToWorld(kind === 'rect' ? layer.getBounds().getCenter() : layer.getLatLngs()[0]);

    const uiMapId = costZoneWorldToUiMapId(anchor);
    if (uiMapId < 0) {
        console.warn('Drawn shape is outside every known map area - discarded');
        return true;
    }

    if (kind === 'road') {
        layer.setStyle({ color: ROAD_COLOR, weight: 4, opacity: 0.9 });
    }

    const shape = costZoneRegister(layer, kind, { uiMapId });
    costZoneSave(uiMapId).then(() => shape.layer.openPopup());

    return true;
}

/// Returns true when the edited layers were cost zones.
function costZoneHandleEdited(layers) {
    const touched = new Set();

    for (const layer of layers) {
        if (layer.costZone === undefined) continue;
        touched.add(layer.costZone.uiMapId);
    }

    for (const uiMapId of touched) {
        costZoneSave(uiMapId);
    }

    return touched.size > 0;
}

function costZoneHandleDeleted(layers) {
    const touched = new Set();

    for (const layer of layers) {
        if (layer.costZone === undefined) continue;
        touched.add(layer.costZone.uiMapId);
        costZoneRemove(layer.costZone);
    }

    for (const uiMapId of touched) {
        costZoneSave(uiMapId);
    }
}

/// Toggled from the sidebar: when on, a drawn polyline is a road rather than
/// a bot path.
function setRoadDrawMode(on) {
    roadDrawMode = !!on;
}

function getRoadDrawMode() {
    return roadDrawMode;
}
