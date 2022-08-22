﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using Microsoft.Extensions.Logging;
using SharedLib.Extensions;

namespace Core
{
    public class MinimapNodeFinder
    {
        private struct Score
        {
            public int X;
            public int Y;
            public int count;
        }

        private readonly ILogger logger;
        private readonly WowScreen wowScreen;
        private readonly IAddonReader addonReader;
        public event EventHandler<MinimapNodeEventArgs>? NodeEvent;

        private const int MinScore = 2;
        private const int MaxBlue = 34;
        private const int MinRedGreen = 176;


        //minimap 

        // TODO: adjust these values based on resolution
        // The reference resolution is 1920x1080
        int minX = 0;
        int maxX = 168;
        int minY = 0;
        int maxY = 168;

        Rectangle rect;
        Point center;
        float radius;

        public MinimapNodeFinder(ILogger logger, WowScreen wowScreen, IAddonReader addonReader)
        {
            this.logger = logger;
            this.wowScreen = wowScreen;
            this.addonReader = addonReader;
        }

        public void TryFind()
        {
            wowScreen.UpdateMinimapBitmap();

            var list = FindYellowPoints();
            ScorePoints(list, out Score best);
            addonReader.PlayerReader.BestGatherPos = GetMiniMapWorldLoc(best.X, best.Y);
            NodeEvent?.Invoke(this, new MinimapNodeEventArgs(best.X, best.Y, list.Count(x => x.count > MinScore)));
        }

        private List<Score> FindYellowPoints()
        {
            //find
            List<Score> points = new(100);
            Bitmap bitmap = wowScreen.MiniMapBitmap;
            int minX = 0;
            int maxX = wowScreen.MiniMapBitmap.Width;
            int minY = 0;
            int maxY = wowScreen.MiniMapBitmap.Height;

            rect = new(minX, minY, maxX - minX, maxY - minY);
            center = rect.Centre();
            radius = (maxX - minX) / 2f;

            unsafe
            {
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                int bytesPerPixel = Bitmap.GetPixelFormatSize(bitmap.PixelFormat) / 8;

                //for (int y = minY; y < maxY; y++)
                Parallel.For(minY, maxY, y =>
                {
                    byte* currentLine = (byte*)data.Scan0 + (y * data.Stride);
                    for (int x = minX; x < maxX; x++)
                    {
                        if (!IsValidSquareLocation(x, y, center, radius))
                            continue;

                        int xi = x * bytesPerPixel;
                        if (IsMatch(currentLine[xi + 2], currentLine[xi + 1], currentLine[xi]))
                        {
                            if (points.Capacity == points.Count)
                                return;

                            points.Add(new Score { X = x, Y = y, count = 0 });
                            currentLine[xi + 2] = 255;
                            currentLine[xi + 1] = 0;
                            currentLine[xi + 0] = 0;
                        }
                    }
                });

                bitmap.UnlockBits(data);
            }

            if (points.Count == points.Capacity)
            {
                logger.LogWarning("Too much yellow in this image!");
                points.Clear();
            }

            return points;
        }

        public Vector3 GetMiniMapWorldLoc(int x, int y)
        {
            //zoom from 0-5
            //at Zoom5, the world size covered by the minimap is 60,60
            //at Zoom0(default zoom), the world size covered by the minimap is 220,220
            //Also tested few other zoom and it looks that
            //the size vs zoom can be calculated by size = 220 - (GetZoom * 32)(edited)
            //while at 1920x1080, the minimap world size is at 168px x 168px
            //               x+
            //world map   y+ p  y-
            //               x-

            //               y-
            //image       x- p  x+
            //               y+

            double distanceperpixel = 0.3571;// (60 / 168);
            int xoff = x - center.X;
            int yoff = y - center.Y;
            float finalx = (float)(addonReader.PlayerReader.WorldPos.X - yoff * distanceperpixel);
            float finaly = (float)(addonReader.PlayerReader.WorldPos.Y - xoff * distanceperpixel);
            Vector3 vector3 = new Vector3(finalx, finaly, addonReader.PlayerReader.WorldPosZ);
            return vector3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMatch(byte red, byte green, byte blue)
        {
            return blue < MaxBlue && red > MinRedGreen && green > MinRedGreen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidSquareLocation(int x, int y, Point center, float width)
        {
            return Math.Sqrt(((x - center.X) * (x - center.X)) + ((y - center.Y) * (y - center.Y))) < width;
        }

        private static bool ScorePoints(List<Score> points, out Score best)
        {
            best = new Score();
            const int size = 5;

            for (int i = 0; i < points.Count; i++)
            {
                Score p = points[i];
                p.count =
                    points.Where(s => Math.Abs(s.X - p.X) < size) // + or - n pixels horizontally
                    .Count(s => Math.Abs(s.Y - p.Y) < size);

                points[i] = p;
            }

            points.Sort((a, b) => a.count.CompareTo(b.count));

            if (points.Count > 0 && points[^1].count > MinScore)
            {
                best = points[^1];
                return true;
            }

            return false;
        }
    }
}