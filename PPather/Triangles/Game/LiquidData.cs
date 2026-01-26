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

    public LiquidData(
        uint offsetData1,
        int used,
        uint offsetData2,
        MH2OData1 data1,
        float[] water_height,
        byte[] water_flags)
    {
        this.offsetData1 = offsetData1;
        this.used = used;
        this.offsetData2 = offsetData2;
        this.data1 = data1;

        this.water_height = water_height;
        this.water_flags = water_flags;
    }
}
