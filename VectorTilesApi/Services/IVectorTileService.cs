// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OpenDataHubVectorTileApi.Services;

public interface IVectorTileService
{
    //Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y);

    //Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y, string source, string geocolumn);

    Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y, string? source, string? jsonselector, string geocolumn, List<string>? idlist);
}