using Core.Cursor;
using Core.NpcFinder;
using Core.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using SharedLib;
using Game;

namespace Core
{
    public class NpcNameFinder
    {
        private const int MOUSE_DELAY = 40;

        public enum NPCType
        {
            Enemy,
            Friendly,
            Neutral,
            FriendlyOrNeutral,
            Corpse
        }

        private NPCType _NPCType = NPCType.Enemy;

        private bool ColorMatch(Color pixel)
        {
            switch(_NPCType)
            {
                case NPCType.FriendlyOrNeutral:
                    return (pixel.R == 0 && pixel.G > 250 && pixel.B == 0) ||
                        (pixel.R > 250 && pixel.G > 250 && pixel.B == 0);
                case NPCType.Friendly:
                    return pixel.R == 0 && pixel.G > 250 && pixel.B == 0;
                case NPCType.Neutral:
                    return pixel.R > 250 && pixel.G > 250 && pixel.B == 0;
                case NPCType.Corpse:
                    return pixel.R == 128 && pixel.G == 128 && pixel.B == 128;
                case NPCType.Enemy:
                default:
                    return pixel.R > 240 && pixel.G <= 35 && pixel.B <= 35;
            }
        }

        private List<LineOfNpcName> npcNameLine { get; set; } = new List<LineOfNpcName>();
        private List<List<LineOfNpcName>> npcs { get; set; } = new List<List<LineOfNpcName>>();

        private readonly ILogger logger;
        private readonly IDirectBitmapProvider bitmapProvider;
        private readonly IMouseInput mouseInput;

        public Rectangle Area { private set; get; }

        private float scaleToRefWidth = 1;
        private float scaleToRefHeight = 1;

        public List<Point> locTargetingAndClickNpc { get; private set; }
        public List<Point> locFindByCursorType { get; private set; }

        public List<NpcPosition> Npcs { get; private set; } = new List<NpcPosition>();
        public int NpcCount => npcs.Count;

        public int Sequence { get; private set; } = 0;

        public NpcNameFinder(ILogger logger, IDirectBitmapProvider bitmapProvider, IMouseInput mouseInput)
        {
            this.logger = logger;
            this.mouseInput = mouseInput;
            this.bitmapProvider = bitmapProvider;

            locTargetingAndClickNpc = new List<Point>
            {
                new Point(0, 0),
                new Point(-10, 15).Scale(scaleToRefWidth, scaleToRefHeight),
                new Point(10, 15).Scale(scaleToRefWidth, scaleToRefHeight),
            };

            locFindByCursorType = new List<Point>
            {
                new Point(0, 0),
                new Point(0, 25).Scale(scaleToRefWidth, scaleToRefHeight),
                new Point(-25, 50).Scale(scaleToRefWidth, scaleToRefHeight),
                new Point(25, 50).Scale(scaleToRefWidth, scaleToRefHeight),
                new Point(0, 90).Scale(scaleToRefWidth, scaleToRefHeight),
                new Point(0, 130).Scale(scaleToRefWidth, scaleToRefHeight),
                new Point(0, 160).Scale(scaleToRefWidth, scaleToRefHeight),
            };

            locFindByCursorType.Reverse();
        }

        private float ScaleWidth(int value)
        {
            const float refWidth = 1920;
            return value * (bitmapProvider.DirectBitmap.Width / refWidth);
        }

        private float ScaleHeight(int value)
        {
            const float refHeight = 1080;
            return value * (bitmapProvider.DirectBitmap.Height/ refHeight);
        }

        public void ChangeNpcType(NPCType type)
        {
            if(_NPCType != type)
            {
                _NPCType = type;
                logger.LogInformation($"{GetType().Name}.ChangeNpcType = {type}");
            }
        }

