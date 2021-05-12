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

using PatherPath;
using System;
using System.Linq;
using System.Collections.Generic;
using Wmo;
using System.IO;

namespace WowTriangles
{
    public class MPQTriangleSupplier : TriangleSupplier
    {
        private string continentFile;

        private StormDll.ArchiveSet archive;

        private int sequence = 0;

        //TriangleSet global_triangles = new TriangleSet();

        private WDT wdt;
        private WDTFile wdtf;
        private ModelManager modelmanager;
        private WMOManager wmomanager;
        private DataConfig dataConfig;

        private Dictionary<String, int> zoneToMapId = new Dictionary<string, int>();
        private Dictionary<int, String> mapIdToFile = new Dictionary<int, string>();

        private Dictionary<int, String> areaIdToName = new Dictionary<int, string>();

        public override void Close()
        {
            archive.Close();
            wdt = null;
            wdtf = null;
            modelmanager = null;
            wmomanager = null;
            zoneToMapId = null;
            mapIdToFile = null;
            areaIdToName = null;
            archive = null;
            base.Close();
        }

        private Logger logger;

        public MPQTriangleSupplier(Logger logger, DataConfig dataConfig)
        {
            this.logger = logger;
            this.dataConfig = dataConfig;

            string[] archiveNames = GetArchiveNames(dataConfig, s => logger.WriteLine(s));

            archive = new StormDll.ArchiveSet(this.logger);
            archive.AddArchives(archiveNames);
            modelmanager = new ModelManager(archive, 80, dataConfig);
            wmomanager = new WMOManager(archive, modelmanager, 30, dataConfig);

            var path = Path.Join(dataConfig.PPather, "AreaTable.dbc");

            archive.ExtractFile("..\\PathingAPI\\DBFilesClient\\AreaTable.dbc", path);
            DBC areas = new DBC();
            DBCFile af = new DBCFile(path, areas, this.logger);
            for (int i = 0; i < areas.recordCount; i++)
            {
                int AreaID = (int)areas.GetUint(i, 0);  //0 	 uint 	 AreaID
                int WorldID = (int)areas.GetUint(i, 1); //1 	uint 	Continent (refers to a WorldID)
                int Parent = (int)areas.GetUint(i, 2); //2 	uint 	Region (refers to an AreaID)
                string Name = areas.GetString(i, 11);

                areaIdToName.Add(AreaID, Name);
            }

            for (int i = 0; i < areas.recordCount; i++)
            {
                int AreaID = (int)areas.GetUint(i, 0);
                int WorldID = (int)areas.GetUint(i, 1);
                int Parent = (int)areas.GetUint(i, 2);
                string Name = areas.GetString(i, 11);

                string TotalName = "";
                //areaIdToName.Add(AreaID, Name);
                //areaIdParent.Add(AreaID, Parent);
                string ParentName = "";
                if (!areaIdToName.TryGetValue(Parent, out ParentName))
                {
                    TotalName = ":" + Name;
                }
                else
                    TotalName = Name + ":" + ParentName;
                try
                {
                    zoneToMapId.Add(TotalName, WorldID);
                    //logger.WriteLine(TotalName + " => " + WorldID);
                }
                catch
                {
                    int id;
                    zoneToMapId.TryGetValue(TotalName, out id);
                    ////  logger.WriteLine("Duplicate: " + TotalName + " " + WorldID +" " + id);
                }
                //0 	 uint 	 AreaID
                //1 	uint 	Continent (refers to a WorldID)
                //2 	uint 	Region (refers to an AreaID)
            }
        }

