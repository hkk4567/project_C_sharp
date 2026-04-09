window.ownerHeatmap = (() => {
    let map = null;
    let heatLayer = null;
    let poiMarkers = [];
    let heatmapMarkers = [];

    function init(containerId, centerLat, centerLng, zoom) {
        if (map) {
            map.remove();
            map = null;
            heatLayer = null;
            poiMarkers = [];
            heatmapMarkers = [];
        }

        map = L.map(containerId, { zoomControl: true }).setView([centerLat, centerLng], zoom || 17);

        L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> &copy; <a href="https://carto.com/attributions">CARTO</a>',
            maxZoom: 19,
            referrerPolicy: 'origin'
        }).addTo(map);

        return true;
    }

    function renderHeatmap(points) {
        if (!map) return;

        if (heatLayer) {
            map.removeLayer(heatLayer);
            heatLayer = null;
        }

        if (!points || points.length === 0) return;

        const latlngs = points.map(p => [
            p.latitude,
            p.longitude,
            Math.min((p.hitCount ?? 0) / 10, 1)
        ]);

        heatLayer = L.heatLayer(latlngs, {
            radius: 42,
            blur: 32,
            maxZoom: 19,
            max: 1.0,
            minOpacity: 0.18,
            gradient: {
                0.0: '#64d8ff',
                0.25: '#4fc3f7',
                0.45: '#66bb6a',
                0.65: '#ffee58',
                0.82: '#ff9800',
                1.0: '#ff5252'
            }
        }).addTo(map);
    }

    function renderHeatmapMarkers(points) {
        if (!map) return;

        heatmapMarkers.forEach(marker => map.removeLayer(marker));
        heatmapMarkers = [];

        if (!points || points.length === 0) return;

        points.forEach(point => {
            const hitCount = point.hitCount ?? 0;
            const marker = L.circleMarker([point.latitude, point.longitude], {
                radius: 8,
                fillColor: '#e53935',
                color: '#ffffff',
                weight: 2,
                opacity: 1,
                fillOpacity: 0.95
            });

            marker.bindTooltip(
                `<b style="font-size:13px;color:#e53935">Điểm nóng</b><br/>Lượt ghé: ${hitCount}`,
                { permanent: false, direction: 'top', offset: [0, -8] }
            );

            marker.addTo(map);
            heatmapMarkers.push(marker);
        });
    }

    function renderPoiMarkers(pois) {
        if (!map) return;

        poiMarkers.forEach(m => map.removeLayer(m));
        poiMarkers = [];

        if (!pois || pois.length === 0) return;

        pois.forEach(poi => {
            const marker = L.circleMarker([poi.latitude, poi.longitude], {
                radius: 10,
                fillColor: '#e53935',
                color: '#ffffff',
                weight: 2,
                opacity: 1,
                fillOpacity: 0.9
            });

            marker.bindTooltip(
                `<b style="font-size:13px">${poi.name}</b>`,
                { permanent: false, direction: 'top', offset: [0, -8] }
            );

            marker.addTo(map);
            poiMarkers.push(marker);
        });
    }

    function fitToPois(pois) {
        if (!map || !pois || pois.length === 0) return;
        const bounds = pois.map(p => [p.latitude, p.longitude]);
        map.fitBounds(bounds, { padding: [50, 50], maxZoom: 17 });
    }

    function invalidate() {
        if (map) {
            setTimeout(() => map.invalidateSize(), 100);
        }
    }

    return { init, renderHeatmap, renderHeatmapMarkers, renderPoiMarkers, fitToPois, invalidate };
})();
