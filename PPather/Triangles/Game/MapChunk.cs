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

namespace Wmo;

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
    public readonly bool legacyWater;


    private static readonly int[] holetab = [
        0x1111 & 0x000F, 0x1111 & 0x00F0, 0x1111 & 0x0F00, 0x1111 & 0xF000,
        0x2222 & 0x000F, 0x2222 & 0x00F0, 0x2222 & 0x0F00, 0x2222 & 0xF000,
        0x4444 & 0x000F, 0x4444 & 0x00F0, 0x4444 & 0x0F00, 0x4444 & 0xF000,
        0x8888 & 0x000F, 0x8888 & 0x00F0, 0x8888 & 0x0F00, 0x8888 & 0xF000
    ];

    // 0 ..3, 0 ..3
    public bool IsHole(int i, int j)
    {
        if (!hasholes)
            return false;

        i >>= 1;
        j >>= 1;

        if (i > 3 || j > 3)
            return false;

        int index = (i << 2) | j;

        return (holes & holetab[index]) != 0;
    }


    public MapChunk(float xbase, float ybase, float zbase,
        uint areaID, bool haswater, uint holes, float[] vertices,
        float water_height1, float water_height2, float[] water_height, byte[] water_flags, bool legacyWater)
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

        this.legacyWater = legacyWater;
    }
}
