// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

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

    public async Task<byte[]> GetVectorTileAsync(string tableName, string type, int z, int x, int y, string? source, string? tagfilter, string? jsonselector, string geocolumn, List<string>? idlist, bool cluster = false)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Calculate tile bounds using Web Mercator projection (EPSG:3857)
            var (xmin, ymin, xmax, ymax) = TileToBounds(x, y, z);

            // var idlistquery = idlist != null 
            //                 ? $@" AND WHERE id = ANY(@ids)"
            //                 : "";

            var idlistquery = CreateIdFilter(idlist, tableName, out var idparameters);
            
            var additionalwhereclause = " AND gen_access_role @> Array['ANONYMOUS']";

            var jsonselectorqueryresult = CreateJsonBSelector(jsonselector);

            //Special Case geoshape
            if (tableName == "geoshapes")
            {
                jsonselectorqueryresult.Item1 = "name";
                additionalwhereclause = "";
            }

            //Special Case spatialdata
            if (tableName == "spatialdatas")
            {
                additionalwhereclause = "";
            }

            //Get the Source query
            var sourcequery = CreateSourceFilter(source, tableName, out var sourceparameters);
            var tagquery = CreateTagFilter(tagfilter, tableName, out var tagparameters);


            var query = GetQuery(cluster, z, tableName, type, sourcequery, tagquery, idlistquery, jsonselectorqueryresult.Item1, jsonselectorqueryresult.Item2, geocolumn, additionalwhereclause);
    
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

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
            {
                return Array.Empty<byte>();
            }

            return (byte[])result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating vector tile for table {TableName} at z:{Z} x:{X} y:{Y}",
                tableName, z, x, y);
            throw;
        }
    }
    

    private static string GetQuery(bool cluster, int zoomlevel, string tableName, string type, string sourcequery, string tagquery, string idlistquery, string jsonselectorquery, string jsonselectorquerycluster, string geocolumn, string additionalwhereclause)
    {
        if (!cluster || zoomlevel >= 17)
        {
            // Build the query using raw SQL with ST_AsMVT
            // Note: SqlKata doesn't directly support PostGIS functions, so we use raw SQL
            return $@"
                WITH mvtgeom AS (
                    SELECT
                        id,
                        {jsonselectorquery},
                        ST_AsMVTGeom(
                            ST_Transform({geocolumn}, 3857),
                            ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857),
                            4096,
                            256,
                            true
                        ) AS geom
                    FROM {tableName}
                     WHERE ST_Intersects(
                         ST_Transform({geocolumn}, 3857),
                         ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857)
                     ){sourcequery}{tagquery}{idlistquery}{additionalwhereclause}
                )
                SELECT ST_AsMVT(mvtgeom.*, '{type}', 4096, 'geom')
                FROM mvtgeom
                WHERE geom IS NOT NULL;
            ";
        }
        else
        {
            return $@"WITH bounds AS (
                    SELECT ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857) AS geom
                ),

                points AS (
                    SELECT
                        id,
                        {jsonselectorquery},
                        ST_Transform({geocolumn}, 3857) AS geom
                    FROM {tableName}, bounds
                    WHERE ST_Intersects(
                        ST_Transform({geocolumn}, 3857),
                        bounds.geom
                    ){sourcequery}{tagquery}{idlistquery}{additionalwhereclause}
                ),

                grid AS (
                    SELECT
                        *,
                        ST_SnapToGrid(
                            geom,
                            (@xmax - @xmin) / 
                            CASE
                                WHEN @zoom < 8 THEN 16
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
                    GROUP BY gridcell
                ),

                mvtgeom AS (
                    SELECT
                        id,
                        data,
                        count,
                        (count > 1) AS cluster,
                        ST_AsMVTGeom(
                            geom,
                            ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857),
                            4096,
                            256,
                            true
                        ) AS geom
                    FROM clustered
                )

                SELECT ST_AsMVT(mvtgeom.*, '{type}', 4096, 'geom')
                FROM mvtgeom
                WHERE geom IS NOT NULL;"
                ;
        }
      
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

    private static string CreateSourceFitler(string source, string tableName)
    {
        var sourcequery = source != null 
                ? $@" AND gen_source = @source"
                : "";

        //Special Case geoshape
        if(tableName == "geoshapes")
        {            
            sourcequery = source != null 
                        ? $@" AND source = @source"
                        : "";            
        }

        return sourcequery;
    }

    private static string CreateSourceFilter(string source, string tableName, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();
        
        if (string.IsNullOrWhiteSpace(source))
        {
            return "";
        }

        var columnName = tableName == "geoshapes" ? "source" : "gen_source";
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

    private static string CreateIdFilter(List<string> idlist, string tableName, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();
        
        if (idlist == null || idlist.Count == 0)
        {
            return "";
        }                
        
        if (idlist.Count == 1)
        {
            // Single source - use equality
            parameters["id"] = idlist[0];
            return $" AND id = @id";
        }
        
        // Multiple sources - use IN clause
        var paramNames = new List<string>();
        for (int i = 0; i < idlist.Count - 1; i++)
        {
            var paramName = $"id{i}";
            parameters[paramName] = idlist[i];
            paramNames.Add($"@{paramName}");
        }
        
        return $" AND id IN ({string.Join(", ", paramNames)})";
    }

    private static string CreateTagFilter(string tags, string tableName, out Dictionary<string, object> parameters)
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

        if (tableName == "geoshapes")
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

    private static (string, string) CreateJsonBSelector(string jsonselector)
    {
        if (jsonselector == null)
            return ("gen_shortname as data","MIN(data)");

        var parts = jsonselector.Split(',');

        var jsonBuildParts = parts
            .Select(p =>
            {
                var trimmed = p.Trim();

                // Detail.de.Title → Detail,de,Title
                var jsonPath = string.Join(",", trimmed.Split('.'));

                return $"'{trimmed}', data#>'{{{jsonPath}}}'";
            });

        var selectquery = $@" jsonb_strip_nulls(jsonb_build_object({string.Join(", ", jsonBuildParts)})) AS data";

        return (selectquery, "jsonb_agg(data)->0");
    }
}