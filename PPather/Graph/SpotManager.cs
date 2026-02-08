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
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PPather.Graph;

/// <summary>
/// Manages spot data in Structure-of-Arrays (SoA) layout for cache efficiency.
///
/// MIGRATION DESIGN:
/// - Old Spot objects (reference types) are used as the interface
/// - Each Spot is assigned an internal array index when registered
/// - Data is stored in parallel contiguous arrays (SoA pattern)
/// - Search state is pooled in SearchBuffer (allocated per search)
///
/// This allows:
/// - Cache-friendly sequential access in pathfinding
/// - Gradual migration from object-oriented to data-oriented design
/// - Backwards compatibility with existing GraphChunk/Spot code
///
/// The mapping from Spot object → array index is maintained via Spot.ManagerIndex.
/// The reverse mapping (index → Spot) uses a flat Spot[] array for O(1) access.
/// </summary>
public class SpotManager
{
    // ==================== CONFIGURATION ====================
    private const int DefaultCapacity = 10000;
    private const float CapacityGrowthFactor = 1.5f;
    private const int AveragePathsPerSpot = 32;
    private const int InitialEdgeCapacity = 8; // Matches 8-neighbor grid connectivity

    // ==================== CORE STATE ====================
    private int count;  // Current number of registered spots

    // ==================== SPOT MAPPING ====================

    // Reverse mapping (index → Spot) for efficient traceback lookups
    // Flat array indexed by spot index — O(1) access without hash computation
    private Spot[] indexToSpot;

    // ==================== HOT DATA (accessed in A* loops) ====================
    // Parallel arrays indexed by internal index (from Spot.ManagerIndex)

    private Vector3[] locations;        // Spot location in 3D space
    private uint[] flags;               // Environmental flags (water, blocked, etc.)

    // ==================== WARM DATA (path connectivity) ====================

    private int[] pathCounts;           // Number of outgoing paths per spot
    private float[] allEdges;           // Flattened edges: [x0,y0,z0, x1,y1,z1, ...]
    private int[] edgeOffsets;          // Where this spot's edges start in allEdges
    private int nextFreeEdgeOffset;     // Next available position in allEdges (O(1) allocation)
    private int[] edgeCapacities;       // Pre-allocated edge slot count per spot
    private int[] nextIndices;          // Linked list: next spot at same x,y
    private Spot[] edgeSpotCache;       // Cached resolved Spot for each edge target (parallel to allEdges/3)

    // ==================== REUSABLE BUFFERS ====================

    private Spot[] neighborBuffer = new Spot[16]; // Reusable buffer for GetPathsToSpots (single-threaded A*)

    // ==================== CACHED SCORING ====================

    private const short TriangleScoreNotComputed = -1;
    private short[] cachedTriangleScores;  // Cached combined triangle score per spot (-1 = not computed)

    // ==================== COLD DATA (metadata, infrequent access) ====================

    private GraphChunk[] chunks;        // Chunk ownership references

    // ==================== SEARCH STATE (POOLED) ====================
    // Allocated per search, returned to pool when search completes

    private SearchBuffer currentSearch;

    // ==================== INITIALIZATION ====================

    public SpotManager(int capacity = DefaultCapacity)
    {
        count = 0;
        indexToSpot = new Spot[capacity];

        // Allocate hot data arrays
        locations = new Vector3[capacity];
        flags = new uint[capacity];

        // Allocate warm data arrays
        pathCounts = new int[capacity];
        allEdges = new float[capacity * AveragePathsPerSpot * 3];
        edgeOffsets = new int[capacity];
        edgeCapacities = new int[capacity];
        nextIndices = new int[capacity];
        edgeSpotCache = new Spot[capacity * AveragePathsPerSpot];

        // Allocate cold data arrays
        chunks = new GraphChunk[capacity];

        // Allocate cached scoring
        cachedTriangleScores = new short[capacity];

        // Initialize to sentinel values
        Array.Fill(edgeOffsets, -1);
        Array.Fill(nextIndices, -1);
        Array.Fill(cachedTriangleScores, TriangleScoreNotComputed);
    }

    public int Count => count;
    public int Capacity => locations.Length;

    // ==================== SPOT REGISTRATION ====================

