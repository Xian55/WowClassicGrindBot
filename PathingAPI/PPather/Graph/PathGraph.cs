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

*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WowTriangles;

namespace PatherPath.Graph
{
    public class PathGraph
    {
        public static bool SearchEnabled = false;

        private static Object m_LockObject = new Object();

        public enum eSearchScoreSpot
        {
            OriginalPather,
            A_Star,
            A_Star_With_Model_Avoidance,
        }

        public eSearchScoreSpot searchScoreSpot = eSearchScoreSpot.A_Star_With_Model_Avoidance;
        public int sleepMSBetweenSpots = 0;

        public const float toonHeight = 2.0f;
        public const float toonSize = 0.5f;

        public const float MinStepLength = 2f;
        public const float WantedStepLength = 3f;
        public const float MaxStepLength = 5f;

        public Path lastReducedPath = null;

        public static float IsCloseToModelRange = 2;

        /*
		public const float IndoorsWantedStepLength = 1.5f;
		public const float IndoorsMaxStepLength = 2.5f;
		*/

        public const float CHUNK_BASE = 100000.0f; // Always keep positive
        public const int MaximumAllowedRangeFromTarget = 100;
        public string BaseDir = string.Empty;
        private string Continent;
        private SparseMatrix2D<GraphChunk> chunks;

        public ChunkedTriangleCollection triangleWorld;
        public TriangleCollection paint;

        private List<GraphChunk> ActiveChunks = new List<GraphChunk>();
        private long LRU = 0;

        public int GetTriangleClosenessScore(Location loc)
        {
            if (!triangleWorld.IsCloseToModel(loc.X, loc.Y, loc.Z, 3))
            {
                return 0;
            }

            if (!triangleWorld.IsCloseToModel(loc.X, loc.Y, loc.Z, 2))
            {
                return 8;
            }

            if (!triangleWorld.IsCloseToModel(loc.X, loc.Y, loc.Z, 1))
            {
                return 64;
            }

            return 256;
        }

        public int GetTriangleGradiantScore(Location loc, int gradiantMax)
        {
            if (triangleWorld.GradiantScore(loc.X, loc.Y, loc.Z, 1) > gradiantMax)
            {
                return 256;
            }

            if (triangleWorld.GradiantScore(loc.X, loc.Y, loc.Z, 2) > gradiantMax)
            {
                return 64;
            }

            if (triangleWorld.GradiantScore(loc.X, loc.Y, loc.Z, 3) > gradiantMax)
            {
                return 8;
            }

            return 0;
        }

        public static int TimeoutSeconds = 20;
        public static int ProgressTimeoutSeconds = 10;

        private Logger logger;
        private DataConfig dataConfig;

        public PathGraph(string continent,
                         ChunkedTriangleCollection triangles,
                         TriangleCollection paint, Logger logger, DataConfig dataConfig)
        {
            this.logger = logger;
            this.Continent = continent;
            this.triangleWorld = triangles;
            this.paint = paint;
            this.dataConfig = dataConfig;
            BaseDir = dataConfig.PathInfo;
            Clear();
        }

        public void Close()
        {
            triangleWorld.Close();
        }

        public void Clear()
        {
            chunks = new SparseMatrix2D<GraphChunk>(8);
        }

        private void GetChunkCoord(float x, float y, out int ix, out int iy)
        {
            ix = (int)((CHUNK_BASE + x) / GraphChunk.CHUNK_SIZE);
            iy = (int)((CHUNK_BASE + y) / GraphChunk.CHUNK_SIZE);
        }

        private void GetChunkBase(int ix, int iy, out float bx, out float by)
        {
            bx = (float)ix * GraphChunk.CHUNK_SIZE - CHUNK_BASE;
            by = (float)iy * GraphChunk.CHUNK_SIZE - CHUNK_BASE;
        }

        private GraphChunk GetChunkAt(float x, float y)
        {
            int ix, iy;
            GetChunkCoord(x, y, out ix, out iy);
            GraphChunk c = chunks.Get(ix, iy);
            if (c != null)
                c.LRU = LRU++;
            return c;
        }

        private void CheckForChunkEvict()
        {
            lock (this)
            {
                if (ActiveChunks.Count < 512)
                    return;

                GraphChunk evict = null;
                foreach (GraphChunk gc in ActiveChunks)
                {
                    if (evict == null || gc.LRU < evict.LRU)
                    {
                        evict = gc;
                    }
                }

                // It is full!
                evict.Save(BaseDir + "\\" + Continent + "\\");
                ActiveChunks.Remove(evict);
                chunks.Clear(evict.ix, evict.iy);
                evict.Clear();
            }
        }

