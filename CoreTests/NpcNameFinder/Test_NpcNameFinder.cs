using System.Drawing;
using Core.Goals;
using SharedLib.NpcFinder;
using Microsoft.Extensions.Logging;
using SharedLib;
using System.Threading;
using System.Text;
using System.Diagnostics;
using System.Linq;
using GameOverlay.Windows;
using System.Collections.Generic;
using System;
using SharpDX.Direct2D1;
using WowheadDB;
using SharedLib.Extensions;
using Core;

#pragma warning disable 0162

namespace CoreTests
{
    public class Test_NpcNameFinder
    {
        private const bool saveImage = true;
        private const bool LogEachUpdate = true;
        private const bool LogShowResult = false;

        private const bool debugTargeting = true;
        private const bool debugSkinning = false;

        private readonly ILogger logger;
        private readonly NpcNameFinder npcNameFinder;
        private readonly NpcNameTargeting npcNameTargeting;
        private readonly RectProvider rectProvider;
        private readonly BitmapCapturer capturer;

        private readonly Stopwatch stopwatch = new();
        private readonly StringBuilder stringBuilder = new();

        private readonly Graphics paint;
        private readonly System.Drawing.Bitmap paintBitmap;
        private readonly Font font = new("Arial", 10);
        private readonly SolidBrush brush = new(Color.White);
        private readonly Pen whitePen = new(Color.White, 1);

        private readonly GameOverlay.Windows.GraphicsWindow _window;
        private readonly GameOverlay.Drawing.Graphics _graphics;

        private readonly Dictionary<string, GameOverlay.Drawing.SolidBrush> _brushes;
        private readonly Dictionary<string, GameOverlay.Drawing.Font> _fonts;
        private readonly Dictionary<string, GameOverlay.Drawing.Image> _images;
        public Test_NpcNameFinder(ILogger logger, NpcNames types)
        {
            this.logger = logger;

            rectProvider = new();
            rectProvider.GetRectangle(out Rectangle rect);
            capturer = new(rect);

            npcNameFinder = new(logger, capturer, new AutoResetEvent(false));

            MockWoWProcess mockWoWProcess = new();
            MockWoWScreen mockWoWScreen = new();
            MockMouseOverReader mouseOverReader = new();
            npcNameTargeting = new(logger, new(), mockWoWScreen, npcNameFinder, mockWoWProcess, mouseOverReader, new NoBlacklist(), null);

            npcNameFinder.ChangeNpcType(types);

            if (saveImage)
            {
                paintBitmap = capturer.Bitmap;
                paint = Graphics.FromImage(paintBitmap);
            }


            //game overlay
            _brushes = new Dictionary<string, GameOverlay.Drawing.SolidBrush>();
            _fonts = new Dictionary<string, GameOverlay.Drawing.Font>();
            _images = new Dictionary<string, GameOverlay.Drawing.Image>();

            _graphics = new GameOverlay.Drawing.Graphics()
            {
                MeasureFPS = true,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true,
                UseMultiThreadedFactories = false,
                VSync = false,
                WindowHandle = IntPtr.Zero
            };
            IntPtr ip = System.Diagnostics.Process.GetProcessesByName("WoWClassicT")[0].MainWindowHandle;
            _window = new StickyWindow(ip, _graphics)
            {
                FPS = 60,
                AttachToClientArea = true,
                BypassTopmost = true,
            };
            _window.SetupGraphics += _window_SetupGraphics;
            _window.DestroyGraphics += _window_DestroyGraphics;
            _window.DrawGraphics += _window_DrawGraphics;

            _window.Create();
        }

        private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
            _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
            _brushes["background"] = gfx.CreateSolidBrush(0, 0x27, 0x31, 255.0f * 0.0f);

            Console.WriteLine(_window.Handle.ToString("X"));

            // fonts don't need to be recreated since they are owned by the font factory and not the drawing device
            if (e.RecreateResources) return;

            _fonts.Add("arial", gfx.CreateFont("Arial", 14));
        }

