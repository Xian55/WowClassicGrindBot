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

using System.Runtime.InteropServices;

namespace Wmo;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct WMOLiquidHeader
{
    public readonly int xverts;
    public readonly int yverts;

    public readonly int xtiles;
    public readonly int ytiles;

    public readonly float pos_x;
    public readonly float pos_y;
    public readonly float pos_z;
    public readonly short material;
}
