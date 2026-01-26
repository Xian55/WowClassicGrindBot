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

using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PPather.Graph;

public sealed class Path
{
    public List<Vector3> locations { get; } = [];

    public Vector3 GetFirst => locations[0];
    public Vector3 GetLast => locations[^1];

    public Path(List<Spot> steps)
    {
        locations.Capacity = steps.Count;

        var span = CollectionsMarshal.AsSpan(steps);
        for (int i = 0; i < span.Length; i++)
        {
            Add(span[i].Loc);
        }
    }

    public void Add(Vector3 l)
    {
        locations.Add(l);
    }
}