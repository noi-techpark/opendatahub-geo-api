// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Data;
using System.Text.RegularExpressions;
using Npgsql;


namespace OpenDataHubVectorTileApi.Services;

public partial class VectorTileService : IVectorTileService
{
    // Tables having the generated column gen_active (bool)
    public static readonly HashSet<string> TablesWithActive =
    [
        "accommodations", "accommodationrooms", "announcements", "areas", "articles", "districts",
        "eventeuracnoi", "events", "experienceareas", "measuringpoints", "metaregions", "municipalities",
        "regions", "skiareas", "skiregions", "smgpois", "spatialdatas", "tvs", "urbangreens",
        "venues", "webcams", "wines"
    ];

    // Tables having the generated column gen_publishedon (text[])
    public static readonly HashSet<string> TablesWithPublishedOn =
    [
        "accommodations", "accommodationrooms", "articles", "districts", "eventeuracnoi", "events",
        "experienceareas", "measuringpoints", "metaregions", "municipalities", "regions",
        "skiareas", "skiregions", "smgpois", "smgtags", "tags", "tvs", "venues", "webcams", "wines"
    ];

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
        int displayTracksonZoomLevel = 12,
        DateTime? begindate = null,
        DateTime? enddate = null,
        bool? active = true,
        string? publishedon = null
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
            additionalwhereclause += CreateEventDateFilter(begindate, enddate, out var dateparameters);
            additionalwhereclause += CreateActiveFilter(active, tableName, out var activeparameters);
            additionalwhereclause += CreatePublishedOnFilter(publishedon, tableName, out var publishedonparameters);

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
            foreach (var dateparam in dateparameters)
            {
                cmd.Parameters.AddWithValue(dateparam.Key, dateparam.Value);
            }
            foreach (var activeparam in activeparameters)
            {
                cmd.Parameters.AddWithValue(activeparam.Key, activeparam.Value);
            }
            foreach (var publishedonparam in publishedonparameters)
            {
                cmd.Parameters.AddWithValue(publishedonparam.Key, publishedonparam.Value);
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
        LIMIT CASE
            WHEN @zoom < 8  THEN 5000
            WHEN @zoom < 12 THEN 10000
            ELSE NULL
        END
    ),

    {CreatePointClusterCtes(clusterpoints, jsonselectorquerycluster)},

