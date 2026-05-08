<!--
SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>

SPDX-License-Identifier: CC0-1.0
-->

# Open Data Hub Geo API Documentation

## How PostGIS generates vector tiles

Here is the pipeline, step by step:

---

### 1. Define the tile boundary — `ST_MakeEnvelope` + `ST_Transform`

A map tile is just a rectangle. Given z/x/y you calculate `xmin, ymin, xmax, ymax` in Web Mercator (EPSG:3857), then transform it to match your data's SRID (4326 in your case).

```sql
ST_Transform(ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857), 4326)
```

This is your "cookie cutter" — everything outside gets ignored.

---

### 2. Spatial filter — `ST_Intersects`

```sql
WHERE ST_Intersects(gen_center_position, bounds.geom)
```

Cuts down 300k rows to only those whose geometry touches the tile rectangle. This is where the GiST index does its job.

---

### 3. Simplify geometry — `ST_Simplify` (tracks only)

```sql
ST_Simplify(geo, 0.0001)
```

Reduces the number of vertices in a line/polygon based on zoom level. At low zoom a track with 10,000 points gets reduced to ~50 — smaller data to send to the client.

---

### 4. Cluster points — `ST_SnapToGrid` + `ST_Centroid` + `ST_Collect`

```sql
ST_SnapToGrid(geom, tile_width / 32)  -- snap nearby points to same grid cell
ST_Centroid(ST_Collect(geom))         -- merge snapped points into one centroid
```

Points close together on screen get merged into one cluster marker. `ST_Collect` groups them, `ST_Centroid` places the marker at the center of the group.

---

### 5. Clip and scale to tile coordinates — `ST_AsMVTGeom`

```sql
ST_AsMVTGeom(geom, bounds.geom, 4096, 256, true)
```

Translates geographic coordinates (longitude/latitude) into **pixel coordinates** within a 4096×4096 tile grid. Also clips geometries that cross the tile edge. Without this the browser wouldn't know where to draw anything.

---

### 6. Encode as binary MVT — `ST_AsMVT`

```sql
SELECT ST_AsMVT(mvtgeom.*, 'typename', 4096, 'geom')
```

Takes all the clipped geometries + their properties and encodes everything into the **Mapbox Vector Tile binary format** (protobuf). This is the actual `.mvt` byte blob your API returns to the map client (Mapbox GL, MapLibre, etc.).

---

### The full pipeline

```
300k rows in DB
    │
    ▼ ST_Intersects + GiST index
~few hundred rows in this tile
    │
    ├─ points → ST_SnapToGrid → ST_Centroid → ST_AsMVTGeom
    └─ tracks → ST_Simplify              → ST_AsMVTGeom
    │
    ▼ ST_AsMVT
binary blob → HTTP response → MapLibre renders it
```

The key insight is that PostGIS does **all the heavy lifting server-side** — filtering, simplifying, clustering, encoding — so the client receives a tiny binary blob ready to render, not raw coordinates.

---

## Vector Tiles API

### Endpoints

| Method | URL | Description |
|--------|-----|-------------|
| `GET`  | `/api/tiles/{type}/{z}/{x}/{y}.pbf` | Fetch a single tile; filters passed as query string |
| `POST` | `/api/tiles/{type}/{z}/{x}/{y}.pbf` | Same as GET but the `idlist` is sent as a JSON array in the request body (useful for large ID lists) |
| `GET`  | `/api/tiles/health` | Health check — returns `{ status, timestamp }` |

The response is a binary **Mapbox Vector Tile (protobuf)** file with content-type `application/x-protobuf`. An empty tile returns HTTP `204 No Content`.

---

### URL path parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `type` | string | ODH data type (see [Supported types](#supported-types)) |
| `z` | int | Zoom level (0–22) |
| `x` | int | Tile X coordinate |
| `y` | int | Tile Y coordinate |

---

