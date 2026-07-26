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

using PPather.Extensions;

using StormDll;

using System;
using System.Buffers;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Wmo;

internal static partial class MapTileFile // adt file
{
    private static readonly MH2OData1 eMH2OData1;
    public static ref readonly MH2OData1 EmptyMH2OData1 => ref eMH2OData1;

    private static readonly LiquidData eLiquidData = new(0, 0, 0, EmptyMH2OData1, [], []);
    public static ref readonly LiquidData EmptyLiquidData => ref eLiquidData;

    /// <summary>
    /// MCNK header flag 0x10000: the chunk carries a 64-bit 8x8 hole mask in the
    /// header words that otherwise hold ofsHeight/ofsNormal. Mists-era only;
    /// no pre-Cataclysm client sets it.
    /// </summary>
    private const uint MCNK_FLAG_HIGH_RES_HOLES = 0x10000;

    /// <summary>ADT file extension, including the dot.</summary>
    private const string AdtExtension = ".adt";

    /// <summary>
    /// Cataclysm-era companion file carrying the model/WMO placement chunks that
    /// a pre-Cataclysm root ADT holds inline. Its sibling <c>_tex0</c>/<c>_tex1</c>
    /// files are texture data only and are never read by the bake.
    /// </summary>
    private const string ObjectFileSuffix = "_obj0.adt";

    public static MapTile Read(ArchiveSet archive, ReadOnlySpan<char> name, WMOManager wmomanager, ModelManager modelmanager)
    {
        LiquidData[] LiquidDataChunk = [];

        Span<SMChunkInfo> mcin = stackalloc SMChunkInfo[MapTile.SIZE * MapTile.SIZE];
        Span<uint> mcnkOffsets = stackalloc uint[MapTile.SIZE * MapTile.SIZE];
        int mcnkCount = 0;
        bool haveMcin = false;

        string[] models = [];
        string[] wmos = [];

        WMOInstance[] wmois = [];
        ModelInstance[] modelis = [];

        MapChunk[] chunks = new MapChunk[MapTile.SIZE * MapTile.SIZE];
        BitArray hasChunk = new(chunks.Length);

        using MpqFileStream mpq = archive.GetStream(name);
        int length = (int)mpq.Length;

        var pooler = ArrayPool<byte>.Shared;
        byte[] buffer = pooler.Rent(length);
        mpq.ReadAllBytesTo(buffer);

        using MemoryStream stream = new(buffer, 0, length, false);
        using BinaryReader file = new(stream);

        do
        {
            long chunkStart = file.BaseStream.Position;

            uint type = file.ReadUInt32();
            uint size = file.ReadUInt32();
            long nextPos = file.BaseStream.Position + size;

            switch (type)
            {
                case ChunkReader.MCIN:
                    HandleMCIN(file, mcin);
                    haveMcin = true;
                    break;
                case ChunkReader.MCNK when mcnkCount < mcnkOffsets.Length:
                    // Only used when MCIN is absent, but recording the offsets
                    // costs one store per chunk and keeps this a single pass.
                    mcnkOffsets[mcnkCount++] = (uint)chunkStart;
                    break;
                case ChunkReader.MMDX when size != 0:
                    models = ChunkReader.ExtractFileNames(file, size);
                    break;
                case ChunkReader.MWMO when size != 0:
                    wmos = ChunkReader.ExtractFileNames(file, size);
                    break;
                case ChunkReader.MDDF:
                    HandleMDDF(file, modelmanager, models, size, out modelis);
                    break;
                case ChunkReader.MODF:
                    HandleMODF(file, wmos, wmomanager, size, out wmois);
                    break;
                case ChunkReader.MH2O:
                    HandleMH2O(file, out LiquidDataChunk);
                    break;
            }

            file.BaseStream.Seek(nextPos, SeekOrigin.Begin);
        } while (!file.EOF());

        if (wmos.Length != 0)
            ArrayPool<string>.Shared.Return(wmos);

        if (models.Length != 0)
            ArrayPool<string>.Shared.Return(models);

        // Cataclysm split the ADT: the root lost MCIN and handed model/WMO
        // placement to a `_obj0` companion. Keyed off the absence of MCIN in the
        // file itself rather than the configured client version, so vanilla/TBC/
        // WotLK keep the exact pre-existing path and one reader serves both
        // layouts.
        if (!haveMcin)
            ReadObjectFile(archive, name, wmomanager, modelmanager, ref modelis, ref wmois);

        // NOTE: `file`/`stream` read directly out of `buffer`, so the buffer must
        // stay rented until the MCNK pass below is done. Returning it earlier
        // happened to work only because nothing else rented in between - it
        // corrupts geometry as soon as ADTs are parsed concurrently.
        for (int index = 0; index < MapTile.SIZE * MapTile.SIZE; index++)
        {
            int off;
            if (haveMcin)
            {
                off = (int)mcin[index].offset;
            }
            else
            {
                // A split ADT lists its MCNKs in grid order with no offset table,
                // so encounter order is the index. A short tile leaves the tail
                // unflagged rather than reading from offset 0.
                if (index >= mcnkCount)
                    continue;

                off = (int)mcnkOffsets[index];
            }

            file.BaseStream.Seek(off, SeekOrigin.Begin);

            chunks[index] = ReadMapChunk(file, LiquidDataChunk.Length > 0 ? LiquidDataChunk[index] : EmptyLiquidData);
            hasChunk[index] = true;
        }

        pooler.Return(buffer);

        return new(modelis, wmois, chunks, hasChunk);
    }

