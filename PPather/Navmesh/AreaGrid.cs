using System;
using System.IO;
using System.Runtime.InteropServices;

using Wmo;

namespace PPather.Navmesh;

/// <summary>
/// Compact spatial WoW area-id map for one continent, baked once from the
/// client ADTs so the runtime can answer "which zone is world (x, y) in?" with
/// no MPQ / game-file access.
///
/// Resolution is one cell per MCNK (<see cref="ChunkReader.CHUNKSIZE"/> yd - the
/// native granularity of ADT areaID), stored as a dense ushort grid cropped to
/// the continent's occupied bounding box in global MCNK coordinates. WoW area
/// ids stay well under <see cref="ushort.MaxValue"/> (pre-Cataclysm peak ~4800).
/// </summary>
public sealed class AreaGrid
{
    /// <summary>"AGRD", little-endian.</summary>
    public const uint Magic = 0x44524741;

    /// <summary>Bump when the serialized layout changes.</summary>
    public const int Version = 1;

    /// <summary>MCNK cells per world side: 64 ADTs * 16 MCNK.</summary>
    public const int CellsPerSide = WDT.SIZE * MapTile.SIZE;

    private readonly int minCellX;
    private readonly int minCellY;
    private readonly int width;
    private readonly int height;
    private readonly ushort[] cells;

    public AreaGrid(int minCellX, int minCellY, int width, int height, ushort[] cells)
    {
        this.minCellX = minCellX;
        this.minCellY = minCellY;
        this.width = width;
        this.height = height;
        this.cells = cells;
    }

    public int Width => width;
    public int Height => height;
    public int CellCount => cells.Length;

    /// <summary>
    /// Global MCNK cell for a world position. Collapses
    /// MPQTriangleSupplier.GetChunkCoord1 + GetChunkIndex into one divide:
    /// floor((ZEROPOINT - axis) / CHUNKSIZE) == adt * 16 + mcnkWithinAdt.
    /// </summary>
    public static void WorldToCell(float worldX, float worldY, out int cellX, out int cellY)
    {
        cellX = (int)MathF.Floor((ChunkReader.ZEROPOINT - worldY) / ChunkReader.CHUNKSIZE);
        cellY = (int)MathF.Floor((ChunkReader.ZEROPOINT - worldX) / ChunkReader.CHUNKSIZE);
    }

    /// <summary>Area id at a world position, or 0 when outside the baked bounds.</summary>
    public int GetAreaId(float worldX, float worldY)
    {
        WorldToCell(worldX, worldY, out int cellX, out int cellY);

        int lx = cellX - minCellX;
        int ly = cellY - minCellY;
        if ((uint)lx >= (uint)width || (uint)ly >= (uint)height)
        {
            return 0;
        }

        return cells[(ly * width) + lx];
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using FileStream fs = File.Create(path);
        using BinaryWriter bw = new(fs);

        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(minCellX);
        bw.Write(minCellY);
        bw.Write(width);
        bw.Write(height);
        bw.Write(MemoryMarshal.AsBytes<ushort>(cells));
    }

    public static AreaGrid? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream fs = File.OpenRead(path);
        using BinaryReader br = new(fs);

        if (br.ReadUInt32() != Magic || br.ReadInt32() != Version)
        {
            return null;
        }

        int minCellX = br.ReadInt32();
        int minCellY = br.ReadInt32();
        int width = br.ReadInt32();
        int height = br.ReadInt32();

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        ushort[] cells = new ushort[width * height];
        Span<byte> bytes = MemoryMarshal.AsBytes<ushort>(cells.AsSpan());
        return br.Read(bytes) == bytes.Length
            ? new AreaGrid(minCellX, minCellY, width, height, cells)
            : null;
    }
}
