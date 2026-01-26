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

using SharedLib.Extensions;

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PPather.Graph;

public sealed class Spot
{
    public const float Z_RESOLUTION = PathGraph.MinStepLength / 2f; // Z spots max this close

    public const uint FLAG_VISITED = 0x0001;
    public const uint FLAG_BLOCKED = 0x0002;
    public const uint FLAG_MPQ_MAPPED = 0x0004;
    public const uint FLAG_WATER = 0x0008;
    public const uint FLAG_INDOORS = 0x0010;
    public const uint FLAG_CLOSETOMODEL = 0x0020;

    public Vector3 Loc;

    public uint flags;

    public int n_paths;
    public float[] paths = []; // 3 floats per outgoing path

    public GraphChunk chunk;
    public Spot next;  // list on same x,y, used by chunk

    public int searchID;
    public Spot traceBack; // Used by search
    public float traceBackDistance; // Used by search
    public float score; // Used by search
    public bool closed, scoreSet;

    public Spot(float x, float y, float z) : this(new(x, y, z)) { }

    public Spot(Vector3 l)
    {
        Loc = l;
    }

    public void Clear()
    {
        next = null;
        chunk = null;
        traceBack = null;
    }

    public bool IsCloseToModel()
    {
        return IsFlagSet(FLAG_CLOSETOMODEL);
    }

    public bool IsBlocked()
    {
        return IsFlagSet(FLAG_BLOCKED);
    }

    public bool IsInWater()
    {
        return IsFlagSet(FLAG_WATER);
    }

    public float GetDistanceTo(Vector3 l)
    {
        return Vector3.Distance(Loc, l);
    }

    public float GetDistanceTo(Spot s)
    {
        return Vector3.Distance(Loc, s.Loc);
    }

    public float GetDistanceTo2D(Spot s)
    {
        return Vector2.Distance(Loc.AsVector2(), s.Loc.AsVector2());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsCloseZ(float z)
    {
        float dz = z - Loc.Z;
        return dz >= -Z_RESOLUTION && dz <= Z_RESOLUTION;
    }

    public void SetFlag(uint flag, bool val)
    {
        uint old = flags;
        if (val)
            flags |= flag;
        else
            flags &= ~flag;
        if (chunk != null && old != flags)
            chunk.modified = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsFlagSet(uint flag)
    {
        return (flags & flag) != 0;
    }

    public bool GetPath(int i, out float x, out float y, out float z)
    {
        if (i > n_paths)
        {
            x = y = z = 0;
            return false;
        }

        int off = i * 3;
        x = paths[off + 0];
        y = paths[off + 1];
        z = paths[off + 2];
        return true;
    }

    public Spot GetToSpot(PathGraph pg, int i)
    {
        GetPath(i, out float x, out float y, out float z);
        return pg.GetSpot(x, y, z);
    }

    public ReadOnlySpan<Spot> GetPathsToSpots(PathGraph pg)
    {
        var pooler = ArrayPool<Spot>.Shared;
        Spot[] array = pooler.Rent(n_paths);

        int j = 0;
        for (int i = 0; i < n_paths; i++)
        {
            Spot spot = GetToSpot(pg, i);
            if (spot != null)
                array[j++] = spot;
        }

        pooler.Return(array);
        return new(array, 0, j);
    }

    public bool HasPathTo(PathGraph pg, Spot s)
    {
        for (int i = 0; i < n_paths; i++)
        {
            Spot to = GetToSpot(pg, i);
            if (to == s)
                return true;
        }
        return false;
    }

    public bool HasPathTo(float x, float y, float z)
    {
        ReadOnlySpan<float> pos = [x, y, z];
        return paths.AsSpan().IndexOf(pos) != -1;
    }

    public void AddPathTo(Spot s)
    {
        AddPathTo(s.Loc);
    }

    public void AddPathTo(Vector3 l)
    {
        AddPathTo(l.X, l.Y, l.Z);
    }

    public void AddPathTo(float x, float y, float z)
    {
        if (HasPathTo(x, y, z))
            return;

        Span<float> span = paths.AsSpan();
        int old_size = span.Length / 3;
        if (n_paths + 1 > old_size)
        {
            int new_size = old_size * 2;
            if (new_size < 4)
                new_size = 4;
            Array.Resize(ref paths, new_size * 3);
            span = paths.AsSpan();
        }

        int off = n_paths * 3;
        span[off + 0] = x;
        span[off + 1] = y;
        span[off + 2] = z;
        n_paths++;
        if (chunk != null)
            chunk.modified = true;
    }

    public void RemovePathTo(Vector3 l)
    {
        RemovePathTo(l.X, l.Y, l.Z);
    }

    public void RemovePathTo(float x, float y, float z)
    {
        // look for it
        int found_index = -1;
        for (int i = 0; i < n_paths && found_index == -1; i++)
        {
            int off = i * 3;
            if (paths[off + 0] == x &&
                paths[off + 1] == y &&
                paths[off + 2] == z)
            {
                found_index = i;
            }
        }

        if (found_index == -1)
        {
            return;
        }

        for (int i = found_index; i < n_paths - 1; i++)
        {
            int off = i * 3;
            paths[off + 0] = paths[off + 3];
            paths[off + 1] = paths[off + 4];
            paths[off + 2] = paths[off + 5];
        }
        n_paths--;
        if (chunk != null)
            chunk.modified = true;
    }

    // search stuff

    public bool SetSearchID(int id)
    {
        if (searchID == id)
        {
            return false;
        }

        closed = false;
        scoreSet = false;
        searchID = id;
        return true;
    }

    public bool SearchIsClosed(int id)
    {
        return id == searchID && closed;
    }

    public void SearchClose(int id)
    {
        SetSearchID(id);
        closed = true;
    }

    public bool SearchScoreIsSet(int id)
    {
        return id == searchID && scoreSet;
    }

    public float SearchScoreGet(int id)
    {
        return id == searchID ? score : float.MaxValue;
    }

    public void SearchScoreSet(int id, float score)
    {
        SetSearchID(id);
        this.score = score;
        scoreSet = true;
    }
}