    /// <summary>
    /// Loads the model/WMO placement chunks for a Cataclysm-era split ADT from its
    /// <c>_obj0</c> companion. Leaves the outputs untouched when the companion is
    /// absent, so a root ADT that simply carries no placements stays empty.
    /// The companion's own MCNK chunks hold per-chunk doodad references, which the
    /// bake does not use - <c>MPQTriangleSupplier</c> culls against instance bounds
    /// instead - so they are skipped.
    /// </summary>
    private static void ReadObjectFile(ArchiveSet archive, ReadOnlySpan<char> name,
        WMOManager wmomanager, ModelManager modelmanager,
        ref ModelInstance[] modelis, ref WMOInstance[] wmois)
    {
        if (!name.EndsWith(AdtExtension, StringComparison.OrdinalIgnoreCase))
            return;

        ReadOnlySpan<char> stem = name[..^AdtExtension.Length];

        Span<char> objName = stackalloc char[stem.Length + ObjectFileSuffix.Length];
        stem.CopyTo(objName);
        ObjectFileSuffix.CopyTo(objName[stem.Length..]);

        if (!archive.Exists(objName))
            return;

        string[] models = [];
        string[] wmos = [];

        using MpqFileStream mpq = archive.GetStream(objName);
        int length = (int)mpq.Length;

        var pooler = ArrayPool<byte>.Shared;
        byte[] buffer = pooler.Rent(length);
        mpq.ReadAllBytesTo(buffer);

        using MemoryStream stream = new(buffer, 0, length, false);
        using BinaryReader file = new(stream);

        do
        {
            uint type = file.ReadUInt32();
            uint size = file.ReadUInt32();
            long nextPos = file.BaseStream.Position + size;

            switch (type)
            {
                case ChunkReader.MMDX when size != 0:
                    models = ChunkReader.ExtractFileNames(file, size);
                    break;
                case ChunkReader.MWMO when size != 0:
                    wmos = ChunkReader.ExtractFileNames(file, size);
                    break;
                case ChunkReader.MDDF:
                    HandleMDDF(file, modelmanager, models, size, out modelis);
                    break;
                case ChunkReader.MODF:
                    HandleMODF(file, wmos, wmomanager, size, out wmois);
                    break;
            }

            file.BaseStream.Seek(nextPos, SeekOrigin.Begin);
        } while (!file.EOF());

        if (wmos.Length != 0)
            ArrayPool<string>.Shared.Return(wmos);

        if (models.Length != 0)
            ArrayPool<string>.Shared.Return(models);

        pooler.Return(buffer);
    }

