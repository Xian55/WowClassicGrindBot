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

using System;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using SharedLib.Data;

namespace Wmo
{
    internal unsafe class ChunkReader
    {
        public const float TILESIZE = 533.33333f;
        public const float ZEROPOINT = 32.0f * TILESIZE;
        public const float CHUNKSIZE = TILESIZE / 16.0f;
        public const float UNITSIZE = CHUNKSIZE / 8.0f;

        public static uint ToBin(ReadOnlySpan<char> s)
        {
            return (uint)s[3] | ((uint)s[2] << 8) | ((uint)s[1] << 16) | ((uint)s[0] << 24);
        }

        public static string ExtractString(byte[] buff, int off)
        {
            fixed (byte* bp = buff)
            {
                sbyte* sp = (sbyte*)bp;
                sp += off;
                return new string(sp);
            }
        }

        public static readonly uint MWMO = ToBin(nameof(MWMO));
        public static readonly uint MODF = ToBin(nameof(MODF));
        public static readonly uint MAIN = ToBin(nameof(MAIN));
        public static readonly uint MPHD = ToBin(nameof(MPHD));
        public static readonly uint MVER = ToBin(nameof(MVER));
        public static readonly uint MOGI = ToBin(nameof(MOGI));
        public static readonly uint MOHD = ToBin(nameof(MOHD));
        public static readonly uint MODN = ToBin(nameof(MODN));
        public static readonly uint MODS = ToBin(nameof(MODS));
        public static readonly uint MODD = ToBin(nameof(MODD));
        public static readonly uint MOPY = ToBin(nameof(MOPY));
        public static readonly uint MOVI = ToBin(nameof(MOVI));
        public static readonly uint MOVT = ToBin(nameof(MOVT));
        public static readonly uint MCIN = ToBin(nameof(MCIN));
        public static readonly uint MMDX = ToBin(nameof(MMDX));
        public static readonly uint MDDF = ToBin(nameof(MDDF));
        public static readonly uint MCNR = ToBin(nameof(MCNR));
        public static readonly uint MCVT = ToBin(nameof(MCVT));
        public static readonly uint MCLQ = ToBin(nameof(MCLQ));
        public static readonly uint MH2O = ToBin(nameof(MH2O));
    }

    public static class Unique
    {
        public static int Index;
    }

    public class WMOManager : Manager<WMO>
    {
        private readonly StormDll.ArchiveSet set;
        private readonly ModelManager modelmanager;
        private readonly DataConfig dataConfig;

        public WMOManager(StormDll.ArchiveSet set, ModelManager modelmanager, int maxItems, DataConfig dataConfig)
            : base(maxItems)
        {
            this.set = set;
            this.modelmanager = modelmanager;
            this.dataConfig = dataConfig;
        }

        public override bool Load(string path, out WMO t)
        {
            string tempFile = Path.Join(dataConfig.PPather, $"{Unique.Index++}.tmp"); //wmo
            set.SFileExtractFile(path, tempFile);
            if (!File.Exists(tempFile))
            {
                t = default;
                return false;
            }

            t = new()
            {
                fileName = path
            };

            _ = new WmoRootFile(tempFile, t, modelmanager);

            for (int i = 0; i < t.groups.Length; i++)
            {
                ReadOnlySpan<char> part = path[..^4].AsSpan();
                string gf = string.Format("{0}_{1,3:000}.wmo", part.ToString(), i);

                set.SFileExtractFile(gf, tempFile);
                if (!File.Exists(tempFile))
                    continue;

                _ = new WmoGroupFile(t.groups[i], tempFile);
            }
            return true;
        }
    }

