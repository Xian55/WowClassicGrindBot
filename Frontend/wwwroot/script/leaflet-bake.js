/*
    Interactive navmesh baking.

    "Bake on click" turns the map into an ADT picker: hovering outlines the ADT
    under the cursor, clicking queues it. One ADT is 16 navmesh tiles and takes
    roughly ten seconds, most of it spent parsing the ADT out of the MPQ rather
    than in recast.

    ADT indices here use the same convention as the ADT Grid overlay and the
    bake API - grid x counts down from the world origin along world X, grid y
    along world Y. PPather has a second, transposed ADT convention internally;
    the server reconciles them, this file must not.
*/

const BAKE_POLL_MS = 1500;

/// How often coverage may be redrawn while a bake runs.
const BAKE_COVERAGE_MS = 10000;

let bakeLastCoverageTiles = -1;
let bakeLastCoverageAt = 0;

let bakeClickMode = false;
let bakeHoverRect = null;
let bakeStatusEl = null;
let bakePollTimer = null;
let bakeContinent = 'Azeroth';

function bakeAdtFromWorld(worldX, worldY) {
    return {
        x: Math.floor((maxSize - worldX) / adtSize),
        y: Math.floor((maxSize - worldY) / adtSize)
    };
}

function bakeAdtWorldBounds(adtX, adtY) {
    const maxX = maxSize - (adtX * adtSize);
    const maxY = maxSize - (adtY * adtSize);
    return { minX: maxX - adtSize, minY: maxY - adtSize, maxX, maxY };
}

function bakeAdtRect(adtX, adtY, style) {
    const b = bakeAdtWorldBounds(adtX, adtY);
    return L.rectangle([worldTolatLng(b.minX, b.minY), worldTolatLng(b.maxX, b.maxY)], style);
}

function bakeSetStatus(text) {
    if (bakeStatusEl) {
        bakeStatusEl.textContent = text;
    }
}

async function bakeFetchStatus() {
    try {
        return await fetch('/api/PPather/Bake/Status').then(r => r.ok ? r.json() : null);
    } catch {
        return null;
    }
}

function bakeStartPolling() {
    if (bakePollTimer !== null) {
        return;
    }

    bakePollTimer = setInterval(async () => {
        const s = await bakeFetchStatus();
        if (!s) {
            return;
        }

        if (s.running) {
            const pct = s.adtTotal > 0 ? Math.round((s.adtDone / s.adtTotal) * 100) : 0;
            bakeSetStatus(`${pct}% - adt ${s.adtDone}/${s.adtTotal}, ${s.tilesDone} tiles, `
                + `${Math.round(s.elapsedSeconds)}s (${s.message})`);

            // Show coverage filling in as it goes, but not on every poll: the
            // endpoint enumerates the whole cache directory and the tile layer
            // can be tens of thousands of rectangles.
            if (s.tilesDone !== bakeLastCoverageTiles &&
                Date.now() - bakeLastCoverageAt >= BAKE_COVERAGE_MS) {
                bakeLastCoverageTiles = s.tilesDone;
                bakeLastCoverageAt = Date.now();
                bakeRefreshCoverage();
            }

            return;
        }

        clearInterval(bakePollTimer);
        bakePollTimer = null;

        bakeSetStatus(`${s.message} - ${s.tilesDone} tiles baked, `
            + `${s.tilesAlreadyOnDisk} already present, ${Math.round(s.elapsedSeconds)}s`);

        bakeRefreshCoverage();
    }, BAKE_POLL_MS);
}

function bakeRefreshCoverage() {
    if (typeof navmeshCoverageRefresh !== 'function') {
        return;
    }

    navmeshCoverageRefresh(bakeContinent).catch(err => console.error('bake: coverage refresh', err));
}

async function bakePost(url, label) {
    try {
        const res = await fetch(url, { method: 'POST' });
        if (res.status === 409) {
            bakeSetStatus('a bake is already running');
            return;
        }

        if (!res.ok) {
            bakeSetStatus(`${label} failed: ${res.status}`);
            return;
        }

        bakeSetStatus(`${label} started`);
        bakeStartPolling();
    } catch (err) {
        console.error('bake: ' + url, err);
        bakeSetStatus(`${label} failed - see console`);
    }
}

function bakeAdt(adtX, adtY) {
    bakePost(`/api/PPather/Bake?continent=${encodeURIComponent(bakeContinent)}`
        + `&adtX=${adtX}&adtY=${adtY}`, `ADT ${adtX},${adtY}`);
}

