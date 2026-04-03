window.adminHeatmap = (() => {
    let map = null;
    let heatLayer = null;
    let heatmapMarkers = [];

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function init(containerId, centerLat, centerLng, zoom) {
        if (map) {
            map.remove();
            map = null;
            heatLayer = null;
            heatmapMarkers = [];
        }

        map = L.map(containerId, { zoomControl: true }).setView([centerLat, centerLng], zoom || 17);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
            maxZoom: 19
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

    function renderHeatmapFlags(points) {
        if (!map) return;

        heatmapMarkers.forEach(marker => map.removeLayer(marker));
        heatmapMarkers = [];

        if (!points || points.length === 0) return;

        points.forEach(point => {
            const hitCount = point.hitCount ?? 0;
            const marker = L.circleMarker([point.latitude, point.longitude], {
                radius: 11,
                fillColor: '#e53935',
                color: '#ffffff',
                weight: 3,
                opacity: 1,
                fillOpacity: 0.95
            });

            const pointName = escapeHtml(point.name || 'POI');
            marker.bindTooltip(
                `<b style="font-size:13px;color:#e53935">${pointName}</b><br/>Lượt ghé: ${hitCount}`,
                { permanent: false, direction: 'top', offset: [0, -12] }
            );

            marker.addTo(map);
            heatmapMarkers.push(marker);
        });
    }

    function invalidate() {
        if (map) {
            setTimeout(() => map.invalidateSize(), 100);
        }
    }

    return { init, renderHeatmap, renderHeatmapFlags, invalidate };
})();