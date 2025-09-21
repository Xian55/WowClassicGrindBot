using SixLabors.ImageSharp;

using System;

namespace Core;

public sealed class MinimapNodeEventArgs : EventArgs
{
    public int X { get; }
    public int Y { get; }
    public int Amount { get; }
    public Rectangle Rect { get; }

    public MinimapNodeEventArgs(int x, int y, int amount, Rectangle rect)
    {
        X = x;
        Y = y;
        Amount = amount;
        Rect = rect;
    }
}