        public void Save()
        {
            lock (m_LockObject)
            {
                Log("Saving GraphChunks.....");
                ICollection<GraphChunk> l = chunks.GetAllElements();
                foreach (GraphChunk gc in l)
                {
                    if (gc.modified)
                    {
                        gc.Save(BaseDir + "\\" + Continent + "\\");
                    }
                }
            }
        }

        // Create and load from file if exisiting
        private void LoadChunk(float x, float y)
        {
            GraphChunk gc = GetChunkAt(x, y);
            if (gc == null)
            {
                int ix, iy;
                GetChunkCoord(x, y, out ix, out iy);

                float base_x, base_y;
                GetChunkBase(ix, iy, out base_x, out base_y);

                gc = new GraphChunk(base_x, base_y, ix, iy, this.logger);
                gc.LRU = LRU++;

                CheckForChunkEvict();

                gc.Load(BaseDir + "\\" + Continent + "\\");
                chunks.Set(ix, iy, gc);
                ActiveChunks.Add(gc);
            }
        }

        public Spot AddSpot(Spot s)
        {
            LoadChunk(s.X, s.Y);
            GraphChunk gc = GetChunkAt(s.X, s.Y);
            return gc.AddSpot(s);
        }

        // Connect according to MPQ data
        public Spot AddAndConnectSpot(Spot s)
        {
            s = AddSpot(s);
            List<Spot> close = FindAllSpots(s.location, MaxStepLength);
            if (!s.IsFlagSet(Spot.FLAG_MPQ_MAPPED))
            {
                foreach (Spot cs in close)
                {
                    if (cs.HasPathTo(this, s) && s.HasPathTo(this, cs) || cs.IsBlocked())
                    {
                    }
                    else if (!triangleWorld.IsStepBlocked(s.X, s.Y, s.Z, cs.X, cs.Y, cs.Z, toonHeight, toonSize, null))
                    {
                        float mid_x = (s.X + cs.X) / 2;
                        float mid_y = (s.Y + cs.Y) / 2;
                        float mid_z = (s.Z + cs.Z) / 2;
                        float stand_z;
                        int flags;
                        if (triangleWorld.FindStandableAt(mid_x, mid_y, mid_z - WantedStepLength * .75f, mid_z + WantedStepLength * .75f, out stand_z, out flags, toonHeight, toonSize))
                        {
                            s.AddPathTo(cs);
                            cs.AddPathTo(s);
                        }
                    }
                }
            }
            return s;
        }

        public Spot GetSpot(float x, float y, float z)
        {
            LoadChunk(x, y);
            GraphChunk gc = GetChunkAt(x, y);
            return gc.GetSpot(x, y, z);
        }

        public Spot GetSpot2D(float x, float y)
        {
            LoadChunk(x, y);
            GraphChunk gc = GetChunkAt(x, y);
            return gc.GetSpot2D(x, y);
        }

        public Spot GetSpot(Location l)
        {
            if (l == null)
                return null;
            return GetSpot(l.X, l.Y, l.Z);
        }

        // this can be slow...

        public Spot FindClosestSpot(Location l_d)
        {
            return FindClosestSpot(l_d, 30.0f, null);
        }

        public Spot FindClosestSpot(Location l_d, Set<Spot> Not)
        {
            return FindClosestSpot(l_d, 30.0f, Not);
        }

        public Spot FindClosestSpot(Location l, float max_d)
        {
            return FindClosestSpot(l, max_d, null);
        }

        public Spot FindClosestSpot(string description, Location l, float max_d)
        {
            try
            {
                return FindClosestSpot(l, max_d, null);
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Failed to find closest spot to {description}: {l.X},{l.Y} - {ex.Message}");
                return null;
            }
        }