    /// <summary>
    /// Register an existing Spot object with the manager.
    /// This assigns it an internal array index and stores its data.
    /// </summary>
    public void RegisterSpot(Spot spot, Vector3 location, uint spotFlags, GraphChunk chunk)
    {
        // Check if already registered via cached index
        if (spot.ManagerIndex >= 0)
            return;

        // Grow arrays if needed
        if (count >= Capacity)
            Grow();

        int index = count++;

        // Store cached index on spot for O(1) future lookups
        spot.ManagerIndex = index;

        // Store reverse mapping (index → Spot)
        indexToSpot[index] = spot;

        // Store data in arrays
        locations[index] = location;
        flags[index] = spotFlags;
        pathCounts[index] = 0;
        edgeOffsets[index] = -1;
        edgeCapacities[index] = 0;
        nextIndices[index] = -1;
        cachedTriangleScores[index] = TriangleScoreNotComputed;
        chunks[index] = chunk;
    }

    private void Grow()
    {
        int newCapacity = (int)(Capacity * CapacityGrowthFactor);

        Array.Resize(ref locations, newCapacity);
        Array.Resize(ref flags, newCapacity);
        Array.Resize(ref pathCounts, newCapacity);
        int newEdgeSize = newCapacity * AveragePathsPerSpot * 3;
        Array.Resize(ref allEdges, newEdgeSize);
        Array.Resize(ref edgeSpotCache, newEdgeSize / 3);
        Array.Resize(ref edgeOffsets, newCapacity);
        Array.Resize(ref edgeCapacities, newCapacity);
        Array.Resize(ref nextIndices, newCapacity);
        Array.Resize(ref chunks, newCapacity);
        Array.Resize(ref cachedTriangleScores, newCapacity);
        Array.Resize(ref indexToSpot, newCapacity);

        // Initialize new slots
        for (int i = count; i < newCapacity; i++)
        {
            edgeOffsets[i] = -1;
            nextIndices[i] = -1;
            cachedTriangleScores[i] = TriangleScoreNotComputed;
        }

        // If a search is active, grow SearchBuffer to match
        if (currentSearch != null)
            currentSearch.EnsureCapacity(newCapacity);
    }

    // ==================== INTERNAL HELPERS ====================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetIndex(Spot spot)
    {
        int idx = spot.ManagerIndex;
        if (idx >= 0)
            return idx;

        // Auto-register spot on first access (lazy registration)
        // This handles spots that were created/loaded without explicit registration
        // Capture index BEFORE RegisterSpot (which will increment count)
        idx = count;
        RegisterSpot(spot, spot.Loc, spot.flags, spot.chunk);
        return idx;
    }

