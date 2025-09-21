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
        Zoom = packed1 & 0b111;                         // bits 0–2
        ZoomLevels = ((packed1 >> 3) & 0b111) - 1;      // bits 3–5
        Width = ((packed1 >> 6) & 0x3FF) - 2;           // bits 6–15
        RotateMinimap = ((packed1 >> 23) & 1) == 1;

        RightOffset = packed2 % 10000;
        TopOffset = packed2 / 10000;
    }
}