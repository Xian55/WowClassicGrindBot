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

using PPather.Extensions;

using System;
using System.IO;
using System.Numerics;

namespace Wmo;

public readonly struct ModelInstance
{
    public readonly Model model;
    public readonly Vector3 pos;
    public readonly Vector3 dir;
    public readonly float w;
    public readonly float scale;

    public ModelInstance(BinaryReader file, Model model)
    {
        this.model = model;
        //_ = file.ReadUInt32(); // uint d1
        file.BaseStream.Seek(sizeof(UInt32), SeekOrigin.Current);

        pos = file.ReadVector3();
        dir = file.ReadVector3();

        w = 0;
        scale = file.ReadUInt32() / 1024.0f;
    }

    public ModelInstance(Model m, Vector3 pos, Vector3 dir, float sc, float w)
    {
        this.model = m;
        this.pos = pos;
        this.dir = dir;
        this.scale = sc;
        this.w = w;
    }
}
