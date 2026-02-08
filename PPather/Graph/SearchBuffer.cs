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
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PPather.Graph;

/// <summary>
/// Pooled search state buffer for A* pathfinding.
///
/// Design:
/// - Allocated once per search, then returned to pool for reuse
/// - Organized as arrays for cache-friendly sequential access
/// - Each array element corresponds to a spot index
///
/// This avoids:
/// - Allocating search state on each spot (70+ bytes × 10k spots)
/// - Scattering search data across memory
/// - Object allocation overhead per search
///
/// Instead provides:
/// - One contiguous buffer per active search (~180 KB for 10k spots)
/// - Reused buffer pool (same buffer used for multiple searches)
/// - Tight memory layout for L1/L2 cache efficiency
/// </summary>
public class SearchBuffer
{
    // ==================== POOLING ====================

    private static readonly ConcurrentStack<SearchBuffer> Pool = new();

    /// <summary>
    /// Get or create a search buffer from the pool
    /// </summary>
    public static SearchBuffer Get(int searchID, int capacity)
    {
        if (Pool.TryPop(out var buffer))
        {
            buffer.Reset(searchID, capacity);
            return buffer;
        }
        return new SearchBuffer(searchID, capacity);
    }

    /// <summary>
    /// Return buffer to pool for reuse
    /// </summary>
    public void Return()
    {
        Pool.Push(this);
    }

    // ==================== SEARCH STATE ====================
    // All arrays indexed by spot.Index for O(1) access

    private int searchID;

    // Core search state - accessed in tight A* loops
    private int[] searchIDs;              // Which search visited this spot? (validation)
    private int[] traceBackIndices;       // Parent spot index for path reconstruction
    private float[] traceBackDistances;   // G_Score (cost from start)
    private float[] scores;               // F_Score (estimated total cost)
    private BitArray closed;              // Visited/in closed set?
    private BitArray scoreSet;            // Has score been computed?

    private int capacity;

    // ==================== STATISTICS ====================
    private long startTimestamp;          // When this search started (Stopwatch ticks)
    private int closedCount;              // Number of spots visited/closed
    private int scoredCount;              // Number of spots scored

    // ==================== INITIALIZATION ====================

    public SearchBuffer(int id, int capacity)
    {
        searchID = id;
        this.capacity = capacity;

        searchIDs = new int[capacity];
        traceBackIndices = new int[capacity];
        traceBackDistances = new float[capacity];
        scores = new float[capacity];
        closed = new BitArray(capacity);
        scoreSet = new BitArray(capacity);

        Reset(id, capacity);
    }

    /// <summary>
    /// Reset buffer for a new search, growing arrays if needed
    /// </summary>
    public void Reset(int newSearchID, int newCapacity)
    {
        searchID = newSearchID;

        // Initialize statistics
        startTimestamp = Stopwatch.GetTimestamp();
        closedCount = 0;
        scoredCount = 0;

        // Grow arrays if needed
        if (newCapacity > capacity)
        {
            Array.Resize(ref searchIDs, newCapacity);
            Array.Resize(ref traceBackIndices, newCapacity);
            Array.Resize(ref traceBackDistances, newCapacity);
            Array.Resize(ref scores, newCapacity);
            closed.Length = newCapacity;
            scoreSet.Length = newCapacity;
            capacity = newCapacity;
        }

        // Clear arrays - only clear the used range for efficiency
        Array.Clear(searchIDs, 0, newCapacity);
        Array.Clear(traceBackIndices, 0, newCapacity);
        Array.Clear(traceBackDistances, 0, newCapacity);
        Array.Clear(scores, 0, newCapacity);
        closed.SetAll(false);
        scoreSet.SetAll(false);
    }

    /// <summary>
    /// Ensure buffer has at least the specified capacity, growing if needed.
    /// Called by SpotManager when it grows during a search.
    /// </summary>
    public void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= capacity)
            return;

        Array.Resize(ref searchIDs, requiredCapacity);
        Array.Resize(ref traceBackIndices, requiredCapacity);
        Array.Resize(ref traceBackDistances, requiredCapacity);
        Array.Resize(ref scores, requiredCapacity);
        closed.Length = requiredCapacity;
        scoreSet.Length = requiredCapacity;
        capacity = requiredCapacity;
    }

    public int SearchID => searchID;
    public int Capacity => capacity;

    // ==================== SEARCH OPERATIONS ====================
    // All methods are aggressively inlined - they're called in tight A* loops

    /// <summary>
    /// Set or validate search ID for a spot.
    /// Returns true if this is a new visit in this search.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetSearchID(int spotIdx, int id)
    {
        if (searchIDs[spotIdx] == id)
            return false;

        closed.Set(spotIdx, false);
        scoreSet.Set(spotIdx, false);
        searchIDs[spotIdx] = id;
        return true;
    }

    /// <summary>
    /// Check if spot is in the closed set (visited in current search)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SearchIsClosed(int spotIdx)
    {
        return searchIDs[spotIdx] == searchID && closed.Get(spotIdx);
    }

    /// <summary>
    /// Mark spot as closed (visited) in current search
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SearchClose(int spotIdx)
    {
        SetSearchID(spotIdx, searchID);
        if (!closed.Get(spotIdx))
        {
            closed.Set(spotIdx, true);
            closedCount++;
        }
    }

    /// <summary>
    /// Check if score has been set for this spot
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SearchScoreIsSet(int spotIdx)
    {
        return searchIDs[spotIdx] == searchID && scoreSet.Get(spotIdx);
    }

    /// <summary>
    /// Get F-Score (estimated total cost) for spot.
    /// Returns float.MaxValue if not in current search or not scored.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float SearchScoreGet(int spotIdx)
    {
        return searchIDs[spotIdx] == searchID ? scores[spotIdx] : float.MaxValue;
    }

    /// <summary>
    /// Set F-Score for a spot in current search
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSearchScore(int spotIdx, float score)
    {
        SetSearchID(spotIdx, searchID);
        scores[spotIdx] = score;
        if (!scoreSet.Get(spotIdx))
        {
            scoreSet.Set(spotIdx, true);
            scoredCount++;
        }
    }

    /// <summary>
    /// Get parent spot index for path reconstruction
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTraceBack(int spotIdx) => traceBackIndices[spotIdx];

    /// <summary>
    /// Set parent spot index for path reconstruction
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTraceBack(int spotIdx, int parentIdx)
        => traceBackIndices[spotIdx] = parentIdx;

    /// <summary>
    /// Get G-Score (cost from start) for spot
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetTraceBackDistance(int spotIdx)
        => traceBackDistances[spotIdx];

    /// <summary>
    /// Set G-Score (cost from start) for spot
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTraceBackDistance(int spotIdx, float distance)
        => traceBackDistances[spotIdx] = distance;

    // ==================== STATISTICS ACCESSORS ====================

    /// <summary>
    /// Number of spots closed/visited during this search
    /// </summary>
    public int ClosedCount => closedCount;

    /// <summary>
    /// Number of spots scored during this search
    /// </summary>
    public int ScoredCount => scoredCount;

    /// <summary>
    /// Elapsed time since search started
    /// </summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(startTimestamp);

    /// <summary>
    /// Elapsed time in milliseconds since search started
    /// </summary>
    public double ElapsedMs => Elapsed.TotalMilliseconds;
}