        public static string[] GetArchiveNames(DataConfig dataConfig, Action<string> log)
        {
            string[] archiveNames = {
                    "patch.MPQ",
                    "enUS\\patch-enUS.MPQ",
                    "enGB\\patch-enGB.MPQ",
                    "lichking.MPQ",
                    "common-2.MPQ",
                    "common.MPQ",
                    "expansion.MPQ",
                    "enUS\\lichking-locale-enUS.MPQ", "enUS\\locale-enUS.MPQ", "enUS\\expansion-locale-enUS.MPQ",
                    "enGB\\lichking-locale-enGB.MPQ", "enGB\\locale-enGB.MPQ", "enGB\\expansion-locale-enGB.MPQ",
                    "enUS\\base-enUS.MPQ",
                    "enGB\\base-enGB.MPQ",
                    "enUS\\backup-enUS.MPQ",
                    "enGB\\backup-enGB.MPQ",
                    "expansion1.MPQ",
                    "expansion2.MPQ",
                    "OldWorld.MPQ",
                    "world.MPQ",
                    "enGB\\expansion1-locale-enGB.MPQ",
                    "enGB\\expansion2-locale-enGB.MPQ",
                    "enGB\\locale-enGB.MPQ",
                    "enGB\\locale-enGB.MPQ",

                    "wow-update-13164.MPQ",
                    "wow-update-oldworld-13154.MPQ",
                    "Cache\\patch-base-oldworld-13154.MPQ",
                    "Cache\\enGB\\patch-enGB-oldworld-13154.MPQ",
                    "expansion2.MPQ","expansion1.MPQ",
                    "world.MPQ",
                    "sound.MPQ",
                    "art.MPQ",
                    "enGB\\expansion2-locale-enGB.MPQ",
                    "enGB\\expansion1-locale-enGB.MPQ",
                    "enGB\\locale-enGB.MPQ",
                    "enGB\\base-enGB.MPQ"
                                    };

            string regGameDir = dataConfig.MPQ;
            log("MPQ dir is " + regGameDir);
            return archiveNames.Select(a => Path.Join(regGameDir, a)).ToArray();
        }

        public void SetContinent(string continent)
        {
            if (continent == null) {
                return;
            }

            continentFile = continent;

            wdt = new WDT();

            wdtf = new WDTFile(archive, continentFile, wdt, wmomanager, modelmanager, this.logger, dataConfig);
            if (!wdtf.loaded)
            {
                wdt = null; // bad
                throw new Exception("Failed to set continent to: " + continent);
            }
            else
            {
                // logger.WriteLine("  global Objects " + wdt.gwmois.Count + " Models " + wdt.gwmois.Count);
                //global_triangles.color = new float[3] { 0.8f, 0.8f, 1.0f };
            }
        }

        public string SetZone(string zone)
        {
            int continentID;

            if (!zoneToMapId.TryGetValue(zone, out continentID))
            {
                int colon = zone.IndexOf(":");
                if (colon == -1)
                    return null;
                zone = zone.Substring(colon);
                if (!zoneToMapId.TryGetValue(zone, out continentID))
                {
                    return null;
                }
            }

            var path = Path.Join(dataConfig.PPather, "Map.dbc");

            archive.ExtractFile("..\\PathingAPI\\DBFilesClient\\Map.dbc", "PPather\\Map.dbc");
            DBC maps = new DBC();
            DBCFile mf = new DBCFile(path, maps, this.logger);

            for (int i = 0; i < maps.recordCount; i++)
            {
                int mapID = maps.GetInt(i, 0);
                logger.WriteLine("   ID:" + maps.GetInt(i, 0));
                logger.WriteLine(" File: " + maps.GetString(i, 1));
                logger.WriteLine(" Name: " + maps.GetString(i, 4)); // the file!!!

                if (mapID == continentID) // file == continentFile)
                {
                    //  logger.WriteLine(String.Format("{0,4} {1}", mapID, maps.GetString(i, 1)));
                    string file = maps.GetString(i, 1);
                    SetContinent(file);
                    return continentFile;
                }
            }
            if (wdt == null)
            {
                return "Failed to open file files for continent ID" + continentID;
            }
            return null;
        }

