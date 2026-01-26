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

    public static MapTile Read(ArchiveSet archive, ReadOnlySpan<char> name, WMOManager wmomanager, ModelManager modelmanager)
    {
        LiquidData[] LiquidDataChunk = [];

        Span<SMChunkInfo> mcin = stackalloc SMChunkInfo[MapTile.SIZE * MapTile.SIZE];

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
            uint type = file.ReadUInt32();
            uint size = file.ReadUInt32();
            long nextPos = file.BaseStream.Position + size;

            switch (type)
            {
                case ChunkReader.MCIN:
                    HandleMCIN(file, mcin);
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

        pooler.Return(buffer);

        for (int index = 0; index < MapTile.SIZE * MapTile.SIZE; index++)
        {
            int off = (int)mcin[index].offset;
            file.BaseStream.Seek(off, SeekOrigin.Begin);

            chunks[index] = ReadMapChunk(file, LiquidDataChunk.Length > 0 ? LiquidDataChunk[index] : EmptyLiquidData);
            hasChunk[index] = true;
        }

        return new(modelis, wmois, chunks, hasChunk);
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

        file.BaseStream.Seek(sizeof(UInt32) * 15, SeekOrigin.Current);

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
            areaID, haswater, holes,
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