    /// <summary>
    /// Lean pass that pulls only the per-MCNK areaID for all 256 chunks and
    /// skips geometry, models and WMOs entirely. Handles both the pre-Cataclysm
    /// layout (chunk starts from MCIN) and the Cataclysm-era split ADT (no MCIN;
    /// MCNKs walked in grid order) - the MCNK header itself is unchanged between
    /// the two. Used to bake the standalone
    /// <see cref="PPather.Navmesh.AreaGrid"/> cheaply and without touching (or
    /// crashing on) tile collision geometry.
    /// </summary>
    public static void ReadAreaIds(ArchiveSet archive, ReadOnlySpan<char> name, Span<uint> areaIds)
    {
        Span<SMChunkInfo> mcin = stackalloc SMChunkInfo[MapTile.SIZE * MapTile.SIZE];
        Span<uint> mcnkOffsets = stackalloc uint[MapTile.SIZE * MapTile.SIZE];
        int mcnkCount = 0;
        bool haveMcin = false;

        using MpqFileStream mpq = archive.GetStream(name);
        int length = (int)mpq.Length;

        var pooler = ArrayPool<byte>.Shared;
        byte[] buffer = pooler.Rent(length);
        mpq.ReadAllBytesTo(buffer);

        using MemoryStream stream = new(buffer, 0, length, false);
        using BinaryReader file = new(stream);

        do
        {
            long chunkStart = file.BaseStream.Position;

            uint type = file.ReadUInt32();
            uint size = file.ReadUInt32();
            long nextPos = file.BaseStream.Position + size;

            if (type == ChunkReader.MCIN)
            {
                HandleMCIN(file, mcin);
                haveMcin = true;
                break;
            }

            // Cataclysm-era split ADT: no offset table, MCNKs run in grid order.
            // Keep walking to collect them - unlike the MCIN case there is no
            // early chunk to stop on.
            if (type == ChunkReader.MCNK && mcnkCount < mcnkOffsets.Length)
                mcnkOffsets[mcnkCount++] = (uint)chunkStart;

            file.BaseStream.Seek(nextPos, SeekOrigin.Begin);
        } while (!file.EOF());

        // areaID sits at offset 0x34 of the MCNK header, i.e. the chunk's
        // (tag + size) plus 13 header uints == 60 bytes past the chunk start.
        if (haveMcin)
        {
            for (int i = 0; i < mcin.Length && i < areaIds.Length; i++)
            {
                if (mcin[i].offset == 0)
                {
                    areaIds[i] = 0;
                    continue;
                }

                file.BaseStream.Seek(mcin[i].offset + (sizeof(uint) * 15), SeekOrigin.Begin);
                areaIds[i] = file.ReadUInt32();
            }
        }
        else
        {
            for (int i = 0; i < mcnkCount && i < areaIds.Length; i++)
            {
                file.BaseStream.Seek(mcnkOffsets[i] + (sizeof(uint) * 15), SeekOrigin.Begin);
                areaIds[i] = file.ReadUInt32();
            }
        }

        pooler.Return(buffer);
    }

    private static void HandleMH2O(BinaryReader file, out LiquidData[] liquidData)
    {
        liquidData = new LiquidData[LiquidData.SIZE];

        Span<byte> buffer = stackalloc byte[Marshal.SizeOf<MH2OData1>()];

        long chunkStart = file.BaseStream.Position;
        for (int i = 0; i < LiquidData.SIZE; i++)
        {
            uint offsetData1 = file.ReadUInt32();
            int used = file.ReadInt32();
            uint offsetData2 = file.ReadUInt32();
            MH2OData1 data1 = EmptyMH2OData1;

            if (offsetData1 != 0)
            {
                long lastPos = file.BaseStream.Position;

                file.BaseStream.Seek(chunkStart + offsetData1, SeekOrigin.Begin);

                int readSize = file.Read(buffer);
                data1 = MemoryMarshal.Read<MH2OData1>(buffer);

                file.BaseStream.Seek(lastPos, SeekOrigin.Begin);
            }

            float[] water_height = new float[LiquidData.HEIGHT_SIZE * LiquidData.HEIGHT_SIZE];
            byte[] water_flags = new byte[LiquidData.FLAG_SIZE * LiquidData.FLAG_SIZE];

            if (used != 0 && offsetData1 != 0 && data1.offsetData2b != 0 && (data1.flags & 1) == 1)
            {
                long lastPos = file.BaseStream.Position;
                file.BaseStream.Seek(chunkStart + data1.offsetData2b, SeekOrigin.Begin);

                for (int x = data1.xOffset; x <= data1.xOffset + data1.Width; x++)
                {
                    for (int y = data1.yOffset; y <= data1.yOffset + data1.Height; y++)
                    {
                        int index = y * LiquidData.HEIGHT_SIZE + x;
                        water_height[index] = file.ReadSingle();
                    }
                }

                for (int x = data1.xOffset; x < data1.xOffset + data1.Width; x++)
                {
                    for (int y = data1.yOffset; y < data1.yOffset + data1.Height; y++)
                    {
                        int index = y * LiquidData.FLAG_SIZE + x;
                        water_flags[index] = file.ReadByte();
                    }
                }

                file.BaseStream.Seek(lastPos, SeekOrigin.Begin);
            }

            liquidData[i] = new
            (
                offsetData1,
                used,
                offsetData2,
                data1,
                water_height,
                water_flags
            );
        }
    }

