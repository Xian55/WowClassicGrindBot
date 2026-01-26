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
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Wmo;

internal static class WmoRootFile
{
    public static void Load(ArchiveSet archive, ReadOnlySpan<char> name, WMORoot wmo, ModelManager modelmanager)
    {
        using MpqFileStream mpq = archive.GetStream(name);

        var pooler = ArrayPool<byte>.Shared;
        byte[] buffer = pooler.Rent((int)mpq.Length);
        mpq.ReadAllBytesTo(buffer);

        using MemoryStream stream = new(buffer, 0, (int)mpq.Length, false);
        using BinaryReader file = new(stream);

        do
        {
            uint type = file.ReadUInt32();
            uint size = file.ReadUInt32();
            long nextPos = file.BaseStream.Position + size;

            switch (type)
            {
                case ChunkReader.MOHD:
                    HandleMOHD(file, wmo, size);
                    break;
                case ChunkReader.MOGI:
                    HandleMOGI(file, wmo, size);
                    break;
                case ChunkReader.MODS:
                    HandleMODS(file, wmo);
                    break;
                case ChunkReader.MODD:
                    HandleMODD(file, wmo, modelmanager, size);
                    break;
                case ChunkReader.MODN:
                    HandleMODN(file, wmo, size);
                    break;
            }

            file.BaseStream.Seek(nextPos, SeekOrigin.Begin);
        } while (!file.EOF());

        pooler.Return(buffer);
    }

    private static void HandleMOHD(BinaryReader file, WMORoot wmo, uint size)
    {
        //file.ReadUInt32(); // uint nTextures
        file.BaseStream.Seek(sizeof(UInt32), SeekOrigin.Current);

        uint nGroups = file.ReadUInt32();
        //file.ReadUInt32(); // uint nP
        //file.ReadUInt32(); // uint nLights
        file.BaseStream.Seek(sizeof(UInt32) * 2, SeekOrigin.Current);

        wmo.nModels = file.ReadUInt32();
        wmo.nDoodads = file.ReadUInt32();
        wmo.nDoodadSets = file.ReadUInt32();

        //file.ReadUInt32(); //uint col
        //file.ReadUInt32(); //uint nX
        file.BaseStream.Seek(sizeof(UInt32) * 2, SeekOrigin.Current);

        wmo.v1 = file.ReadVector3();
        wmo.v2 = file.ReadVector3();

        wmo.groups = new WMOGroup[nGroups];

        wmo.flags = file.ReadUInt32();
    }

    private static void HandleMODS(BinaryReader file, WMORoot wmo)
    {
        wmo.doodads = new DoodadSet[wmo.nDoodadSets];

        Span<byte> byteSpan = MemoryMarshal.Cast<DoodadSet, byte>(wmo.doodads.AsSpan());
        file.Read(byteSpan);
    }

    private static void HandleMODD(BinaryReader file, WMORoot wmo, ModelManager modelmanager, uint size)
    {
        // 40 bytes per doodad instance, nDoodads entries.
        // While WMOs and models (M2s) in a map tile are rotated along the axes,
        //  doodads within a WMO are oriented using quaternions! Hooray for consistency!
        /*
        0x00 	uint32 		Offset to the start of the model's filename in the MODN chunk.
        0x04 	3 * float 	Position (X,Z,-Y)
        0x10 	float 		W component of the orientation quaternion
        0x14 	3 * float 	X, Y, Z components of the orientaton quaternion
        0x20 	float 		Scale factor
        0x24 	4 * uint8 	(B,G,R,A) color. Unknown. It is often (0,0,0,255). (something to do with lighting maybe?)
		*/

        uint sets = size / 0x28;
        wmo.doodadInstances = new ModelInstance[wmo.nDoodads];

        for (int i = 0; i < sets; i++)
        {
            uint nameOffsetInMODN = file.ReadUInt32(); // 0x00

            Vector3 pos = file.ReadVector3_XZY();

            float quatw = file.ReadSingle(); // 0x10
            Vector3 dir = file.ReadVector3();

            float scale = file.ReadSingle(); // 0x20

            //file.ReadUInt32(); // lighning crap 0x24
            file.BaseStream.Seek(sizeof(UInt32), SeekOrigin.Current);

            string name = ChunkReader.ExtractString(wmo.MODNraw, (int)nameOffsetInMODN);
            Model m = modelmanager.AddAndLoadIfNeeded(name);

            ModelInstance mi = new(m, pos, dir, scale, quatw);
            wmo.doodadInstances[i] = mi;
        }
    }

    private static void HandleMODN(BinaryReader file, WMORoot wmo, uint size)
    {
        // List of filenames for M2 (mdx) models that appear in this WMO.
        wmo.MODNraw = file.ReadBytes((int)size);
    }

    private static void HandleMOGI(BinaryReader file, WMORoot wmo, uint size)
    {
        for (int i = 0; i < wmo.groups.Length; i++)
        {
            WMOGroup g = new();
            wmo.groups[i] = g;

            g.mogpFlags = file.ReadUInt32();

            g.v1 = file.ReadVector3();
            g.v2 = file.ReadVector3();

            //_ = file.ReadUInt32(); // uint nameOfs
            file.BaseStream.Seek(sizeof(UInt32), SeekOrigin.Current);
        }
    }
}