        // this can be slow...
        public Spot FindClosestSpot(Location l, float max_d, Set<Spot> Not)
        {
            Spot closest = null;
            float closest_d = 1E30f;
            int d = 0;
            while ((float)d <= max_d + 0.1f)
            {
                for (int i = -d; i <= d; i++)
                {
                    float x_up = l.X + (float)d;
                    float x_dn = l.X - (float)d;
                    float y_up = l.Y + (float)d;
                    float y_dn = l.Y - (float)d;

                    Spot s0 = GetSpot2D(x_up, l.Y + i);
                    Spot s2 = GetSpot2D(x_dn, l.Y + i);

                    Spot s1 = GetSpot2D(l.X + i, y_dn);
                    Spot s3 = GetSpot2D(l.X + i, y_up);
                    Spot[] sv = { s0, s1, s2, s3 };
                    foreach (Spot s in sv)
                    {
                        Spot ss = s;
                        while (ss != null)
                        {
                            float di = ss.GetDistanceTo(l);
                            if (di < max_d && !ss.IsBlocked() &&
                                (di < closest_d))
                            {
                                closest = ss;
                                closest_d = di;
                            }
                            ss = ss.next;
                        }
                    }
                }

                if (closest_d < d) // can't get better
                {
                    //Log("Closest2 spot to " + l + " is " + closest);
                    return closest;
                }
                d++;
            }
            //Log("Closest1 spot to " + l + " is " + closest);
            return closest;
        }

        public List<Spot> FindAllSpots(Location l, float max_d)
        {
            List<Spot> sl = new List<Spot>();

            int d = 0;
            while ((float)d <= max_d + 0.1f)
            {
                for (int i = -d; i <= d; i++)
                {
                    float x_up = l.X + (float)d;
                    float x_dn = l.X - (float)d;
                    float y_up = l.Y + (float)d;
                    float y_dn = l.Y - (float)d;

                    Spot s0 = GetSpot2D(x_up, l.Y + i);
                    Spot s2 = GetSpot2D(x_dn, l.Y + i);

                    Spot s1 = GetSpot2D(l.X + i, y_dn);
                    Spot s3 = GetSpot2D(l.X + i, y_up);
                    Spot[] sv = { s0, s1, s2, s3 };
                    foreach (Spot s in sv)
                    {
                        Spot ss = s;
                        while (ss != null)
                        {
                            float di = ss.GetDistanceTo(l);
                            if (di < max_d)
                            {
                                sl.Add(ss);
                            }
                            ss = ss.next;
                        }
                    }
                }
                d++;
            }
            return sl;
        }

        public List<Spot> FindAllSpots(float min_x, float min_y, float max_x, float max_y)
        {
            // hmm, do it per chunk
            List<Spot> l = new List<Spot>();
            for (float mx = min_x; mx <= max_x + GraphChunk.CHUNK_SIZE - 1; mx += GraphChunk.CHUNK_SIZE)
            {
                for (float my = min_y; my <= max_y + GraphChunk.CHUNK_SIZE - 1; my += GraphChunk.CHUNK_SIZE)
                {
                    LoadChunk(mx, my);
                    GraphChunk gc = GetChunkAt(mx, my);
                    List<Spot> sl = gc.GetAllSpots();
                    foreach (Spot s in sl)
                    {
                        if (s.X >= min_x && s.X <= max_x &&
                           s.Y >= min_y && s.Y <= max_y)
                        {
                            l.Add(s);
                        }
                    }
                }
            }
            return l;
        }

        public Spot TryAddSpot(Spot wasAt, Location isAt)
        {
            //if (IsUnderwaterOrInAir(isAt)) { return wasAt; }
            Spot isAtSpot = FindClosestSpot(isAt, WantedStepLength);
            if (isAtSpot == null)
            {
                isAtSpot = GetSpot(isAt);
                if (isAtSpot == null)
                {
                    Spot s = new Spot(isAt);
                    s = AddSpot(s);
                    isAtSpot = s;
                }
                if (isAtSpot.IsFlagSet(Spot.FLAG_BLOCKED))
                {
                    isAtSpot.SetFlag(Spot.FLAG_BLOCKED, false);
                    Log("Cleared blocked flag");
                }
                if (wasAt != null)
                {
                    wasAt.AddPathTo(isAtSpot);
                    isAtSpot.AddPathTo(wasAt);
                }

                List<Spot> sl = FindAllSpots(isAtSpot.location, MaxStepLength);
                int connected = 0;
                foreach (Spot other in sl)
                {
                    if (other != isAtSpot)
                    {
                        other.AddPathTo(isAtSpot);
                        isAtSpot.AddPathTo(other);
                        connected++;
                        // Log("  connect to " + other.location);
                    }
                }
                Log("Learned a new spot at " + isAtSpot.location + " connected to " + connected + " other spots");
                wasAt = isAtSpot;
            }
            else
            {
                if (wasAt != null && wasAt != isAtSpot)
                {
                    // moved to an old spot, make sure they are connected
                    wasAt.AddPathTo(isAtSpot);
                    isAtSpot.AddPathTo(wasAt);
                }
                wasAt = isAtSpot;
            }

            return wasAt;
        }

