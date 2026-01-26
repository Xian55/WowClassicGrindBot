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

public readonly struct Model
{
    public readonly float[] vertices;           // 3 per vertex
    public readonly float[] boundingVertices;   // 3 per vertex
    public readonly ushort[] boundingTriangles;

    public Model(float[] vertices, ushort[] boundingTriangles, float[] boundingVertices)
    {
        this.vertices = vertices;
        this.boundingTriangles = boundingTriangles;
        this.boundingVertices = boundingVertices;
    }
}
