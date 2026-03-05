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
    private List<string> _allowedJsonSelectors;

    public VectorTilesController(IVectorTileService vectorTileService, ILogger<VectorTilesController> logger)
    {
        _vectorTileService = vectorTileService;
        _logger = logger;
        _allowedJsonSelectors = new List<string>() { "Shortname", "Source", "Active", "Detail.de.Title", "Detail.de.BaseText" };
    }

    /// <summary>
    /// Get a vector tile in Mapbox Vector Tile (MVT/protobuf) format
    /// </summary>
    /// <param name="type">Name of the Open Data Hub data type</param>
    /// <param name="z">Zoom level</param>
    /// <param name="x">Tile X coordinate</param>
    /// <param name="y">Tile Y coordinate</param>
    /// <param name="idlist">Filter by a List of Ids</param>
    /// <param name="source">Source Filter</param>
    /// <param name="geocolumn">Define column where geoinfo is stored (standard is taken), needed if more geo columns are available on an object (Example geo column with center_postion, geo column with points,polygons etc...)</param>
    /// <param name="tagfilter">Filter by Tags Separator "," see Open data hub Content Api tagfilter logic</param>
    /// <param name="jsonselector">Include More data (by standard Id and where possible a name is included in the Vector Tiles)</param>    
    /// <returns>Vector tile in protobuf format</returns>
    [HttpGet("{type}/{z}/{x}/{y}.pbf")]
    [Produces("application/x-protobuf")]
    public async Task<IActionResult> GetVectorTile(
        string type,
        int z,
        int x,
        int y,
        string? idlist,
        string? source = null,
        string? tagfilter = null,
        string? jsonselector = null,
        AllowedOperationMode operationmode = AllowedOperationMode.points,  //points,tracks,pointsandtracks
        int displaytracksonzoomlevel = 12,
        bool enableclustering = true
        )
    {
        try
        {
            //Validate passed parameters
            var (isValid, errorMessage) = ValidateParamters(type, z, x, y, source, jsonselector);
            if (!isValid)
                return BadRequest(errorMessage);

            var tile = await GetVectorTilesFromService(type, z, x, y, !String.IsNullOrEmpty(idlist) ? idlist.Split(",").ToList() : null, source, tagfilter, jsonselector, operationmode, displaytracksonzoomlevel, enableclustering);

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
                type, z, x, y);
            return StatusCode(500, "Error generating vector tile");
        }
    }

    /// <summary>
    /// Post a vector tile in Mapbox Vector Tile (MVT/protobuf) format
    /// </summary>
    /// <param name="type">Name of the Open Data Hub Data type</param>
    /// <param name="z">Zoom level</param>
    /// <param name="x">Tile X coordinate</param>
    /// <param name="y">Tile Y coordinate</param>
    /// <param name="source">Additional Source Filter</param>
    /// <param name="geocolumn">Overwrite column with geoinfo (default: geo)</param>
    /// <returns>Vector tile in protobuf format</returns>
    [HttpPost("{type}/{z}/{x}/{y}.pbf")]
    [Produces("application/x-protobuf")]
    public async Task<IActionResult> PostVectorTile(
        [FromBody] List<string> idlist,
        string type,
        int z,
        int x,
        int y,
        string? source = null,
        string? tagfilter = null,
        string? jsonselector = null,
        AllowedOperationMode operationmode = AllowedOperationMode.points,  //points,tracks,pointsandtracks
        int displaytracksonzoomlevel = 12,
        bool enableclustering = true
        )
    {
        try
        {
            //Validate passed parameters
            var (isValid, errorMessage) = ValidateParamters(type, z, x, y, source, jsonselector);
            if (!isValid)
                return BadRequest(errorMessage);

            var tile = await GetVectorTilesFromService(type, z, x, y, idlist, source, tagfilter, jsonselector, operationmode, displaytracksonzoomlevel, enableclustering);

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
                type, z, x, y);
            return StatusCode(500, "Error generating vector tile");
        }
    }
    
    private async Task<byte[]> GetVectorTilesFromService(
        string type,
        int z,
        int x,
        int y,
        List<string>? idlist,
        string? source = null,
        string? tagfilter = null,
        string? jsonselector = null,
        AllowedOperationMode operationmode = AllowedOperationMode.points,
        int displaytracksonzoomlevel = 12,
        bool clusterpoints = true
    )
    {
        var (geometry_column, geometry_center_column) = TranslateTypeString2GeoColumns(type);

        return await _vectorTileService.GetVectorTileAsync(
            TranslateTypeString2Table(type), 
            type, 
            z, x, y, 
            source, 
            tagfilter, 
            jsonselector, 
            geometry_column, 
            geometry_center_column, 
            idlist, 
            clusterpoints, 
            operationmode, 
            displaytracksonzoomlevel);
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    private (bool IsValid, string? ErrorMessage) ValidateParamters(
        string type,
        int z,
        int x,
        int y,
        string? source,
        string? jsonselector
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

        TranslateTypeString2Table(type);

        // if (!String.IsNullOrEmpty(geocolumn) && !_allowedGeoColumns.Contains(geocolumn))
        //     return (false, "Invalid geo column");

        if (!String.IsNullOrEmpty(jsonselector) && !jsonselector.Split(',').Select(x => x.Trim()).All(x => _allowedJsonSelectors.Contains(x)))
            return (false, "Invalid json selector");

        return (true, null);
    }


    /// <summary>
    /// Translates Type (Metadata) as String to PG table Name
    /// </summary>
    /// <param name="odhtype"></param>
    /// <returns></returns>
    public static string TranslateTypeString2Table(string odhtype)
    {
        return odhtype switch
        {
            "accommodation" => "accommodations",
            "accommodationroom" => "accommodationrooms",
            "ltsactivity" => "activities",
            "ltspoi" => "pois",
            //"ltsgastronomy" => "gastronomies",
            "event" => "events",
            "odhactivitypoi" => "smgpois",
            "package" => "packages",
            "measuringpoint" => "measuringpoints",
            "webcam" => "webcams",
            "article" => "articles",
            "venue" => "venues_v2",
            "eventshort" => "eventeuracnoi",
            "experiencearea" => "experienceareas",
            "metaregion" => "metaregions",
            "region" => "regions",
            "tourismassociation" => "tvs",
            "municipality" => "municipalities",
            "district" => "districts",
            "skiarea" => "skiareas",
            "skiregion" => "skiregions",
            "area" => "areas",
            "wineaward" => "wines",
            "odhtag" => "smgtags",
            "publisher" => "publishers",
            "source" => "sources",
            "weatherhistory" => "weatherdatahistory",
            "odhmetadata" => "metadata",
            "tag" => "tags",
            "geoshape" => "geoshapes",
            "announcement" => "announcements",
            "urbangreen" => "urbangreens",
            "spatialdata" => "spatialdatas",
            "timeseries" => "timeseries",
            _ => throw new Exception("not known type"),
        };
    }

    public static (string?,string) TranslateTypeString2GeoColumns(string odhtype)
    {
        return odhtype switch
        {
            "timeseries" => (null,"pointprojection"),
            "geoshape" => ("geometry4326","gen_center_position"),
            "spatialdata" => ("geo","gen_center_position"),
            "announcement" => ("geo","gen_center_position"),
            "urbangreen" => ("geo","gen_center_position"),
            _ => (null,"gen_position"),
        };
    }

    public static bool CheckOperationModeOnType(string type, AllowedOperationMode operationMode, string? geometrycolumn, string geometrycentercolumn)
    {
        return true;
    }


    // Helper method to add CORS headers explicitly
    // private void AddCorsHeaders()
    // {
    //     Response.Headers.Append("Access-Control-Allow-Origin", "*");
    //     Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
    //     Response.Headers.Append("Access-Control-Allow-Headers", "*");
    //     Response.Headers.Append("Access-Control-Max-Age", "86400");
    // }
}