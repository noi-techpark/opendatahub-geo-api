// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Npgsql;
using SqlKata;
using SqlKata.Compilers;

namespace OpenDataHubVectorTileApi.Services;

/// <summary>
/// Alternative implementation showing how to use SqlKata Query Builder
/// for more complex queries with filters
/// </summary>
public class AdvancedVectorTileService : IVectorTileService
{
    private readonly string _connectionString;
    private readonly ILogger<AdvancedVectorTileService> _logger;
    private readonly PostgresCompiler _compiler;

    public AdvancedVectorTileService(IConfiguration configuration, ILogger<AdvancedVectorTileService> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL") 
            ?? throw new ArgumentNullException("PostgreSQL connection string is not configured");
        _logger = logger;
        _compiler = new PostgresCompiler();
    }

    public async Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y, string? source, string? jsonselector, string geocolumn, List<string>? idlist)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var (xmin, ymin, xmax, ymax) = TileToBounds(x, y, z);

            // Using SqlKata for the subquery to build the CTE
            var subQuery = new Query(tableName)
                .Select("id")
                .Select("data")
                .SelectRaw($@"
                    ST_AsMVTGeom(
                        ST_Transform(geo, 3857),
                        ST_MakeEnvelope({xmin}, {ymin}, {xmax}, {ymax}, 3857),
                        4096,
                        256,
                        true
                    ) AS geom")
                .WhereRaw($@"
                    ST_Intersects(
                        geo,
                        ST_Transform(
                            ST_MakeEnvelope({xmin}, {ymin}, {xmax}, {ymax}, 3857),
                            ST_SRID(geo)
                        )
                    )");

            // For demonstration: You can add additional filters using SqlKata
            // Example: Filter by JSONB data
            // subQuery.WhereJsonb("data->>'type'", "landmark");

            var compiledSubQuery = _compiler.Compile(subQuery);

            // Build the final query with CTE and ST_AsMVT
            var finalQuery = $@"
                WITH mvtgeom AS (
                    {compiledSubQuery.Sql}
                )
                SELECT ST_AsMVT(mvtgeom.*, '{tableName}', 4096, 'geom')
                FROM mvtgeom
                WHERE geom IS NOT NULL;
            ";

            await using var cmd = new NpgsqlCommand(finalQuery, connection);
            
            // Add parameters from SqlKata compilation
            foreach (var (key, value) in compiledSubQuery.NamedBindings)
            {
                cmd.Parameters.AddWithValue(key, value);
            }

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

/// <summary>
/// Extension methods for SqlKata to handle JSONB queries
/// </summary>
public static class SqlKataJsonbExtensions
{
    public static Query WhereJsonb(this Query query, string jsonPath, object value)
    {
        return query.WhereRaw($"{jsonPath} = ?", value);
    }

    public static Query WhereJsonbContains(this Query query, string column, string key, object value)
    {
        return query.WhereRaw($"{column} @> ?::jsonb", $"{{\"{key}\": \"{value}\"}}");
    }
}