/*
    Custom POIs: user-placed named points that also become entries in the
    From/To search boxes.

    Right-click the map in edit mode to drop one. Points live in localStorage so
    they survive a reload, and each is pushed to the Blazor page over the
    DotNetObjectReference handed to init(), which forwards it to the search
    component's autocomplete list.

    Without that reference (the bot's own Leaflet page passes none) the markers
    still work locally - only the search-box sync is skipped.
*/

const CUSTOM_POI_KEY = 'customPois';
const CUSTOM_POI_GROUP = 'Custom POIs';

const customPoiMarkers = {};

function loadCustomPois() {
    try {
        return JSON.parse(localStorage.getItem(CUSTOM_POI_KEY) || '[]');
    } catch {
        return [];
    }
}

function saveCustomPoi(poi) {
    const pois = loadCustomPois();
    pois.push(poi);
    localStorage.setItem(CUSTOM_POI_KEY, JSON.stringify(pois));
}

function removeCustomPoiFromStorage(description) {
    localStorage.setItem(CUSTOM_POI_KEY,
        JSON.stringify(loadCustomPois().filter(p => p.description !== description)));
}

function addCustomPoiMarker(poi) {
    const icon = L.divIcon({
        className: 'custom-poi-icon',
        html: `<span class="custom-poi-dot"></span><span class="custom-poi-label">${poi.description}</span>`,
        iconSize: [0, 0],
        iconAnchor: [5, 5]
    });

    const marker = L.marker(worldTolatLng(poi.x, poi.y), { icon: icon });

    marker.bindPopup(
        `<div style="font-size:12px;"><b>${poi.description}</b><br>` +
        `<span style="color:#888;">${poi.x}, ${poi.y}, ${poi.z} (map ${poi.mapId})</span><br>` +
        `<button class="sidebar-coord-btn custom-poi-remove-btn" data-poi-name="${poi.description}" style="margin-top:4px;">Remove</button></div>`);

    addGroupLayer(CUSTOM_POI_GROUP, CUSTOM_POI_GROUP);
    addToggleLayer(CUSTOM_POI_GROUP, 'POI: ' + poi.description, marker, true);

    customPoiMarkers[poi.description] = marker;
}

function deleteCustomPoi(description) {
    removeCustomPoiFromStorage(description);

    const marker = customPoiMarkers[description];
    if (marker) {
        if (LeafletMap.hasLayer(marker)) {
            LeafletMap.removeLayer(marker);
        }

        delete customPoiMarkers[description];
        delete layerNames['POI: ' + description];

        const group = groupedOverlays[CUSTOM_POI_GROUP];
        if (group) {
            delete group['POI: ' + description];
        }

        scheduleGroupedLayerControlUpdate();
    }

    if (dotNetHelper) {
        dotNetHelper.invokeMethodAsync('RemoveCustomPoi', description);
    }
}

function restoreCustomPois() {
    const pois = loadCustomPois().filter(p => p.mapId === config.MapID);

    for (const poi of pois) {
        addCustomPoiMarker(poi);

        if (dotNetHelper) {
            dotNetHelper.invokeMethodAsync('AddCustomPoi',
                poi.x, poi.y, poi.z, poi.mapId, poi.description);
        }
    }

    if (pois.length > 0) {
        scheduleGroupedLayerControlUpdate();
    }
}

/// Wires the right-click placement popup and restores saved points.
function customPoiInit() {
    LeafletMap.on('contextmenu', async function (e) {
        const worldPos = screenToWorld(LeafletMap.project(e.latlng, config.maxZoom));
        const response = await getAreaIdAndZFromService(worldPos);
        const zPos = response.z.toFixed(0);

        L.popup({ closeButton: true, className: 'custom-poi-popup' })
            .setLatLng(e.latlng)
            .setContent(
                `<div class="custom-poi-form">` +
                `<label>Add Custom POI</label>` +
                `<div style="font-size:11px;color:#888;margin-bottom:4px;">` +
                `${worldPos.x.toFixed(1)}, ${worldPos.y.toFixed(1)}, ${zPos}</div>` +
                `<input class="sidebar-input custom-poi-name-input" type="text" placeholder="POI name" style="margin-bottom:4px;" />` +
                `<button class="sidebar-coord-btn custom-poi-add-btn">Add POI</button>` +
                `</div>`)
            .openOn(LeafletMap);

        // Handed to the add button when the popup opens.
        LeafletMap._pendingPoiData = {
            x: parseFloat(worldPos.x.toFixed(2)),
            y: parseFloat(worldPos.y.toFixed(2)),
            z: parseFloat(zPos),
            mapId: config.MapID
        };
    });

    LeafletMap.on('popupopen', function (e) {
        const container = e.popup.getElement();
        if (!container) {
            return;
        }

        const addBtn = container.querySelector('.custom-poi-add-btn');
        if (addBtn) {
            addBtn.onclick = function () {
                const nameInput = container.querySelector('.custom-poi-name-input');
                const name = nameInput ? nameInput.value.trim() : '';
                if (!name) {
                    nameInput?.focus();
                    return;
                }

                const data = LeafletMap._pendingPoiData;
                if (!data) {
                    return;
                }

                const poi = { x: data.x, y: data.y, z: data.z, mapId: data.mapId, description: name };

                saveCustomPoi(poi);
                addCustomPoiMarker(poi);
                scheduleGroupedLayerControlUpdate();

                if (dotNetHelper) {
                    dotNetHelper.invokeMethodAsync('AddCustomPoi',
                        poi.x, poi.y, poi.z, poi.mapId, poi.description);
                }

                LeafletMap.closePopup();
            };
        }

        const removeBtn = container.querySelector('.custom-poi-remove-btn');
        if (removeBtn) {
            removeBtn.onclick = function () {
                const description = removeBtn.getAttribute('data-poi-name');
                if (description) {
                    deleteCustomPoi(description);
                }

                LeafletMap.closePopup();
            };
        }
    });

    restoreCustomPois();
}