### Query parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `source` | string | — | Filter by data source. Comma-separated for multiple values (e.g. `lts,idm`). Maps to `gen_source` column (or `origin` for timeseries, `source` for geoshape). |
| `tagfilter` | string | — | Filter by tags. Comma-separated OR logic — records matching **any** of the specified tags are returned (`gen_tags && ARRAY[...]`). |
| `idlist` | string | — | Comma-separated list of record IDs to include. On POST, sent as a JSON array in the body instead. |
| `jsonselector` | string | — | Dot-path fields from the `data` JSONB column to include in the tile properties. Comma-separated. See [jsonselector](#jsonselector). |
| `operationmode` | enum | `points` | Controls what geometry is rendered. See [Operation modes](#operation-modes). |
| `displaytracksonzoomlevel` | int | `12` | Minimum zoom level at which tracks are shown in `pointsandtracks` mode. Ignored for `tracks`-only mode (tracks always visible). Use with caution below zoom 11 — tile generation will be slow. |
| `enableclustering` | bool | `true` | When `true`, nearby points within the same tile grid cell are merged into a single cluster marker. The `cluster` property on the feature is `true` and `count` holds the number of merged points. Clustering is always off at zoom ≥ 17. |

---

### Operation modes

The `operationmode` parameter controls which geometry types are rendered in the tile.

| Value | Points rendered | Tracks rendered | Notes |
|-------|----------------|-----------------|-------|
| `points` | yes | no | Default. Uses `gen_position` or `gen_center_position` column. Fast path — uses `CreateQueryRawSQLPointsOnly`. |
| `tracks` | no | yes | Uses the geometry column (`geo` / `geometry4326`). Tracks are only shown at zoom ≥ `displaytracksonzoomlevel`. |
| `pointsandtracks` | yes | yes | Combines both in a single query. Each feature carries a `geom_type` property (`"point"` or `"track"`) so the client can style them differently. |

For types that have both a center point and a full geometry (e.g. `spatialdata`, `announcement`, `urbangreen`), `pointsandtracks` lets you show a marker at low zoom and the full shape when zoomed in.

---

### jsonselector

By default each tile feature only contains `id` and a short name (`gen_shortname` or `name` for geoshapes). Use `jsonselector` to include additional fields from the `data` JSONB column.

Allowed top-level fields (prefix match, case-insensitive):

- `Shortname`
- `Source`
- `Active`
- `Detail`
- `ContactInfos`
- `Mapping`
- `StartTime`
- `EndTime`

Nested paths use dot notation. For keys that contain dots themselves, use bracket notation:

```
jsonselector=Detail.de.Title,Mapping['tirol.mapservices.eu'].id
```

Passing a field that does not start with one of the allowed prefixes returns `400 Bad Request`.

---

### Supported types

Each type maps to a PostgreSQL table and a pair of geometry columns.

| Type | Table | Point column | Track/shape column |
|------|-------|--------------|--------------------|
| `accommodation` | `accommodations` | `gen_position` | — |
| `accommodationroom` | `accommodationrooms` | `gen_position` | — |
| `ltsactivity` | `activities` | `gen_position` | — |
| `ltspoi` | `pois` | `gen_position` | — |
| `event` | `events` | `gen_position` | — |
| `odhactivitypoi` | `smgpois` | `gen_position` | — |
| `package` | `packages` | `gen_position` | — |
| `measuringpoint` | `measuringpoints` | `gen_position` | — |
| `webcam` | `webcams` | `gen_position` | — |
| `article` | `articles` | `gen_position` | — |
| `venue` | `venues_v2` | `gen_position` | — |
| `eventshort` | `eventeuracnoi` | `gen_position` | — |
| `experiencearea` | `experienceareas` | `gen_position` | — |
| `metaregion` | `metaregions` | `gen_position` | — |
| `region` | `regions` | `gen_position` | — |
| `tourismassociation` | `tvs` | `gen_position` | — |
| `municipality` | `municipalities` | `gen_position` | — |
| `district` | `districts` | `gen_position` | — |
| `skiarea` | `skiareas` | `gen_position` | — |
| `skiregion` | `skiregions` | `gen_position` | — |
| `area` | `areas` | `gen_position` | — |
| `wineaward` | `wines` | `gen_position` | — |
| `odhtag` | `smgtags` | `gen_position` | — |
| `publisher` | `publishers` | `gen_position` | — |
| `source` | `sources` | `gen_position` | — |
| `weatherhistory` | `weatherdatahistory` | `gen_position` | — |
| `odhmetadata` | `metadata` | `gen_position` | — |
| `tag` | `tags` | `gen_position` | — |
| `geoshape` | `geoshapes` | `gen_center_position` | `geometry4326` |
| `announcement` | `announcements` | `gen_center_position` | `geo` |
| `urbangreen` | `urbangreens` | `gen_center_position` | `geo` |
| `spatialdata` | `spatialdatas` | `gen_center_position` | `geo` |
| `timeseries` | `timeseries` | `pointprojection` | — |

Types with a track/shape column support `operationmode=tracks` and `operationmode=pointsandtracks`. All others only support `operationmode=points`.

Most types also enforce `gen_access_role @> ARRAY['ANONYMOUS']` — the exceptions are `geoshapes`, `spatialdatas`, and `timeseries`.

---

### How it all works together

```
GET /api/tiles/odhactivitypoi/12/2178/1430.pbf
    ?source=lts
    &tagfilter=hiking
    &jsonselector=Detail.de.Title
    &operationmode=points
    &enableclustering=true
```

1. The controller resolves `odhactivitypoi` → table `smgpois`, point column `gen_position`.
2. Parameters are validated (zoom range, tile coordinates, jsonselector whitelist).
3. The service calculates the tile bounding box for z=12, x=2178, y=1430.
4. PostGIS filters rows with `ST_Intersects(gen_position, tile_bounds)` using the GiST index.
5. The `source=lts` filter adds `AND gen_source = 'lts'`.
6. The `tagfilter=hiking` filter adds `AND gen_tags @> ARRAY['hiking']`.
7. Points are snapped to a grid, clustered, and encoded as MVT with `Detail.de.Title` included in the feature properties.
8. The binary blob is returned to the map client.