    private static void HandleMCIN(BinaryReader file, Span<SMChunkInfo> mcnk)
    {
        file.Read(MemoryMarshal.Cast<SMChunkInfo, byte>(mcnk));
    }

    private static void HandleMDDF(BinaryReader file, ModelManager modelmanager, Span<string> models, uint size, out ModelInstance[] modelis)
    {
        int nMDX = (int)size / 36;

        modelis = new ModelInstance[nMDX];
        for (int i = 0; i < nMDX; i++)
        {
            int id = file.ReadInt32();

            string path = models[id];
            Model model = modelmanager.AddAndLoadIfNeeded(path);
            modelis[i] = new(file, model);
        }
    }

    private static void HandleMODF(BinaryReader file, Span<string> wmos, WMOManager wmomanager, uint size, out WMOInstance[] wmois)
    {
        int nWMO = (int)size / 64;
        wmois = new WMOInstance[nWMO];

        for (int i = 0; i < nWMO; i++)
        {
            int id = file.ReadInt32();
            WMORoot wmo = wmomanager.AddAndLoadIfNeeded(wmos[id]);

            wmois[i] = new(file, wmo);
        }
    }

    /* MapChunk */

    private static MapChunk ReadMapChunk(BinaryReader file, in LiquidData liquidData)
    {
        // Read away Magic and size
        //_ = file.ReadUInt32(); // uint crap_head
        //_ = file.ReadUInt32(); // uint crap_size

        // Each map chunk has 9x9 vertices,
        // and in between them 8x8 additional vertices, several texture layers, normal vectors, a shadow map, etc.

        //_ = file.ReadUInt32(); // uint flags
        //_ = file.ReadUInt32(); // uint ix
        //_ = file.ReadUInt32(); // uint iy
        //_ = file.ReadUInt32(); // uint nLayers
        //_ = file.ReadUInt32(); // uint nDoodadRefs
        //_ = file.ReadUInt32(); // uint ofsHeight
        //_ = file.ReadUInt32(); // uint ofsNormal
        //_ = file.ReadUInt32(); // uint ofsLayer
        //_ = file.ReadUInt32(); // uint ofsRefs
        //_ = file.ReadUInt32(); // uint ofsAlpha
        //_ = file.ReadUInt32(); // uint sizeAlpha
        //_ = file.ReadUInt32(); // uint ofsShadow
        //_ = file.ReadUInt32(); // uint sizeShadow

        // Header words are read rather than blind-skipped from here on, because
        // Mists overlays holes_high_res on ofsHeight/ofsNormal (words 5-6) when
        // the flag below is set. Layout is otherwise unchanged from vanilla.
        file.BaseStream.Seek(sizeof(UInt32) * 2, SeekOrigin.Current); // magic + size
        uint mcnkFlags = file.ReadUInt32();                           // [0] flags
        file.BaseStream.Seek(sizeof(UInt32) * 4, SeekOrigin.Current); // [1..4]
        ulong holesHighResRaw = file.ReadUInt64();                    // [5..6]
        file.BaseStream.Seek(sizeof(UInt32) * 6, SeekOrigin.Current); // [7..12]

        ulong holesHighRes = (mcnkFlags & MCNK_FLAG_HIGH_RES_HOLES) != 0
            ? holesHighResRaw
            : 0;

        uint areaID = file.ReadUInt32();
        //_ = file.ReadUInt32(); // uint nMapObjRefs
        file.BaseStream.Seek(sizeof(UInt32), SeekOrigin.Current);
        uint holes = file.ReadUInt32();

        //_ = file.ReadUInt16(); // ushort s1
        //_ = file.ReadUInt16(); // ushort s2

        //_ = file.ReadUInt32(); // uint d1
        //_ = file.ReadUInt32(); // uint d2
        //_ = file.ReadUInt32(); // uint d3
        //_ = file.ReadUInt32(); // uint predTex
        //_ = file.ReadUInt32(); // uint nEffectDoodad
        //_ = file.ReadUInt32(); // uint ofsSndEmitters 
        //_ = file.ReadUInt32(); // uint nSndEmitters
        //_ = file.ReadUInt32(); // uint ofsLiquid

        file.BaseStream.Seek((sizeof(UInt16) * 2) + (sizeof(UInt32) * 8), SeekOrigin.Current);

        uint sizeLiquid = file.ReadUInt32();
        float zpos = file.ReadSingle();
        float xpos = file.ReadSingle();
        float ypos = file.ReadSingle();

        //_ = file.ReadUInt32(); // uint textureId
        //_ = file.ReadUInt32(); // uint props 
        //_ = file.ReadUInt32(); // uint effectId

        file.BaseStream.Seek(sizeof(UInt32) * 3, SeekOrigin.Current);

        float xbase = -xpos + ChunkReader.ZEROPOINT;
        float ybase = ypos;
        float zbase = -zpos + ChunkReader.ZEROPOINT;

        float[] vertices = new float[3 * ((9 * 9) + (8 * 8))];

        bool haswater = false;
        float water_height1 = 0;
        float water_height2 = 0;
        float[] water_height = new float[LiquidData.HEIGHT_SIZE * LiquidData.HEIGHT_SIZE];
        byte[] water_flags = new byte[LiquidData.FLAG_SIZE * LiquidData.FLAG_SIZE];

        bool legacyWater = false;

        //logger.WriteLine("  " + zpos + " " + xpos + " " + ypos);
        do
        {
            uint type = file.ReadUInt32();
            uint size = file.ReadUInt32();
            long curpos = file.BaseStream.Position;

            if (type == ChunkReader.MCNR)
            {
                size = 0x1C0; // WTF
            }

            if (type == ChunkReader.MCVT)
            {
                HandleChunkMCVT(file, xbase, ybase, zbase, vertices);
            }
            else if (type == ChunkReader.MCLQ)
            {
                /* Some .adt-files are still using the old MCLQ chunks. Far from all though.
                * And those which use the MH2O chunk does not use these MCLQ chunks */
                size = sizeLiquid;
                if (sizeLiquid != 8)
                {
                    legacyWater = true;
                    haswater = true;
                    HandleChunkMCLQ(file, out water_height1, out water_height2, water_height, water_flags);
                }
            }

            file.BaseStream.Seek(Math.Min(curpos + size, file.BaseStream.Length), SeekOrigin.Begin);
        } while (!file.EOF());

        //set liquid info from the MH2O chunk since the old MCLQ is no more
        if (liquidData.offsetData1 != 0)
        {
            haswater = (liquidData.used & 1) == 1;

            water_height1 = liquidData.data1.heightLevel1;
            water_height2 = liquidData.data1.heightLevel2;

            //TODO: set height map and flags, very important
            water_height = liquidData.water_height;
            water_flags = liquidData.water_flags;
        }

        return new(xbase, ybase, zbase,
            areaID, haswater, holes, holesHighRes,
            vertices, water_height1, water_height2,
            water_height, water_flags, legacyWater);
    }

