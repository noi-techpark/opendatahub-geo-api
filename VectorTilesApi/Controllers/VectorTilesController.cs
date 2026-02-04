// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.AspNetCore.Mvc;
using OpenDataHubVectorTileApi.Services;

namespace OpenDataHubVectorTileApi.Controllers;

[ApiController]
[Route("api/tiles")]
public class VectorTilesController : ControllerBase
{
    private readonly IVectorTileService _vectorTileService;
    private readonly ILogger<VectorTilesController> _logger;

    public VectorTilesController(IVectorTileService vectorTileService, ILogger<VectorTilesController> logger)
    {
        _vectorTileService = vectorTileService;
        _logger = logger;
    }

    /// <summary>
    /// Get a vector tile in Mapbox Vector Tile (MVT/protobuf) format
    /// </summary>
    /// <param name="tableName">Name of the PostGIS table</param>
    /// <param name="z">Zoom level</param>
    /// <param name="x">Tile X coordinate</param>
    /// <param name="y">Tile Y coordinate</param>
    /// <returns>Vector tile in protobuf format</returns>
    [HttpGet("{tableName}/{z}/{x}/{y}.pbf")]
    [Produces("application/x-protobuf")]
    public async Task<IActionResult> GetVectorTile(string tableName, int z, int x, int y)
    {
        try
        {
            // Validate tile coordinates
            var maxTile = (int)Math.Pow(2, z) - 1;
            if (x < 0 || x > maxTile || y < 0 || y > maxTile)
            {
                return BadRequest("Invalid tile coordinates");
            }

            // Validate zoom level (typical range: 0-22)
            if (z < 0 || z > 22)
            {
                return BadRequest("Invalid zoom level");
            }

            var tile = await _vectorTileService.GetVectorTileAsync(tableName, z, x, y);

            if (tile == null || tile.Length == 0)
            {
                // Return empty tile (204 No Content) or empty MVT
                return NoContent();
            }

            // Return the MVT tile with appropriate content type
            return File(tile, "application/x-protobuf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vector tile for {TableName}/{Z}/{X}/{Y}", 
                tableName, z, x, y);
            return StatusCode(500, "Error generating vector tile");
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}