    // ==================== DATA ACCESS ====================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 GetLocation(Spot spot) => locations[GetIndex(spot)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLocation(Spot spot, Vector3 location)
        => locations[GetIndex(spot)] = location;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetFlags(Spot spot) => flags[GetIndex(spot)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFlags(Spot spot, uint value)
        => flags[GetIndex(spot)] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsFlagSet(Spot spot, uint flag)
        => (flags[GetIndex(spot)] & flag) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFlag(Spot spot, uint flag, bool value)
    {
        int idx = GetIndex(spot);
        uint old = flags[idx];
        if (value)
            flags[idx] = old | flag;
        else
            flags[idx] = old & ~flag;
    }

    // ==================== CACHED TRIANGLE SCORE ACCESS ====================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCachedTriangleScore(Spot spot, out int score)
    {
        short cached = cachedTriangleScores[GetIndex(spot)];
        score = cached;
        return cached != TriangleScoreNotComputed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetCachedTriangleScore(Spot spot, int score)
        => cachedTriangleScores[GetIndex(spot)] = (short)score;

    // ==================== DISTANCE CALCULATIONS ====================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetDistance(Spot a, Spot b)
        => Vector3.Distance(locations[GetIndex(a)], locations[GetIndex(b)]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetDistance2D(Spot a, Spot b)
    {
        var aIdx = GetIndex(a);
        var bIdx = GetIndex(b);
        return MathF.Sqrt(
            (locations[aIdx].X - locations[bIdx].X) * (locations[aIdx].X - locations[bIdx].X) +
            (locations[aIdx].Y - locations[bIdx].Y) * (locations[aIdx].Y - locations[bIdx].Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetDistanceTo(Spot spot, Vector3 target)
        => Vector3.Distance(locations[GetIndex(spot)], target);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsCloseZ(Spot spot, float z)
    {
        float dz = z - locations[GetIndex(spot)].Z;
        return dz >= -Spot.Z_RESOLUTION && dz <= Spot.Z_RESOLUTION;
    }

    // ==================== CHUNK MANAGEMENT ====================

    public GraphChunk GetChunk(Spot spot)
        => chunks[GetIndex(spot)];

    public void SetChunk(Spot spot, GraphChunk chunk)
        => chunks[GetIndex(spot)] = chunk;

    // ==================== PATH MANAGEMENT (connectivity edges) ====================

    /// <summary>
    /// Get the number of outgoing paths from a spot
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPathCount(Spot spot)
        => pathCounts[GetIndex(spot)];

    /// <summary>
    /// Get a specific path destination as Vector3
    /// </summary>
    public bool GetPath(Spot spot, int pathIndex, out float x, out float y, out float z)
    {
        int idx = GetIndex(spot);
        if (pathIndex >= pathCounts[idx])
        {
            x = y = z = 0;
            return false;
        }

        int offset = edgeOffsets[idx] + pathIndex * 3;
        x = allEdges[offset + 0];
        y = allEdges[offset + 1];
        z = allEdges[offset + 2];
        return true;
    }

    /// <summary>
    /// Add a path to a destination (as coordinates, target Spot unknown).
    /// Delegates to AddPathToCore with null target — the Spot will be
    /// resolved lazily on first GetPathsToSpots access.
    /// </summary>
    public void AddPathTo(Spot spot, float x, float y, float z)
        => AddPathToCore(spot, x, y, z, null);

    /// <summary>
    /// Add a path to a known target Spot.
    /// Populates the edge spot cache at creation time, eliminating
    /// the costly GetSpot coordinate round-trip in GetPathsToSpots.
    /// </summary>
    public void AddPathTo(Spot source, Spot target)
        => AddPathToCore(source, target.Loc.X, target.Loc.Y, target.Loc.Z, target);

    /// <summary>
    /// Add a path to another spot (Vector3 overload)
    /// </summary>
    public void AddPathTo(Spot spot, Vector3 destination)
        => AddPathTo(spot, destination.X, destination.Y, destination.Z);

    /// <summary>
    /// Core implementation for adding a path edge.
    /// If this spot's edge block is not at the tail of allEdges,
    /// the edges are relocated to the tail first to prevent overwriting
    /// adjacent spot data.
    /// </summary>
    private void AddPathToCore(Spot spot, float x, float y, float z, Spot targetSpot)
    {
        int idx = GetIndex(spot);

        // Check if path already exists (use index-based overload to avoid redundant GetIndex)
        if (HasPathTo(idx, x, y, z))
            return;

        int pathCount = pathCounts[idx];
        int capacity = edgeCapacities[idx];

        if (pathCount == 0 && capacity == 0)
        {
            // Case 1: First edge — reserve InitialEdgeCapacity slots upfront
            int slotsNeeded = InitialEdgeCapacity * 3;
            int requiredTotal = nextFreeEdgeOffset + slotsNeeded;
            EnsureEdgeCapacity(requiredTotal);

            edgeOffsets[idx] = nextFreeEdgeOffset;
            edgeCapacities[idx] = InitialEdgeCapacity;
            nextFreeEdgeOffset += slotsNeeded;
        }
        else if (pathCount >= capacity)
        {
            // Case 2: Overflow — relocate with doubled capacity (rare path)
            int newCapacity = capacity * 2;
            int newOffset = nextFreeEdgeOffset;
            int newSlotsNeeded = newCapacity * 3;
            int requiredTotal = newOffset + newSlotsNeeded;
            EnsureEdgeCapacity(requiredTotal);

            int oldOffset = edgeOffsets[idx];
            int oldCacheBase = oldOffset / 3;
            int newCacheBase = newOffset / 3;

            // Copy existing edges to new location
            Array.Copy(allEdges, oldOffset, allEdges, newOffset, pathCount * 3);
            // Copy existing edge spot cache entries to new location
            Array.Copy(edgeSpotCache, oldCacheBase, edgeSpotCache, newCacheBase, pathCount);

            edgeOffsets[idx] = newOffset;
            edgeCapacities[idx] = newCapacity;
            nextFreeEdgeOffset = newOffset + newSlotsNeeded;
        }
        // Case 3: Fast path — pathCount < capacity, write directly in-place

        // Write path coordinates
        int offset = edgeOffsets[idx] + pathCount * 3;
        allEdges[offset + 0] = x;
        allEdges[offset + 1] = y;
        allEdges[offset + 2] = z;

        // Cache the target Spot (null if unknown — resolved lazily)
        edgeSpotCache[offset / 3] = targetSpot;

        pathCounts[idx]++;

        // Mark chunk as modified
        if (chunks[idx] != null)
            chunks[idx].modified = true;
    }

    /// <summary>
    /// Ensure allEdges and edgeSpotCache have room for at least requiredTotal float entries.
    /// </summary>
    private void EnsureEdgeCapacity(int requiredTotal)
    {
        if (requiredTotal <= allEdges.Length)
            return;

        int newSize = Math.Max((int)(allEdges.Length * CapacityGrowthFactor), requiredTotal + 1024);
        Array.Resize(ref allEdges, newSize);
        Array.Resize(ref edgeSpotCache, newSize / 3);
    }

    /// <summary>
    /// Check if a path to coordinates exists (index-based, avoids redundant GetIndex)
    /// </summary>
    private bool HasPathTo(int idx, float x, float y, float z)
    {
        int pathCount = pathCounts[idx];
        int baseOffset = edgeOffsets[idx];

        for (int i = 0; i < pathCount; i++)
        {
            int offset = baseOffset + i * 3;
            if (allEdges[offset + 0] == x &&
                allEdges[offset + 1] == y &&
                allEdges[offset + 2] == z)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a path to coordinates exists
    /// </summary>
    public bool HasPathTo(Spot spot, float x, float y, float z)
        => HasPathTo(GetIndex(spot), x, y, z);

    /// <summary>
    /// Check if a path to destination spot exists
    /// </summary>
    public bool HasPathTo(Spot spot, Spot destination)
        => HasPathTo(spot, destination.Loc.X, destination.Loc.Y, destination.Loc.Z);

    /// <summary>
    /// Remove a path to coordinates
    /// </summary>
    public void RemovePathTo(Spot spot, float x, float y, float z)
    {
        int idx = GetIndex(spot);
        int pathCount = pathCounts[idx];

        // Find the path to remove
        int foundIndex = -1;
        for (int i = 0; i < pathCount; i++)
        {
            if (GetPath(spot, i, out float px, out float py, out float pz))
            {
                if (px == x && py == y && pz == z)
                {
                    foundIndex = i;
                    break;
                }
            }
        }

        if (foundIndex < 0)
            return;

        // Shift remaining paths down (swap with last)
        if (foundIndex < pathCount - 1)
        {
            int offset = edgeOffsets[idx];
            int lastOffset = offset + (pathCount - 1) * 3;
            int removeOffset = offset + foundIndex * 3;

            // Copy last path to removed position
            allEdges[removeOffset + 0] = allEdges[lastOffset + 0];
            allEdges[removeOffset + 1] = allEdges[lastOffset + 1];
            allEdges[removeOffset + 2] = allEdges[lastOffset + 2];

            // Also swap the edge spot cache entry
            edgeSpotCache[removeOffset / 3] = edgeSpotCache[lastOffset / 3];
        }

        // Clear the removed (now-last) cache entry
        int clearedOffset = edgeOffsets[idx] + (pathCount - 1) * 3;
        edgeSpotCache[clearedOffset / 3] = null;

        pathCounts[idx]--;

        // Mark chunk as modified
        if (chunks[idx] != null)
            chunks[idx].modified = true;
    }

    /// <summary>
    /// Remove a path to destination spot
    /// </summary>
    public void RemovePathTo(Spot spot, Vector3 destination)
        => RemovePathTo(spot, destination.X, destination.Y, destination.Z);

    /// <summary>
    /// Get all connected spots for a given spot (for pathfinding).
    /// </summary>
    /// <remarks>
    /// Returns a span over a reusable internal buffer (neighborBuffer).
    /// Safe because A* is single-threaded and the returned span is consumed
    /// before the next call to this method.
    /// Uses edgeSpotCache to avoid costly GetSpot coordinate round-trips.
    /// Cache misses (null entries from disk-loaded edges) are resolved lazily
    /// and cached for subsequent searches.
    /// </remarks>
    public ReadOnlySpan<Spot> GetPathsToSpots(Spot spot, PathGraph pathGraph)
    {
        int idx = GetIndex(spot);
        int pathCount = pathCounts[idx];

        if (pathCount == 0)
            return ReadOnlySpan<Spot>.Empty;

        // Grow reusable buffer if needed (extremely rare — typically max 8 neighbors)
        if (pathCount > neighborBuffer.Length)
            neighborBuffer = new Spot[pathCount];

        int baseOffset = edgeOffsets[idx];
        int resultCount = 0;
        for (int i = 0; i < pathCount; i++)
        {
            int cacheIdx = baseOffset / 3 + i;
            Spot connectedSpot = edgeSpotCache[cacheIdx];

            if (connectedSpot == null)
            {
                // Cache miss — resolve from coordinates and cache for future calls
                int offset = baseOffset + i * 3;
                connectedSpot = pathGraph.GetSpot(allEdges[offset], allEdges[offset + 1], allEdges[offset + 2]);
                if (connectedSpot != null)
                    edgeSpotCache[cacheIdx] = connectedSpot;
            }

            if (connectedSpot != null)
                neighborBuffer[resultCount++] = connectedSpot;
        }

        return new(neighborBuffer, 0, resultCount);
    }

    // ==================== SEARCH BUFFER MANAGEMENT ====================

    /// <summary>
    /// Initialize search with SpotManager.
    /// Allocates SearchBuffer from pool and initializes starting spot.
    /// </summary>
    public void InitializeSearch(int searchID, Spot startSpot)
    {
        // Reset build timing accumulator
        buildTicks = 0;

        // Get or create search buffer from pool
        currentSearch = SearchBuffer.Get(searchID, Capacity);

        // Initialize starting spot
        int startIdx = GetIndex(startSpot);
        currentSearch.SetSearchScore(startIdx, 0f);
        currentSearch.SetTraceBack(startIdx, -1);
        currentSearch.SetTraceBackDistance(startIdx, 0f);
    }

    // ==================== BUILD TIMING ====================
    // Accumulated ticks spent in CreateSpotsAroundSpot during the current search
    private long buildTicks;

    /// <summary>
    /// Record build ticks (time spent in CreateSpotsAroundSpot).
    /// Called from PathGraph.Search() around each CreateSpotsAroundSpot call.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddBuildTicks(long ticks) => buildTicks += ticks;

    /// <summary>
    /// Complete search and return SearchBuffer to pool.
    /// Returns search statistics (elapsed time, build time, spots visited, spots scored).
    /// </summary>
    public (double elapsedMs, double buildMs, int closedCount, int scoredCount) CompleteSearch()
    {
        if (currentSearch != null)
        {
            double buildMs = Stopwatch.GetElapsedTime(0, buildTicks).TotalMilliseconds;
            var stats = (currentSearch.ElapsedMs, buildMs, currentSearch.ClosedCount, currentSearch.ScoredCount);
            currentSearch.Return();
            currentSearch = null;
            return stats;
        }
        return (0, 0, 0, 0);
    }

    /// <summary>
    /// Get current search statistics (if search is active).
    /// </summary>
    public (double elapsedMs, double buildMs, int closedCount, int scoredCount) GetSearchStats()
    {
        if (currentSearch != null)
        {
            double buildMs = Stopwatch.GetElapsedTime(0, buildTicks).TotalMilliseconds;
            return (currentSearch.ElapsedMs, buildMs, currentSearch.ClosedCount, currentSearch.ScoredCount);
        }
        return (0, 0, 0, 0);
    }

    // ==================== SEARCH STATE DELEGATION ====================
    // These methods forward to current SearchBuffer

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SearchIsClosed(Spot spot)
        => currentSearch?.SearchIsClosed(GetIndex(spot)) ?? false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SearchClose(Spot spot)
        => currentSearch?.SearchClose(GetIndex(spot));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SearchScoreIsSet(Spot spot)
        => currentSearch?.SearchScoreIsSet(GetIndex(spot)) ?? false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float SearchScoreGet(Spot spot)
        => currentSearch?.SearchScoreGet(GetIndex(spot)) ?? float.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SearchScoreSet(Spot spot, float score)
        => currentSearch?.SetSearchScore(GetIndex(spot), score);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Spot GetTraceBack(Spot spot)
    {
        if (currentSearch == null)
            return null;

        int traceBackIdx = currentSearch.GetTraceBack(GetIndex(spot));
        if (traceBackIdx < 0)
            return null;

        // Direct array index for O(1) lookup without hash computation
        return indexToSpot[traceBackIdx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTraceBack(Spot spot, Spot parent)
    {
        if (currentSearch == null)
            return;

        int idx = GetIndex(spot);
        int parentIdx = parent != null ? GetIndex(parent) : -1;
        currentSearch.SetTraceBack(idx, parentIdx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetTraceBackDistance(Spot spot)
        => currentSearch?.GetTraceBackDistance(GetIndex(spot)) ?? 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTraceBackDistance(Spot spot, float distance)
        => currentSearch?.SetTraceBackDistance(GetIndex(spot), distance);

    // ==================== UTILITY METHODS ====================

    /// <summary>
    /// Check if a spot is registered with this manager
    /// </summary>
    public static bool IsRegistered(Spot spot)
        => spot.ManagerIndex >= 0;

    /// <summary>
    /// Enumerate all registered spots
    /// </summary>
    public IEnumerable<Spot> AllSpots()
    {
        for (int i = 0; i < count; i++)
            yield return indexToSpot[i];
    }

    /// <summary>
    /// Clear all data
    /// </summary>
    public void Clear()
    {
        count = 0;
        nextFreeEdgeOffset = 0;
        Array.Clear(indexToSpot, 0, indexToSpot.Length);
        Array.Clear(edgeSpotCache);
        currentSearch = null;
    }
}
