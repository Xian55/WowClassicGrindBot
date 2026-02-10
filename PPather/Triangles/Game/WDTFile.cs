/*
  This file is part of ppather.

    PPather is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    PPather is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with ppather.  If not, see <http://www.gnu.org/licenses/>.

    Copyright Pontus Borg 2008

 */

using Microsoft.Extensions.Logging;

using PPather.Extensions;

using SharedLib.Data;

using StormDll;

using System;
using System.Buffers;
using System.IO;

namespace Wmo;

internal sealed class WDTFile
{
    private readonly ILogger logger;
    private readonly WMOManager wmomanager;
    private readonly ModelManager modelmanager;
    private readonly WDT wdt;
    private readonly ArchiveSet archive;
    private readonly float mapId;

    private readonly string pathName;

    public bool loaded;

    public WDTFile(ArchiveSet archive, float mapId, WDT wdt, WMOManager wmomanager, ModelManager modelmanager, ILogger logger)
    {
        this.logger = logger;
        this.pathName = ContinentDB.IdToName[mapId];
        this.mapId = mapId;

        this.wdt = wdt;
        this.wmomanager = wmomanager;
        this.modelmanager = modelmanager;
        this.archive = archive;

        // MPQ listfile entries use backslashes; avoid Path.Join ("/") on Linux
        string wdtfile = $"World\\Maps\\{pathName}\\{pathName}.wdt";

        logger.LogInformation("Loading WDT for map {MapId} from {WdtFile}", mapId, wdtfile);

        MpqFileStream mpq;
        try
        {
            mpq = archive.GetStream(wdtfile);
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is IOException)
        {
            logger.LogError(ex, "Failed to open WDT {WdtFile} for map {MapId}; archives searched: {Archives}",
                wdtfile, mapId, string.Join(", ", archive.ArchiveNames));
            throw;
        }

        using (mpq)
        {
            var pooler = ArrayPool<byte>.Shared;
            byte[] buffer = pooler.Rent((int)mpq.Length);
            mpq.ReadAllBytesTo(buffer);

            using MemoryStream stream = new(buffer, 0, (int)mpq.Length, false);
            using BinaryReader file = new(stream);

            string[] gwmos = [];

            do
            {
                uint type = file.ReadUInt32();
                uint size = file.ReadUInt32();
                long curpos = file.BaseStream.Position;

                switch (type)
                {
                    case ChunkReader.MVER:
                        break;
                    case ChunkReader.MPHD:
                        break;
                    case ChunkReader.MODF:
                        HandleMODF(file, wdt, gwmos, wmomanager, size);
                        break;
                    case ChunkReader.MWMO when size != 0:
                        gwmos = ChunkReader.ExtractFileNames(file, size);
                        break;
                    case ChunkReader.MAIN:
                        HandleMAIN(file, size);
                        break;
                    default:
                        //logger.LogWarning($"WDT Unknown {type} - {file.BaseStream.Length} - {curpos} - {size}");
                        break;
                }
                file.BaseStream.Seek(Math.Min(curpos + size, file.BaseStream.Length), SeekOrigin.Begin);
            } while (!file.EOF());

            if (gwmos.Length != 0)
                ArrayPool<string>.Shared.Return(gwmos);

            pooler.Return(buffer);
        }

        loaded = true;
    }

    public void LoadMapTile(int x, int y, int index)
    {
        if (!wdt.maps[index])
            return;

        ReadOnlySpan<char> path = pathName.AsSpan();
        ReadOnlySpan<char> filename = $"World\\Maps\\{path}\\{path}_{x}_{y}.adt";

        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("Reading adt: {Filename}", filename.ToString());

        try
        {
            wdt.maptiles[index] = MapTileFile.Read(archive, filename, wmomanager, modelmanager);
            wdt.loaded[index] = true;
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is IOException)
        {
            logger.LogError(ex, "Failed to load ADT {Filename} for map {MapId}; archives searched: {Archives}",
                filename.ToString(), mapId, string.Join(", ", archive.ArchiveNames));
            throw;
        }
    }

    private static void HandleMODF(BinaryReader file, WDT wdt, Span<string> gwmos, WMOManager wmomanager, uint size)
    {
        // global wmo instance data
        int gnWMO = (int)size / 64;
        wdt.gwmois = new WMOInstance[gnWMO];

        for (uint i = 0; i < gnWMO; i++)
        {
            int id = file.ReadInt32();
            string path = gwmos[id];

            WMORoot wmo = wmomanager.AddAndLoadIfNeeded(path);
            wdt.gwmois[i] = new(file, wmo);
        }
    }

    private void HandleMAIN(BinaryReader file, uint size)
    {
        // global map objects
        for (int index = 0; index < WDT.SIZE * WDT.SIZE; index++)
        {
            wdt.maps[index] = file.ReadInt32() != 0;
            //file.ReadInt32(); // kasta
            file.BaseStream.Seek(sizeof(Int32), SeekOrigin.Current);
        }
    }
}
