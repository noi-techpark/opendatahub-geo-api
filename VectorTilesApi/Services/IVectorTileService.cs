// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OpenDataHubVectorTileApi.Services;

public interface IVectorTileService
{
    //Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y);

    //Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y, string source, string geocolumn);

    Task<byte[]> 
    GetVectorTileAsync(
        string tableName, 
        string type, 
        int z, 
        int x, 
        int y, 
        string? source, 
        string? tagfilter, 
        string? jsonselector, 
        string? geometry_column, 
        string geometry_center_column, 
        List<string>? idlist, 
        bool cluster = false,
        AllowedOperationMode operationMode = AllowedOperationMode.points,
        int displayTracksonZoomLevel = 12
        );
}