    mvtgeom_points AS (
        SELECT
            c.id,
            c.data,
            c.count,
            (c.count > 1) AS cluster,
            c.members,
            ST_AsMVTGeom(c.geom, bounds.geom, 4096, 256, true) AS geom
        FROM clustered c, bounds
    )" : @"
    mvtgeom_points AS (
        SELECT null::text AS id, null::text AS data, null::int AS count, false AS cluster, null::text AS members, null::geometry AS geom
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
        SELECT id, data::text, null AS count, false AS cluster, null::text AS members, 'track' AS geom_type, geom
        FROM mvtgeom_tracks
        WHERE geom IS NOT NULL

        UNION ALL

         SELECT id, data::text, count, cluster, members, 'point' AS geom_type, geom
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

        {CreatePointClusterCtes(clusterpoints, jsonselectorquerycluster)},

        mvtgeom AS (
            SELECT
                c.id,
                c.data::text,
                c.count,
                (c.count > 1) AS cluster,
                c.members,
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


    // Markers closer than this distance (pixels on a 512px tile) overlap on screen and are always merged
    private const int OverlapPixels = 12;

    // Clusters up to this size carry their members (id, data) so clients can list them
    private const int MaxClusterMembers = 10;

    /// <summary>
    /// Creates the clustered CTE from the points CTE (id, data, geom, tile_width).
    /// Clustering: 1. merge markers overlapping on screen (distance based, not split by grid lines),
    /// 2. up to zoom 16 merge the resulting groups on a grid whose cell size shrinks with the zoom.
    /// Without clustering every point is returned on its own.
    /// </summary>
    private static string CreatePointClusterCtes(bool clusterpoints, string jsonselectorquerycluster)
    {
        if (!clusterpoints)
        {
            return @"
    clustered AS (
        SELECT id, data, 1::bigint AS count, null::text AS members, geom
        FROM points
    )";
        }

        return $@"
    overlap AS (
        SELECT
            *,
            ST_ClusterDBSCAN(
                ST_Transform(geom, 3857),
                eps := (@xmax - @xmin) / 512.0 * {OverlapPixels},
                minpoints := 1
            ) OVER () AS overlapgroup
        FROM points
    ),

    overlapcenter AS (
        SELECT overlapgroup, ST_Centroid(ST_Collect(geom)) AS center
        FROM overlap
        GROUP BY overlapgroup
    ),

    grid AS (
        SELECT
            o.*,
            CASE WHEN @zoom <= 16
                THEN ST_SnapToGrid(
                    c.center,
                    o.tile_width /
                    CASE
                        WHEN @zoom < 8  THEN 16
                        WHEN @zoom < 12 THEN 32
                        ELSE 64
                    END
                )
                ELSE c.center
            END AS gridcell
        FROM overlap o
        JOIN overlapcenter c USING (overlapgroup)
    ),

    clustered AS (
        SELECT
            MIN(id) AS id,
            CASE WHEN COUNT(*) = 1
                THEN {jsonselectorquerycluster}
                ELSE NULL
            END AS data,
            COUNT(*) AS count,
            CASE WHEN COUNT(*) BETWEEN 2 AND {MaxClusterMembers}
                THEN jsonb_agg(jsonb_build_object('id', id, 'data', data) ORDER BY id)::text
                ELSE NULL
            END AS members,
            ST_Centroid(ST_Collect(geom)) AS geom
        FROM grid
        GROUP BY gridcell
    )";
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
                return " AND type = @type";
            }

            // Multiple sources - use IN clause
            var paramNames = new List<string>();
            for (int i = 0; i < tagarray.Length; i++)
            {
                var paramName = $"tag{i}";
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

            return $" AND gen_tags && ARRAY[{string.Join(", ", paramNames)}]";
        }
    }

    /// <summary>
    /// Filters by gen_active, only applied on tables having the gen_active column.
    /// active null returns active and inactive records.
    /// </summary>
    private static string CreateActiveFilter(bool? active, string tableName, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();

        if (active == null || !TablesWithActive.Contains(tableName))
        {
            return "";
        }

        parameters["active"] = active.Value;
        return " AND gen_active = @active";
    }

    /// <summary>
    /// Filters by gen_publishedon (text array). Comma-separated OR logic, records published on any of the given channels are returned.
    /// </summary>
    private static string CreatePublishedOnFilter(string? publishedon, string tableName, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();

        if (string.IsNullOrWhiteSpace(publishedon) || !TablesWithPublishedOn.Contains(tableName))
        {
            return "";
        }

        var publishedonarray = publishedon.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();

        if (publishedonarray.Length == 0)
        {
            return "";
        }

        var paramNames = new List<string>();
        for (int i = 0; i < publishedonarray.Length; i++)
        {
            var paramName = $"publishedon{i}";
            parameters[paramName] = publishedonarray[i];
            paramNames.Add($"@{paramName}");
        }

        return $" AND gen_publishedon && ARRAY[{string.Join(", ", paramNames)}]";
    }

    /// <summary>
    /// Filters Events whose EventDates (gen_eventdates tsmultirange) overlap the requested period.
    /// An enddate without time component includes the whole day.
    /// </summary>
    private static string CreateEventDateFilter(DateTime? begindate, DateTime? enddate, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();

        if (begindate == null && enddate == null)
        {
            return "";
        }

        var lower = "NULL";
        var upper = "NULL";

        if (begindate != null)
        {
            parameters["begindate"] = DateTime.SpecifyKind(begindate.Value, DateTimeKind.Unspecified);
            lower = "@begindate::timestamp";
        }

        if (enddate != null)
        {
            var end = enddate.Value.TimeOfDay == TimeSpan.Zero ? enddate.Value.AddDays(1) : enddate.Value;
            parameters["enddate"] = DateTime.SpecifyKind(end, DateTimeKind.Unspecified);
            upper = "@enddate::timestamp";
        }

        return $" AND gen_eventdates && tsrange({lower}, {upper}, '[)')";
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
            jsonselectordefault = ("name as data", "MIN(data)");
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
    [GeneratedRegex(@"^[A-Za-z0-9_.]+$")]
    private static partial Regex SafeJsonSegmentRegex();

    private static string ToPostgresJsonPath(string field, string jsonColumn = "data")
    {
        var segments = SplitPath(field);
        return segments.Aggregate(jsonColumn, (path, segment) =>
        {
            // Only alphanumeric, underscores, and dots (for bracket-notation keys) are allowed.
            // Reject anything else to prevent SQL injection via single-quote breakout.
            if (!SafeJsonSegmentRegex().IsMatch(segment))
                throw new ArgumentException($"Invalid jsonselector segment: '{segment}'");
            return $"{path}->'{segment}'";
        });
    }

    private static (bool, bool, bool, int) CheckOperationMode(AllowedOperationMode operationmode, bool enableclustering, int displaytracksonzoomlevel)
    {
        bool clusterpoints = enableclustering;
        bool showpoints = false;
        bool showtracks = false;
        int showtracksatzoomlevel;

        switch (operationmode)
        {
            case AllowedOperationMode.points:
                showpoints = true;
                showtracksatzoomlevel = 0;
                break;
            case AllowedOperationMode.tracks:
                showtracks = true;
                // tracks-only: no point clusters to worry about, show from zoom 0 by default
                showtracksatzoomlevel = 0;
                break;
            case AllowedOperationMode.pointsandtracks:
                showpoints = true;
                showtracks = true;
                // combined mode: use caller-supplied threshold to avoid visual clutter at low zoom
                showtracksatzoomlevel = displaytracksonzoomlevel;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return (clusterpoints, showpoints, showtracks, showtracksatzoomlevel);
    }
}