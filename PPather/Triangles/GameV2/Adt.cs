using SharedLib;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PPather.Triangles.GameV2;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MMDX
{
    public readonly Magic magic;
    public readonly UInt32 size;
    public readonly byte filenames; // char filenames[1];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MMID
{
    public readonly Magic magic;
    public readonly UInt32 size;
    public readonly UInt32_T1 offsets;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MDDF_Entry
{
    public readonly UInt32 id;
    public readonly UInt32 uniqueId;
    public readonly float x;
    public readonly float y;
    public readonly float z;
    public readonly float rx;
    public readonly float ry;
    public readonly float rz;
    public readonly ushort scale;
    public readonly ushort flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MDDF
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly Entry_1 entries;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MWMO
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly Char_1 filenames;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MWID
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly UInt32_T1 offsets;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MODF_Entry
{
    public readonly UInt32 id;
    public readonly UInt32 uniqueId;
    public readonly float x;
    public readonly float y;
    public readonly float z;
    public readonly float rx;
    public readonly float ry;
    public readonly float rz;
    public readonly float bbMinX;
    public readonly float bbMinY;
    public readonly float bbMinZ;
    public readonly float bbMaxX;
    public readonly float bbMaxY;
    public readonly float bbMaxZ;
    public readonly ushort flags;
    public readonly ushort doodadSet;
    public readonly ushort nameSet;
    public readonly ushort scale;

}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MODF
{
    public readonly Magic magic;
    public readonly UInt32 size;
    public readonly MODF_Entry_1 entries;
};

public enum AdtLiquidVertexFormat : ushort
{
    HeightDepth = 0,
    HeightTextureCoord = 1,
    Depth = 2,
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct AdtLiquid
{
    public readonly ushort type;
    public readonly AdtLiquidVertexFormat vertexFormat;
    public readonly float minHeightLevel;
    public readonly float maxHeightLevel;
    public readonly byte offsetX;
    public readonly byte offsetY;
    public readonly byte width;
    public readonly byte height;
    public readonly UInt32 offsetRenderMask;
    public readonly UInt32 offsetVertexData;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct AdtLiquidAttributes
{
    public readonly ulong fishable;
    public readonly ulong deep;
};

// MH2O data block

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MCVT
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly MCVT_HeightMap heightMap;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MCNK
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly UInt32 flags;
    public readonly UInt32 ix;
    public readonly UInt32 iy;
    public readonly UInt32 nLayers;
    public readonly UInt32 nDoodadRefs;
    public readonly UInt32 offsMcvt;
    public readonly UInt32 offsMcnr;
    public readonly UInt32 offsMcly;
    public readonly UInt32 offsMcrf;
    public readonly UInt32 offsMcal;
    public readonly UInt32 sizeMcal;
    public readonly UInt32 offsMcsh;
    public readonly UInt32 sizeMcsh;
    public readonly UInt32 areaid;
    public readonly UInt32 nMapObjRefs;
    public readonly UInt32 holes;
    public readonly UShort_2 s;
    public readonly UInt32_T3 data;
    public readonly UInt32 predTex;
    public readonly UInt32 nEffectDoodad;
    public readonly UInt32 offsMcse;
    public readonly UInt32 nSndEmitters;
    public readonly UInt32 offsMclq;
    public readonly UInt32 sizeMclq;
    public readonly float x;
    public readonly float y;
    public readonly float z;
    public readonly UInt32 offsMccv;
    public readonly UInt32 props;
    public readonly UInt32 effectId;

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }

    public bool IsHole(int x, int y) { return holes == 0 || 0 != ((holes & 0x0000FFFFu) & ((1 << (x / 2)) << ((y / 4) << 2))); }
};


[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MCIN_Cell
{
    public readonly UInt32 offsetMcnk;
    public readonly UInt32 size;
    public readonly UInt32 flags;
    public readonly UInt32 asyncId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MCIN
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly MCIN_Cell_Array cells;

    public MCIN_Cell this[uint x, uint y] => cells[((int)x * Adt.ADT_CELLS_PER_GRID) + (int)y];
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MHDR
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly UInt32 flags;
    public readonly UInt32 offsetMcin;
    public readonly UInt32 offsetMtex;
    public readonly UInt32 offsetMmdx;
    public readonly UInt32 offsetMmid;
    public readonly UInt32 offsetMwmo;
    public readonly UInt32 offsetMwid;
    public readonly UInt32 offsetMddf;
    public readonly UInt32 offsetModf;
    public readonly UInt32 offsetMfbo;
    public readonly UInt32 offsetMh2o;

    public readonly UInt32_T5 data;
};

public sealed class Adt
{
    public const float TileSize = 533.33333f;
    public const float WorldSize = TileSize * 32.0f;
    public const float ChunkSize = TileSize / 16.0f;
    public const float UnitSize = ChunkSize / 8.0f;
    public const float HalfUnitSize = UnitSize / 2.0f;

    public const int ADT_CELLS_PER_GRID = 16;
    public const int ADT_CELL_SIZE = 8;
    public const int ADT_GRID_SIZE = ADT_CELLS_PER_GRID * ADT_CELL_SIZE;

    // The raw binary data for the ADT file.
    public byte[] Data { get; }
    public uint Size { get; }

    public Adt(byte[] data, uint size)
    {
        Data = data;
        Size = size;
    }

    // Mver() returns the first struct from Data.
    public MVER Mver() =>
        MemoryMarshal.Read<MVER>(Data.AsSpan(0, Marshal.SizeOf<MVER>()));

    // Mhdr() comes immediately after MVER.
    public MHDR Mhdr() =>
        MemoryMarshal.Read<MHDR>(Data.AsSpan(Marshal.SizeOf<MVER>(), Marshal.SizeOf<MHDR>()));

    // Generic method to get a substructure at a given offset.
    // The offset stored in MHDR is relative to a base that is sizeof(MVER)+8 bytes into Data.
    public T? GetSub<T>(uint offset) where T : struct
    {
        if (offset == 0)
            return null;

        int baseOffset = Marshal.SizeOf<MVER>() + 8;
        int absOffset = baseOffset + (int)offset;

        if (absOffset + Marshal.SizeOf<T>() <= Size)
            return MemoryMarshal.Read<T>(Data.AsSpan(absOffset, Marshal.SizeOf<T>()));

        return null;
    }

    public MCIN? Mcin() => GetSub<MCIN>(Mhdr().offsetMcin);
    public MH2O? Mh2o() => GetSub<MH2O>(Mhdr().offsetMh2o);
    public MMDX? Mmdx() => GetSub<MMDX>(Mhdr().offsetMmdx);
    public MMID? Mmid() => GetSub<MMID>(Mhdr().offsetMmid);
    public MDDF? Mddf() => GetSub<MDDF>(Mhdr().offsetMddf);
    public MWMO? Mwmo() => GetSub<MWMO>(Mhdr().offsetMwmo);
    public MWID? Mwid() => GetSub<MWID>(Mhdr().offsetMwid);
    public MODF? Modf() => GetSub<MODF>(Mhdr().offsetModf);

    // Returns the MCNK for a given cell (x, y) using MCIN's cell data.
    public MCNK? Mcnk(uint x, uint y)
    {
        var mcin = Mcin();
        if (mcin == null)
            return null;

        // Assume cells is a two-dimensional array (or a jagged array) of LiquidCell
        uint offset = mcin.Value[x, y].offsetMcnk;
        if (offset == 0 || offset + Marshal.SizeOf<MCNK>() > Size)
            return null;

        return MemoryMarshal.Read<MCNK>(Data.AsSpan((int)offset, Marshal.SizeOf<MCNK>()));
    }

    // In C++ Mcvt() uses pointer arithmetic on the MCNK pointer.
    // In C# we must know the absolute offset at which the MCNK was read.
    // For this example we pass the mcnkOffset (e.g. from the MCIN cell).
    public MCVT? Mcvt(MCNK mcnk, int mcnkOffset)
    {
        uint offsMcvt = mcnk.offsMcvt;
        if (offsMcvt == 0)
            return null;

        int absOffset = mcnkOffset + (int)offsMcvt;
        if (absOffset + Marshal.SizeOf<MCVT>() > Size)
            return null;

        return MemoryMarshal.Read<MCVT>(Data.AsSpan(absOffset, Marshal.SizeOf<MCVT>()));
    }

    public void GetTerrainVertsAndTris(uint x, uint y, Structure structure)
    {
        // Get the terrain cell (MCNK) for (x,y)
        var mcnk_ = Mcnk(x, y);
        if (mcnk_ == null)
            return;

        var mcnk = mcnk_.Value;

        // We assume the MCNK was read from Data at the offset stored in MCIN.
        var mcin = Mcin();
        int mcnkOffset = (int)mcin.Value[x, y].offsetMcnk;
        var mcvt = Mcvt(mcnk, mcnkOffset);

        //lock (structure.Mutex)
        {
            // heightMap index (0–144)
            int mcvtIndex = 0;
            for (int j = 0; j < 17; j++) // 17 rows (alternating 9 and 8 units)
            {
                int unitCount = (j % 2 == 1) ? 8 : 9;
                for (int i = 0; i < unitCount; i++)
                {
                    // Compute the vertex position based on MCNK position
                    Vector3 v3 = new(
                        mcnk.x - (j * HalfUnitSize),
                        mcnk.y - (i * UnitSize),
                        mcnk.z
                    );

                    // Add the height offset from MCVT if available
                    if (mcvt != null)
                    {
                        v3.Z += mcvt.Value.heightMap[mcvtIndex];
                    }
                    mcvtIndex++;

                    int vertexCount = structure.Verts.Count;
                    structure.Verts.Add(v3); ///*.ToRDCoords()*/

                    if (unitCount != 8)
                    {
                        continue;
                    }

                    // For inner rows shift by half unit
                    v3.Y -= HalfUnitSize;

                    // If this cell is a hole, add terrain triangles
                    if (!mcnk.IsHole(i, j))
                    {
                        continue;
                    }

                    structure.Tris.Add(new Tri(vertexCount - 9, vertexCount, vertexCount - 8)); // Top
                    structure.Tris.Add(new Tri(vertexCount + 9, vertexCount, vertexCount + 8)); // Bottom
                    structure.Tris.Add(new Tri(vertexCount - 8, vertexCount, vertexCount + 9)); // Right
                    structure.Tris.Add(new Tri(vertexCount + 8, vertexCount, vertexCount - 9)); // Left

                    structure.TriTypes.Add(TriAreaId.TERRAIN_GROUND);
                    structure.TriTypes.Add(TriAreaId.TERRAIN_GROUND);
                    structure.TriTypes.Add(TriAreaId.TERRAIN_GROUND);
                    structure.TriTypes.Add(TriAreaId.TERRAIN_GROUND);
                }
            }
        }
    }

    public void GetLiquidVertsAndTris(uint x, uint y, Structure structure)
    {
        var mcnk_ = Mcnk(x, y);
        if (mcnk_ == null)
            return;

        var mh2o = Mh2o();
        if (mh2o == null)
            return;

        var liquid_ = mh2o.Value.GetInstance(Data, x, y);
        if (liquid_ == null)
            return;

        var mcnk = mcnk_.Value;
        var liquid = liquid_.Value;

        // Get attributes if needed (the default is returned if no offset is provided)
        //var attributes = mh2o.Value.GetAttributes(x, y);

        bool isOcean = liquid.type == 2;
        // For simplicity, we assume renderMask and liquidHeights would be obtained similarly.
        // (In a complete implementation you’d check liquid.offsetRenderMask and liquid.offsetVertexData.)
        byte[] renderMask = null;
        ReadOnlySpan<float> liquidHeights = []; // mh2o.Value.GetLiquidHeight(liquid.Value);

        // Loop over the liquid grid based on offsetX/offsetY, width, height
        for (byte i = liquid.offsetY; i < liquid.offsetY + liquid.height; i++)
        {
            float cx = mcnk.x - (i * UnitSize);
            for (byte j = liquid.offsetX; j < liquid.offsetX + liquid.width; j++)
            {
                if (!isOcean && renderMask != null)
                {
                    continue;
                }

                float cy = mcnk.y - (j * UnitSize);
                float cz = liquid.maxHeightLevel; // isOcean ?  : liquidHeights[liquidHeightIndex]
                //lock (structure.Mutex)
                {
                    int vertsIndex = structure.Verts.Count;
                    structure.Verts.Add(new Vector3(cx, cy, cz)); ///*.ToRDCoords()*/
                    structure.Verts.Add(new Vector3(cx - UnitSize, cy, cz)); ///*.ToRDCoords()*/
                    structure.Verts.Add(new Vector3(cx, cy - UnitSize, cz)); ///*.ToRDCoords()*/
                    structure.Verts.Add(new Vector3(cx - UnitSize, cy - UnitSize, cz)); ///*.ToRDCoords()*/

                    structure.Tris.Add(new Tri(vertsIndex + 2, vertsIndex, vertsIndex + 1));
                    structure.Tris.Add(new Tri(vertsIndex + 1, vertsIndex + 3, vertsIndex + 2));

                    structure.TriTypes.Add(isOcean ? TriAreaId.LIQUID_OCEAN : TriAreaId.LIQUID_WATER);
                    structure.TriTypes.Add(isOcean ? TriAreaId.LIQUID_OCEAN : TriAreaId.LIQUID_WATER);
                }
            }
        }
    }

    public void CalculateAreaBoundingBox(uint x, uint y, Dictionary<int, SubZoneArea> areaBounds)
    {
        var mcnk_ = Mcnk(x, y);
        if (mcnk_ == null)
            return;

        var mcnk = mcnk_.Value;

        int areaId = (int)mcnk.areaid;

        //lock (structure.Mutex)
        {
            Vector3 min = mcnk.ToVector3();
            Vector3 max = mcnk.ToVector3();

            /*
            for (int i = 0; i < 17; i++)
            {
                int unitCount = (i % 2 == 1) ? 8 : 9;
                for (int j = 0; j < unitCount; j++)
                {
                    Vector3 v3 = new(mcnk.x - (i * HalfUnitSize), mcnk.y - (j * UnitSize), mcnk.z);
                    min = Vector3.Min(min, v3);
                    max = Vector3.Max(max, v3);
                }
            }
            */

            /*
            const int n_i = 8; // since 17 iterations: indices -8 .. 8
            const int n_j = 4; // for 9 units: indices -4 .. 4

            for (int i = -n_i; i <= n_i; i++)
            {
                for (int j = -n_j; j <= n_j; j++)
                {
                    Vector3 v3 = new(mcnk.x + (i * HalfUnitSize), mcnk.y + (j * UnitSize), mcnk.z);
                    min = Vector3.Min(min, v3);
                    max = Vector3.Max(max, v3);
                }
            }
            */

            const int n_i = 8;  // for i: indices -8 ... 8
                                // For j, use two different centers:
            const float centerEven = 0;         // even rows (9 vertices) are centered naturally
            const float centerOdd = -0.5f * UnitSize;  // odd rows (8 vertices) shift by half a UnitSize

            for (int i = -n_i; i <= n_i; i++)
            {
                // Determine number of j iterations based on the row type
                int unitCount = (Math.Abs(i) % 2 == 1) ? 8 : 9;
                // Calculate j center offset for this row
                float jCenterOffset = (MathF.Abs(i) % 2 == 1) ? centerOdd : centerEven;

                // Compute j bounds (using the maximum count of 9 for even rows as a reference)
                int n_j_even = 4; // for rows with 9 vertices

                // For odd rows, if you have 8 units, they would naturally range from -3.5 to +3.5.
                // You can iterate using a double step if needed or convert to int with an offset.
                // Here, we assume a similar loop but then add the jCenterOffset.
                for (int j = -n_j_even; j <= n_j_even; j++)
                {
                    // Calculate the vertex position and apply the row offset for j
                    Vector3 v3 = new(
                        mcnk.x + (i * HalfUnitSize),
                        mcnk.y + (j * UnitSize) + jCenterOffset,
                        mcnk.z
                    );
                    min = Vector3.Min(min, v3);
                    max = Vector3.Max(max, v3);
                }
            }

            ref SubZoneArea bb = ref CollectionsMarshal.GetValueRefOrAddDefault(areaBounds, areaId, out bool exists);

            if (!exists)
            {
                bb = new SubZoneArea { Id = areaId, Min = min, Max = max };
            }
            else
            {
                bb = bb with { Min = Vector3.Min(bb.Min, min) };
                bb = bb with { Max = Vector3.Max(bb.Max, max) };
            }
        }
    }

    // TODO: fix this
    /*
    public void GetWmoVertsAndTris(CachedFileReader reader, Structure structure)
    {
        var modf = Modf();
        if (modf == null)
            return;

        var wmoFiles = new Dictionary<uint, byte[]>();
        int entryCount = (int)(modf.Value.size / Marshal.SizeOf<MODF_Entry>());
        for (int i = 0; i < entryCount; i++)
        {
            var entry = modf.Value.entries[i];
            // Get the WMO root filename using Mwmo() and Mwid()
            var mwmo = Mwmo();
            var mwid = Mwid();
            if (mwmo == null || mwid == null)
                continue;

            string wmoRootFilename = mwmo.Value.filenames + mwid.Value.offsets[entry.id];

            var wmo = reader.GetFileContent<Wmo>(wmoRootFilename);
            if (wmo == null)
                continue;
            var mohd = wmo.Mohd();
            if (mohd == null)
                continue;

            Matrix4x4 tranform = new();
            if (entry.x != 0.0f || entry.y != 0.0f || entry.z != 0.0f)
            {
                tranform.Translation = new Vector3(-(entry.z - WorldSize),
                                                     -(entry.x - WorldSize),
                                                     entry.y);
            }

            tranform.Rotate(new Vector3(entry.rz, entry.rx, entry.ry + 180.0f));

            for (uint w = 0; w < mohd.Value.groupCount; w++)
            {
                string wmoRF = wmoRootFilename;
                string wmoGroupName = string.Format("{0}_{1:D3}.wmo",
                    wmoRF.Substring(0, wmoRF.LastIndexOf('.')), w);
                var wmoGroup = reader.GetFileContent<WmoGroup>(wmoGroupName);
                if (wmoGroup == null)
                    continue;

                var movt = wmoGroup.Movt();
                var movi = wmoGroup.Movi();
                var mopy = wmoGroup.Mopy();
                if (movt != null && movi != null && mopy != null)
                {
                    lock (structure.Mutex)
                    {
                        int vertsBase = structure.Verts.Count;
                        for (int d = 0; d < movt.Value.Count(); d++)
                        {
                            structure.Verts.Add(tranform.Transform(movt.Value.Verts[d])); //.ToRDCoords()
                        }
                        for (int d = 0; d < movi.Value.Count(); d += 3)
                        {
                            // Skip triangles with certain flags/materials
                            if (((mopy.Value.data[d / 3].flags & 0x04) != 0) &&
                                (mopy.Value.data[d / 3].materials != 0xFF))
                                continue;

                            structure.Tris.Add(new Tri(vertsBase + (int)movi.Value.Tris[d],
                                                        vertsBase + (int)movi.Value.Tris[d + 1],
                                                        vertsBase + (int)movi.Value.Tris[d + 2]));

                            structure.TriTypes.Add(TriAreaId.WMO);
                        }
                    }
                }

                var mliq = wmoGroup.Mliq();
                if (mliq != null)
                {
                    int vertCount = mliq.Value.countYVertices * mliq.Value.countXVertices;
                    int mliqOffset = Marshal.SizeOf<MLIQ>();
                    // Get vertex data immediately following the MLIQ struct.
                    var dataSpan = Data.AsSpan(mliqOffset, vertCount * Marshal.SizeOf<MLIQVert>());
                    var dataPtr = MemoryMarshal.Cast<byte, MLIQVert>(dataSpan).ToArray();
                    int flagsOffset = mliqOffset + (vertCount * Marshal.SizeOf<MLIQVert>());
                    var flagsSpan = Data.AsSpan(flagsOffset, vertCount);

                    for (uint y = 0; y < mliq.Value.height; y++)
                    {
                        for (uint x = 0; x < mliq.Value.width; x++)
                        {
                            lock (structure.Mutex)
                            {
                                int vertsIndex = structure.Verts.Count;
                                AddLiquidVert(mliq.Value, dataPtr, (int)y, (int)x, structure.Verts, tranform);
                                AddLiquidVert(mliq.Value, dataPtr, (int)y, (int)x + 1, structure.Verts, tranform);
                                AddLiquidVert(mliq.Value, dataPtr, (int)y + 1, (int)x, structure.Verts, tranform);
                                AddLiquidVert(mliq.Value, dataPtr, (int)y + 1, (int)x + 1, structure.Verts, tranform);
                                
                                byte f = 0; // TODO: interpret flags from flagsSpan as needed.
                                if (true || f != 0x0F)
                                {
                                    structure.Tris.Add(new Tri(vertsIndex + 2, vertsIndex, vertsIndex + 1));
                                    structure.Tris.Add(new Tri(vertsIndex + 1, vertsIndex + 3, vertsIndex + 2));
                                    
                                    TriAreaId t = TriAreaId.LIQUID_WATER;
                                    if ((f & 1) != 0)
                                        t = TriAreaId.LIQUID_WATER;
                                    else if ((f & 2) != 0)
                                        t = TriAreaId.LIQUID_OCEAN;
                                    else if ((f & 4) != 0)
                                        t = TriAreaId.LIQUID_LAVA;
                                    else if ((f & 8) != 0)
                                        t = TriAreaId.LIQUID_SLIME;

                                    structure.TriTypes.Add(t);
                                    structure.TriTypes.Add(t);
                                }
                            }
                        }
                    }
                }
            }

            var modd = wmo.Modd();
            if (modd != null && modd.Value.size > 0)
            {
                var modn = wmo.Modn();
                if (modn != null)
                {
                    int defCount = (int)(modd.Value.size / Marshal.SizeOf<MODD.Definition>());
                    for (int m = 0; m < defCount; m++)
                    {
                        var definition = modd.Value.defs[m];
                        string doodadPath = modn.Value.names + definition.nameIndex;
                        string m2Name = string.Format("{0}.m2",
                            doodadPath.Substring(0, doodadPath.LastIndexOf('.')));
                        var m2 = reader.GetFileContent<M2>(m2Name);
                        if (m2 == null)
                            continue;
                        var md20 = m2.Md20();
                        if (md20 != null && m2.IsCollideable())
                        {
                            Matrix4x4 doodadTranform = new()
                            {
                                Translation = definition.position
                            };
                            doodadTranform.SetRotation(new Vector3(0.0f, 180.0f, 0.0f));
                            
                            // Set rotation from quaternion (negative values as in C++ code)
                            doodadTranform.SetRotation(-definition.qy, definition.qz, -definition.qx, definition.qw);
                            doodadTranform.Multiply(tranform);
                            
                            lock (structure.Mutex)
                            {
                                int vertsBase = structure.Verts.Count;
                                for (uint d = 0; d < md20.Value.countBoundingVertices; d++)
                                {
                                    Vector3 v3 = m2.Vertex(d);
                                    structure.Verts.Add(doodadTranform.Transform(v3)); //.ToRDCoords()
                                }
                                for (uint d = 0; d < md20.Value.countBoundingTriangles; d += 3)
                                {
                                    var t = m2.Tri(d);
                                    structure.Tris.Add(new Tri(vertsBase + (int)t[0],
                                                                vertsBase + (int)t[1],
                                                                vertsBase + (int)t[2]));
                                    structure.TriTypes.Add(TriAreaId.WMO);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Adds a liquid vertex from the MLIQ data.
    public void AddLiquidVert(MLIQ mliq, MLIQVert[] dataPtr, int y, int x, List<Vector3> verts, Matrix4x4 tranform)
    {
        int index = (y * mliq.width) + x;
        var liq = dataPtr[index];
        Vector3 basePos = new Vector3(
            mliq.position.x + (x * UnitSize),
            mliq.position.y + (y * UnitSize),
            Math.Abs(liq.waterVert.height) > 0.5f ? liq.waterVert.height : mliq.position.z + liq.waterVert.height
        );
        verts.Add(tranform.Transform(basePos)); //.ToRDCoords()
    }

    public void GetDoodadVertsAndTris(CachedFileReader reader, Structure structure)
    {
        var mddf = Mddf();
        if (mddf == null)
            return;
        int entryCount = (int)(mddf.Value.size / Marshal.SizeOf<MDDF_Entry>());
        for (int i = 0; i < entryCount; i++)
        {
            var entry = mddf.Value.entries[i];
            Matrix4x4 tranform = new Matrix4x4();
            if (entry.x != 0.0f || entry.y != 0.0f || entry.z != 0.0f)
            {
                tranform.Translation = new Vector3(-(entry.z - WorldSize),
                                                     -(entry.x - WorldSize),
                                                     entry.y);
            }
            tranform.SetRotation(new Vector3(entry.rz, entry.rx, entry.ry + 180.0f));
            
            string doodadPath = Mmdx().Value.filenames + Mmid().Value.offsets[entry.id];
            string m2Name = string.Format("{0}.m2", doodadPath.Substring(0, doodadPath.LastIndexOf('.')));
            
            var m2 = reader.GetFileContent<M2>(m2Name);
            if (m2 == null)
                continue;

            var md20 = m2.Md20();
            if (md20 != null && m2.IsCollideable())
            {
                lock (structure.Mutex)
                {
                    int vertsBase = structure.Verts.Count;
                    for (uint d = 0; d < md20.Value.countBoundingVertices; d++)
                    {
                        Vector3 v3 = m2.Vertex(d);
                        structure.Verts.Add(tranform.Transform(v3)); //.ToRDCoords()
                    }
                    for (uint d = 0; d < md20.Value.countBoundingTriangles; d += 3)
                    {
                        var t = m2.Tri(d);
                        structure.Tris.Add(new Tri(vertsBase + (int)t[0],
                                                    vertsBase + (int)t[1],
                                                    vertsBase + (int)t[2]));

                        structure.TriTypes.Add(TriAreaId.DOODAD);
                    }
                }
            }
        }
    }
    */
}