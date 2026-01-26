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
using System.Runtime.InteropServices;

namespace Wmo;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MH2OData1
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
}
