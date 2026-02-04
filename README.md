<!--
SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>

SPDX-License-Identifier: CC0-1.0
-->

# VectorTilesApi

A .NET Core 9 Web API that serves Mapbox Vector Tiles (MVT) from PostGIS using ST_AsMVT.

## Prerequisites

- .NET 9 SDK
- PostgreSQL with PostGIS extension
- Visual Studio Code

## Setup

### 1. Database Setup

Create your PostgreSQL database with PostGIS:

```sql
-- Create database
CREATE DATABASE your_database;

-- Connect to the database and enable PostGIS
\c your_database
CREATE EXTENSION IF NOT EXISTS postgis;

-- Create your table
CREATE TABLE your_table (
    id VARCHAR(255) PRIMARY KEY,
    data JSONB,
    geo GEOMETRY(Geometry, 4326)  -- Adjust SRID as needed
);

-- Create a spatial index for better performance
CREATE INDEX idx_your_table_geo ON your_table USING GIST(geo);

-- Example: Insert some sample data
INSERT INTO your_table (id, data, geo) VALUES
(
    'point-1',
    '{"name": "Sample Point", "type": "landmark"}'::jsonb,
    ST_SetSRID(ST_MakePoint(-122.4194, 37.7749), 4326)
),
(
    'point-2',
    '{"name": "Another Point", "type": "restaurant"}'::jsonb,
    ST_SetSRID(ST_MakePoint(-122.4094, 37.7849), 4326)
);
```

### 2. Configure Connection String

Edit `appsettings.json` with your PostgreSQL connection details:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=your_database;Username=your_username;Password=your_password"
  }
}
```

### 3. Restore and Build

```bash
cd VectorTilesApi
dotnet restore
dotnet build
```

### 4. Run the API

```bash
dotnet run
```

The API will start at `http://localhost:5023` (or `https://localhost:5023`).

## Usage

### API Endpoints

#### Get Vector Tile
```
GET /api/tiles/{tableName}/{z}/{x}/{y}.pbf
```

Parameters:
- `tableName`: Name of your PostGIS table (e.g., "your_table")
- `z`: Zoom level (0-22)
- `x`: Tile X coordinate
- `y`: Tile Y coordinate

Example:
```
http://localhost:5000/api/tiles/your_table/14/2621/6333.pbf
```

#### Health Check
```
GET /api/tiles/health
```

### Testing with MapLibre/Mapbox

```html



    
    Vector Tiles Test
    
    
    
    
        body { margin: 0; padding: 0; }
        #map { position: absolute; top: 0; bottom: 0; width: 100%; }
    


    
    
        const map = new maplibregl.Map({
            container: 'map',
            style: {
                version: 8,
                sources: {
                    'osm': {
                        type: 'raster',
                        tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                        tileSize: 256,
                        attribution: '© OpenStreetMap contributors'
                    },
                    'your-data': {
                        type: 'vector',
                        tiles: ['http://localhost:5000/api/tiles/your_table/{z}/{x}/{y}.pbf'],
                        minzoom: 0,
                        maxzoom: 22
                    }
                },
                layers: [
                    {
                        id: 'osm-layer',
                        type: 'raster',
                        source: 'osm'
                    },
                    {
                        id: 'your-layer',
                        type: 'circle',
                        source: 'your-data',
                        'source-layer': 'your_table',
                        paint: {
                            'circle-radius': 6,
                            'circle-color': '#ff0000'
                        }
                    }
                ]
            },
            center: [-122.4194, 37.7749],
            zoom: 12
        });
    


```

### Testing with cURL

```bash
# Get a vector tile
curl -o tile.pbf http://localhost:5000/api/tiles/your_table/14/2621/6333.pbf

# Check health
curl http://localhost:5000/api/tiles/health
```

## Project Structure

```
VectorTileApi/
├── Controllers/
│   └── VectorTilesController.cs    # API endpoints
├── Services/
│   ├── IVectorTileService.cs       # Service interface
│   └── VectorTileService.cs        # ST_AsMVT implementation
├── Program.cs                       # Application setup
├── appsettings.json                 # Configuration
└── VectorTileApi.csproj            # Project file
```

## How It Works

1. **Tile Coordinates**: The API receives tile coordinates (z, x, y) in the Web Mercator projection
2. **Bounds Calculation**: Converts tile coordinates to geographic bounds
3. **PostGIS Query**: Uses `ST_AsMVTGeom` to prepare geometries and `ST_AsMVT` to generate MVT
4. **Spatial Filtering**: Queries only features that intersect with the tile bounds
5. **Protobuf Response**: Returns binary protobuf data (MVT format)

## Key Features

- ✅ Returns Mapbox Vector Tiles (MVT/protobuf format)
- ✅ Uses PostGIS ST_AsMVT for efficient tile generation
- ✅ Includes JSONB data in tiles
- ✅ Spatial indexing for performance
- ✅ Coordinate validation
- ✅ CORS enabled for web clients
- ✅ Swagger documentation

## Performance Tips

1. **Create spatial indexes** on your geometry columns
2. **Use appropriate SRID** (4326 for lat/lon, 3857 for Web Mercator)
3. **Consider table partitioning** for large datasets
4. **Add caching** (Redis, CDN) for frequently accessed tiles
5. **Set appropriate zoom level limits** based on your data density

## Troubleshooting

### Empty tiles returned
- Check that your data intersects with the requested tile bounds
- Verify the SRID of your geometries matches the query
- Ensure PostGIS extension is installed: `SELECT postgis_version();`

### Connection errors
- Verify PostgreSQL connection string in appsettings.json
- Check PostgreSQL is running and accessible
- Confirm database user has SELECT permissions

### Performance issues
- Add spatial index: `CREATE INDEX idx_geo ON your_table USING GIST(geo);`
- Analyze query performance: `EXPLAIN ANALYZE` your query
- Consider using materialized views for complex queries

## License

MIT