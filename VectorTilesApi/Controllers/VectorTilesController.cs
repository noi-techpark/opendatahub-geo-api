// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using OpenDataHubVectorTileApi.Services;

namespace OpenDataHubVectorTileApi.Controllers;

[ApiController]
[EnableCors("AllowAll")]
[Route("api/tiles")]
public partial class VectorTilesController : ControllerBase
{
    private readonly IVectorTileService _vectorTileService;
    private readonly ILogger<VectorTilesController> _logger;

    // Types no longer supported, requests are rejected with a deprecation message
    private static readonly HashSet<string> DeprecatedTypes = ["odhtag", "area"];

    // Types without geometry (no gen_position column), no vector tiles can be generated
    private static readonly HashSet<string> UnsupportedTypes =
    [
        "accommodationroom", "article", "eventshort", "experiencearea", "wineaward",
        "publisher", "source", "weatherhistory", "odhmetadata", "tag"
    ];

    [GeneratedRegex(@"^[a-z]+$")]
    private static partial Regex SafeTypeRegex();

    [GeneratedRegex(@"^(Shortname|Source|Active|Detail|ContactInfos|Mapping|StartTime|EndTime)(\.[A-Za-z0-9_]+|\['[A-Za-z0-9_.]+'\])*$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeJsonSelectorRegex();

    public VectorTilesController(IVectorTileService vectorTileService, ILogger<VectorTilesController> logger)
    {
        _vectorTileService = vectorTileService;
        _logger = logger;
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
    /// <param name="tagfilter">Filter by Tags Separator "," see Open data hub Content Api tagfilter logic</param>
    /// <param name="jsonselector">Include More data (by standard Id and where possible a name is included in the Vector Tiles)</param>
    /// <param name="operationmode">points --> Display only points, tracks --> Display tracks only, --> pointsandtracks --> Display points and tracks (Default: points)</param>
    /// <param name="displaytracksonzoomlevel">zoom level when tracks are shown (default 12). Please use with caution service can be slow if zoom levels < 11 are used</param>
    /// <param name="enableclustering">Enable Clustering on Points, defaul: true</param>
    /// <param name="begindate">Only Events taking place on or after this date (yyyy-MM-dd), only supported on type event</param>
    /// <param name="enddate">Only Events taking place on or before this date (yyyy-MM-dd), only supported on type event</param>
    /// <param name="active">Filter by Active state: true, false or null (active and inactive) (default: true), ignored on types without Active field</param>
    /// <param name="publishedon">Filter by PublishedOn channels Separator "," (e.g. idm-marketplace), any of the channels matches</param>
    /// <returns>Vector tile in protobuf format</returns>
    [HttpGet("{type}/{z}/{x}/{y}.pbf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/x-protobuf")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
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
        bool enableclustering = true,
        DateTime? begindate = null,
        DateTime? enddate = null,
        string? active = "true",
        string? publishedon = null
        )
    {
        try
        {
            //Validate passed parameters
            var (isValid, errorMessage) = ValidateParamters(type, z, x, y, jsonselector, operationmode, begindate, enddate, publishedon);
            if (!isValid)
                return BadRequest(errorMessage);

            if (!TryParseActive(active, out var activefilter))
                return BadRequest("active must be true, false or null");

            var tile = await GetVectorTilesFromService(type, z, x, y, !String.IsNullOrEmpty(idlist) ? idlist.Split(",").ToList() : null, source, tagfilter, jsonselector, operationmode, displaytracksonzoomlevel, enableclustering, begindate, enddate, activefilter, publishedon);

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
    /// <param name="type">Name of the Open Data Hub data type</param>
    /// <param name="z">Zoom level</param>
    /// <param name="x">Tile X coordinate</param>
    /// <param name="y">Tile Y coordinate</param>
    /// <param name="idlist">Filter by a List of Ids</param>
    /// <param name="source">Source Filter</param>
    /// <param name="tagfilter">Filter by Tags Separator "," see Open data hub Content Api tagfilter logic</param>
    /// <param name="jsonselector">Include More data (by standard Id and where possible a name is included in the Vector Tiles)</param>
    /// <param name="operationmode">points --> Display only points, tracks --> Display tracks only, --> pointsandtracks --> Display points and tracks (Default: points)</param>
    /// <param name="displaytracksonzoomlevel">zoom level when tracks are shown (default 12). Please use with caution service can be slow if zoom levels < 11 are used</param>
    /// <param name="enableclustering">Enable Clustering on Points, defaul: true</param>
    /// <param name="begindate">Only Events taking place on or after this date (yyyy-MM-dd), only supported on type event</param>
    /// <param name="enddate">Only Events taking place on or before this date (yyyy-MM-dd), only supported on type event</param>
    /// <param name="active">Filter by Active state: true, false or null (active and inactive) (default: true), ignored on types without Active field</param>
    /// <param name="publishedon">Filter by PublishedOn channels Separator "," (e.g. idm-marketplace), any of the channels matches</param>
    /// <returns>Vector tile in protobuf format</returns>
    [HttpPost("{type}/{z}/{x}/{y}.pbf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/x-protobuf")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
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
        bool enableclustering = true,
        DateTime? begindate = null,
        DateTime? enddate = null,
        string? active = "true",
        string? publishedon = null
        )
    {
        try
        {
            //Validate passed parameters
            var (isValid, errorMessage) = ValidateParamters(type, z, x, y, jsonselector, operationmode, begindate, enddate, publishedon);
            if (!isValid)
                return BadRequest(errorMessage);

            if (!TryParseActive(active, out var activefilter))
                return BadRequest("active must be true, false or null");

            var tile = await GetVectorTilesFromService(type, z, x, y, idlist, source, tagfilter, jsonselector, operationmode, displaytracksonzoomlevel, enableclustering, begindate, enddate, activefilter, publishedon);

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
        bool clusterpoints = true,
        DateTime? begindate = null,
        DateTime? enddate = null,
        bool? active = true,
        string? publishedon = null
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
            displaytracksonzoomlevel,
            begindate,
            enddate,
            active,
            publishedon);
    }

    /// <summary>
    /// Parses the active parameter, "null" or empty means no filter on Active
    /// </summary>
    private static bool TryParseActive(string? active, out bool? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(active) || active.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
            return true;

        if (bool.TryParse(active.Trim(), out var parsed))
        {
            result = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    private static (bool IsValid, string? ErrorMessage) ValidateParamters(
        string type,
        int z,
        int x,
        int y,
        string? jsonselector,
        AllowedOperationMode operationmode,
        DateTime? begindate,
        DateTime? enddate,
        string? publishedon
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

        // Explicit allowlist check — do not rely solely on the switch exception as a security guard
        if (!SafeTypeRegex().IsMatch(type))
            return (false, "Invalid type");

        if (DeprecatedTypes.Contains(type))
            return (false, $"type {type} is deprecated");

        if (UnsupportedTypes.Contains(type))
            return (false, $"type {type} is not supported");

        string tableName;
        try
        {
            tableName = TranslateTypeString2Table(type);
        }
        catch (Exception)
        {
            return (false, "Invalid type");
        }

        // if (!String.IsNullOrEmpty(geocolumn) && !_allowedGeoColumns.Contains(geocolumn))
        //     return (false, "Invalid geo column");

        var (geometrycolumn, geometrycentercolumn) = TranslateTypeString2GeoColumns(type);
        if (!CheckOperationModeOnType(type, operationmode, geometrycolumn, geometrycentercolumn))
            return (false, $"operationmode {operationmode} is not supported on type {type}, use points");

        // Full-value regex check — prevents prefix bypass (e.g. "Detail.x'--" starting with "Detail")
        if (!string.IsNullOrEmpty(jsonselector) &&
            !jsonselector.Split(',')
                 .Select(x => x.Trim())
                 .All(x => SafeJsonSelectorRegex().IsMatch(x)))
            return (false, "Invalid json selector");

        // Date filter relies on the gen_eventdates column, only available on events
        if ((begindate != null || enddate != null) && type != "event")
            return (false, "begindate/enddate are only supported on type event");

        if (begindate != null && enddate != null && begindate > enddate)
            return (false, "begindate must be before enddate");

        // PublishedOn filter relies on the gen_publishedon column, not available on all types
        if (!string.IsNullOrWhiteSpace(publishedon) && !VectorTileService.TablesWithPublishedOn.Contains(tableName))
            return (false, $"publishedon is not supported on type {type}");

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
            "event" => "events",
            "odhactivitypoi" => "smgpois",
            "measuringpoint" => "measuringpoints",
            "webcam" => "webcams",
            "article" => "articles",
            "venue" => "venues",
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
            "publisher" => "publishers",
            "source" => "sources",
            "weatherhistory" => "weatherdatahistory",
            "odhmetadata" => "metadata",
            "tag" => "tags",
            "geoshape" => "geoshapes",
            "announcement" => "announcements",
            "urbangreen" => "urbangreens",
            "spatialdata" => "spatialdatas",
            //"timeseries" => "timeseries",
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
            "skiarea" => ("geo","gen_position"),
            "skiregion" => ("geo","gen_position"),
            _ => (null,"gen_position"),
        };
    }

    public static bool CheckOperationModeOnType(string type, AllowedOperationMode operationMode, string? geometrycolumn, string geometrycentercolumn)
    {
        // Tracks need a geometry column, types without one only support points
        return operationMode == AllowedOperationMode.points || geometrycolumn != null;
    }
}