window.adminHeatmap = (() => {
    let map = null;
    let heatLayer = null;
    let heatmapMarkers = [];

    function resolveContainer(container) {
        if (typeof container === 'string') {
            return document.getElementById(container);
        }

        return container && container.nodeType === 1 ? container : null;
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    async function init(container, centerLat, centerLng, zoom) {
        if (map) {
            map.remove();
            map = null;
            heatLayer = null;
            heatmapMarkers = [];
        }

        let containerElement = resolveContainer(container);
        if (!containerElement) {
            await new Promise(requestAnimationFrame);
            containerElement = resolveContainer(container);
            if (!containerElement) {
                return false;
            }
        }

        map = L.map(containerElement, { zoomControl: true }).setView([centerLat, centerLng], zoom || 17);

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

        const coordinateBuckets = new Map();

        points.forEach(point => {
            const hitCount = point.hitCount ?? 0;
            const lat = Number(point.latitude ?? 0);
            const lng = Number(point.longitude ?? 0);
            const coordinateKey = `${lat.toFixed(6)}:${lng.toFixed(6)}`;
            const bucketIndex = coordinateBuckets.get(coordinateKey) ?? 0;
            coordinateBuckets.set(coordinateKey, bucketIndex + 1);

            // Tách nhẹ các điểm bị chồng tọa độ để người dùng vẫn thấy đủ POI.
            // Dịch chuyển rất nhỏ, không làm sai ý nghĩa vị trí trên map.
            const offsetDistance = bucketIndex * 0.00018;
            const offsetAngle = bucketIndex * (Math.PI / 3);
            const displayLat = lat + Math.cos(offsetAngle) * offsetDistance;
            const displayLng = lng + Math.sin(offsetAngle) * offsetDistance;

            const marker = L.circleMarker([point.latitude, point.longitude], {
                radius: 11,
                fillColor: '#e53935',
                color: '#ffffff',
                weight: 3,
                opacity: 1,
                fillOpacity: 0.95
            });

            const pointName = escapeHtml(point.name || 'POI');
            const poiId = point.poiId != null ? `#${point.poiId}` : '';
            marker.bindTooltip(
                `<b style="font-size:13px;color:#e53935">${pointName} ${poiId}</b><br/>Lượt nghe: ${hitCount}`,
                { permanent: false, direction: 'top', offset: [0, -12] }
            );

            marker.setLatLng([displayLat, displayLng]);

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