        private void GetChunkData(TriangleCollection triangles, int chunk_x, int chunk_y, SparseMatrix3D<WMO> instances)
        {
            if (chunk_x < 0)
                return;
            if (chunk_y < 0)
                return;
            if (chunk_x > 63)
                return;
            if (chunk_y > 63)
                return;

            if (triangles == null)
                return;

            if (wdtf == null)
                return;
            if (wdt == null)
                return;
            wdtf.LoadMapTile(chunk_x, chunk_y);

            MapTile t = wdt.maptiles[chunk_x, chunk_y];
            if (t != null)
            {
                //System.Diagnostics.Debug.Write(" render");
                // Map tiles
                for (int ci = 0; ci < 16; ci++)
                {
                    for (int cj = 0; cj < 16; cj++)
                    {
                        MapChunk c = t.chunks[ci, cj];
                        if (c != null)
                            AddTriangles(triangles, c);
                    }
                }

                // World objects

                foreach (WMOInstance wi in t.wmois)
                {
                    if (wi != null && wi.wmo != null)
                    {
                        String fn = wi.wmo.fileName;
                        int last = fn.LastIndexOf('\\');
                        fn = fn.Substring(last + 1);
                        // logger.WriteLine("    wmo: " + fn + " at " + wi.pos);
                        if (fn != null)
                        {
                            WMO old = instances.Get((int)wi.pos.x, (int)wi.pos.y, (int)wi.pos.z);
                            if (old == wi.wmo)
                            {
                                //logger.WriteLine("Already got " + fn);
                            }
                            else
                            {
                                instances.Set((int)wi.pos.x, (int)wi.pos.y, (int)wi.pos.z, wi.wmo);
                                AddTriangles(triangles, wi);
                            }
                        }
                    }
                }

                foreach (ModelInstance mi in t.modelis)
                {
                    if (mi != null && mi.model != null)
                    {
                        String fn = mi.model.fileName;
                        int last = fn.LastIndexOf('\\');
                        // fn = fn.Substring(last + 1);
                        //logger.WriteLine("    wmi: " + fn + " at " + mi.pos);
                        AddTriangles(triangles, mi);

                        //logger.WriteLine("    model: " + fn);
                    }
                }

                logger.WriteLine("wee");
                /*logger.WriteLine(
					String.Format(" Triangles - Map: {0,6} Objects: {1,6} Models: {2,6}",
								  map_triangles.GetNumberOfTriangles(),
								  obj_triangles.GetNumberOfTriangles(),
								  model_triangles.GetNumberOfTriangles()));
				*/
            }
            logger.WriteLine(" done");
            wdt.maptiles[chunk_x, chunk_y] = null; // clear it atain
                                                   //myChunk.triangles.ClearVertexMatrix(); // not needed anymore
                                                   //return myChunk;
        }

        private void GetChunkCoord(float x, float y, out int chunk_x, out int chunk_y)
        {
            // yeah, this is ugly. But safe
            for (chunk_x = 0; chunk_x < 64; chunk_x++)
            {
                float max_y = ChunkReader.ZEROPOINT - (float)(chunk_x) * ChunkReader.TILESIZE;
                float min_y = max_y - ChunkReader.TILESIZE;
                if (y >= min_y - 0.1f && y < max_y + 0.1f)
                    break;
            }
            for (chunk_y = 0; chunk_y < 64; chunk_y++)
            {
                float max_x = ChunkReader.ZEROPOINT - (float)(chunk_y) * ChunkReader.TILESIZE;
                float min_x = max_x - ChunkReader.TILESIZE;
                if (x >= min_x - 0.1f && x < max_x + 0.1f)
                    break;
            }
            if (chunk_y == 64 || chunk_x == 64)
            {
                logger.WriteLine(x + " " + y + " is at " + chunk_x + " " + chunk_y);
                //GetChunkCoord(x, y, out chunk_x, out chunk_y);
            }
        }

        public override void GetTriangles(TriangleCollection to, float min_x, float min_y, float max_x, float max_y)
        {
            //logger.WriteLine("TotalMemory " + System.GC.GetTotalMemory(false)/(1024*1024) + " MB");
            foreach (WMOInstance wi in wdt.gwmois)
            {
                AddTriangles(to, wi);
            }
            SparseMatrix3D<WMO> instances = new SparseMatrix3D<WMO>();
            for (float x = min_x; x < max_x; x += ChunkReader.TILESIZE)
            {
                for (float y = min_y; y < max_y; y += ChunkReader.TILESIZE)
                {
                    int chunk_x, chunk_y;
                    GetChunkCoord(x, y, out chunk_x, out chunk_y);
                    /*ChunkData d = */
                    GetChunkData(to, chunk_x, chunk_y, instances);

                    //to.AddAllTrianglesFrom(d.triangles);
                }
            }
        }

