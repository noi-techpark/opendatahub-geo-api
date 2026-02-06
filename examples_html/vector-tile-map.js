// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * VectorTileMap - Reusable vector tile map component
 * @param {Object} config - Configuration object
 * @param {string} config.containerId - ID of the map container element
 * @param {string} config.type - Name of the open data hub type
 * @param {string} config.apiUrl - Base URL of the vector tile API
 * @param {string} config.additional - Additional query parameters (optional)
 * @param {Array} config.center - Map center [longitude, latitude]
 * @param {number} config.zoom - Initial zoom level
 * @param {Object} config.styles - Custom layer styles (optional)
 */
function VectorTileMap(config) {
    const {
        containerId = 'map',
        type,
        apiUrl,
        additional = '',
        center = [11.35, 46.5],
        zoom = 10,
        styles = {}
    } = config;

    if (!type || !apiUrl) {
        throw new Error('type and apiUrl are required');
    }

    // Default styles
    const defaultStyles = {
        polygons: {
            'fill-color': '#0080FF',
            'fill-opacity': 0.4,
            'fill-outline-color': '#004080'
        },
        lines: {
            'line-color': '#404040',
            'line-width': [
                'interpolate', ['linear'], ['zoom'],
                8, 1,
                12, 2,
                16, 4,
                20, 6
            ],
            'line-opacity': 0.8
        },
        points: {
            'circle-radius': [
                'interpolate', ['linear'], ['zoom'],
                0, 2,
                10, 5,
                14, 10,
                18, 15
            ],
            'circle-color': '#FF0000',
            'circle-stroke-width': 2,
            'circle-stroke-color': '#FFFFFF',
            'circle-opacity': 0.8
        }
    };

    // Merge custom styles with defaults
    const mergedStyles = {
        polygons: { ...defaultStyles.polygons, ...styles.polygons },
        lines: { ...defaultStyles.lines, ...styles.lines },
        points: { ...defaultStyles.points, ...styles.points }
    };

    const map = new maplibregl.Map({
        container: containerId,
        style: {
            version: 8,
            sources: {
                'osm': {
                    type: 'raster',
                    tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                    tileSize: 256,
                    attribution: '© OpenStreetMap'
                },
                'vector-tiles': {
                    type: 'vector',
                    tiles: [`${apiUrl}/api/tiles/${type}/{z}/{x}/{y}.pbf${additional}`],
                    minzoom: 0,
                    maxzoom: 22
                }
            },
            layers: [
                {
                    id: 'osm-background',
                    type: 'raster',
                    source: 'osm',
                    minzoom: 0,
                    maxzoom: 22
                },
                {
                    id: 'polygons',
                    type: 'fill',
                    source: 'vector-tiles',
                    'source-layer': type,
                    filter: ['any',
                        ['==', ['geometry-type'], 'Polygon'],
                        ['==', ['geometry-type'], 'MultiPolygon']
                    ],
                    paint: mergedStyles.polygons
                },
                {
                    id: 'lines',
                    type: 'line',
                    source: 'vector-tiles',
                    'source-layer': type,
                    filter: ['any',
                        ['==', ['geometry-type'], 'LineString'],
                        ['==', ['geometry-type'], 'MultiLineString']
                    ],
                    paint: mergedStyles.lines,
                    layout: {
                        'line-cap': 'round',
                        'line-join': 'round'
                    }
                },
                {
                    id: 'points',
                    type: 'circle',
                    source: 'vector-tiles',
                    'source-layer': type,
                    filter: ['==', ['geometry-type'], 'Point'],
                    paint: mergedStyles.points
                }
            ]
        },
        center: center,
        zoom: zoom
    });

    // Add controls
    map.addControl(new maplibregl.NavigationControl());
    map.addControl(new maplibregl.ScaleControl());

    // Debug info update function
    function updateInfo() {
        const center = map.getCenter();
        const zoom = map.getZoom();
        const features = map.queryRenderedFeatures({ layers: ['polygons', 'lines', 'points'] });
        
        const infoElement = document.getElementById('info');
        if (infoElement) {
            infoElement.innerHTML = `
                <strong>Zoom:</strong> ${zoom.toFixed(2)}<br>
                <strong>Center:</strong> ${center.lat.toFixed(4)}°N, ${center.lng.toFixed(4)}°E<br>
                <strong>Table:</strong> ${type}<br>
                <strong>Visible features:</strong> ${features.length}<br>
                <strong>Source-layer:</strong> ${type}
            `;
        }
    }

    // Event handlers
    map.on('load', () => {
        console.log('✅ Map loaded');
        console.log('📊 Table name:', type);
        console.log('🔗 Tile URL:', `${apiUrl}/api/tiles/${type}/{z}/{x}/{y}.pbf${additional}`);
        console.log('Polygons:', map.queryRenderedFeatures({ layers: ['polygons'] }).length);
        console.log('Lines:', map.queryRenderedFeatures({ layers: ['lines'] }).length);
        console.log('Points:', map.queryRenderedFeatures({ layers: ['points'] }).length);
        updateInfo();
    });

    map.on('moveend', updateInfo);
    map.on('zoomend', updateInfo);

    map.on('data', (e) => {
        if (e.sourceId === 'vector-tiles' && e.tile) {
            console.log('📦 Tile loaded:', e.tile.tileID);
            updateInfo();
        }
    });

    map.on('error', (e) => {
        console.error('❌ Error:', e);
        const infoElement = document.getElementById('info');
        if (e.error && infoElement) {
            infoElement.innerHTML += `<br><span style="color:red;">Error: ${e.error.message}</span>`;
        }
    });

    // Feature popup helper
    function createPopup(feature, featureType) {
        const props = feature.properties;
        let html = `<strong>${featureType} Feature</strong><br>`;
        html += `<strong>ID:</strong> ${props.id}<br>`;
        
        if (featureType === 'Line') {
            html += `<strong>Type:</strong> ${feature.geometry.type}<br>`;
        }
        
        if (props.data) {
            try {
                const data = JSON.parse(props.data);
                Object.keys(data).forEach(key => {
                    html += `<strong>${key}:</strong> ${data[key]}<br>`;
                });
            } catch (e) {
                html += `<strong>Data:</strong> ${props.data}<br>`;
            }
        }
        
        return html;
    }

    // Click handlers
    map.on('click', 'polygons', (e) => {
        const feature = e.features[0];
        console.log('🎯 Clicked polygon:', feature);
        new maplibregl.Popup()
            .setLngLat(e.lngLat)
            .setHTML(createPopup(feature, 'Polygon'))
            .addTo(map);
    });

    map.on('click', 'lines', (e) => {
        const feature = e.features[0];
        console.log('🎯 Clicked line:', feature);
        new maplibregl.Popup()
            .setLngLat(e.lngLat)
            .setHTML(createPopup(feature, 'Line'))
            .addTo(map);
    });

    map.on('click', 'points', (e) => {
        const feature = e.features[0];
        console.log('🎯 Clicked point:', feature);
        new maplibregl.Popup()
            .setLngLat(e.lngLat)
            .setHTML(createPopup(feature, 'Point'))
            .addTo(map);
    });

    // Hover effects
    ['points', 'polygons', 'lines'].forEach(layer => {
        map.on('mouseenter', layer, () => {
            map.getCanvas().style.cursor = 'pointer';
        });

        map.on('mouseleave', layer, () => {
            map.getCanvas().style.cursor = '';
        });
    });

    // Click anywhere for debug
    map.on('click', (e) => {
        const features = map.queryRenderedFeatures(e.point);
        console.log('🖱️ All features at click:', features);
        console.log('🎯 Vector features:', features.filter(f => f.source === 'vector-tiles'));
    });

    // Return map instance for external control
    return map;
}