        private bool LineCrosses(Location line0, Location line1, Location point)
        {
            float LineMag = line0.GetDistanceTo(line1); // Magnitude( LineEnd, LineStart );

            float U =
                (((point.X - line0.X) * (line1.X - line0.X)) +
                  ((point.Y - line0.Y) * (line1.Y - line0.Y)) +
                  ((point.Z - line0.Z) * (line1.Z - line0.Z))) /
                (LineMag * LineMag);

            if (U < 0.0f || U > 1.0f)
                return false;

            float InterX = line0.X + U * (line1.X - line0.X);
            float InterY = line0.Y + U * (line1.Y - line0.Y);
            float InterZ = line0.Z + U * (line1.Z - line0.Z);

            float Distance = point.GetDistanceTo(new Location(InterX, InterY, InterZ));
            if (Distance < 0.5f)
                return true;
            return false;
        }

        public void MarkBlockedAt(Location loc)
        {
            Spot s = new Spot(loc);
            s = AddSpot(s);
            s.SetFlag(Spot.FLAG_BLOCKED, true);
            // Find all paths leading though this one

            List<Spot> sl = FindAllSpots(loc, 5.0f);
            foreach (Spot sp in sl)
            {
                List<Location> paths = sp.GetPaths();
                foreach (Location to in paths)
                {
                    if (LineCrosses(sp.location, to, loc))
                    {
                        sp.RemovePathTo(to);
                    }
                }
            }
        }

        public void BlacklistStep(Location from, Location to)
        {
            Spot froms = GetSpot(from);
            if (froms != null)
                froms.RemovePathTo(to);
        }

        public void MarkStuckAt(Location loc, float heading)
        {
            // TODO another day...
            Location inf = loc.InFrontOf(heading, 1.0f);
            MarkBlockedAt(inf);

            // TODO
        }

        //////////////////////////////////////////////////////
        // Searching
        //////////////////////////////////////////////////////

        public Spot currentSearchStartSpot = null;
        public Spot currentSearchSpot = null;

        private float TurnCost(Spot from, Spot to)
        {
            Spot prev = from.traceBack;
            if (prev == null) { return 0.0f; }
            return TurnCost(prev.X, prev.Y, prev.Z, from.X, from.Y, from.Z, to.X, to.Y, to.Z);
        }

        private float TurnCost(float x0, float y0, float z0, float x1, float y1, float z1, float x2, float y2, float z2)
        {
            float v1x = x1 - x0;
            float v1y = y1 - y0;
            float v1z = z1 - z0;

            float v1l = (float)Math.Sqrt(v1x * v1x + v1y * v1y + v1z * v1z);
            v1x /= v1l;
            v1y /= v1l;
            v1z /= v1l;

            float v2x = x2 - x1;
            float v2y = y2 - y1;
            float v2z = z2 - z1;

            float v2l = (float)Math.Sqrt(v2x * v2x + v2y * v2y + v2z * v2z);
            v2x /= v2l;
            v2y /= v2l;
            v2z /= v2l;

            float ddx = v1x - v2x;
            float ddy = v1y - v2y;
            float ddz = v1z - v2z;
            return (float)Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
        }

        // return null if failed or the last spot in the path found

        //SearchProgress searchProgress;
        //public SearchProgress SearchProgress
        //{
        //    get
        //    {
        //        return searchProgress;
        //    }
        //}
        private int searchID = 0;

        private float heuristicsFactor = 5f;

        public Spot ClosestSpot = null;
        public Spot PeekSpot = null;

