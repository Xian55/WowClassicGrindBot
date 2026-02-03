namespace SharedLib;

public readonly record struct MinimapSettings
{
    public readonly int Zoom;
    public readonly int ZoomLevels;
    public readonly int Width;
    public readonly bool RotateMinimap;

    public readonly int RightOffset;
    public readonly int TopOffset;

    public MinimapSettings(int packed1, int packed2)
    {
        // packed1 layout:
        // bits 0-2  : zoom
        // bits 3-5  : zoomlevels (Lua sends 1-based, convert to 0-based)
        // bit  6    : rotateMinimap
        // bits 7-16 : width
        Zoom = packed1 & 0b111;
        ZoomLevels = ((packed1 >> 3) & 0b111) - 1;
        RotateMinimap = ((packed1 >> 6) & 1) == 1;
        Width = (packed1 >> 7) & 0x3FF;

        // packed2 layout (bit-packed):
        // bits 0-11  : offsetRight
        // bits 12-23 : offsetTop
        RightOffset = packed2 & 0xFFF;
        TopOffset = (packed2 >> 12) & 0xFFF;
    }
}