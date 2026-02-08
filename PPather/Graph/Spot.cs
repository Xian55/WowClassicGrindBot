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
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PPather.Graph;

/// <summary>
/// Minimal navigation graph node identifier.
///
/// Spot is now a lightweight reference type used for type safety and backwards compatibility.
/// All data is managed by SpotManager in Structure-of-Arrays layout:
/// - Location and flags: Stored in SpotManager arrays
/// - Path connectivity: Stored in flattened edge arrays in SpotManager
/// - Search state: Pooled in SearchBuffer, not stored in Spot
///
/// This class only holds references needed for spatial indexing (chunk, next).
/// </summary>
public sealed class Spot
{
    public const float Z_RESOLUTION = PathGraph.MinStepLength / 2f; // Z spots max this close

    public const uint FLAG_VISITED = 0x0001;
    public const uint FLAG_BLOCKED = 0x0002;
    public const uint FLAG_MPQ_MAPPED = 0x0004;
    public const uint FLAG_WATER = 0x0008;
    public const uint FLAG_INDOORS = 0x0010;
    public const uint FLAG_CLOSETOMODEL = 0x0020;
    public const uint FLAG_ON_OBJECT = 0x0040;

    public Vector3 Loc;

    public uint flags;

    public GraphChunk chunk;
    public Spot next;  // list on same x,y, used by chunk

    /// <summary>
    /// Cached index into SpotManager arrays. Set during RegisterSpot.
    /// Avoids Dictionary&lt;Spot,int&gt; lookups on every SpotManager method call.
    /// </summary>
    internal int ManagerIndex = -1;

    public Spot(float x, float y, float z) : this(new(x, y, z)) { }

    public Spot(Vector3 l)
    {
        Loc = l;
    }

    public void Clear()
    {
        next = null;
        chunk = null;
        ManagerIndex = -1;
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
}