        private void _window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            foreach (var pair in _brushes) pair.Value.Dispose();
            foreach (var pair in _fonts) pair.Value.Dispose();
            foreach (var pair in _images) pair.Value.Dispose();
        }

        private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            gfx.ClearScene(_brushes["background"]);
            GameOverlay.Drawing.SolidBrush brushOL = _brushes["white"];
            //the Overlay is drawing at correct position
            gfx.DrawText(_fonts["arial"], 22, brushOL, 0, 0, $"Overlay FPS: {gfx.FPS}");
            gfx.DrawText(_fonts["arial"], 22, brushOL, 1000, 740, $"LR");
            if (npcNameFinder.Npcs.Any())
            {

                //None = 0,
                //Enemy = 1,
                //Friendly = 2,
                //Neutral = 4,
                //Corpse = 8
                //we could use white by default, sometimes the same color would confuse the color processing for npcnamefinder
                if (npcNameFinder.nameType.HasFlag(NpcNames.Neutral))
                {
                    brushOL = gfx.CreateSolidBrush(NpcNameFinder.sN_R, NpcNameFinder.sN_G, NpcNameFinder.sN_B);
                }
                if (npcNameFinder.nameType.HasFlag(NpcNames.Enemy))
                {
                    brushOL = gfx.CreateSolidBrush(NpcNameFinder.sE_R, NpcNameFinder.sE_G, NpcNameFinder.sE_B);
                }
                if (npcNameFinder.nameType.HasFlag(NpcNames.Friendly))
                {
                    brushOL = gfx.CreateSolidBrush(NpcNameFinder.sF_R, NpcNameFinder.sF_G, NpcNameFinder.sF_B);
                }
                if (npcNameFinder.nameType.HasFlag(NpcNames.Corpse))
                {
                    brushOL = gfx.CreateSolidBrush(NpcNameFinder.fC_RGB, NpcNameFinder.fC_RGB, NpcNameFinder.fC_RGB);
                }

                //the original image drawing from NPCNamefinder seems off, would be cool to remove this calculator someday.
                int topoffset = 35;

                gfx.DrawRectangle(brushOL, npcNameFinder.Area.Left, npcNameFinder.Area.Top - topoffset, npcNameFinder.Area.Right, npcNameFinder.Area.Bottom - topoffset, 2.0f);

                //paint.DrawRectangle(whitePen, npcNameFinder.Area);

                int j = 0;
                foreach (var npc in npcNameFinder.Npcs)
                {
                    if (debugTargeting)
                    {

                        foreach (var l in npcNameTargeting.locTargeting)
                        {
                            //paint.DrawEllipse(whitePen, l.X + npc.ClickPoint.X, l.Y + npc.ClickPoint.Y, 5, 5);
                            gfx.DrawCircle(brushOL, l.X + npc.ClickPoint.X, l.Y + npc.ClickPoint.Y - topoffset, 5, 2);
                        }
                    }

                    if (debugSkinning)
                    {
                        int c = npcNameTargeting.locFindBy.Length;
                        int ex = 3;
                        Point[] attemptPoints = new Point[c + (c * ex)];
                        for (int i = 0; i < c; i += ex)
                        {
                            Point p = npcNameTargeting.locFindBy[i];
                            attemptPoints[i] = p;
                            attemptPoints[i + c] = new Point(npc.Rect.Width / 2, p.Y).Scale(npcNameFinder.ScaleToRefWidth, npcNameFinder.ScaleToRefHeight);
                            attemptPoints[i + c + 1] = new Point(-npc.Rect.Width / 2, p.Y).Scale(npcNameFinder.ScaleToRefWidth, npcNameFinder.ScaleToRefHeight);
                        }

                        foreach (var l in attemptPoints)
                        {
                            gfx.DrawCircle(brushOL, l.X + npc.ClickPoint.X, l.Y + npc.ClickPoint.Y - topoffset, 5, 2);
                        }
                    }

                    //paint.DrawRectangle(whitePen, npc.Rect);
                    //paint.DrawString(j.ToString(), font, brushOL, new PointF(npc.Left - 20f, npc.Top));
                    gfx.DrawRectangle(brushOL, npc.Rect.Left, npc.Rect.Top - topoffset, npc.Rect.Right, npc.Rect.Bottom - topoffset, 2);
                    gfx.DrawText(_fonts["arial"], 10, brushOL, npc.Rect.Left - 20f, npc.Rect.Top - topoffset, j.ToString());
                    j++;
                }
            }
        }

        public void Execute()
        {
            if (LogEachUpdate)
                stopwatch.Restart();

            capturer.Capture();

            if (LogEachUpdate)
                logger.LogInformation($"Capture: {stopwatch.ElapsedMilliseconds}ms");

            if (LogEachUpdate)
                stopwatch.Restart();

            npcNameFinder.Update();

            if (LogEachUpdate)
                logger.LogInformation($"Update: {stopwatch.ElapsedMilliseconds}ms");

            if (saveImage)
            {
                SaveImage();
            }

            if (LogEachUpdate && LogShowResult)
            {
                stringBuilder.Length = 0;

                if (npcNameFinder.Npcs.Any())
                    stringBuilder.AppendLine();

                int i = 0;
                foreach (NpcPosition n in npcNameFinder.Npcs)
                {
                    stringBuilder.Append($"{i,2}");
                    stringBuilder.Append(" -> rect=");
                    stringBuilder.Append(n.Rect);
                    stringBuilder.Append(" ClickPoint=");
                    stringBuilder.AppendLine($"{{{n.ClickPoint.X,4},{n.ClickPoint.Y,4}}}");
                    i++;
                }

                logger.LogInformation(stringBuilder.ToString());
            }
        }

        private void SaveImage()
        {
            if (npcNameFinder.Npcs.Any())
            {
                paint.DrawRectangle(whitePen, npcNameFinder.Area);

                int j = 0;
                foreach (var npc in npcNameFinder.Npcs)
                {
                    if (debugTargeting)
                    {
                        foreach (var l in npcNameTargeting.locTargeting)
                        {
                            paint.DrawEllipse(whitePen, l.X + npc.ClickPoint.X, l.Y + npc.ClickPoint.Y, 5, 5);
                        }
                    }

                    if (debugSkinning)
                    {
                        int c = npcNameTargeting.locFindBy.Length;
                        int e = 3;
                        Point[] attemptPoints = new Point[c + (c * e)];
                        for (int i = 0; i < c; i += e)
                        {
                            Point p = npcNameTargeting.locFindBy[i];
                            attemptPoints[i] = p;
                            attemptPoints[i + c] = new Point(npc.Rect.Width / 2, p.Y).Scale(npcNameFinder.ScaleToRefWidth, npcNameFinder.ScaleToRefHeight);
                            attemptPoints[i + c + 1] = new Point(-npc.Rect.Width / 2, p.Y).Scale(npcNameFinder.ScaleToRefWidth, npcNameFinder.ScaleToRefHeight);
                        }

                        foreach (var l in attemptPoints)
                        {
                            paint.DrawEllipse(whitePen, l.X + npc.ClickPoint.X, l.Y + npc.ClickPoint.Y, 5, 5);
                        }
                    }

                    paint.DrawRectangle(whitePen, npc.Rect);
                    paint.DrawString(j.ToString(), font, brush, new PointF(npc.Rect.Left - 20f, npc.Rect.Top));
                    j++;
                }
            }

            paintBitmap.Save("target_names.png");
        }
    }
}
