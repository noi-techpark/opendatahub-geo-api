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

### 5. Run with Docker

```bash
cd VectorTilesApi
docker compose build
docker compose up -d
```

The API will start at `http://localhost:5023`.

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

### Testing with MapLibre/Mapbox html

```bash
cd ..
cd html
serve -p PORT
```

The Html file is available on the defined Port, "test-map.html"


### Testing with cURL

```bash
# Get a vector tile
curl -o tile.pbf http://localhost:5023/api/tiles/your_table/14/2621/6333.pbf

# Check health
curl http://localhost:5023/api/tiles/health
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

## How to use the api in a web application

Visit the `examples_html` section.
There are various maps with data from Open Data Hub Content Api.

Simply include the `vector-tile-map.js` in a html file  
`<script src="vector-tile-map.js"></script>`
initialize with config    

```html
    <!-- Initialize with your config -->
    <script>
        const myMap = new VectorTileMap({
            containerId: 'map',
            type: 'odhactivitypoi',
            apiUrl: 'https://geo.api.opendatahub.testingmachine.eu',
            additional: '?source=suedtirolwein',
            center: [11.35, 46.5],
            zoom: 10
        });
    </script>
```

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

### Performance issues
- Add spatial index: `CREATE INDEX idx_geo ON your_table USING GIST(geo);`
- Analyze query performance: `EXPLAIN ANALYZE` your query
- Consider using materialized views for complex queries

## License

MIT