        private Spot Search(Spot fromSpot, Spot destinationSpot, float minHowClose, ILocationHeuristics locationHeuristics)
        {
            var searchDuration = new Stopwatch();
            searchDuration.Start();
            var timeSinceProgress = new Stopwatch();
            timeSinceProgress.Start();

            var closest = 99999f;
            ClosestSpot = null;

            currentSearchStartSpot = fromSpot;
            searchID++;
            int currentSearchID = searchID;
            //searchProgress = new SearchProgress(fromSpot, destinationSpot, searchID);

            // lowest first queue
            PriorityQueue<Spot, float> prioritySpotQueue = new PriorityQueue<Spot, float>(); // (new SpotSearchComparer(dst, score)); ;
            prioritySpotQueue.Enqueue(fromSpot, -fromSpot.GetDistanceTo(destinationSpot) * heuristicsFactor);

            fromSpot.SearchScoreSet(currentSearchID, 0.0f);
            fromSpot.traceBack = null;
            fromSpot.traceBackDistance = 0;

            // A* -ish algorithm
            while (prioritySpotQueue.Count != 0 && SearchEnabled)
            {
                if (sleepMSBetweenSpots != 0) { Thread.Sleep(sleepMSBetweenSpots); } // slow down the pathing

                float prio;
                currentSearchSpot = prioritySpotQueue.Dequeue(out prio); // .Value;

                // force the world to be loaded
                TriangleCollection tc = triangleWorld.GetChunkAt(currentSearchSpot.X, currentSearchSpot.Y);

                if (currentSearchSpot.SearchIsClosed(currentSearchID)) { continue; }
                currentSearchSpot.SearchClose(currentSearchID);

                //update status
                //if (!searchProgress.CheckProgress(currentSearchSpot)) { break; }

                // are we there?

                var distance = currentSearchSpot.location.GetDistanceTo(destinationSpot.location);

                if (distance <= minHowClose)
                {
                    return currentSearchSpot; // got there
                }

                if (distance < closest)
                {
                    // spamming as hell
                    //logger.WriteLine($"Closet spot is {distance} from the target");
                    closest = distance;
                    ClosestSpot = currentSearchSpot;
                    PeekSpot = ClosestSpot;
                    timeSinceProgress.Reset();
                    timeSinceProgress.Start();
                }

                if (timeSinceProgress.Elapsed.TotalSeconds > ProgressTimeoutSeconds || searchDuration.Elapsed.TotalSeconds > TimeoutSeconds)
                {
                    logger.WriteLine("search failed, 10 seconds since last progress, returning the closest spot.");
                    return ClosestSpot;
                }

                //Find spots to link to
                CreateSpotsAroundSpot(currentSearchSpot);

                //score each spot around the current search spot and add them to the queue
                foreach (Spot spotLinkedToCurrent in currentSearchSpot.GetPathsToSpots(this))
                {
                    if (spotLinkedToCurrent != null && !spotLinkedToCurrent.IsBlocked() && !spotLinkedToCurrent.SearchIsClosed(currentSearchID))
                    {
                        ScoreSpot(spotLinkedToCurrent, destinationSpot, currentSearchID, locationHeuristics, prioritySpotQueue);
                    }
                }
            }

            //we ran out of spots to search
            //searchProgress.LogStatus("  search failed. ");

            if (ClosestSpot != null && closest < MaximumAllowedRangeFromTarget)
            {
                logger.WriteLine("search failed, returning the closest spot.");
                return ClosestSpot;
            }
            return null;
        }

        private void ScoreSpot(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, ILocationHeuristics locationHeuristics, PriorityQueue<Spot, float> prioritySpotQueue)
        {
            switch (searchScoreSpot)
            {
                case eSearchScoreSpot.A_Star:
                    ScoreSpot_A_Star(spotLinkedToCurrent, destinationSpot, currentSearchID, locationHeuristics, prioritySpotQueue);
                    break;

                case eSearchScoreSpot.A_Star_With_Model_Avoidance:
                    ScoreSpot_A_Star_With_Model_And_Gradient_Avoidance(spotLinkedToCurrent, destinationSpot, currentSearchID, locationHeuristics, prioritySpotQueue);
                    break;

                case eSearchScoreSpot.OriginalPather:
                default:
                    ScoreSpot_Pather(spotLinkedToCurrent, destinationSpot, currentSearchID, locationHeuristics, prioritySpotQueue);
                    break;
            }
        }

