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

using StormDll;

using System;

namespace Wmo;

public sealed class WMOManager : Manager<WMORoot>
{
    private readonly ArchiveSet archive;
    private readonly ModelManager modelmanager;

    public WMOManager(ArchiveSet archive, ModelManager modelmanager)
    {
        this.archive = archive;
        this.modelmanager = modelmanager;
    }

    public override bool Load(ReadOnlySpan<char> path, out WMORoot wmoRoot)
    {
        wmoRoot = new();

        WmoRootFile.Load(archive, path, wmoRoot, modelmanager);

        ReadOnlySpan<char> part = path[..^4];

        for (int i = 0; i < wmoRoot.groups.Length; i++)
        {
            ReadOnlySpan<char> name = $"{part}_{i,3:000}.wmo";
            WmoGroupFile.Load(archive, name, wmoRoot, wmoRoot.groups[i]);
        }
        return true;
    }
}