        public async Task TargetingAndClickNpc(int threshold, bool leftClick)
        {
            var npc = GetClosestNpc();
            if (npc != null)
            {
                if (npc.Height >= threshold)
                {
                    foreach (var location in locTargetingAndClickNpc)
                    {
                        var clickPostion = bitmapProvider.DirectBitmap.ToScreenCoordinates(npc.ClickPoint.X + location.X, npc.ClickPoint.Y + location.Y);
                        mouseInput.SetCursorPosition(clickPostion);
                        await Task.Delay(MOUSE_DELAY);
                        CursorClassifier.Classify(out var cls).Dispose();
                        if (cls == CursorClassification.Kill)
                        {
                            await AquireTargetAtCursor(clickPostion, npc, leftClick);
                            return;
                        }
                        else if(cls == CursorClassification.Vendor)
                        {
                            await AquireTargetAtCursor(clickPostion, npc, leftClick);
                            return;
                        }
                    }
                }
                else
                {
                    logger.LogInformation($"{this.GetType().Name}.FindAndClickNpc: NPC found but below threshold {threshold}! Height={npc.Height}, width={npc.Width}");
                }
            }
            else
            {
                //logger.LogInformation($"{ this.GetType().Name}.FindAndClickNpc: No NPC found!");
            }
        }

