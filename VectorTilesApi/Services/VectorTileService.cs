// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Npgsql;
using SqlKata.Execution;
using SqlKata.Compilers;

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
        // _logger.LogInformation("Database connection configured for host: {Host}", 
        //     new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Host);
    }

    public async Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y, string? source, string? jsonselector, string geocolumn, List<string>? idlist)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Calculate tile bounds using Web Mercator projection (EPSG:3857)
            var (xmin, ymin, xmax, ymax) = TileToBounds(x, y, z);

            var sourcequery = source != null 
                            ? $@" AND gen_source = @source"
                            : "";

            var idlistquery = idlist != null 
                            ? $@" AND WHERE id = ANY(@ids)"
                            : "";

            var jsonselectorquery = "data#>>'{Shortname}' as data";

            if(jsonselector != null)
            {
                jsonselectorquery = "";
                var jsonselectorfields = jsonselector.Split(",");
                foreach(var jsonselectorfield in jsonselectorfields)
                {
                    var jsonselectparsed = jsonselectorfield.Replace(".",",");
                    jsonselectorquery = jsonselectorquery + $@"data#>>'{jsonselectparsed}' as data.{jsonselectparsed}";
                }

            }

            // Build the query using raw SQL with ST_AsMVT
            // Note: SqlKata doesn't directly support PostGIS functions, so we use raw SQL
            var query = $@"
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
                        {geocolumn},
                        ST_Transform(
                            ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 3857),
                            ST_SRID({geocolumn})
                        )
                    ){sourcequery}{idlistquery}
                )
                SELECT ST_AsMVT(mvtgeom.*, '{tableName}', 4096, 'geom')
                FROM mvtgeom
                WHERE geom IS NOT NULL;
            ";

            await using var cmd = new NpgsqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@xmin", xmin);
            cmd.Parameters.AddWithValue("@ymin", ymin);
            cmd.Parameters.AddWithValue("@xmax", xmax);
            cmd.Parameters.AddWithValue("@ymax", ymax);
            if(source != null)
                cmd.Parameters.AddWithValue("@source", source);
            if(idlist != null)
                cmd.Parameters.AddWithValue("ids", idlist.ToArray());
    

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
}