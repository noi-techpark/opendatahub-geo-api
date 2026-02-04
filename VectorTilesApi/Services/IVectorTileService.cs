// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OpenDataHubVectorTileApi.Services;

public interface IVectorTileService
{
    Task<byte[]> GetVectorTileAsync(string tableName, int z, int x, int y);
}