        public async Task<bool> FindByCursorType(params CursorClassification[] cursor)
        {
            foreach (var npc in Npcs)
            {
                foreach (var location in locFindByCursorType)
                {
                    var clickPostion = bitmapProvider.DirectBitmap.ToScreenCoordinates(npc.ClickPoint.X + location.X, npc.ClickPoint.Y + location.Y);
                    mouseInput.SetCursorPosition(clickPostion);
                    await Task.Delay(MOUSE_DELAY);
                    CursorClassifier.Classify(out var cls).Dispose();
                    if (cursor.Contains(cls))
                    {
                        await AquireTargetAtCursor(clickPostion, npc);
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task AquireTargetAtCursor(Point clickPostion, NpcPosition npc, bool leftClick=false)
        {
            if(leftClick)
                await mouseInput.LeftClickMouse(clickPostion);
            else
                await mouseInput.RightClickMouse(clickPostion);

            logger.LogInformation($"{ this.GetType().Name}.FindAndClickNpc: NPC found! Height={npc.Height}, width={npc.Width}, pos={clickPostion}");
        }

        public void Update()
        {
            scaleToRefWidth = ScaleWidth(1);
            scaleToRefHeight = ScaleHeight(1);

            Area = new Rectangle(new Point(0, (int)ScaleHeight(30)), 
                new Size(bitmapProvider.DirectBitmap.Width, bitmapProvider.DirectBitmap.Height / 2));

            PopulateLinesOfNpcNames();
            DetermineNpcs();

            Npcs = npcs.
                Select(s => new NpcPosition(new Point(s.Min(x => x.XStart), s.Min(x => x.Y)),
                    new Point(s.Max(x => x.XEnd), s.Max(x => x.Y)), bitmapProvider.DirectBitmap.Width, ScaleHeight(20), ScaleHeight(5)))
                .Where(s => s.Width < ScaleWidth(250)) // 150 - fine // 200 - fine // 250 fine
                .Distinct(new OverlappingNames())
                .OrderBy(npc => RectangleExt.SqrDistance(Area.BottomCentre(), npc.ClickPoint))
                .ToList();

            Sequence++;

            UpdatePotentialAddsExist();
        }

        public bool MobsVisible => npcs.Count > 0;

        public bool PotentialAddsExist { get; private set; }
        public DateTime LastPotentialAddsSeen { get; private set; } = default;

        public void UpdatePotentialAddsExist()
        {
            var countAdds = Npcs.Where(c => c.IsAdd).Where(c => c.Height > ScaleHeight(2)).Count();
            var MobsVisible = Npcs.Where(c => c.Height > ScaleHeight(2)).Any();

            if (countAdds > 0)
            {
                PotentialAddsExist = true;
                LastPotentialAddsSeen = DateTime.Now;
            }
            else
            {
                if (PotentialAddsExist && (DateTime.Now - LastPotentialAddsSeen).TotalSeconds > 2)
                {
                    PotentialAddsExist = false;
                }
            }
        }

        public NpcPosition? GetClosestNpc()
        {
            var info = string.Join(", ", Npcs.Select(n => n.Height.ToString() + $"({n.Min.X},{n.Min.Y})"));
            if(!string.IsNullOrEmpty(info))
                logger.LogInformation($"> NPCs found: {info}");

            return Npcs.Count == 0 ? null : Npcs.First();
        }

        private void DetermineNpcs()
        {
            npcs = new List<List<LineOfNpcName>>();
            for (int i = 0; i < npcNameLine.Count; i++)
            {
                var npcLine = this.npcNameLine[i];
                var group = new List<LineOfNpcName>() { npcLine };
                var lastY = npcLine.Y;

                if (!npcLine.IsInAgroup)
                {
                    for (int j = i + 1; j < this.npcNameLine.Count; j++)
                    {
                        var laterNpcLine = this.npcNameLine[j];
                        if (laterNpcLine.Y > npcLine.Y + ScaleHeight(10)) { break; }
                        if (laterNpcLine.Y > lastY + ScaleHeight(5)) { break; } // 2

                        if (laterNpcLine.XStart <= npcLine.X && laterNpcLine.XEnd >= npcLine.X && laterNpcLine.Y > lastY)
                        {
                            laterNpcLine.IsInAgroup = true;
                            group.Add(laterNpcLine);
                            lastY = laterNpcLine.Y;
                        }
                    }
                    if (group.Count > 0) { npcs.Add(group); }
                }
            }
        }

        private void PopulateLinesOfNpcNames()
        {
            npcNameLine = new List<LineOfNpcName>();

            float minLength = ScaleWidth(15); // original 22 // 18 fine
            float lengthDiff = ScaleWidth(4);
            float minEndLength = minLength - lengthDiff; // original 18

            bool isEndOfSection;
            for (int y = Area.Top; y < Area.Height; y++)
            {
                var lengthStart = -1;
                var lengthEnd = -1;
                for (int x = Area.Left; x < Area.Right; x++)
                {
                    var pixel = bitmapProvider.DirectBitmap.GetPixel(x, y);
                    var isTargetColor = ColorMatch(pixel);

                    if (isTargetColor)
                    {
                        var isSameSection = lengthStart > -1 && (x - lengthEnd) < minLength;

                        if (isSameSection)
                        {
                            lengthEnd = x;
                        }
                        else
                        {
                            isEndOfSection = lengthStart > -1 && lengthEnd - lengthStart > minEndLength;

                            if (isEndOfSection)
                            {
                                npcNameLine.Add(new LineOfNpcName(lengthStart, lengthEnd, y));
                            }

                            lengthStart = x;
                        }
                        lengthEnd = x;
                    }
                }

                isEndOfSection = lengthStart > -1 && lengthEnd - lengthStart > minEndLength;
                if (isEndOfSection)
                {
                    npcNameLine.Add(new LineOfNpcName(lengthStart, lengthEnd, y));
                }
            }
        }

        public async Task WaitForNUpdate(int n)
        {
            var s = this.Sequence;
            while (this.Sequence <= s + n)
            {
                await Task.Delay(25);
            }
        }

        public void ShowNames(Graphics gr)
        {
            if (Npcs.Count > 0)
            {
                using (var whitePen = new Pen(Color.White, 3))
                {
                    Npcs.ForEach(n => gr.DrawRectangle(whitePen, new Rectangle(n.Min, new Size(n.Width, n.Height))));

                    /*
                    Npcs.ForEach(n =>
                    {
                        locFindByCursorType.ForEach(l =>
                        {
                            gr.DrawEllipse(whitePen, l.X + n.ClickPoint.X, l.Y + n.ClickPoint.Y, 5, 5);
                        });
                    });
                    */
                }
            }
        }
    }
}