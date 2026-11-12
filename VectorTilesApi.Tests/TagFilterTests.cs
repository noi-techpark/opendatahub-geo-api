// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using OpenDataHubVectorTileApi.Services;
using Xunit;

namespace VectorTilesApi.Tests;

/// <summary>
/// Parsing of the tagfilter syntax (same as the Open Data Hub Content Api), no database needed.
/// </summary>
public class TagFilterTests
{
    [Theory]
    [InlineData("hiking", false, new[] { "hiking" })]
    [InlineData("hiking,cycling", false, new[] { "hiking", "cycling" })]
    [InlineData("or(hiking,cycling)", false, new[] { "hiking", "cycling" })]
    [InlineData("OR(hiking, cycling)", false, new[] { "hiking", "cycling" })]
    [InlineData("and(hiking,south tyrol)", true, new[] { "hiking", "south tyrol" })]
    [InlineData(" AND( hiking , south tyrol ) ", true, new[] { "hiking", "south tyrol" })]
    [InlineData("and(hiking,hiking,)", true, new[] { "hiking" })]
    [InlineData("and()", true, new string[0])]
    [InlineData("", false, new string[0])]
    [InlineData(null, false, new string[0])]
    public void ParseTagFilter_ReturnsConjunctionAndTags(string? input, bool matchall, string[] tags)
    {
        var result = VectorTileService.ParseTagFilter(input);

        Assert.Equal(matchall, result.matchall);
        Assert.Equal(tags, result.tags);
    }
}