function bakeInit(continent) {
    bakeContinent = continent;

    LeafletMap.on('mousemove', (e) => {
        if (!bakeClickMode) {
            return;
        }

        const w = latLngToWorld(e.latlng);
        const adt = bakeAdtFromWorld(w.x, w.y);

        if (bakeHoverRect) {
            LeafletMap.removeLayer(bakeHoverRect);
        }

        bakeHoverRect = bakeAdtRect(adt.x, adt.y, {
            color: '#ffcc00', weight: 2, fillColor: '#ffcc00', fillOpacity: 0.15, interactive: false
        }).addTo(LeafletMap);

        bakeHoverRect.bindTooltip(`bake ADT ${adt.x},${adt.y}`, { direction: 'center' }).openTooltip();
    });

    LeafletMap.on('click', (e) => {
        if (!bakeClickMode) {
            return;
        }

        const w = latLngToWorld(e.latlng);
        const adt = bakeAdtFromWorld(w.x, w.y);
        bakeAdt(adt.x, adt.y);
    });

    // Pick up a bake that was already running when the page loaded.
    bakeFetchStatus().then(s => { if (s && s.running) { bakeStartPolling(); } });
}

function bakeSetClickMode(on) {
    bakeClickMode = !!on;

    if (!bakeClickMode && bakeHoverRect) {
        LeafletMap.removeLayer(bakeHoverRect);
        bakeHoverRect = null;
    }
}

/// Builds the sidebar section. Called from createSidebar in leaflet-watch.js.
function bakeBuildSection(body) {
    bakeStatusEl = L.DomUtil.create('div', 'section-label', body);
    bakeStatusEl.style.cssText = 'margin-bottom:6px;font-size:11px;color:#bbb;white-space:normal;';
    bakeStatusEl.textContent = 'idle';

    const clickRow = L.DomUtil.create('label', 'sidebar-checkbox-row', body);
    const clickCb = document.createElement('input');
    clickCb.type = 'checkbox';
    clickCb.onchange = () => bakeSetClickMode(clickCb.checked);
    clickRow.appendChild(clickCb);
    const clickLabel = L.DomUtil.create('span', 'cb-label', clickRow);
    clickLabel.textContent = 'Bake ADT on click';
    clickRow.title = 'Hover outlines the ADT, clicking bakes its 16 navmesh tiles (~10s)';

    const row = L.DomUtil.create('div', 'sidebar-coord-row', body);
    row.style.cssText = 'display:flex;gap:4px;flex-wrap:wrap;margin-top:6px;';

    const contBtn = L.DomUtil.create('button', 'sidebar-coord-btn', row);
    contBtn.textContent = 'Bake continent';
    contBtn.title = 'Every ADT this continent has terrain for - hours, hundreds of MB';
    contBtn.onclick = () =>
        bakePost(`/api/PPather/Bake?continent=${encodeURIComponent(bakeContinent)}`, 'continent bake');

    const cancelBtn = L.DomUtil.create('button', 'sidebar-coord-btn', row);
    cancelBtn.textContent = 'Cancel';
    cancelBtn.onclick = () => bakePost('/api/PPather/Bake/Cancel', 'cancel');

    const clearBtn = L.DomUtil.create('button', 'sidebar-coord-btn', row);
    clearBtn.textContent = 'Clear cache';
    clearBtn.title = 'Deletes every baked tile for this continent - click twice to confirm';
    let clearArmed = false;
    clearBtn.onclick = async () => {
        if (!clearArmed) {
            clearArmed = true;
            clearBtn.textContent = 'Really clear?';
            bakeSetStatus('click again to delete this continent\'s baked tiles');
            setTimeout(() => {
                clearArmed = false;
                clearBtn.textContent = 'Clear cache';
            }, 4000);
            return;
        }

        clearArmed = false;
        clearBtn.textContent = 'Clear cache';

        try {
            const res = await fetch(
                `/api/PPather/Bake/Cache?continent=${encodeURIComponent(bakeContinent)}`,
                { method: 'DELETE' });

            if (!res.ok) {
                bakeSetStatus('clear refused: ' + await res.text());
                return;
            }

            const j = await res.json();
            bakeSetStatus(`cleared ${j.freedMB.toFixed(1)} MB`);

            if (typeof navmeshCoverageRefresh === 'function') {
                navmeshCoverageRefresh(bakeContinent).catch(err => console.error(err));
            }
        } catch (err) {
            console.error('bake: clear', err);
            bakeSetStatus('clear failed - see console');
        }
    };
}
