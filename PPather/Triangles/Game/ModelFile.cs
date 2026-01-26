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
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static System.Buffers.Binary.BinaryPrimitives;

namespace Wmo;

public static class ModelFile
{
    [SkipLocalsInit]
    public static Model Read(ArchiveSet archive, ReadOnlySpan<char> fileName)
    {
        using MpqFileStream mpq = archive.GetStream(fileName);
        int length = (int)mpq.Length;

        var pooler = ArrayPool<byte>.Shared;
        byte[] array = null;

        Span<byte> stream = length <= MpqFileStream.MaxStackLimit
            ? stackalloc byte[length]
            : (array = pooler.Rent(length)).AsSpan(0, length);

        mpq.Read(stream);

        ReadOnlySpan<byte> begining = stream;

        stream = stream[((sizeof(byte) * 4) + (sizeof(UInt32) * 14))..];

        uint nVertices = ReadUInt32LittleEndian(stream);
        stream = stream[sizeof(UInt32)..];

        uint ofsVertices = ReadUInt32LittleEndian(stream);
        stream = stream[sizeof(UInt32)..];

        stream = stream[((sizeof(UInt32) * 23) + (sizeof(Single) * 14))..];

        uint nBoundingTriangles = ReadUInt32LittleEndian(stream);
        stream = stream[sizeof(UInt32)..];

        uint ofsBoundingTriangles = ReadUInt32LittleEndian(stream);
        stream = stream[sizeof(UInt32)..];

        uint nBoundingVertices = ReadUInt32LittleEndian(stream);
        stream = stream[sizeof(UInt32)..];

        uint ofsBoundingVertices = ReadUInt32LittleEndian(stream);
        stream = stream[sizeof(UInt32)..];

        stream = stream[(sizeof(UInt32) * 18)..];

        if (array != null)
            pooler.Return(array);

        return new(
            ReadVertices(begining, nVertices, ofsVertices),
            ReadBoundingTriangles(begining, nBoundingTriangles, ofsBoundingTriangles),
            ReadBoundingVertices(begining, nBoundingVertices, ofsBoundingVertices));
    }

    [SkipLocalsInit]
    private static float[] ReadBoundingVertices(ReadOnlySpan<byte> stream, uint nVertices, uint ofsVertices)
    {
        if (nVertices == 0)
            return [];

        ReadOnlySpan<byte> vertexData = stream.Slice((int)ofsVertices, (int)nVertices * 3 * sizeof(float));
        float[] output = new float[nVertices * 3];

        MemoryMarshal.Cast<byte, float>(vertexData).CopyTo(output);
        return output;
    }

    [SkipLocalsInit]
    private static ushort[] ReadBoundingTriangles(ReadOnlySpan<byte> stream, uint nTriangles, uint ofsTriangles)
    {
        if (nTriangles == 0)
            return [];

        ReadOnlySpan<byte> triangleData = stream.Slice((int)ofsTriangles, (int)(nTriangles * sizeof(ushort)));

        ushort[] output = new ushort[nTriangles];
        MemoryMarshal.Cast<byte, ushort>(triangleData).CopyTo(output.AsSpan());

        return output;
    }

    [SkipLocalsInit]
    private static float[] ReadVertices(ReadOnlySpan<byte> stream, uint nVertices, uint ofsVertices)
    {
        float[] output = new float[nVertices * 3];

        ReadOnlySpan<byte> verticesData = stream[(int)ofsVertices..];

        const int stride = (sizeof(UInt32) * 2) + (sizeof(float) * 7);
        const int vertexCoordsSize = sizeof(float) * 3;

        for (int i = 0; i < nVertices; i++)
        {
            int offset = i * stride;

            ReadOnlySpan<byte> vertexBytes = verticesData.Slice(offset, vertexCoordsSize);
            MemoryMarshal.Cast<byte, float>(vertexBytes)
                         .CopyTo(output.AsSpan(i * 3, 3));
        }
        return output;
    }
}