        public void ScoreSpot_A_Star(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, ILocationHeuristics locationHeuristics, PriorityQueue<Spot, float> prioritySpotQueue)
        {
            //score spot
            float G_Score = currentSearchSpot.traceBackDistance + currentSearchSpot.GetDistanceTo(spotLinkedToCurrent);//  the movement cost to move from the starting point A to a given square on the grid, following the path generated to get there.
            float H_Score = spotLinkedToCurrent.GetDistanceTo2D(destinationSpot) * heuristicsFactor;// the estimated movement cost to move from that given square on the grid to the final destination, point B. This is often referred to as the heuristic, which can be a bit confusing. The reason why it is called that is because it is a guess. We really don�t know the actual distance until we find the path, because all sorts of things can be in the way (walls, water, etc.). You are given one way to calculate H in this tutorial, but there are many others that you can find in other articles on the web.
            float F_Score = G_Score + H_Score;

            if (spotLinkedToCurrent.IsFlagSet(Spot.FLAG_WATER)) { F_Score += 30; }

            if (!spotLinkedToCurrent.SearchScoreIsSet(currentSearchID) || F_Score < spotLinkedToCurrent.SearchScoreGet(currentSearchID))
            {
                // shorter path to here found
                spotLinkedToCurrent.traceBack = currentSearchSpot;
                spotLinkedToCurrent.traceBackDistance = G_Score;
                spotLinkedToCurrent.SearchScoreSet(currentSearchID, F_Score);
                prioritySpotQueue.Enqueue(spotLinkedToCurrent, -F_Score);
            }
        }

        public static int gradiantMax = 5;

        public void ScoreSpot_A_Star_With_Model_And_Gradient_Avoidance(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, ILocationHeuristics locationHeuristics, PriorityQueue<Spot, float> prioritySpotQueue)
        {
            //score spot
            float G_Score = currentSearchSpot.traceBackDistance + currentSearchSpot.GetDistanceTo(spotLinkedToCurrent);//  the movement cost to move from the starting point A to a given square on the grid, following the path generated to get there.
            float H_Score = spotLinkedToCurrent.GetDistanceTo2D(destinationSpot) * heuristicsFactor;// the estimated movement cost to move from that given square on the grid to the final destination, point B. This is often referred to as the heuristic, which can be a bit confusing. The reason why it is called that is because it is a guess. We really don�t know the actual distance until we find the path, because all sorts of things can be in the way (walls, water, etc.). You are given one way to calculate H in this tutorial, but there are many others that you can find in other articles on the web.
            float F_Score = G_Score + H_Score;

            if (spotLinkedToCurrent.IsFlagSet(Spot.FLAG_WATER)) { F_Score += 30; }

            int score = GetTriangleClosenessScore(spotLinkedToCurrent.location);
            score += GetTriangleGradiantScore(spotLinkedToCurrent.location, gradiantMax);
            F_Score += score * 2;

            if (!spotLinkedToCurrent.SearchScoreIsSet(currentSearchID) || F_Score < spotLinkedToCurrent.SearchScoreGet(currentSearchID))
            {
                // shorter path to here found
                spotLinkedToCurrent.traceBack = currentSearchSpot;
                spotLinkedToCurrent.traceBackDistance = G_Score;
                spotLinkedToCurrent.SearchScoreSet(currentSearchID, F_Score);
                prioritySpotQueue.Enqueue(spotLinkedToCurrent, -F_Score);
            }
        }

        public void ScoreSpot_Pather(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, ILocationHeuristics locationHeuristics, PriorityQueue<Spot, float> prioritySpotQueue)
        {
            //score spots
            float currentSearchSpotScore = currentSearchSpot.SearchScoreGet(currentSearchID);
            float linkedSpotScore = 1E30f;
            float new_score = currentSearchSpotScore + currentSearchSpot.GetDistanceTo(spotLinkedToCurrent) + TurnCost(currentSearchSpot, spotLinkedToCurrent);

            if (locationHeuristics != null) { new_score += locationHeuristics.Score(currentSearchSpot.X, currentSearchSpot.Y, currentSearchSpot.Z); }
            if (spotLinkedToCurrent.IsFlagSet(Spot.FLAG_WATER)) { new_score += 30; }

            if (spotLinkedToCurrent.SearchScoreIsSet(currentSearchID))
            {
                linkedSpotScore = spotLinkedToCurrent.SearchScoreGet(currentSearchID);
            }

            if (new_score < linkedSpotScore)
            {
                // shorter path to here found
                spotLinkedToCurrent.traceBack = currentSearchSpot;
                spotLinkedToCurrent.SearchScoreSet(currentSearchID, new_score);
                prioritySpotQueue.Enqueue(spotLinkedToCurrent, -(new_score + spotLinkedToCurrent.GetDistanceTo(destinationSpot) * heuristicsFactor));
            }
        }

