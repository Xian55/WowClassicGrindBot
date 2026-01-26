using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

using static WinAPI.NativeMethods;

#pragma warning disable CS0162

namespace Core;

public sealed class CursorClassifier : IDisposable
{
    private const bool saveImage = false;

    // index matches CursorType order
    private static readonly ulong[][] imageHashes =
    [
        [4645529528554094592, 4665762466636896256, 6376251547633783040, 6376251547633783552],                           // None
        [9286546093378506253, 16208728271425048093, 16208728271425052189, 16276563724689350168, 16276563724695641624],  // Kill
        [16205332705670085656, 16495805933079509016, 16283318718186420248, 18012595853599471616],                       // Loot
        [13901748381153107456, 16207591392111181312, 9798142283158525952],                                              // Skin
        [4669700909741929478, 4669700909674820614],                                                                     // Mine
        [4683320813727784960, 4669700909741929478, 4683461550142398464],                                                // Herb
        [17940331276560775168, 17940331276594329600, 17940331276594460672, 18012595827828094976],                       // Vendor
        [16207573517913036808, 4669140166357294088, 14185844589096599552, 16491828335798648832],                        // Repair
        [4667452417086599168, 4676529985085517824],                                                                     // Innkeeper
        [4682718988357606424, 4682718988358655000],                                                                     // Quest
        [108225472810063872]                                                                                            // Speak    
    ];

    private readonly Bitmap bitmap;
    private readonly Graphics graphics;

    private readonly Bitmap scaledBitmap;
    private readonly Graphics scaledGraphics;

    public CursorClassifier()
    {
        SixLabors.ImageSharp.Size size = GetCursorSize();
        bitmap = new(size.Width, size.Height);
        graphics = Graphics.FromImage(bitmap);

        scaledBitmap = new(8, 8, PixelFormat.Format32bppArgb);
        scaledGraphics = Graphics.FromImage(scaledBitmap);
        scaledGraphics.CompositingQuality = CompositingQuality.HighQuality;
        scaledGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        scaledGraphics.SmoothingMode = SmoothingMode.HighQuality;
    }

    public void Dispose()
    {
        graphics.Dispose();
        bitmap.Dispose();

        scaledGraphics.Dispose();
        scaledBitmap.Dispose();
    }


    public void Classify(out CursorType classification, out double similarity)
    {
        CURSORINFO cursorInfo = new();
        cursorInfo.cbSize = Marshal.SizeOf(cursorInfo);
        if (GetCursorInfo(ref cursorInfo) &&
            cursorInfo.flags == CURSOR_SHOWING)
        {
            graphics.Clear(Color.Transparent);
            DrawIcon(graphics.GetHdc(), 0, 0, cursorInfo.hCursor);
            graphics.ReleaseHdc();
        }

        ulong cursorHash = ImageHashing.AverageHash(bitmap, scaledBitmap, scaledGraphics);
        if (saveImage)
        {
            string path = Path.Join("..", "..", "..", "..", "Cursors", $"{cursorHash}.bmp");
            if (!File.Exists(path))
            {
                bitmap.Save(path);
            }
        }

        int index = 0;
        similarity = 0;
        for (int i = 0; i < imageHashes.Length; i++)
        {
            for (int j = 0; j < imageHashes[i].Length; j++)
            {
                double sim = ImageHashing.Similarity(cursorHash, imageHashes[i][j]);
                if (sim > 80 && sim > similarity)
                {
                    index = i;
                    similarity = sim;
                }
            }
        }

        classification = (CursorType)index;
        Debug.WriteLine($"[CursorClassifier.Classify] {cursorHash} - {classification.ToStringF()} - {similarity}");
    }
}