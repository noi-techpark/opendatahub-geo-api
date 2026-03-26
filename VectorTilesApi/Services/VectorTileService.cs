// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Data;
using System.Text.RegularExpressions;
using Npgsql;


namespace OpenDataHubVectorTileApi.Services;

public class VectorTileService : IVectorTileService
{
    private readonly string _connectionString;    
    private readonly ILogger<VectorTileService> _logger;

    public VectorTileService(IConfiguration configuration, ILogger<VectorTileService> logger)
    {
        // Try to get full connection string first
        _connectionString = configuration != null && !String.IsNullOrEmpty(configuration["PG_CONNECTION"]) 
        ? configuration["PG_CONNECTION"]! 
        : "";
        
        _logger = logger;        
        _logger.LogInformation("Database connection configured for host: {Host}", 
            new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Host);
    }    

    public async Task<byte[]> GetVectorTileAsync(
        string tableName, 
        string type, 
        int z, 
        int x, 
        int y, 
        string? source, 
        string? tagfilter, 
        string? jsonselector, 
        string? geometrycolumn, 
        string geometrycentercolumn, 
        List<string>? idlist, 
        bool cluster = false,
        AllowedOperationMode operationMode = AllowedOperationMode.points,
        int displayTracksonZoomLevel = 12
        )
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Calculate tile bounds using Web Mercator projection (EPSG:3857)
            var (xmin, ymin, xmax, ymax) = TileToBounds(x, y, z);

            var idlistquery = CreateIdFilter(idlist, type, out var idparameters);
            
            var additionalwhereclause = " AND gen_access_role @> Array['ANONYMOUS']";

            var jsonselectorqueryresult = CreateJsonBSelector(type, jsonselector);

            //Special Case geoshape and spatialdata clear additionalwhereclause 
            if (tableName == "geoshapes" || tableName == "spatialdatas")
            {
                additionalwhereclause = "";
            }
            else  if (tableName == "timeseries")
            {
                //TODO
                additionalwhereclause = "";
            }

            //Get the Source query
            var sourcequery = CreateSourceFilter(source, type, out var sourceparameters);
            var tagquery = CreateTagFilter(tagfilter, type, out var tagparameters);

            var (clusterpoints, showpoints, showtracks, showtracksatzoomlevel) = CheckOperationMode(operationMode, cluster, displayTracksonZoomLevel);

            var query = GetQuery(                
                z, 
                tableName, 
                type, 
                sourcequery, 
                tagquery, 
                idlistquery,
                jsonselectorqueryresult.Item1, 
                jsonselectorqueryresult.Item2,
                geometrycolumn,
                geometrycentercolumn,
                additionalwhereclause,
                clusterpoints, 
                showpoints,
                showtracks,
                showtracksatzoomlevel
                );
    
            await using var cmd = new NpgsqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@xmin", xmin);
            cmd.Parameters.AddWithValue("@ymin", ymin);
            cmd.Parameters.AddWithValue("@xmax", xmax);
            cmd.Parameters.AddWithValue("@ymax", ymax);
            cmd.Parameters.AddWithValue("@zoom", z);
            // if(idlist != null)
            //     cmd.Parameters.AddWithValue("ids", idlist.ToArray());

