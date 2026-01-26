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

using System.Numerics;

namespace Wmo;

public sealed class WMORoot
{
    public WMOGroup[] groups;

    //int nTextures, nGroups, nP, nLight nX;
    public Vector3 v1, v2; // bounding box

    public byte[] MODNraw;
    public uint nModels;
    public uint nDoodads;
    public uint nDoodadSets;

    public DoodadSet[] doodads;
    public ModelInstance[] doodadInstances;

    public uint flags;
}
