// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using OpenDataHubVectorTileApi.Services;

namespace OpenDataHubVectorTileApi.Controllers;

[ApiController]
[EnableCors("AllowAll")]
[Route("api/tiles")]
public class VectorTilesController : ControllerBase
{
    private readonly IVectorTileService _vectorTileService;
    private readonly ILogger<VectorTilesController> _logger;

    private List<string> _allowedTableNames;
    private List<string> _allowedGeoColumns;
    private List<string> _allowedJsonSelectors;

    public VectorTilesController(IVectorTileService vectorTileService, ILogger<VectorTilesController> logger)
    {
        _vectorTileService = vectorTileService;
        _logger = logger;
        _allowedTableNames = new List<string>(){"announcements","accommodations","smgpois","geodata"};
        _allowedGeoColumns = new List<string>(){"geo","gen_position","geometry"};
        _allowedJsonSelectors = new List<string>(){"Shortname","Source","Active","Detail.de.Title"};
    }

    /// <summary>
    /// Get a vector tile in Mapbox Vector Tile (MVT/protobuf) format
    /// </summary>
    /// <param name="tableName">Name of the PostGIS table</param>
    /// <param name="z">Zoom level</param>
    /// <param name="x">Tile X coordinate</param>
    /// <param name="y">Tile Y coordinate</param>
    /// <param name="source">Additional Source Filter</param>
    /// <param name="geocolumn">Overwrite column with geoinfo (default: geo)</param>
    /// <returns>Vector tile in protobuf format</returns>
    [HttpGet("{tableName}/{z}/{x}/{y}.pbf")]
    [Produces("application/x-protobuf")]
    public async Task<IActionResult> GetVectorTile(
        string tableName, 
        int z, 
        int x, 
        int y, 
        string? source = null, 
        string? jsonselector = null,
        string geocolumn = "geo")
    {
        try
        {
            //Validate passed parameters
            var (isValid, errorMessage) = ValidateParamters(tableName, z, x, y, source, jsonselector, geocolumn);
            if (!isValid)
                return BadRequest(errorMessage);            

            var tile = await _vectorTileService.GetVectorTileAsync(tableName, z, x, y, source, jsonselector, geocolumn, null);

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
    /// Post a vector tile in Mapbox Vector Tile (MVT/protobuf) format
    /// </summary>
    /// <param name="tableName">Name of the PostGIS table</param>
    /// <param name="z">Zoom level</param>
    /// <param name="x">Tile X coordinate</param>
    /// <param name="y">Tile Y coordinate</param>
    /// <param name="source">Additional Source Filter</param>
    /// <param name="geocolumn">Overwrite column with geoinfo (default: geo)</param>
    /// <returns>Vector tile in protobuf format</returns>
    [HttpPost("{tableName}/{z}/{x}/{y}.pbf")]
    [Produces("application/x-protobuf")]
    public async Task<IActionResult> PostVectorTile(
        [FromBody] List<string> idlist,
        string tableName, 
        int z, 
        int x, 
        int y, 
        string? source = null, 
        string? jsonselector = null, 
        string geocolumn = "geo"
        )
    {
        try
        {
            //Validate passed parameters
            var (isValid, errorMessage) = ValidateParamters(tableName, z, x, y, source, jsonselector, geocolumn);
            if (!isValid)
                return BadRequest(errorMessage);            

            var tile = await _vectorTileService.GetVectorTileAsync(tableName, z, x, y, source, jsonselector, geocolumn, idlist);

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

    private (bool IsValid, string? ErrorMessage) ValidateParamters(
        string tableName, 
        int z, 
        int x, 
        int y, 
        string? source, 
        string? jsonselector,
        string geocolumn
    )
    {
                    // Validate tile coordinates
            var maxTile = (int)Math.Pow(2, z) - 1;
            if (x < 0 || x > maxTile || y < 0 || y > maxTile)
            {
                return (false, "Invalid tile coordinates");
            }

            // Validate zoom level (typical range: 0-22)
            if (z < 0 || z > 22)
            {
                return (false, "Invalid zoom level");
            }

            if(!_allowedTableNames.Contains(tableName))
                return (false, "Invalid table name");
            
            if(geocolumn != null && !_allowedGeoColumns.Contains(geocolumn))
                return (false, "Invalid geo column");

            if(jsonselector != null && !_allowedJsonSelectors.Contains(jsonselector))
                return (false, "Invalid json selector");

            return (true, null); 
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