        public void CreateSpotsAroundSpot(Spot currentSearchSpot)
        {
            CreateSpotsAroundSpot(currentSearchSpot, currentSearchSpot.IsFlagSet(Spot.FLAG_MPQ_MAPPED));
        }

        public void CreateSpotsAroundSpot(Spot currentSearchSpot, bool mapped)
        {
            if (!mapped)
            {
                //mark as mapped
                currentSearchSpot.SetFlag(Spot.FLAG_MPQ_MAPPED, true);

                float PI = (float)Math.PI;

                //loop through the spots in a circle around the current search spot
                for (float radianAngle = 0; radianAngle < PI * 2; radianAngle += PI / 8)
                {
                    //calculate the location of the spot at the angle
                    float nx = currentSearchSpot.X + (float)Math.Sin(radianAngle) * WantedStepLength;// *0.8f;
                    float ny = currentSearchSpot.Y + (float)Math.Cos(radianAngle) * WantedStepLength;// *0.8f;

                    PeekSpot = new Spot(nx, ny, currentSearchSpot.Z);

                    //find the spot at this location, stop if there is one already
                    if (GetSpot(nx, ny, currentSearchSpot.Z) != null) { continue; } //found a spot so don't create a new one

                    //see if there is a close spot, stop if there is
                    if (FindClosestSpot(new Location(nx, ny, currentSearchSpot.Z), MinStepLength) != null)
                    {
                        continue;
                    } // TODO: this is slow

                    // check we can stand at this new location
                    float new_z;
                    int flags;
                    if (!triangleWorld.FindStandableAt(nx, ny, currentSearchSpot.Z - WantedStepLength * .75f, currentSearchSpot.Z + WantedStepLength * .75f, out new_z, out flags, toonHeight, toonSize))
                    {
                        continue;
                    }

                    //see if a spot already exists at this location
                    if (FindClosestSpot(new Location(nx, ny, new_z), MinStepLength) != null)
                    {
                        continue;
                    }

                    //if the step is blocked then stop
                    if (triangleWorld.IsStepBlocked(currentSearchSpot.X, currentSearchSpot.Y, currentSearchSpot.Z, nx, ny, new_z, toonHeight, toonSize, null))
                    {
                        continue;
                    }

                    //create a new spot and connect it
                    Spot newSpot = AddAndConnectSpot(new Spot(nx, ny, new_z));
                    //PeekSpot = newSpot;

                    //check flags return by triangleWorld.FindStandableA
                    if ((flags & ChunkedTriangleCollection.TriangleFlagDeepWater) != 0)
                    {
                        newSpot.SetFlag(Spot.FLAG_WATER, true);
                    }
                    if (((flags & ChunkedTriangleCollection.TriangleFlagModel) != 0) || ((flags & ChunkedTriangleCollection.TriangleFlagObject) != 0))
                    {
                        newSpot.SetFlag(Spot.FLAG_INDOORS, true);
                    }
                    if (triangleWorld.IsCloseToModel(newSpot.X, newSpot.Y, newSpot.Z, IsCloseToModelRange))
                    {
                        newSpot.SetFlag(Spot.FLAG_CLOSETOMODEL, true);
                    }
                }
            }
        }

        private Spot lastCurrentSearchSpot = null;

        public List<Spot> CurrentSearchPath()
        {
            if (lastCurrentSearchSpot == currentSearchSpot)
            {
                return null;
            }

            lastCurrentSearchSpot = currentSearchSpot;
            return FollowTraceBack(currentSearchStartSpot, currentSearchSpot); ;
        }

        private List<Spot> FollowTraceBack(Spot from, Spot to)
        {
            List<Spot> path = new List<Spot>();
            int count = 0;

            Spot r = to;
            path.Insert(0, to); // add last
            while (r != null)
            {
                Spot s = r.traceBack;

                if (s != null)
                {
                    path.Insert(0, s); // add first
                    r = s;
                    if (r == from)
                    {
                        r = null;  // found source
                    }
                }
                else
                {
                    r = null;
                }
                count++;
            }
            path.Insert(0, from); // add first
            return path;
        }

        public bool IsUnderwaterOrInAir(Location l)
        {
            int flags;
            float z;
            if (triangleWorld.FindStandableAt(l.X, l.Y, l.Z - 50.0f, l.Z + 5.0f, out z, out flags, toonHeight, toonSize))
            {
                if ((flags & ChunkedTriangleCollection.TriangleFlagDeepWater) != 0)
                    return true;
                else
                    return false;
            }
            //return true;
            return false;
        }