    private static void HandleChunkMCVT(BinaryReader file, float xbase, float ybase, float zbase, float[] vertices)
    {
        int index = 0;
        for (int j = 0; j < 17; j++)
        {
            for (int i = 0; i < ((j % 2 != 0) ? 8 : 9); i++)
            {
                float y = file.ReadSingle();
                float x = i * ChunkReader.UNITSIZE;
                float z = j * 0.5f * ChunkReader.UNITSIZE;

                if (j % 2 != 0)
                {
                    x += ChunkReader.UNITSIZE * 0.5f;
                }

                vertices[index++] = xbase + x;
                vertices[index++] = ybase + y;
                vertices[index++] = zbase + z;
            }
        }
    }

    private static void HandleChunkMCLQ(BinaryReader file, out float water_height1, out float water_height2, float[] water_height, byte[] water_flags)
    {
        water_height1 = file.ReadSingle();
        water_height2 = file.ReadSingle();

        for (int i = 0; i < LiquidData.HEIGHT_SIZE * LiquidData.HEIGHT_SIZE; i++)
        {
            uint whatIsThis = file.ReadUInt32();
            water_height[i] = file.ReadSingle();
        }

        Span<byte> water_flagsSpan = water_flags.AsSpan();
        file.Read(water_flagsSpan);
    }
}
