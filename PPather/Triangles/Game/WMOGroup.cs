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

namespace Wmo;

public sealed class WMOGroup
{
    public uint nameStart, nameStart2;
    public uint mogpFlags;
    public Vector3 v1;
    public Vector3 v2;
    public UInt16 batchesA;
    public UInt16 batchesB;
    public UInt32 batchesC;
    public UInt32 fogIdx;
    public UInt32 groupLiquid;

    public UInt16 portalStart;
    public UInt16 portalCount;
    public uint id;

    public uint nVertices;
    public float[] vertices; // 3 per vertex

    public uint nTriangles;
    public UInt16[] triangles; // 3 per triangle
    public UInt16[] materials;  // 1 per triangle

    public int LiquEx_size;
    public UInt32 liquflags;
    public WMOLiquidHeader hlq;
    public WMOLiquidVert[] LiquEx;
    public char[] LiquBytes;
}