        public bool IsUnderwaterOrInAir(Spot s)
        {
            return IsUnderwaterOrInAir(s.GetLocation());
        }

        public bool IsInABuilding(Location l)
        {
            //int flags;
            //float z;
            //if (triangleWorld.FindStandableAt(l.X, l.Y, l.Z +12.0f, l.Z + 50.0f, out z, out  flags, toonHeight, toonSize))
            //{
            //   return true;
            //    //return false;
            //}
            //return triangleWorld.IsCloseToModel(l.X,l.Y,l.Z,1);
            //return true;
            return false;
        }

        public Path LastPath = null;

        private Path CreatePath(Spot from, Spot to, float minHowClose, ILocationHeuristics locationHeuristics)
        {
            Spot newTo = Search(from, to, minHowClose, locationHeuristics);
            if (newTo != null)
            {
                if (newTo.GetDistanceTo(to) <= MaximumAllowedRangeFromTarget)
                {
                    List<Spot> path = FollowTraceBack(from, newTo);
                    LastPath = new Path(path);
                    return LastPath;
                }
                else
                {
                    logger.WriteLine($"Closest spot is too far from target. {newTo.GetDistanceTo(to)}>{MaximumAllowedRangeFromTarget}");
                    return null;
                }
            }
            return null;
        }

        private Path CreatePath(Location fromLoc, Location toLoc, float howClose)
        {
            return CreatePath(fromLoc, toLoc, howClose, null);
        }

        private Location GetBestLocations(Location location)
        {
            float newZ = 0;
            int flags = 0;
            bool getOut = false;
            float[] a = new float[] { 0, -1f, -0.5f, 0.5f, 1 };

            foreach (var z in a)
            {
                if (getOut) break;
                foreach (var x in a)
                {
                    if (getOut) break;
                    foreach (var y in a)
                    {
                        if (getOut) break;
                        if (triangleWorld.FindStandableAt(
                            location.X, location.Y,
                            location.Z + 1 - PathGraph.WantedStepLength * .75f, location.Z + 1 + PathGraph.WantedStepLength * .75f,
                            out newZ, out flags, PathGraph.toonHeight, PathGraph.toonSize))
                            getOut = true;
                    }
                }
            }
            if (Math.Abs(newZ - location.Z) > 5) { newZ = location.Z; }

            return new Location(location.X, location.Y, newZ, location.Description, location.Continent);
        }

        public Path CreatePath(Location fromLoc, Location toLoc, float howClose, ILocationHeuristics locationHeuristics)
        {
            logger.WriteLine("Creating Path from " + fromLoc.ToString() + " tot " + toLoc.ToString());

            var sw = new Stopwatch();
            sw.Start();

            fromLoc = GetBestLocations(fromLoc);
            toLoc = GetBestLocations(toLoc);

            Spot from = FindClosestSpot("fromLoc", fromLoc, MinStepLength);
            Spot to = FindClosestSpot("toLoc", toLoc, MinStepLength);

            if (from == null)
            {
                from = AddAndConnectSpot(new Spot(fromLoc));
            }
            if (to == null)
            {
                to = AddAndConnectSpot(new Spot(toLoc));
            }

            Path rawPath = CreatePath(from, to, howClose, locationHeuristics);

            if (rawPath != null && paint != null)
            {
                Location prev = null;
                for (int i = 0; i < rawPath.Count(); i++)
                {
                    Location l = rawPath.Get(i);
                    paint.AddBigMarker(l.X, l.Y, l.Z);
                    if (prev != null)
                    {
                        paint.PaintPath(l.X, l.Y, l.Z, prev.X, prev.Y, prev.Z);
                    }
                    prev = l;
                }
            }
            logger.Debug(string.Format("CreatePath took {0} seconds.", sw.ElapsedMilliseconds / 1000));
            if (rawPath == null)
            {
                return null;
            }
            else
            {
                Location last = rawPath.GetLast();
                if (last.GetDistanceTo(toLoc) > 1.0) { rawPath.AddLast(toLoc); }
            }
            LastPath = rawPath;
            return rawPath;
        }

        private void Log(String s)
        {
            //logger.WriteLine(s);
            logger.Debug(s);
        }
    }
}