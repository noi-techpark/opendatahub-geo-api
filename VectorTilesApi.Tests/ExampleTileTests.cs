// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VectorTilesApi.Tests;

/// <summary>
/// Integration tests derived from every non-legacy example HTML page in wwwroot/examples.
/// Each test fires the same tile request the browser would make (z=10, Bolzano area)
/// and asserts the API returns either:
///   200 application/x-protobuf  — tile has data
///   204 No Content              — tile is valid but empty for this location
/// Any 4xx or 5xx fails the test.
///
/// Prerequisites: PG_CONNECTION environment variable (or a local .env file) must point
/// to a reachable PostgreSQL instance with the ODH geo schema.
/// </summary>
public class ExampleTileTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    // Tile covering the Bolzano / South Tyrol area at zoom 10
    private const int Z = 10;
    private const int X = 544;
    private const int Y = 362;

    public ExampleTileTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // -----------------------------------------------------------------
    // Test cases — one row per example HTML (digiway_old excluded)
    // Format: [test name, path after /api/tiles/]
    // -----------------------------------------------------------------
    public static IEnumerable<object[]> TileRequests =>
    [
        // ── root examples ─────────────────────────────────────────────
        ["geoshapes_istat",                             $"geoshape/{Z}/{X}/{Y}.pbf?source=istat"],
        ["geoshapes_istat_tracks",                      $"geoshape/{Z}/{X}/{Y}.pbf?source=istat&operationmode=tracks"],
        ["skiareas_idm_pointsandtracks",                $"geoshape/{Z}/{X}/{Y}.pbf?source=idm&operationmode=pointsandtracks"],
        ["skiregions_idm_pointsandtracks",              $"geoshape/{Z}/{X}/{Y}.pbf?source=idm&operationmode=pointsandtracks"],
        ["skiareas_all_points",                         $"skiarea/{Z}/{X}/{Y}.pbf?operationmode=points"],
        ["pois_lts",                                    $"odhactivitypoi/{Z}/{X}/{Y}.pbf?source=lts"],
        ["accommodations_unclustered",                  $"accommodation/{Z}/{X}/{Y}.pbf?enableclustering=false"],
        ["accommodations_clustered",                    $"accommodation/{Z}/{X}/{Y}.pbf?enableclustering=true"],
        ["accommodations_byIds",                        $"accommodation/{Z}/{X}/{Y}.pbf?idlist=5CEA544EE34639034F07B79D4AEEB603_REDUCED,2657B7CBCB85380B253D2FBE28AF100E_REDUCED,8245A995887911D3AF4D006008C7C6AD_REDUCED,A5699F23EA3711D1BFF40000B4903BD8_REDUCED"],
        ["wineries_unclustered",                        $"odhactivitypoi/{Z}/{X}/{Y}.pbf?source=suedtirolwein&enableclustering=false"],
        ["wineries_clustered_jsonselector",             $"odhactivitypoi/{Z}/{X}/{Y}.pbf?source=suedtirolwein&enableclustering=true&jsonselector=Detail.de.Title,Detail.de.BaseText"],
        ["urbangreens_points",                          $"urbangreen/{Z}/{X}/{Y}.pbf?operationmode=points"],
        ["urbangreens_tracks",                          $"urbangreen/{Z}/{X}/{Y}.pbf?operationmode=tracks&displaytracksonzoomlevel=9"],
        ["urbangreens_pointsandtracks",                 $"urbangreen/{Z}/{X}/{Y}.pbf?operationmode=pointsandtracks&displaytracksonzoomlevel=10"],
        ["announcements_points",                        $"announcement/{Z}/{X}/{Y}.pbf?operationmode=points"],
        ["announcements_pointsandtracks",               $"announcement/{Z}/{X}/{Y}.pbf?operationmode=pointsandtracks&displaytracksonzoomlevel=8"],

        // ── events ────────────────────────────────────────────────────
        ["events_lts_all",                              $"event/{Z}/{X}/{Y}.pbf?source=lts&enableclustering=true"],
        ["events_lts_begindate",                        $"event/{Z}/{X}/{Y}.pbf?source=lts&enableclustering=true&begindate={DateTime.Today:yyyy-MM-dd}"],
        ["events_lts_begindate_enddate",                $"event/{Z}/{X}/{Y}.pbf?source=lts&begindate={DateTime.Today:yyyy-MM-dd}&enddate={DateTime.Today.AddDays(30):yyyy-MM-dd}"],
        ["events_momentus_noi_begindate",               $"event/{Z}/{X}/{Y}.pbf?source=momentus,noi&enableclustering=true&begindate={DateTime.Today:yyyy-MM-dd}"],

        // ── digiway/spatialdata ────────────────────────────────────────
        ["digiway_spatialdata_civis_geoserver",         $"spatialdata/{Z}/{X}/{Y}.pbf?source=civis.geoserver"],
        ["digiway_spatialdata_civis_cycleways",         $"spatialdata/{Z}/{X}/{Y}.pbf?source=civis.geoserver&tagfilter=cyclewaystyrol&operationmode=pointsandtracks"],
        ["digiway_spatialdata_civis_hikingtrails",      $"spatialdata/{Z}/{X}/{Y}.pbf?source=civis.geoserver&tagfilter=hikingtrails&operationmode=pointsandtracks&displaytracksonzoomlevel=10"],
        ["digiway_spatialdata_civis_mountainbike",      $"spatialdata/{Z}/{X}/{Y}.pbf?source=civis.geoserver&tagfilter=mountainbikeroutes&operationmode=pointsandtracks&displaytracksonzoomlevel=10"],
        ["digiway_spatialdata_civis_intermunicipal",    $"spatialdata/{Z}/{X}/{Y}.pbf?source=civis.geoserver&tagfilter=intermunicipalcyclingroutes&operationmode=pointsandtracks&displaytracksonzoomlevel=10"],
        ["digiway_spatialdata_civis_hiking_and_mtb",    $"spatialdata/{Z}/{X}/{Y}.pbf?source=civis.geoserver&tagfilter=hikingtrails,mountainbikeroutes&operationmode=pointsandtracks&displaytracksonzoomlevel=10"],
        ["digiway_spatialdata_euregio_roadnetwork",     $"spatialdata/{Z}/{X}/{Y}.pbf?source=euregio.roadnetwork"],
        ["digiway_spatialdata_euregio_routes",          $"spatialdata/{Z}/{X}/{Y}.pbf?source=euregio.routes"],
        ["digiway_spatialdata_dservices",               $"spatialdata/{Z}/{X}/{Y}.pbf?source=dservices3.arcgis.com"],
        ["digiway_spatialdata_dservices_accessible",    $"spatialdata/{Z}/{X}/{Y}.pbf?source=dservices3.arcgis.com&tagfilter=accessibletrails_austria"],
        ["digiway_spatialdata_dservices_radrouten",     $"spatialdata/{Z}/{X}/{Y}.pbf?source=dservices3.arcgis.com&tagfilter=radrouten_tirol"],
        ["digiway_spatialdata_siat_cicloviari",         $"spatialdata/{Z}/{X}/{Y}.pbf?source=siat.provincia.tn.it&tagfilter=elementi_cicloviari_v&operationmode=pointsandtracks"],
        ["digiway_spatialdata_siat_mtb",                $"spatialdata/{Z}/{X}/{Y}.pbf?source=siat.provincia.tn.it&tagfilter=mtb_percorsi_v&operationmode=pointsandtracks"],
        ["digiway_spatialdata_siat_sentieri",           $"spatialdata/{Z}/{X}/{Y}.pbf?source=siat.provincia.tn.it&tagfilter=sentieri_della_sat&operationmode=pointsandtracks"],

        // ── digiway/announcements ──────────────────────────────────────
        ["digiway_announcements_zoho",                  $"announcement/{Z}/{X}/{Y}.pbf?source=digiway.zoho"],
        ["digiway_announcements_mapservices",           $"announcement/{Z}/{X}/{Y}.pbf?source=tirol.mapservices.eu&operationmode=pointsandtracks&displaytracksonzoomlevel=10"],
        ["digiway_announcements_mapservices_jsonselector", $"announcement/{Z}/{X}/{Y}.pbf?source=tirol.mapservices.eu&operationmode=pointsandtracks&displaytracksonzoomlevel=10&jsonselector=StartTime,EndTime,Mapping%5B'tirol.mapservices.eu'%5D.description"],

        // ── active / publishedon filters ───────────────────────────────
        ["events_active_default",                       $"event/{Z}/{X}/{Y}.pbf"],
        ["events_active_true",                          $"event/{Z}/{X}/{Y}.pbf?active=true"],
        ["events_active_false",                         $"event/{Z}/{X}/{Y}.pbf?active=false"],
        ["events_active_null",                          $"event/{Z}/{X}/{Y}.pbf?active=null"],
        ["events_publishedon_single",                   $"event/{Z}/{X}/{Y}.pbf?publishedon=idm-marketplace"],
        ["events_publishedon_multiple",                 $"event/{Z}/{X}/{Y}.pbf?publishedon=idm-marketplace,today.noi.bz.it"],
        ["events_active_null_publishedon_tagfilter",    $"event/{Z}/{X}/{Y}.pbf?active=null&publishedon=idm-marketplace&tagfilter=music"],
        ["accommodations_publishedon",                  $"accommodation/{Z}/{X}/{Y}.pbf?publishedon=idm-marketplace"],
        ["urbangreens_active_false",                    $"urbangreen/{Z}/{X}/{Y}.pbf?active=false"],
        ["venues_points",                               $"venue/{Z}/{X}/{Y}.pbf"],
        ["venues_active_null_publishedon",              $"venue/{Z}/{X}/{Y}.pbf?active=null&publishedon=idm-marketplace"],
        ["geoshapes_active_ignored",                    $"geoshape/{Z}/{X}/{Y}.pbf?active=false"],

        // ── geoshape tagfilter (filters on type column) ────────────────
        ["geoshapes_tagfilter_single",                  $"geoshape/{Z}/{X}/{Y}.pbf?tagfilter=municipality"],
        ["geoshapes_tagfilter_multiple",                $"geoshape/{Z}/{X}/{Y}.pbf?tagfilter=municipality,province"],
        ["geoshapes_tagfilter_and_sources_multiple",    $"geoshape/{Z}/{X}/{Y}.pbf?source=istat,idm&tagfilter=municipality,province"],
    ];

    [Theory]
    [MemberData(nameof(TileRequests))]
    public async Task TileRequest_ReturnsValidResponse(string testName, string path)
    {
        var response = await _client.GetAsync($"/api/tiles/{path}");

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"[{testName}] Expected 200 or 204 but got {(int)response.StatusCode} for /api/tiles/{path}"
        );

        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(bytes);
        }
    }

    [Theory]
    [InlineData("accommodation/10/544/362.pbf?begindate=2026-01-01")]
    [InlineData("event/10/544/362.pbf?begindate=2026-02-01&enddate=2026-01-01")]
    [InlineData("event/10/544/362.pbf?begindate=notadate")]
    [InlineData("event/10/544/362.pbf?active=foo")]
    [InlineData("urbangreen/10/544/362.pbf?publishedon=idm-marketplace")]
    [InlineData("event/10/544/362.pbf?operationmode=pointsandtracks")]
    [InlineData("accommodation/10/544/362.pbf?operationmode=tracks")]
    [InlineData("event/10/544/362.pbf?jsonselector=Detail.de.Title'--")]
    [InlineData("event/10/2000/362.pbf")]
    [InlineData("nonsense/10/544/362.pbf")]
    [InlineData("odhtag/10/544/362.pbf")]
    [InlineData("area/10/544/362.pbf")]
    [InlineData("ltsactivity/10/544/362.pbf")]
    [InlineData("ltspoi/10/544/362.pbf")]
    [InlineData("package/10/544/362.pbf")]
    [InlineData("timeseries/10/544/362.pbf")]
    public async Task InvalidRequest_ReturnsBadRequest(string path)
    {
        var response = await _client.GetAsync($"/api/tiles/{path}");
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 but got {(int)response.StatusCode} for /api/tiles/{path}"
        );
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData("odhtag")]
    [InlineData("area")]
    public async Task DeprecatedType_ReturnsDeprecationMessage(string type)
    {
        var response = await _client.GetAsync($"/api/tiles/{type}/10/544/362.pbf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("deprecated", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("geoshape/10/544/362.pbf?tagfilter=municipality")]
    [InlineData("geoshape/10/544/362.pbf?source=istat,idm&tagfilter=municipality,province")]
    public async Task GeoshapeTagFilter_ReturnsData(string path)
    {
        var response = await _client.GetAsync($"/api/tiles/{path}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("accommodationroom")]
    [InlineData("article")]
    [InlineData("eventshort")]
    [InlineData("experiencearea")]
    [InlineData("wineaward")]
    [InlineData("publisher")]
    [InlineData("source")]
    [InlineData("weatherhistory")]
    [InlineData("odhmetadata")]
    [InlineData("tag")]
    public async Task UnsupportedType_ReturnsNotSupportedMessage(string type)
    {
        var response = await _client.GetAsync($"/api/tiles/{type}/10/544/362.pbf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not supported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostDeprecatedType_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/tiles/area/10/544/362.pbf", new List<string> { "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/tiles/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
