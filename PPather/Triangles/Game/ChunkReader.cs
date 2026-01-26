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
using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Wmo;

internal static class ChunkReader
{
    public const float TILESIZE = 533.33333f;
    public const float ZEROPOINT = 32.0f * TILESIZE;
    public const float CHUNKSIZE = TILESIZE / 16.0f;
    public const float UNITSIZE = CHUNKSIZE / 8.0f;

    public const uint MWMO = 0b_01001101_01010111_01001101_01001111;
    public const uint MODF = 0b_01001101_01001111_01000100_01000110;
    public const uint MAIN = 0b_01001101_01000001_01001001_01001110;
    public const uint MPHD = 0b_01001101_01010000_01001000_01000100;
    public const uint MVER = 0b_01001101_01010110_01000101_01010010;
    public const uint MOGI = 0b_01001101_01001111_01000111_01001001;
    public const uint MOGP = 0b_01001101_01001111_01000111_01010000;
    public const uint MOHD = 0b_01001101_01001111_01001000_01000100;
    public const uint MODN = 0b_01001101_01001111_01000100_01001110;
    public const uint MODS = 0b_01001101_01001111_01000100_01010011;
    public const uint MODD = 0b_01001101_01001111_01000100_01000100;
    public const uint MOPY = 0b_01001101_01001111_01010000_01011001;
    public const uint MOVI = 0b_01001101_01001111_01010110_01001001;
    public const uint MOVT = 0b_01001101_01001111_01010110_01010100;
    public const uint MCIN = 0b_01001101_01000011_01001001_01001110;
    public const uint MMDX = 0b_01001101_01001101_01000100_01011000;
    public const uint MDDF = 0b_01001101_01000100_01000100_01000110;
    public const uint MCNR = 0b_01001101_01000011_01001110_01010010;
    public const uint MCVT = 0b_01001101_01000011_01010110_01010100;
    public const uint MCLQ = 0b_01001101_01000011_01001100_01010001;
    public const uint MH2O = 0b_01001101_01001000_00110010_01001111;
    public const uint MLIQ = 0b_01001101_01001100_01001001_01010001;

    [SkipLocalsInit]
    public static string ExtractString(ReadOnlySpan<byte> buf, int off)
    {
        const byte nullTerminator = 0;
        int length = buf[off..].IndexOf(nullTerminator);
        if (length == -1 || length > buf.Length)
        {
            length = buf.Length;
        }

        return Encoding.ASCII.GetString(buf.Slice(off, length));
    }

    // NOTE:
    // The caller is responsible to Return the array to the ArrayPool
    public static string[] ExtractFileNames(BinaryReader file, uint size)
    {
        var bytePooler = ArrayPool<byte>.Shared;
        byte[] byteBuffer = bytePooler.Rent((int)size);

        Span<byte> span = byteBuffer.AsSpan(0, (int)size);
        file.Read(span);

        const byte nullTerminator = 0;
        int count = span.Count(nullTerminator);

        var stringPooler = ArrayPool<string>.Shared;
        string[] names = stringPooler.Rent(count);

        int i = 0;
        int offset = 0;
        while (offset < size)
        {
            string s = ExtractString(span, offset);
            offset += s.Length + 1;

            names[i++] = s;
        }

        bytePooler.Return(byteBuffer);

        return names;
    }
}
