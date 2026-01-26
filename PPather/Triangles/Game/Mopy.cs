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

namespace Wmo;

[Flags]
public enum Mopy : ushort
{
    WMO_MATERIAL_UNK01 = 0x01,
    WMO_MATERIAL_NOCAMCOLLIDE = 0x02,
    WMO_MATERIAL_DETAIL = 0x04,
    WMO_MATERIAL_COLLISION = 0x08,
    WMO_MATERIAL_HINT = 0x10,
    WMO_MATERIAL_RENDER = 0x20,
    WMO_MATERIAL_WALL_SURFACE = 0x40, // Guessed
    WMO_MATERIAL_COLLIDE_HIT = 0x80
}