            foreach (var param in idparameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value);
            }

            // Add parameters to your command
            foreach (var param in sourceparameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value);
            }
            foreach (var tagparam in tagparameters)
            {
                cmd.Parameters.AddWithValue(tagparam.Key, tagparam.Value);
            }

            _logger.LogInformation("SQL: {Sql} | Params: {Params}",
                cmd.CommandText,
                cmd.Parameters
                    .ToDictionary(p => p.ParameterName, p => p.Value)
            );

            //This often leads to  "Exception while reading from stream" 
            // var result = await cmd.ExecuteScalarAsync();

            // if (result == null || result == DBNull.Value)
            // {
            //     return Array.Empty<byte>();
            // }

            // return (byte[])result;

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

            if (!await reader.ReadAsync() || reader.IsDBNull(0))
            {
                return Array.Empty<byte>();
            }

            await using var stream = reader.GetStream(0);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating vector tile for table {TableName} at z:{Z} x:{X} y:{Y}",
                tableName, z, x, y);
            throw;
        }
    }
    

    private static string GetQuery(        
        int zoomlevel, 
        string tableName, 
        string type, 
        string sourcequery, 
        string tagquery, 
        string idlistquery, 
        string jsonselectorquery, 
        string jsonselectorquerycluster, 
        string? geometrycolumn,
        string geometrycentercolumn,
        string additionalwhereclause,
        bool clusterpoints, 
        bool showpoints,
        bool showtracks,
        int showtracksatzoomlevel)
    {   
        if(showpoints && !showtracks)
            return CreateQueryRawSQLPointsOnly(
                clusterpoints,
                tableName,
                geometrycentercolumn,
                jsonselectorquery,
                jsonselectorquerycluster,
                sourcequery,
                tagquery,
                idlistquery,
                additionalwhereclause,
                type
            );

        return CreateQueryRawSQLBasedOnParameters(
            showpoints, 
            showtracks, 
            clusterpoints,
            showtracksatzoomlevel, 
            tableName, 
            geometrycolumn,
            geometrycentercolumn,
            jsonselectorquery,
            jsonselectorquerycluster,
            sourcequery,
            tagquery,
            idlistquery,
            additionalwhereclause,
            type
            );      
    }

    private static string CreateQueryRawSQLBasedOnParameters(
        bool showpoints,
        bool showtracks,
        bool clusterpoints,
        int showtracksatzoomlevel,
        string tableName,
        string geometrycolumn,
        string geometrycentercolumn,
        string jsonselectorquery,
        string jsonselectorquerycluster,
        string sourcequery,
        string tagquery,
        string idlistquery,
        string additionalwhereclause,
        string type
        )
    {
       string pointsCte = showpoints ? $@"
    points AS (
        SELECT
            id,
            {jsonselectorquery},
            {geometrycentercolumn} AS geom,
            ST_XMax(bounds.geom) - ST_XMin(bounds.geom) AS tile_width
        FROM {tableName}, bounds
        WHERE ST_Intersects(
            {geometrycentercolumn},
            bounds.geom
        ){sourcequery}{tagquery}{idlistquery}{additionalwhereclause}
    ),

    grid AS (
        SELECT
            *,
            ST_SnapToGrid(
                geom,
                tile_width /
                CASE
                    WHEN @zoom < 8  THEN 16
                    WHEN @zoom < 12 THEN 32
                    ELSE 64
                END
            ) AS gridcell
        FROM points
    ),

    clustered AS (
        SELECT
            MIN(id) AS id,
            CASE WHEN COUNT(*) = 1
                THEN {jsonselectorquerycluster}
                ELSE NULL
            END AS data,
            COUNT(*) AS count,
            ST_Centroid(ST_Collect(geom)) AS geom
        FROM grid
        GROUP BY {(clusterpoints ? "gridcell" : "geom")}
    ),

    mvtgeom_points AS (
        SELECT
            c.id,
            c.data,
            c.count,
            ({(clusterpoints ? "(c.count > 1) AND (@zoom <= 16)" : "false")}) AS cluster,
            ST_AsMVTGeom(c.geom, bounds.geom, 4096, 256, true) AS geom
        FROM clustered c, bounds
    )" : @"
    mvtgeom_points AS (
        SELECT null::text AS id, null::text AS data, null::int AS count, false AS cluster, null::geometry AS geom
        WHERE false
    )";

string tracksCte = showtracks ? $@"
    tracks AS (
        SELECT
            id,
            {jsonselectorquery},
            ST_Simplify(
                {geometrycolumn},
                CASE
                    WHEN @zoom < 14 THEN 0.0001
                    WHEN @zoom < 16 THEN 0.00005
                    ELSE 0.00001
                END
            ) AS geom
        FROM {tableName}, bounds
        WHERE @zoom >= {showtracksatzoomlevel}
        AND ST_Intersects(
            {geometrycolumn},
            bounds.geom
        )
        AND ST_GeometryType({geometrycolumn}) NOT IN ('ST_Point', 'ST_MultiPoint')
        {sourcequery}{tagquery}{idlistquery}{additionalwhereclause}
    ),

    mvtgeom_tracks AS (
        SELECT
            t.id,
            t.data,
            ST_AsMVTGeom(t.geom, bounds.geom, 4096, 256, true) AS geom
        FROM tracks t, bounds
        WHERE t.geom IS NOT NULL
    )" : @"
    mvtgeom_tracks AS (
        SELECT null::text AS id, null::text AS data, null::geometry AS geom
        WHERE false
    )";

return $@"
    WITH bounds AS (
        SELECT
            ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857) AS geom_3857,
            ST_Transform(ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857), 4326) AS geom
    ),
    {pointsCte},
    {tracksCte}

    SELECT ST_AsMVT(mvtgeom.*, '{type}', 4096, 'geom')
    FROM (
        SELECT id, data::text, null AS count, false AS cluster, 'track' AS geom_type, geom
        FROM mvtgeom_tracks
        WHERE geom IS NOT NULL

        UNION ALL

         SELECT id, data::text, count, cluster, 'point' AS geom_type, geom
        FROM mvtgeom_points
        WHERE geom IS NOT NULL        
    ) mvtgeom;";
    }

    private static string CreateQueryRawSQLPointsOnly(
        bool clusterpoints,
        string tableName,
        string geometrycentercolumn,
        string jsonselectorquery,
        string jsonselectorquerycluster,
        string sourcequery,
        string tagquery,
        string idlistquery,
        string additionalwhereclause,
        string type
    )
    {
        // At low zoom levels, cap how many points we scan per tile to avoid
        // hammering 300k rows. The grid clustering will reduce them anyway,
        // so fetching more than this adds no visual value.
        const int lowZoomLimit = 5000;
        const int midZoomLimit = 10000;

        // At zoom >= 17 there is no point clustering even if clusterpoints=true
        string clusterFlag = clusterpoints
            ? "(c.count > 1) AND (@zoom <= 16)"
            : "false";

        string groupBy = clusterpoints ? "gridcell" : "geom";

        return $@"
        WITH bounds AS (
            SELECT
                ST_Transform(ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857), 4326) AS geom
        ),

        -- Pull only the points that intersect the tile, capped per zoom level
        -- to avoid full-table scans at low zoom on large datasets
        points AS (
            SELECT
                id,
                {jsonselectorquery},
                {geometrycentercolumn} AS geom,
                ST_XMax(bounds.geom) - ST_XMin(bounds.geom) AS tile_width
            FROM {tableName}, bounds
            WHERE ST_Intersects(
                {geometrycentercolumn},
                bounds.geom
            ){sourcequery}{tagquery}{idlistquery}{additionalwhereclause}
            LIMIT CASE
                WHEN @zoom < 8  THEN {lowZoomLimit}
                WHEN @zoom < 12 THEN {midZoomLimit}
                ELSE NULL  -- no limit at high zoom, tile area is small
            END
        ),

        -- Snap each point to a grid cell whose size shrinks as zoom increases.
        -- All points in the same cell get merged into one cluster marker.
        grid AS (
            SELECT
                *,
                ST_SnapToGrid(
                    geom,
                    tile_width /
                    CASE
                        WHEN @zoom < 8  THEN 16
                        WHEN @zoom < 12 THEN 32
                        ELSE 64
                    END
                ) AS gridcell
            FROM points
        ),

        clustered AS (
            SELECT
                MIN(id)   AS id,
                CASE WHEN COUNT(*) = 1
                    THEN {jsonselectorquerycluster}
                    ELSE NULL
                END       AS data,
                COUNT(*)  AS count,
                ST_Centroid(ST_Collect(geom)) AS geom
            FROM grid
            GROUP BY {groupBy}
        ),

        mvtgeom AS (
            SELECT
                c.id,
                c.data::text,
                c.count,
                ({clusterFlag}) AS cluster,
                ST_AsMVTGeom(
                    c.geom,
                    bounds.geom,
                    4096, 256, true
                ) AS geom
            FROM clustered c, bounds
            WHERE ST_AsMVTGeom(c.geom, bounds.geom, 4096, 256, true) IS NOT NULL
        )

        SELECT ST_AsMVT(mvtgeom.*, '{type}', 4096, 'geom')
        FROM mvtgeom;";
    }


    /// <summary>
    /// Calculate Web Mercator bounds for a tile
    /// </summary>
    private static (double xmin, double ymin, double xmax, double ymax) TileToBounds(int x, int y, int z)
    {
        const double earthRadius = 6378137.0;
        const double originShift = 2.0 * Math.PI * earthRadius / 2.0;

        var tileSize = 2.0 * originShift / Math.Pow(2, z);
        
        var xmin = x * tileSize - originShift;
        var xmax = (x + 1) * tileSize - originShift;
        var ymin = originShift - (y + 1) * tileSize;
        var ymax = originShift - y * tileSize;

        return (xmin, ymin, xmax, ymax);
    }

    private static string CreateSourceFilter(string source, string type, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();
        
        if (string.IsNullOrWhiteSpace(source))
        {
            return "";
        }

        var columnName = type == "geoshape" ? "source" : "gen_source";
        if(type == "timeseries")
            columnName = "origin";

        var sources = source.Split(',')
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToArray();
        
        if (sources.Length == 0)
        {
            return "";
        }
        
        if (sources.Length == 1)
        {
            // Single source - use equality
            parameters["source"] = sources[0];
            return $" AND {columnName} = @source";
        }
        
        // Multiple sources - use IN clause
        var paramNames = new List<string>();
        for (int i = 0; i < sources.Length; i++)
        {
            var paramName = $"source{i}";
            parameters[paramName] = sources[i];
            paramNames.Add($"@{paramName}");
        }
        
        return $" AND {columnName} IN ({string.Join(", ", paramNames)})";
    }

    private static string CreateIdFilter(List<string> idlist, string type, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();
        string columnName = "id";

        if (type == "timeseries")
        {
            //TODO
        }
        
        if (idlist == null || idlist.Count == 0)
        {
            return "";
        }                
        
        if (idlist.Count == 1)
        {
            // Single source - use equality
            parameters["id"] = idlist[0];
            return $" AND {columnName} = @id";
        }
        
        // Multiple sources - use IN clause
        var paramNames = new List<string>();
        for (int i = 0; i <= idlist.Count - 1; i++)
        {
            var paramName = $"id{i}";
            parameters[paramName] = idlist[i];
            paramNames.Add($"@{paramName}");
        }
        
        return $" AND {columnName} IN ({string.Join(", ", paramNames)})";
    }

    private static string CreateTagFilter(string tags, string type, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();

        if (string.IsNullOrWhiteSpace(tags))
        {
            return "";
        }

        var tagarray = tags.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();

        if (tagarray.Length == 0)
        {
            return "";
        }

        if (type == "geoshape")
        {
            if (tagarray.Length == 1)
            {
                // Single source - use equality
                parameters["type"] = tagarray[0];
                return $" AND {tagarray} = @type";
            }

            // Multiple sources - use IN clause
            var paramNames = new List<string>();
            for (int i = 0; i < tagarray.Length; i++)
            {
                var paramName = $"source{i}";
                parameters[paramName] = tagarray[i];
                paramNames.Add($"@{paramName}");
            }

            return $" AND type IN ({string.Join(", ", paramNames)})";
        }
        else
        {
            // Multiple 
            var paramNames = new List<string>();
            for (int i = 0; i < tagarray.Length; i++)
            {
                var paramName = $"tag{i}";
                parameters[paramName] = tagarray[i];
                paramNames.Add($"@{paramName}");
            }

            return $" AND gen_tags @> ARRAY[{string.Join(", ", paramNames)}]";
        }
    }

    // private static (string, string) CreateJsonBSelector(string type, string jsonselector)    
    // {
    //     var jsonselectordefault = ("gen_shortname as data","MIN(data)");
    //     if(type == "geoshape")
    //         jsonselectordefault = ("name","MIN(name)");
    //     else if (type == "timeseries")
    //     {
    //         //TODO
    //     }    

    //     if (jsonselector == null)
    //         return jsonselectordefault;

    //     var parts = jsonselector.Split(',');

    //     var jsonBuildParts = parts
    //         .Select(p =>
    //         {
    //             var trimmed = p.Trim();

    //             // Detail.de.Title → Detail,de,Title
    //             var jsonPath = string.Join(",", trimmed.Split('.'));

    //             return $"'{trimmed}', data#>'{{{jsonPath}}}'";
    //         });

    //     var selectquery = $@" jsonb_strip_nulls(jsonb_build_object({string.Join(", ", jsonBuildParts)})) AS data";

    //     return (selectquery, "jsonb_agg(data)->0");
    // }

    private static (string, string) CreateJsonBSelector(string type, string jsonselector)
    {
        var jsonselectordefault = ("gen_shortname as data", "MIN(data)");
        if (type == "geoshape")
            jsonselectordefault = ("name", "MIN(name)");
        else if (type == "timeseries")
        {
            //TODO
        }

        if (jsonselector == null)
            return jsonselectordefault;

        var parts = jsonselector.Split(',');

        var jsonBuildParts = parts
            .Select(p =>
            {
                var trimmed = p.Trim();
                var segments = SplitPath(trimmed).ToList();
                var cleanName = string.Join(".", segments);          // "Mapping.tirol.mapservices.eu.id"
                var pgPath    = ToPostgresJsonPath(trimmed);         // data->'Mapping'->'tirol.mapservices.eu'->'id'
                return $"'{cleanName}', {pgPath}";
            });

        var selectquery = $@"jsonb_strip_nulls(jsonb_build_object({string.Join(", ", jsonBuildParts)})) AS data";

        return (selectquery, "jsonb_agg(data)->0");
    }

    /// <summary>
    /// Splits a field path respecting bracket notation for keys containing dots.
    /// e.g. "Mapping['tirol.mapservices.eu'].id" or "Mapping.tirol.mapservices.eu.id"
    /// </summary>
    private static IEnumerable<string> SplitPath(string field)
    {
        return Regex.Matches(field, @"\['([^']+)'\]|([^.\[]+)")
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }

    /// <summary>
    /// Converts a field path to a PostgreSQL JSONB path using -> operator.
    /// e.g. "Detail.de.Title"              → data->'Detail'->'de'->'Title'
    ///      "Mapping['tirol.mapservices.eu'].id" → data->'Mapping'->'tirol.mapservices.eu'->'id'
    /// </summary>
    private static string ToPostgresJsonPath(string field, string jsonColumn = "data")
    {
        var segments = SplitPath(field);
        return segments.Aggregate(jsonColumn, (path, segment) => $"{path}->'{segment}'");
    }

    private static (bool, bool, bool, int) CheckOperationMode(AllowedOperationMode operationmode, bool enableclustering, int displaytracksonzoomlevel)
    {
        bool clusterpoints = enableclustering;
        int showtracksatzoomlevel = 12;
        bool showpoints = false;
        bool showtracks = false;

        switch (operationmode)
        {
            case AllowedOperationMode.points:
                showpoints = true;                
                break;
            case AllowedOperationMode.tracks:
                showtracks = true;
                showtracksatzoomlevel = displaytracksonzoomlevel;
                break;
            case AllowedOperationMode.pointsandtracks:
                showpoints = true;
                showtracks = true;
                showtracksatzoomlevel = displaytracksonzoomlevel;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return (clusterpoints, showpoints, showtracks, showtracksatzoomlevel);
    }
}