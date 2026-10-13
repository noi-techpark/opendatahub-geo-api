// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VectorTilesApi.Tests;

/// <summary>
/// Clustering tests on known records (filtered by idlist) decoding the returned vector tile.
///
/// Prerequisites: PG_CONNECTION environment variable (or a local .env file) must point
/// to a reachable PostgreSQL instance with the ODH geo schema.
/// </summary>
public class ClusteringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    // 6 charging stations with identical coordinates
    private const double IdenticalLon = 11.771856;
    private const double IdenticalLat = 46.53295;
    private static readonly string[] IdenticalIds =
    [
        "echarging_neogy:9534742f9c5c49548193d8a4",
        "echarging_neogy:2b6da89c52f742f38074c832",
        "echarging_neogy-ampeco:253822",
        "echarging_neogy-ampeco:254670",
        "echarging_neogy-ampeco:253187",
        "echarging_neogy-ampeco:253561",
    ];

    // 2 POIs about 4.4 m apart
    private const double NearLon = 11.2255;
    private const double NearLat = 46.64458;
    private static readonly string[] NearIds =
    [
        "smgpoi75232fdd0cdb6584310eeabe88c11c4b_reduced",
        "smgpoib37a49c5b8d284e2cfa4e024a34f58da_reduced",
    ];

    public ClusteringTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static (int X, int Y) LonLatToTile(double lon, double lat, int z)
    {
        var n = Math.Pow(2, z);
        var x = (int)((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (x, y);
    }

    private async Task<List<Dictionary<string, object?>>> GetFeatures(double lon, double lat, int z, string[] ids, string query = "")
    {
        var (x, y) = LonLatToTile(lon, lat, z);
        var idlist = Uri.EscapeDataString(string.Join(",", ids));
        var response = await _client.GetAsync($"/api/tiles/odhactivitypoi/{z}/{x}/{y}.pbf?idlist={idlist}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return MvtReader.ReadFeatures(await response.Content.ReadAsByteArrayAsync());
    }

    private static long Count(Dictionary<string, object?> feature) => Convert.ToInt64(feature["count"]);

    private static bool IsCluster(Dictionary<string, object?> feature) => feature["cluster"] is true;

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(17)]
    [InlineData(20)]
    [InlineData(22)]
    public async Task IdenticalPoints_StayOneClusterWithMembers_OnEveryZoom(int z)
    {
        var features = await GetFeatures(IdenticalLon, IdenticalLat, z, IdenticalIds);

        var feature = Assert.Single(features);
        Assert.True(IsCluster(feature));
        Assert.Equal(IdenticalIds.Length, Count(feature));

        var members = JsonDocument.Parse((string)feature["members"]!).RootElement;
        var memberIds = members.EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToList();
        Assert.Equal(IdenticalIds.Order(), memberIds.Order());
        Assert.All(members.EnumerateArray(), m => Assert.NotEqual(JsonValueKind.Null, m.GetProperty("data").ValueKind));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(16)]
    public async Task NearbyPoints_AreMerged_WhenTheyOverlapOnScreen(int z)
    {
        var features = await GetFeatures(NearLon, NearLat, z, NearIds);

        var feature = Assert.Single(features);
        Assert.True(IsCluster(feature));
        Assert.Equal(2, Count(feature));
        Assert.NotNull(feature["members"]);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(22)]
    public async Task NearbyPoints_AreSeparated_WhenZoomedInEnough(int z)
    {
        var features = await GetFeatures(NearLon, NearLat, z, NearIds);

        Assert.Equal(2, features.Count);
        Assert.All(features, f =>
        {
            Assert.False(IsCluster(f));
            Assert.Equal(1, Count(f));
            Assert.NotNull(f["data"]);
        });
    }

    [Theory]
    [InlineData(14)]
    [InlineData(22)]
    public async Task IdenticalPoints_WithoutClustering_AreReturnedOneByOne(int z)
    {
        var features = await GetFeatures(IdenticalLon, IdenticalLat, z, IdenticalIds, "&enableclustering=false");

        Assert.Equal(IdenticalIds.Length, features.Count);
        Assert.All(features, f =>
        {
            Assert.False(IsCluster(f));
            Assert.NotNull(f["data"]);
        });
    }

    [Fact]
    public async Task IdenticalPoints_PointsAndTracksMode_StayOneClusterWithMembers()
    {
        // pointsandtracks uses a separate query, only valid on types with a track column
        var (x, y) = LonLatToTile(11.35, 46.5, 10);
        var response = await _client.GetAsync($"/api/tiles/announcement/10/{x}/{y}.pbf?operationmode=pointsandtracks&displaytracksonzoomlevel=8");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var points = MvtReader.ReadFeatures(await response.Content.ReadAsByteArrayAsync())
            .Where(f => (string?)f["geom_type"] == "point")
            .ToList();

        Assert.NotEmpty(points);
        Assert.All(points, f => Assert.Equal(Count(f) > 1, IsCluster(f)));
        Assert.All(points.Where(IsCluster).Where(f => Count(f) <= 10), f => Assert.NotNull(f["members"]));
    }
}