    public readonly struct WMOInstance
    {
        public readonly WMO wmo;
        public readonly int id;
        public readonly Vector3 pos, pos2, pos3;
        public readonly Vector3 dir;
        public readonly int d2; //d3
        public readonly int doodadset;

        public WMOInstance(BinaryReader file, WMO wmo)
        {
            // read X bytes from file
            this.wmo = wmo;

            id = file.ReadInt32();
            pos = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            dir = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            pos2 = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            pos3 = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());

            d2 = file.ReadInt32();
            doodadset = file.ReadInt16();
            _ = file.ReadInt16();
        }
    }

    public struct DoodadSet
    {
        public uint firstInstance;
        public uint nInstances;
    }

    public class WMO
    {
        public string fileName;
        public WMOGroup[] groups;

        //int nTextures, nGroups, nP, nLight nX;
        public Vector3 v1, v2; // bounding box

        public byte[] MODNraw;
        public uint nModels;
        public uint nDoodads;
        public uint nDoodadSets;

        public DoodadSet[] doodads;
        public ModelInstance[] doodadInstances;
    }

    public abstract class Manager<T>
    {
        private readonly Dictionary<string, T> items;

        private readonly int maxItems;

        public Manager(int maxItems)
        {
            this.maxItems = maxItems;

            items = new Dictionary<string, T>(maxItems);
        }

        public abstract bool Load(string path, out T t);

        public T AddAndLoadIfNeeded(string path)
        {
            path = path.ToLower();
            if (!items.TryGetValue(path, out T t))
            {
                if (Load(path, out t))
                {
                    items[path] = t;
                }
            }
            return t;
        }
    }

    public class ModelManager : Manager<Model>
    {
        private readonly StormDll.ArchiveSet set;
        private readonly DataConfig dataConfig;

        public ModelManager(StormDll.ArchiveSet set, int maxModels, DataConfig dataConfig)
            : base(maxModels)
        {
            this.set = set;
            this.dataConfig = dataConfig;
        }

        public override bool Load(string path, out Model t)
        {
            // change .mdx to .m2
            //string file=path.Substring(0, path.Length-4)+".m2";

            string file = path;
            if (Path.GetExtension(path).Equals(".mdx") || Path.GetExtension(path).Equals(".mdl"))
            {
                file = Path.ChangeExtension(path, ".m2");
            }

            string tempFile = Path.Join(dataConfig.PPather, $"{Unique.Index++}.tmp"); //model
            set.SFileExtractFile(file, tempFile);
            if (!File.Exists(tempFile))
            {
                t = default;
                return false;
            }

            t = ModelFile.Read(tempFile, file);
            return true;
        }
    }

    public readonly struct ModelInstance
    {
        public readonly Model model;
        public readonly Vector3 pos;
        public readonly Vector3 dir;
        public readonly float w;
        public readonly float scale;

        public ModelInstance(BinaryReader file, Model model)
        {
            this.model = model;
            _ = file.ReadUInt32(); // uint d1
            pos = new(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            dir = new(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            w = 0;
            scale = file.ReadUInt32() / 1024.0f;
        }

        public ModelInstance(Model m, Vector3 pos, Vector3 dir, float sc, float w)
        {
            this.model = m;
            this.pos = pos;
            this.dir = dir;
            this.scale = sc;
            this.w = w;
        }
    }

    public readonly struct Model
    {
        public readonly string fileName;
        public readonly float[] vertices;           // 3 per vertex
        public readonly float[] boundingVertices;   // 3 per vertex
        public readonly UInt16[] boundingTriangles;

        public Model(string fileName, float[] vertices, UInt16[] boundingTriangles, float[] boundingVertices)
        {
            this.fileName = fileName;
            this.vertices = vertices;
            this.boundingTriangles = boundingTriangles;
            this.boundingVertices = boundingVertices;
        }
    }

    public static class ModelFile
    {
        public static Model Read(string path, string fileName)
        {
            using Stream stream = File.OpenRead(path);
            using BinaryReader file = new(stream);

            // UPDATED FOR WOTLK 17.10.2008 by toblakai
            // SOURCE: http://www.madx.dk/wowdev/wiki/index.php?title=M2/WotLK

            _ = file.ReadChars(4);
            //PPather.Debug("M2 MAGIC: {0}",new string(Magic));
            _ = file.ReadUInt32(); // (including \0);
                                   // check that we have the new known WOTLK Magic 0x80100000
                                   //PPather.Debug("M2 HEADER VERSION: 0x{0:x8}",
                                   //    (uint) (version >> 24) | ((version << 8) & 0x00FF0000) | ((version >> 8) & 0x0000FF00) | (version << 24));
            _ = file.ReadUInt32(); // (including \0);
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); // ? always 0, 1 or 3 (mostly 0);
            _ = file.ReadUInt32(); //  - number of global sequences;
            _ = file.ReadUInt32(); //  - offset to global sequences;
            _ = file.ReadUInt32(); //  - number of animation sequences;
            _ = file.ReadUInt32(); //  - offset to animation sequences;
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); // Mapping of global IDs to the entries in the Animation sequences block.
                                   // NOT IN WOTLK uint nD=file.ReadUInt32(); //  - always 201 or 203 depending on WoW client version;
                                   // NOT IN WOTLK uint ofsD=file.ReadUInt32();
            _ = file.ReadUInt32(); //  - number of bones;
            _ = file.ReadUInt32(); //  - offset to bones;
            _ = file.ReadUInt32(); //  - bone lookup table;
            _ = file.ReadUInt32();

            uint nVertices = file.ReadUInt32(); //  - number of vertices;
            uint ofsVertices = file.ReadUInt32(); //  - offset to vertices;

            _ = file.ReadUInt32(); //  - number of views (LOD versions?) 4 for every model;
                                   // NOT IN WOTLK (now in .skins) uint ofsViews=file.ReadUInt32(); //  - offset to views;
            _ = file.ReadUInt32(); //  - number of color definitions;
            _ = file.ReadUInt32(); //  - offset to color definitions;
            _ = file.ReadUInt32(); //  - number of textures;
            _ = file.ReadUInt32(); //  - offset to texture definitions;
            _ = file.ReadUInt32(); //  - number of transparency definitions;
            _ = file.ReadUInt32(); //  - offset to transparency definitions;
                                   // NOT IN WOTLK uint nTexAnims = file.ReadUInt32(); //  - number of texture animations;
                                   // NOT IN WOTLK uint ofsTexAnims = file.ReadUInt32(); //  - offset to texture animations;
            _ = file.ReadUInt32(); //  - always 0;
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); //  - number of blending mode definitions;
            _ = file.ReadUInt32(); //  - offset to blending mode definitions;
            _ = file.ReadUInt32(); //  - bone lookup table;
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); //  - number of texture lookup table entries;
            _ = file.ReadUInt32(); //  - offset to texture lookup table;
            _ = file.ReadUInt32(); //  - texture unit definitions?;
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); //  - number of transparency lookup table entries;
            _ = file.ReadUInt32(); //  - offset to transparency lookup table;
            _ = file.ReadUInt32(); //  - number of texture animation lookup table entries;
            _ = file.ReadUInt32(); //  - offset to texture animation lookup table;

            float[] theFloats = new float[14]; // Noone knows. Meeh, they are here.
            for (int i = 0; i < 14; i++)
                theFloats[i] = file.ReadSingle();

            uint nBoundingTriangles = file.ReadUInt32();
            uint ofsBoundingTriangles = file.ReadUInt32();
            uint nBoundingVertices = file.ReadUInt32();
            uint ofsBoundingVertices = file.ReadUInt32();

            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); //  - number of lights;
            _ = file.ReadUInt32(); //  - offset to lights;
            _ = file.ReadUInt32(); //  - number of cameras;
            _ = file.ReadUInt32(); //  - offset to cameras;
            _ = file.ReadUInt32();
            _ = file.ReadUInt32();
            _ = file.ReadUInt32(); //  - number of ribbon emitters;
            _ = file.ReadUInt32(); //  - offset to ribbon emitters;
            _ = file.ReadUInt32(); //  - number of particle emitters;
            _ = file.ReadUInt32(); //  - offset to particle emitters;

            return new(
                fileName,
                ReadVertices(file, nVertices, ofsVertices),
                ReadBoundingTriangles(file, nBoundingTriangles, ofsBoundingTriangles),
                ReadBoundingVertices(file, nBoundingVertices, ofsBoundingVertices));
        }

        private static float[] ReadBoundingVertices(BinaryReader file, uint nVertices, uint ofsVertices)
        {
            if (nVertices == 0)
                return Array.Empty<float>();

            file.BaseStream.Seek(ofsVertices, SeekOrigin.Begin);
            float[] vertices = new float[nVertices * 3];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = file.ReadSingle();
            }

            return vertices;
        }

        private static UInt16[] ReadBoundingTriangles(BinaryReader file, uint nTriangles, uint ofsTriangles)
        {
            if (nTriangles == 0)
                return Array.Empty<UInt16>();

            file.BaseStream.Seek(ofsTriangles, SeekOrigin.Begin);
            UInt16[] triangles = new UInt16[nTriangles];

            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = file.ReadUInt16();
            }
            return triangles;
        }

        private static float[] ReadVertices(BinaryReader file, uint nVertices, uint ofcVertices)
        {
            float[] vertices = new float[nVertices * 3];

            file.BaseStream.Seek(ofcVertices, SeekOrigin.Begin);
            for (int i = 0; i < nVertices; i++)
            {
                vertices[i * 3 + 0] = file.ReadSingle();
                vertices[i * 3 + 1] = file.ReadSingle();
                vertices[i * 3 + 2] = file.ReadSingle();

                _ = file.ReadUInt32();  // bone weights
                _ = file.ReadUInt32();  // bone indices

                _ = file.ReadSingle(); // normal *3
                _ = file.ReadSingle();
                _ = file.ReadSingle();

                _ = file.ReadSingle(); // texture coordinates
                _ = file.ReadSingle();

                _ = file.ReadSingle(); // some crap
                _ = file.ReadSingle();
            }
            return vertices;
        }
    }

    public class WMOGroup
    {
        public uint nameStart, nameStart2;
        public uint flags;
        public Vector3 v1;
        public Vector3 v2;
        public UInt16 batchesA;
        public UInt16 batchesB;
        public UInt16 batchesC;
        public UInt16 batchesD;
        public UInt16 portalStart;
        public UInt16 portalCount;
        public uint id;

        public uint nVertices;
        public float[] vertices; // 3 per vertex

        public uint nTriangles;
        public UInt16[] triangles; // 3 per triangle
        public UInt16[] materials;  // 1 per triangle

        public const UInt16 MAT_FLAG_NOCAMCOLLIDE = 0x001;
        public const UInt16 MAT_FLAG_DETAIL = 0x002;
        public const UInt16 MAT_FLAG_COLLISION = 0x004;
        public const UInt16 MAT_FLAG_HINT = 0x008;
        public const UInt16 MAT_FLAG_RENDER = 0x010;
        public const UInt16 MAT_FLAG_COLLIDE_HIT = 0x020;
    }

    internal class WDT
    {
        public const int SIZE = 64;

        public readonly bool[] maps = new bool[SIZE * SIZE];
        public readonly MapTile[] maptiles = new MapTile[SIZE * SIZE];
        public readonly bool[] loaded = new bool[SIZE * SIZE];
        public WMOInstance[] gwmois = Array.Empty<WMOInstance>();
    }

    internal class WDTFile
    {
        private readonly ILogger logger;
        private readonly DataConfig dataConfig;
        private readonly WMOManager wmomanager;
        private readonly ModelManager modelmanager;
        private readonly WDT wdt;
        private readonly StormDll.ArchiveSet archive;

        private readonly string pathName;

        public bool loaded;

        public WDTFile(StormDll.ArchiveSet archive, float mapId, WDT wdt, WMOManager wmomanager, ModelManager modelmanager, ILogger logger, DataConfig dataConfig)
        {
            this.logger = logger;
            this.dataConfig = dataConfig;
            this.pathName = ContinentDB.IdToName[mapId];

            this.wdt = wdt;
            this.wmomanager = wmomanager;
            this.modelmanager = modelmanager;
            this.archive = archive;

            string wdtfile = Path.Join("World", "Maps", pathName, pathName + ".wdt");
            string tempFile = Path.Join(dataConfig.PPather, $"{Unique.Index++}.tmp"); //wdt
            archive.SFileExtractFile(wdtfile, tempFile);
            if (!File.Exists(tempFile))
                return;

            using Stream stream = File.OpenRead(tempFile);
            using BinaryReader file = new(stream);

            List<string> gwmos = new();

            do
            {
                uint type = file.ReadUInt32();
                uint size = file.ReadUInt32();
                long curpos = file.BaseStream.Position;

                if (type == ChunkReader.MVER)
                {
                }
                else if (type == ChunkReader.MPHD)
                {
                }
                else if (type == ChunkReader.MODF)
                {
                    HandleMODF(file, wdt, gwmos, wmomanager, size);
                }
                else if (type == ChunkReader.MWMO)
                {
                    HandleMWMO(file, gwmos, size);
                }
                else if (type == ChunkReader.MAIN)
                {
                    HandleMAIN(file, size);
                }
                else
                {
                    logger.LogWarning("WDT Unknown " + type);
                }
                file.BaseStream.Seek(curpos + size, SeekOrigin.Begin);

            } while (file.BaseStream.Position < file.BaseStream.Length);

            loaded = true;
        }

        public void LoadMapTile(int x, int y, int index)
        {
            if (!wdt.maps[index])
                return;

            string filename = Path.Join("World", "Maps", pathName, $"{pathName}_{x}_{y}.adt");
            string tempFile = Path.Join(dataConfig.PPather, $"{Unique.Index++}.tmp"); //adt
            archive.SFileExtractFile(filename, tempFile);
            if (!File.Exists(tempFile))
                return;

            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace($"Reading adt: {filename}");

            wdt.maptiles[index] = MapTileFile.Read(tempFile, wmomanager, modelmanager);
            wdt.loaded[index] = true;
        }

        private static void HandleMWMO(BinaryReader file, List<string> gwmos, uint size)
        {
            if (size != 0)
            {
                int l = 0;
                byte[] raw = file.ReadBytes((int)size);
                while (l < size)
                {
                    string s = ChunkReader.ExtractString(raw, l);
                    l += s.Length + 1;
                    gwmos.Add(s);
                }
            }
        }

        private static void HandleMODF(BinaryReader file, WDT wdt, List<string> gwmos, WMOManager wmomanager, uint size)
        {
            // global wmo instance data
            int gnWMO = (int)size / 64;
            wdt.gwmois = new WMOInstance[gnWMO];

            for (uint i = 0; i < gnWMO; i++)
            {
                int id = file.ReadInt32();
                string path = gwmos[id];

                WMO wmo = wmomanager.AddAndLoadIfNeeded(path);
                wdt.gwmois[i] = new(file, wmo);
            }
        }

        private void HandleMAIN(BinaryReader file, uint size)
        {
            // global map objects
            for (int y = 0; y < WDT.SIZE; y++)
            {
                for (int x = 0; x < WDT.SIZE; x++)
                {
                    int index = y * WDT.SIZE + x;

                    int d = file.ReadInt32();
                    wdt.maps[index] = d != 0;

                    file.ReadInt32(); // kasta
                }
            }
        }
    }

    internal readonly struct MapChunk
    {
        public readonly float xbase, ybase, zbase;
        public readonly uint areaID;
        public readonly bool haswater;
        public readonly bool hasholes;
        public readonly uint holes;
        //public float waterlevel;

        //  0   1   2   3   4   5   6   7   8
        //    9  10  11  12  13  14  15  16
        // 17  18  19  20  21  22  23  24  25
        // ...
        public readonly float[] vertices;

        public readonly float water_height1;
        public readonly float water_height2;
        public readonly float[] water_height;
        public readonly byte[] water_flags;


        private static readonly int[] holetab_h = new int[] { 0x1111, 0x2222, 0x4444, 0x8888 };
        private static readonly int[] holetab_v = new int[] { 0x000F, 0x00F0, 0x0F00, 0xF000 };

        // 0 ..3, 0 ..3
        public bool isHole(int i, int j)
        {
            if (!hasholes)
                return false;
            i /= 2;
            j /= 2;
            if (i > 3 || j > 3)
                return false;

            return (holes & holetab_h[i] & holetab_v[j]) != 0;
        }


        public MapChunk(float xbase, float ybase, float zbase,
            uint areaID, bool haswater, uint holes, float[] vertices,
            float water_height1, float water_height2, float[] water_height, byte[] water_flags)
        {
            this.xbase = xbase;
            this.ybase = ybase;
            this.zbase = zbase;
            this.areaID = areaID;
            this.haswater = haswater;
            this.holes = holes;
            this.hasholes = holes != 0;
            this.vertices = vertices;

            this.water_height1 = water_height1;
            this.water_height2 = water_height2;
            this.water_height = water_height;
            this.water_flags = water_flags;
        }
    }

    internal readonly struct MapTile
    {
        public const int SIZE = 16;

        public readonly ModelInstance[] modelis;
        public readonly WMOInstance[] wmois;

        public readonly MapChunk[] chunks;
        public readonly bool[] hasChunk;

        public MapTile(ModelInstance[] modelis, WMOInstance[] wmois, MapChunk[] chunks, bool[] hasChunk)
        {
            this.modelis = modelis;
            this.wmois = wmois;
            this.chunks = chunks;
            this.hasChunk = hasChunk;
        }
    }

    internal static class MapTileFile // adt file
    {
        public static readonly MH2OData1 eMH2OData1 = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public static ref readonly MH2OData1 EmptyMH2OData1 => ref eMH2OData1;

        public static readonly LiquidData eLiquidData = new(0, 0, 0, EmptyMH2OData1, Array.Empty<float>(), Array.Empty<byte>());
        public static ref readonly LiquidData EmptyLiquidData => ref eLiquidData;

        public static MapTile Read(string name, WMOManager wmomanager, ModelManager modelmanager)
        {
            LiquidData[] LiquidDataChunk = Array.Empty<LiquidData>();
            int[] mcnk_offsets = new int[MapTile.SIZE * MapTile.SIZE];
            int[] mcnk_sizes = new int[MapTile.SIZE * MapTile.SIZE];

            List<string> models = new();
            List<string> wmos = new();

            using Stream stream = File.OpenRead(name);
            using BinaryReader file = new(stream);

            WMOInstance[] wmois = Array.Empty<WMOInstance>();
            ModelInstance[] modelis = Array.Empty<ModelInstance>();

            MapChunk[] chunks = new MapChunk[MapTile.SIZE * MapTile.SIZE];
            bool[] hasChunk = new bool[chunks.Length];

            do
            {
                uint type = file.ReadUInt32();
                uint size = file.ReadUInt32();
                long curpos = file.BaseStream.Position;

                if (type == ChunkReader.MCIN)
                    HandleMCIN(file, mcnk_offsets, mcnk_sizes);
                else if (type == ChunkReader.MMDX && size != 0)
                    HandleMMDX(file, models, size);
                else if (type == ChunkReader.MWMO && size != 0)
                    HandleMWMO(file, wmos, size);
                else if (type == ChunkReader.MDDF)
                    HandleMDDF(file, modelmanager, models, size, out modelis);
                else if (type == ChunkReader.MODF)
                    HandleMODF(file, wmos, wmomanager, size, out wmois);
                else if (type == ChunkReader.MH2O)
                    HandleMH2O(file, out LiquidDataChunk);

                file.BaseStream.Seek(curpos + size, SeekOrigin.Begin);
            } while (file.BaseStream.Position < file.BaseStream.Length);

            for (int y = 0; y < MapTile.SIZE; y++)
            {
                for (int x = 0; x < MapTile.SIZE; x++)
                {
                    int index = y * MapTile.SIZE + x;
                    int off = mcnk_offsets[index];
                    file.BaseStream.Seek(off, SeekOrigin.Begin);

                    chunks[index] = ReadMapChunk(file, LiquidDataChunk.Length > 0 ? LiquidDataChunk[index] : EmptyLiquidData);
                    hasChunk[index] = true;
                }
            }

            return new(modelis, wmois, chunks, hasChunk);
        }

        public readonly struct LiquidData
        {
            public const int SIZE = 256;
            public const int HEIGHT_SIZE = 9;
            public const int FLAG_SIZE = 8;

            public readonly uint offsetData1;
            public readonly int used;
            public readonly uint offsetData2;

            public readonly MH2OData1 data1;

            public readonly float[] water_height;
            public readonly byte[] water_flags;

            public LiquidData(uint offsetData1, int used, uint offsetData2,
                MH2OData1 data1, float[] water_height, byte[] water_flags)
            {
                this.offsetData1 = offsetData1;
                this.used = used;
                this.offsetData2 = offsetData2;
                this.data1 = data1;

                this.water_height = water_height;
                this.water_flags = water_flags;
            }
        }

        public readonly struct MH2OData1
        {
            public readonly UInt16 flags;   //0x1 might mean there is a height map @ data2b ??
            public readonly UInt16 type;    //0 = normal/lake, 1 = lava, 2 = ocean
            public readonly float heightLevel1;
            public readonly float heightLevel2;
            public readonly byte xOffset;
            public readonly byte yOffset;
            public readonly byte Width;
            public readonly byte Height;
            public readonly uint offsetData2a;
            public readonly uint offsetData2b;

            public MH2OData1(UInt16 flags, UInt16 type, float heightLevel1,
                float heightLevel2, byte xOffet, byte yOffset, byte width, byte height,
                uint offsetData2a, uint offsetData2b)
            {
                this.flags = flags;
                this.type = type;
                this.heightLevel1 = heightLevel1;
                this.heightLevel2 = heightLevel2;
                this.xOffset = xOffet;
                this.yOffset = yOffset;
                this.Width = width;
                this.Height = height;
                this.offsetData2a = offsetData2a;
                this.offsetData2b = offsetData2b;
            }
        }

        private static void HandleMH2O(BinaryReader file, out LiquidData[] liquidData)
        {
            liquidData = new LiquidData[LiquidData.SIZE];

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
                    data1 = new MH2OData1
                    (
                        file.ReadUInt16(),
                        file.ReadUInt16(),
                        file.ReadSingle(),
                        file.ReadSingle(),
                        file.ReadByte(),
                        file.ReadByte(),
                        file.ReadByte(),
                        file.ReadByte(),
                        file.ReadUInt32(),
                        file.ReadUInt32()
                    );

                    file.BaseStream.Seek(lastPos, SeekOrigin.Begin);
                }

                float[] water_height = new float[LiquidData.HEIGHT_SIZE * LiquidData.HEIGHT_SIZE];
                byte[] water_flags = new byte[LiquidData.FLAG_SIZE * LiquidData.FLAG_SIZE];

                if ((used & 1) == 1 && offsetData1 != 0 && data1.offsetData2b != 0 && (data1.flags & 1) == 1)
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

        private static void HandleMCIN(BinaryReader file, int[] mcnk_offsets, int[] mcnk_sizes)
        {
            for (int i = 0; i < mcnk_offsets.Length; i++)
            {
                mcnk_offsets[i] = file.ReadInt32();
                mcnk_sizes[i] = file.ReadInt32();
                file.ReadInt32(); // crap
                file.ReadInt32();// crap
            }
        }

        private static void HandleMMDX(BinaryReader file, List<string> models, uint size)
        {
            int l = 0;
            byte[] raw = file.ReadBytes((int)size);
            while (l < size)
            {
                string s = ChunkReader.ExtractString(raw, l);
                l += s.Length + 1;

                models.Add(s);
            }
        }

        private static void HandleMWMO(BinaryReader file, List<string> wmos, uint size)
        {
            int l = 0;
            byte[] raw = file.ReadBytes((int)size);
            while (l < size)
            {
                string s = ChunkReader.ExtractString(raw, l);
                l += s.Length + 1;

                wmos.Add(s);
            }
        }

        private static void HandleMDDF(BinaryReader file, ModelManager modelmanager, List<string> models, uint size, out ModelInstance[] modelis)
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

        private static void HandleMODF(BinaryReader file, List<string> wmos, WMOManager wmomanager, uint size, out WMOInstance[] wmois)
        {
            int nWMO = (int)size / 64;
            wmois = new WMOInstance[nWMO];

            for (int i = 0; i < nWMO; i++)
            {
                int id = file.ReadInt32();
                WMO wmo = wmomanager.AddAndLoadIfNeeded(wmos[id]);

                wmois[i] = new(file, wmo);
            }
        }

        /* MapChunk */

        private static MapChunk ReadMapChunk(BinaryReader file, in LiquidData liquidData)
        {
            // Read away Magic and size
            _ = file.ReadUInt32(); // uint crap_head
            _ = file.ReadUInt32(); // uint crap_size

            // Each map chunk has 9x9 vertices,
            // and in between them 8x8 additional vertices, several texture layers, normal vectors, a shadow map, etc.

            _ = file.ReadUInt32(); // uint flags
            _ = file.ReadUInt32(); // uint ix
            _ = file.ReadUInt32(); // uint iy
            _ = file.ReadUInt32(); // uint nLayers
            _ = file.ReadUInt32(); // uint nDoodadRefs
            _ = file.ReadUInt32(); // uint ofsHeight
            _ = file.ReadUInt32(); // uint ofsNormal
            _ = file.ReadUInt32(); // uint ofsLayer
            _ = file.ReadUInt32(); // uint ofsRefs
            _ = file.ReadUInt32(); // uint ofsAlpha
            _ = file.ReadUInt32(); // uint sizeAlpha
            _ = file.ReadUInt32(); // uint ofsShadow
            _ = file.ReadUInt32(); // uint sizeShadow

            uint areaID = file.ReadUInt32();
            _ = file.ReadUInt32(); // uint nMapObjRefs
            uint holes = file.ReadUInt32();

            _ = file.ReadUInt16(); // ushort s1
            _ = file.ReadUInt16(); // ushort s2
            _ = file.ReadUInt32(); // uint d1
            _ = file.ReadUInt32(); // uint d2
            _ = file.ReadUInt32(); // uint d3
            _ = file.ReadUInt32(); // uint predTex
            _ = file.ReadUInt32(); // uint nEffectDoodad
            _ = file.ReadUInt32(); // uint ofsSndEmitters 
            _ = file.ReadUInt32(); // uint nSndEmitters
            _ = file.ReadUInt32(); // uint ofsLiquid

            uint sizeLiquid = file.ReadUInt32();
            float zpos = file.ReadSingle();
            float xpos = file.ReadSingle();
            float ypos = file.ReadSingle();

            _ = file.ReadUInt32(); // uint textureId
            _ = file.ReadUInt32(); // uint props 
            _ = file.ReadUInt32(); // uint effectId

            float xbase = -xpos + ChunkReader.ZEROPOINT;
            float ybase = ypos;
            float zbase = -zpos + ChunkReader.ZEROPOINT;

            float[] vertices = new float[3 * ((9 * 9) + (8 * 8))];

            bool haswater = false;
            float water_height1 = 0;
            float water_height2 = 0;
            float[] water_height = new float[LiquidData.HEIGHT_SIZE * LiquidData.HEIGHT_SIZE];
            byte[] water_flags = new byte[LiquidData.FLAG_SIZE * LiquidData.FLAG_SIZE];

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
                        haswater = true;
                        HandleChunkMCLQ(file, out water_height1, out water_height2, water_height, water_flags);
                    }
                }

                file.BaseStream.Seek(curpos + size, SeekOrigin.Begin);
            } while (file.BaseStream.Position < file.BaseStream.Length);

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
                water_height, water_flags);
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

            for (int y = 0; y < LiquidData.HEIGHT_SIZE; y++)
            {
                for (int x = 0; x < LiquidData.HEIGHT_SIZE; x++)
                {
                    int index = y * LiquidData.HEIGHT_SIZE + x;
                    _ = file.ReadUInt32();
                    water_height[index] = file.ReadSingle();
                }
            }

            for (int y = 0; y < LiquidData.FLAG_SIZE; y++)
            {
                for (int x = 0; x < LiquidData.FLAG_SIZE; x++)
                {
                    int index = y * LiquidData.FLAG_SIZE + x;
                    water_flags[index] = file.ReadByte();
                }
            }
        }
    }

    internal class WmoRootFile
    {
        public WmoRootFile(string name, WMO wmo, ModelManager modelmanager)
        {
            using Stream stream = File.OpenRead(name);
            using BinaryReader file = new(stream);

            do
            {
                uint type = file.ReadUInt32();
                uint size = file.ReadUInt32();
                long curpos = file.BaseStream.Position;

                if (type == ChunkReader.MOHD)
                {
                    HandleMOHD(file, wmo, size);
                }
                else if (type == ChunkReader.MOGI)
                {
                    HandleMOGI(file, wmo, size);
                }
                else if (type == ChunkReader.MODS)
                {
                    HandleMODS(file, wmo);
                }
                else if (type == ChunkReader.MODD)
                {
                    HandleMODD(file, wmo, modelmanager, size);
                }
                else if (type == ChunkReader.MODN)
                {
                    HandleMODN(file, wmo, size);
                }

                file.BaseStream.Seek(curpos + size, SeekOrigin.Begin);
            } while (file.BaseStream.Position != file.BaseStream.Length);
        }

        private static void HandleMOHD(BinaryReader file, WMO wmo, uint size)
        {
            file.ReadUInt32(); // uint nTextures
            uint nGroups = file.ReadUInt32();
            file.ReadUInt32(); // uint nP
            file.ReadUInt32(); // uint nLights
            wmo.nModels = file.ReadUInt32();
            wmo.nDoodads = file.ReadUInt32();
            wmo.nDoodadSets = file.ReadUInt32();

            file.ReadUInt32(); //uint col
            file.ReadUInt32(); //uint nX

            wmo.v1 = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            wmo.v2 = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());

            wmo.groups = new WMOGroup[nGroups];
        }

        private static void HandleMODS(BinaryReader file, WMO wmo)
        {
            wmo.doodads = new DoodadSet[wmo.nDoodadSets];
            for (int i = 0; i < wmo.nDoodadSets; i++)
            {
                file.ReadBytes(20); // byte[] name
                wmo.doodads[i].firstInstance = file.ReadUInt32();
                wmo.doodads[i].nInstances = file.ReadUInt32();
                file.ReadUInt32();
            }
        }

        private static void HandleMODD(BinaryReader file, WMO wmo, ModelManager modelmanager, uint size)
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
            for (int i = 0; i < sets /*wmo.nDoodads*/; i++)
            {
                byte[] s = file.ReadBytes(4);
                s[3] = 0;
                uint nameOffsetInMODN = BitConverter.ToUInt32(s, 0); // 0x00
                float posx = file.ReadSingle(); // 0x04
                float posz = file.ReadSingle(); // 0x08
                float posy = -file.ReadSingle(); // 0x0c

                float quatw = file.ReadSingle(); // 0x10

                float quatx = file.ReadSingle(); // 0x14
                float quaty = file.ReadSingle(); // 0x18
                float quatz = file.ReadSingle();// 0x1c

                float scale = file.ReadSingle(); // 0x20

                file.ReadUInt32(); // lighning crap 0x24
                                   // float last = file.ReadSingle(); // 0x28

                string name = ChunkReader.ExtractString(wmo.MODNraw, (int)nameOffsetInMODN);
                Model m = modelmanager.AddAndLoadIfNeeded(name);

                Vector3 pos = new(posx, posy, posz);
                Vector3 dir = new(quatz, quaty, quatz);

                ModelInstance mi = new(m, pos, dir, scale, quatw);
                wmo.doodadInstances[i] = mi;
            }
        }

        private static void HandleMODN(BinaryReader file, WMO wmo, uint size)
        {
            wmo.MODNraw = file.ReadBytes((int)size);
            // List of filenames for M2 (mdx) models that appear in this WMO.
        }

        private static void HandleMOGI(BinaryReader file, WMO wmo, uint size)
        {
            for (int i = 0; i < wmo.groups.Length; i++)
            {
                WMOGroup g = new();
                wmo.groups[i] = g;

                g.flags = file.ReadUInt32();
                float f0 = file.ReadSingle();
                float f1 = file.ReadSingle();
                float f2 = file.ReadSingle();
                g.v1 = new Vector3(f0, f1, f2);

                float f3 = file.ReadSingle();
                float f4 = file.ReadSingle();
                float f5 = file.ReadSingle();
                g.v2 = new Vector3(f3, f4, f5);

                uint nameOfs = file.ReadUInt32();
            }
        }
    }

    internal class WmoGroupFile
    {
        public WmoGroupFile(WMOGroup g, string name)
        {
            using Stream stream = File.OpenRead(name);
            using BinaryReader file = new(stream);

            file.BaseStream.Seek(0x14, SeekOrigin.Begin);
            HandleMOGP(file, g, 11);

            file.BaseStream.Seek(0x58, SeekOrigin.Begin);// first chunk

            do
            {
                uint type = file.ReadUInt32();
                uint size = file.ReadUInt32();
                long curpos = file.BaseStream.Position;
                if (type == ChunkReader.MOPY)
                {
                    HandleMOPY(file, g, size);
                }
                else if (type == ChunkReader.MOVI)
                {
                    HandleMOVI(file, g, size);
                }
                else if (type == ChunkReader.MOVT)
                {
                    HandleMOVT(file, g, size);
                }

                file.BaseStream.Seek(curpos + size, SeekOrigin.Begin);
            } while (file.BaseStream.Position != file.BaseStream.Length);
        }

        private static void HandleMOPY(BinaryReader file, WMOGroup g, uint size)
        {
            g.nTriangles = size / 2;
            // materials
            /*  0x01 - inside small houses and paths leading indoors
			 *  0x02 - ???
			 *  0x04 - set on indoor things and ruins
			 *  0x08 - ???
			 *  0x10 - ???
			 *  0x20 - Always set?
			 *  0x40 - sometimes set-
			 *  0x80 - ??? never set
			 *
			 */

            g.materials = new ushort[g.nTriangles];

            for (int i = 0; i < g.nTriangles; i++)
            {
                g.materials[i] = file.ReadUInt16();
            }
        }

        private static void HandleMOVI(BinaryReader file, WMOGroup g, uint size)
        {
            //indicesFileMarker = file.BaseStream.Position;
            g.triangles = new UInt16[g.nTriangles * 3];
            for (uint i = 0; i < g.nTriangles; i++)
            {
                uint off = i * 3;
                g.triangles[off + 0] = file.ReadUInt16();
                g.triangles[off + 1] = file.ReadUInt16();
                g.triangles[off + 2] = file.ReadUInt16();
            }
        }

        private static void HandleMOVT(BinaryReader file, WMOGroup g, uint size)
        {
            g.nVertices = size / 12;
            // let's hope it's padded to 12 bytes, not 16...
            g.vertices = new float[g.nVertices * 3];
            for (uint i = 0; i < g.nVertices; i++)
            {
                uint off = i * 3;
                g.vertices[off + 0] = file.ReadSingle();
                g.vertices[off + 1] = file.ReadSingle();
                g.vertices[off + 2] = file.ReadSingle();
            }
        }

        private static void HandleMOGP(BinaryReader file, WMOGroup g, uint size)
        {
            g.nameStart = file.ReadUInt32();
            g.nameStart2 = file.ReadUInt32();
            g.flags = file.ReadUInt32();

            g.v1 = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
            g.v2 = new Vector3(file.ReadSingle(), file.ReadSingle(), file.ReadSingle());

            g.portalStart = file.ReadUInt16();
            g.portalCount = file.ReadUInt16();
            g.batchesA = file.ReadUInt16();
            g.batchesB = file.ReadUInt16();
            g.batchesC = file.ReadUInt16();
            g.batchesD = file.ReadUInt16();

            file.ReadUInt32(); // uint fogCrap

            file.ReadUInt32(); // uint unknown1 
            g.id = file.ReadUInt32();
            file.ReadUInt32(); // uint unknown2
            file.ReadUInt32(); // uint unknown3
        }
    }
}