        private void AddTriangles(TriangleCollection s, MapChunk c)
        {
            int[,] vertices = new int[9, 9];
            int[,] verticesMid = new int[8, 8];

            for (int row = 0; row < 9; row++)
                for (int col = 0; col < 9; col++)
                {
                    float x, y, z;
                    ChunkGetCoordForPoint(c, row, col, out x, out y, out z);
                    int index = s.AddVertex(x, y, z);
                    vertices[row, col] = index;
                }

            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 8; col++)
                {
                    float x, y, z;
                    ChunkGetCoordForMiddlePoint(c, row, col, out x, out y, out z);
                    int index = s.AddVertex(x, y, z);
                    verticesMid[row, col] = index;
                }
            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    if (!c.isHole(col, row))
                    {
                        int v0 = vertices[row, col];
                        int v1 = vertices[row + 1, col];
                        int v2 = vertices[row + 1, col + 1];
                        int v3 = vertices[row, col + 1];
                        int vMid = verticesMid[row, col];

                        s.AddTriangle(v0, v1, vMid);
                        s.AddTriangle(v1, v2, vMid);
                        s.AddTriangle(v2, v3, vMid);
                        s.AddTriangle(v3, v0, vMid);
                    }
                }
            }

            if (c.haswater)
            {
                // paint the water
                for (int row = 0; row < 9; row++)
                    for (int col = 0; col < 9; col++)
                    {
                        float x, y, z;
                        ChunkGetCoordForPoint(c, row, col, out x, out y, out z);
                        float height = c.water_height[row, col] - 1.5f;
                        int index = s.AddVertex(x, y, height);
                        vertices[row, col] = index;
                    }
                for (int row = 0; row < 8; row++)
                {
                    for (int col = 0; col < 8; col++)
                    {
                        if (c.water_flags[row, col] != 0xf)
                        {
                            int v0 = vertices[row, col];
                            int v1 = vertices[row + 1, col];
                            int v2 = vertices[row + 1, col + 1];
                            int v3 = vertices[row, col + 1];

                            s.AddTriangle(v0, v1, v3, ChunkedTriangleCollection.TriangleFlagDeepWater, 1);
                            s.AddTriangle(v1, v2, v3, ChunkedTriangleCollection.TriangleFlagDeepWater, 1);
                        }
                    }
                }
            }
        }

        private void AddTriangles(TriangleCollection s, WMOInstance wi)
        {
            float dx = wi.pos.x;
            float dy = wi.pos.y;
            float dz = wi.pos.z;

            float dir_x = wi.dir.z;
            float dir_y = wi.dir.y - 90;
            float dir_z = -wi.dir.x;

            logger.WriteLine("modeli: " + dir_x + " " + dir_y + " " + dir_z);
            WMO wmo = wi.wmo;

            foreach (WMOGroup g in wmo.groups)
            {
                sequence++;
                int[] vertices = new int[g.nVertices];

                float minx = float.MaxValue;
                float miny = float.MaxValue;
                float minz = float.MaxValue;
                float maxx = float.MinValue;
                float maxy = float.MinValue;
                float maxz = float.MinValue;

                for (int i = 0; i < g.nVertices; i++)
                {
                    int off = i * 3;

                    float x = g.vertices[off];
                    float y = g.vertices[off + 2];
                    float z = g.vertices[off + 1];

                    rotate(z, y, dir_x, out z, out y);
                    rotate(x, y, dir_z, out x, out y);
                    rotate(x, z, dir_y, out x, out z);

                    float xx = x + dx;
                    float yy = y + dy;
                    float zz = -z + dz;

                    float finalx = ChunkReader.ZEROPOINT - zz;
                    float finaly = ChunkReader.ZEROPOINT - xx;
                    float finalz = yy;

                    vertices[i] = s.AddVertex(finalx, finaly, finalz);

                    if (finalx < minx) { minx = finalx; }
                    if (finaly < miny) { miny = finalx; }
                    if (finalz < minz) { minz = finalx; }

                    if (finalx > maxx) { maxx = finalx; }
                    if (finaly > maxy) { maxy = finalx; }
                    if (finalz > maxz) { maxz = finalx; }
                }

                logger.WriteLine("AddTriangles: x(" + minx + " - " + maxx + ") y(" + miny + " - " + maxy + ") z(" + minz + " - " + maxz + ")");

                for (int i = 0; i < g.nTriangles; i++)
                {
                    //if ((g.materials[i] & 0x1000) != 0)
                    {
                        int off = i * 3;
                        int i0 = vertices[g.triangles[off]];
                        int i1 = vertices[g.triangles[off + 1]];
                        int i2 = vertices[g.triangles[off + 2]];

                        int t = s.AddTriangle(i0, i1, i2, ChunkedTriangleCollection.TriangleFlagObject, sequence);
                        //if(t != -1) s.SetTriangleExtra(t, g.materials[0], 0, 0);
                    }
                }
            }

            int doodadset = wi.doodadset;

            if (doodadset < wmo.nDoodadSets)
            {
                uint firstDoodad = wmo.doodads[doodadset].firstInstance;
                uint nDoodads = wmo.doodads[doodadset].nInstances;

                for (uint i = 0; i < nDoodads; i++)
                {
                    uint d = firstDoodad + i;
                    ModelInstance mi = wmo.doodadInstances[d];
                    if (mi != null)
                    {
                        //logger.WriteLine("I got model " + mi.model.fileName + " at " + mi.pos);
                        //AddTrianglesGroupDoodads(s, mi, wi.dir, wi.pos, 0.0f); // DOes not work :(
                    }
                }
            }
        }

        private void AddTrianglesGroupDoodads(TriangleCollection s, ModelInstance mi, Vec3D world_dir, Vec3D world_off, float rot)
        {
            sequence++;
            float dx = mi.pos.x;
            float dy = mi.pos.y;
            float dz = mi.pos.z;

            rotate(dx, dz, rot + 90f, out dx, out dz);

            dx += world_off.x;
            dy += world_off.y;
            dz += world_off.z;

            Quaternion q;
            q.x = mi.dir.z;
            q.y = mi.dir.x;
            q.z = mi.dir.y;
            q.w = mi.w;
            Matrix4 rotMatrix = new Matrix4();
            rotMatrix.makeQuaternionRotate(q);

            Model m = mi.model;

            if (m.boundingTriangles == null)
            {
            }
            else
            {
                // We got boiuding stuff, that is better
                int nBoundingVertices = m.boundingVertices.Length / 3;
                int[] vertices = new int[nBoundingVertices];

                for (uint i = 0; i < nBoundingVertices; i++)
                {
                    uint off = i * 3;
                    float x = m.boundingVertices[off];
                    float y = m.boundingVertices[off + 2];
                    float z = m.boundingVertices[off + 1];
                    x *= mi.sc;
                    y *= mi.sc;
                    z *= -mi.sc;

                    Vector pos;
                    pos.x = x;
                    pos.y = y;
                    pos.z = z;
                    Vector new_pos = rotMatrix.mutiply(pos);
                    x = pos.x;
                    y = pos.y;
                    z = pos.z;

                    float dir_x = world_dir.z;
                    float dir_y = world_dir.y - 90;
                    float dir_z = -world_dir.x;

                    rotate(z, y, dir_x, out z, out y);
                    rotate(x, y, dir_z, out x, out y);
                    rotate(x, z, dir_y, out x, out z);

                    float xx = x + dx;
                    float yy = y + dy;
                    float zz = -z + dz;

                    float finalx = ChunkReader.ZEROPOINT - zz;
                    float finaly = ChunkReader.ZEROPOINT - xx;
                    float finalz = yy;
                    vertices[i] = s.AddVertex(finalx, finaly, finalz);
                }

                int nBoundingTriangles = m.boundingTriangles.Length / 3;
                for (uint i = 0; i < nBoundingTriangles; i++)
                {
                    uint off = i * 3;
                    int v0 = vertices[m.boundingTriangles[off]];
                    int v1 = vertices[m.boundingTriangles[off + 1]];
                    int v2 = vertices[m.boundingTriangles[off + 2]];
                    s.AddTriangle(v0, v2, v1, ChunkedTriangleCollection.TriangleFlagModel, sequence);
                }
            }
        }

        private void AddTriangles(TriangleCollection s, ModelInstance mi)
        {
            sequence++;
            float dx = mi.pos.x;
            float dy = mi.pos.y;
            float dz = mi.pos.z;

            float dir_x = mi.dir.z;
            float dir_y = mi.dir.y - 90; // -90 is correct!
            float dir_z = -mi.dir.x;

            Model m = mi.model;
            if (m == null)
                return;

            if (m.boundingTriangles == null)
            {
                // /cry no bouding info, revert to normal vertives
                /*
				ModelView mv = m.view[0]; // View number 1 ?!?!
				if (mv == null) return;
				int[] vertices = new int[m.vertices.Length / 3];
				for (uint i = 0; i < m.vertices.Length / 3; i++)
				{
					float x = m.vertices[i * 3];
					float y = m.vertices[i * 3 + 2];
					float z = m.vertices[i * 3 + 1];
					x *= mi.sc;
					y *= mi.sc;
					z *= mi.sc;

					rotate(y, z, dir_x, out y, out z);
					rotate(x, y, dir_z, out x, out y);
					rotate(x, z, dir_y, out x, out z);

					float xx = x + dx;
					float yy = y + dy;
					float zz = -z + dz;

					float finalx = ChunkReader.ZEROPOINT - zz;
					float finaly = ChunkReader.ZEROPOINT - xx;
					float finalz = yy;

					vertices[i] = s.AddVertex(finalx, finaly, finalz);
				}

				for (int i = 0; i < mv.triangleList.Length / 3; i++)
				{
					int off = i * 3;
					UInt16 vi0 = mv.triangleList[off];
					UInt16 vi1 = mv.triangleList[off + 1];
					UInt16 vi2 = mv.triangleList[off + 2];

					int ind0 = mv.indexList[vi0];
					int ind1 = mv.indexList[vi1];
					int ind2 = mv.indexList[vi2];

					int v0 = vertices[ind0];
					int v1 = vertices[ind1];
					int v2 = vertices[ind2];
					s.AddTriangle(v0, v1, v2, ChunkedTriangleCollection.TriangleFlagModel);
				}
				*/
            }
            else
            {
                // We got boiuding stuff, that is better
                int nBoundingVertices = m.boundingVertices.Length / 3;
                int[] vertices = new int[nBoundingVertices];
                for (uint i = 0; i < nBoundingVertices; i++)
                {
                    uint off = i * 3;
                    float x = m.boundingVertices[off];
                    float y = m.boundingVertices[off + 2];
                    float z = m.boundingVertices[off + 1];

                    rotate(z, y, dir_x, out z, out y);
                    rotate(x, y, dir_z, out x, out y);
                    rotate(x, z, dir_y, out x, out z);

                    x *= mi.sc;
                    y *= mi.sc;
                    z *= mi.sc;

                    float xx = x + dx;
                    float yy = y + dy;
                    float zz = -z + dz;

                    float finalx = ChunkReader.ZEROPOINT - zz;
                    float finaly = ChunkReader.ZEROPOINT - xx;
                    float finalz = yy;

                    vertices[i] = s.AddVertex(finalx, finaly, finalz);
                }

                int nBoundingTriangles = m.boundingTriangles.Length / 3;
                for (uint i = 0; i < nBoundingTriangles; i++)
                {
                    uint off = i * 3;
                    int v0 = vertices[m.boundingTriangles[off]];
                    int v1 = vertices[m.boundingTriangles[off + 1]];
                    int v2 = vertices[m.boundingTriangles[off + 2]];
                    s.AddTriangle(v0, v1, v2, ChunkedTriangleCollection.TriangleFlagModel, sequence);
                }
            }
        }

        private static void ChunkGetCoordForPoint(MapChunk c, int row, int col,
                                          out float x, out float y, out float z)
        {
            int off = (row * 17 + col) * 3;
            x = ChunkReader.ZEROPOINT - c.vertices[off + 2];
            y = ChunkReader.ZEROPOINT - c.vertices[off];
            z = c.vertices[off + 1];
        }

        private static void ChunkGetCoordForMiddlePoint(MapChunk c, int row, int col,
                                            out float x, out float y, out float z)
        {
            int off = (9 + (row * 17 + col)) * 3;
            x = ChunkReader.ZEROPOINT - c.vertices[off + 2];
            y = ChunkReader.ZEROPOINT - c.vertices[off];
            z = c.vertices[off + 1];
        }

        public static void rotate(float x, float y, float angle, out float nx, out float ny)
        {
            double rot = (angle) / 360.0 * Math.PI * 2;
            float c_y = (float)Math.Cos(rot);
            float s_y = (float)Math.Sin(rot);

            nx = c_y * x - s_y * y;
            ny = s_y * x + c_y * y;
        }
    }
}