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

using Microsoft.Extensions.Logging;

using PPather.Extensions;

using System;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static System.Diagnostics.Stopwatch;

namespace PPather.Graph;

public sealed class GraphChunk
{
    public const int CHUNK_SIZE = 256;
    public const int SIZE = CHUNK_SIZE * CHUNK_SIZE;
    private const bool saveEnabled = true;

    private const uint FILE_MAGIC = 0x23452350;
    private const uint FILE_ENDMAGIC = 0x54325432;
    private const uint SPOT_MAGIC = 0x53504f54;

    private readonly ILogger logger;
    private readonly float base_x, base_y;
    private readonly string filePath;
    private readonly Spot[] spots = new Spot[SIZE];

    public readonly int ix, iy;
    public bool modified;
    public long LRU;

    public int count;

    // Per spot:
    // uint32 magic
    // uint32 reserved;
    // uint32 flags;
    // float x;
    // float y;
    // float z;
    // uint32 no_paths
    //   for each path
    //     float x;
    //     float y;
    //     float z;

    public GraphChunk(float base_x, float base_y, int ix, int iy, ILogger logger, string baseDir)
    {
        this.logger = logger;
        this.base_x = base_x;
        this.base_y = base_y;

        this.ix = ix;
        this.iy = iy;

        filePath = System.IO.Path.Join(baseDir, string.Format("c_{0,3:000}_{1,3:000}.bin", ix, iy));
    }

    public void Clear()
    {
        ReadOnlySpan<Spot> span = spots.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i]?.Clear();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LocalCoords(float x, float y, out int ix, out int iy)
    {
        ix = (int)(x - base_x);
        iy = (int)(y - base_y);
    }

    public Spot GetSpot2D(float x, float y)
    {
        LocalCoords(x, y, out int ix, out int iy);
        return spots[Index(ix, iy)];
    }

    public Spot GetSpot(float x, float y, float z)
    {
        Spot s = GetSpot2D(x, y);

        while (s != null && !s.IsCloseZ(z))
        {
            s = s.next;
        }

        return s;
    }

    // return old spot at conflicting position
    // or the same as passed the function if all was ok
    public Spot AddSpot(Spot s)
    {
        Spot old = GetSpot(s.Loc.X, s.Loc.Y, s.Loc.Z);
        if (old != null)
            return old;

        s.chunk = this;

        LocalCoords(s.Loc.X, s.Loc.Y, out int x, out int y);

        int i = Index(x, y);
        s.next = spots[i];
        spots[i] = s;
        modified = true;
        count++;
        return s;
    }

    public ReadOnlySpan<Spot> GetAllSpots()
    {
        var pool = ArrayPool<Spot>.Shared;
        var output = pool.Rent(count);
        int j = 0;

        var span = spots.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            Spot s = span[i];
            while (s != null)
            {
                output[j++] = s;
                s = s.next;
            }
        }

        pool.Return(output);
        return output.AsSpan(0, j);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Index(int x, int y)
    {
        return (y * CHUNK_SIZE) + x;
    }

    public bool Load()
    {
        try
        {
            long startTime = GetTimestamp();

            using FileStream stream = File.OpenRead(filePath);
            using BinaryReader br = new(stream);

            if (br.ReadUInt32() != FILE_MAGIC)
            {
                br.Close();
                stream.Close();

                File.Delete(filePath);
                logger.LogWarning($"[{nameof(GraphChunk)}] {nameof(FILE_MAGIC)} mismatch! Delete '{filePath}'!");

                return false;
            }

            count = 0;
            while (br.ReadUInt32() != FILE_ENDMAGIC)
            {
                count++;
                uint reserved = br.ReadUInt32();
                uint flags = br.ReadUInt32();
                Vector3 pos = br.ReadVector3();
                uint n_paths = br.ReadUInt32();

                if (pos == Vector3.Zero)
                {
                    continue;
                }

                Spot s = new(pos)
                {
                    flags = flags,
                    n_paths = (int)n_paths,
                    paths = new float[(int)n_paths * 3]
                };
                br.Read(MemoryMarshal.Cast<float, byte>(s.paths.AsSpan()));

                _ = AddSpot(s);

                // After loading a Chunk mark it unmodified
                modified = false;
            }

            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace($"[{nameof(GraphChunk)}] Loaded {filePath} {count} spots {GetElapsedTime(startTime).TotalMilliseconds} ms");

            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
        }

        modified = false;
        return false;
    }

    public void Save()
    {
        if (!saveEnabled || !modified)
            return;

        try
        {
            using FileStream stream = File.Create(filePath);
            using BinaryWriter bw = new(stream);
            bw.Write(FILE_MAGIC);

            int n_spots = 0;
            ReadOnlySpan<Spot> span = GetAllSpots();
            for (int j = 0; j < span.Length; j++)
            {
                Spot s = span[j];

                bw.Write(SPOT_MAGIC);
                bw.Write((uint)0); // reserved
                bw.Write(s.flags);
                bw.Write(s.Loc.X);
                bw.Write(s.Loc.Y);
                bw.Write(s.Loc.Z);
                uint n_paths = (uint)s.n_paths;
                bw.Write(n_paths);
                for (uint i = 0; i < n_paths; i++)
                {
                    uint off = i * 3;
                    bw.Write(s.paths[off]);
                    bw.Write(s.paths[off + 1]);
                    bw.Write(s.paths[off + 2]);
                }
                n_spots++;
            }
            bw.Write(FILE_ENDMAGIC);

            modified = false;

            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace($"[{nameof(GraphChunk)}] Saved {filePath} {n_spots} spots");
        }
        catch (Exception e)
        {
            logger.LogError($"[{nameof(GraphChunk)}] Save failed " + e);
        }
    }
}