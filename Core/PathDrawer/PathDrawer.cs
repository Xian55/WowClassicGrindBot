using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net.Http;
using System.Numerics;

namespace Core;

public static class PathDrawer
{
    private const int BORDER_SIZE = 40;

    private const int MIN_SIZE = 200;
    private const int UPSCALE_BELOW_MIN_SIZE = 2;

    private const float MAP_SCALAR = 100f;
    private const float RADIUS = 3f;

    public static void Execute(
        List<Vector3> mapPath, string imgUrl, string outputPath)
    {
        if (!ValidMapCoordinates(mapPath))
            return;

        List<PointF> points = new(mapPath.Count);

        points.AddRange(mapPath.ConvertAll(
            p => new PointF(p.X / MAP_SCALAR, p.Y / MAP_SCALAR)));

        Bitmap background = DownloadBitmap(imgUrl);

        using Graphics g = Graphics.FromImage(background);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;

        float width = background.Width;
        float height = background.Height;
        DrawPath(g, points, width, height, RADIUS);

        Rectangle rect = CalculateBounds(points, width, height);

        Font font = SystemFonts.DefaultFont;

        // Black background
        PointF startPoint = points.First();
        string startText =
            $"{startPoint.X * MAP_SCALAR:0.##} " +
            $"{startPoint.Y * MAP_SCALAR:0.##}";

        SizeF sizeStart = g.MeasureString(startText, font);

        Rectangle bottomInfoPanelRect = new(
            new(rect.X, rect.Bottom - (int)sizeStart.Height),
            new(rect.Width, (int)sizeStart.Height));

        g.FillRectangle(Brushes.Black, bottomInfoPanelRect);

        // xx.xx yy.yy
        Rectangle startRect = new(
            new(bottomInfoPanelRect.X, bottomInfoPanelRect.Y),
            new((int)(sizeStart.Width + 1), bottomInfoPanelRect.Height));

        g.DrawString(startText, font, Brushes.White, startRect);

        Bitmap output =
            background.Clone(rect, PixelFormat.Format32bppArgb);

        if (output.Width < MIN_SIZE || output.Height < MIN_SIZE)
        {
            Bitmap scaled = new(output,
                output.Width * UPSCALE_BELOW_MIN_SIZE,
                output.Height * UPSCALE_BELOW_MIN_SIZE);

            output.Dispose();
            output = scaled;
        }

        ImageCodecInfo jpgEncoder = ImageCodecInfo.GetImageEncoders().
            First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        EncoderParameters encParams = new()
        {
            Param = [
                new EncoderParameter(Encoder.Quality, 100L)
            ]
        };

        output.Save(outputPath, jpgEncoder, encParams);
    }

    private static bool ValidMapCoordinates(List<Vector3> mapPath)
    {
        return !mapPath.Any(p =>
            p.X < 0 || p.X > MAP_SCALAR ||
            p.Y < 0 || p.Y > MAP_SCALAR);
    }

    private static Rectangle CalculateBounds(
        List<PointF> list, float width, float height)
    {
        float minX = list.Min(p => p.X);
        float minY = list.Min(p => p.Y);
        float maxX = list.Max(p => p.X);
        float maxY = list.Max(p => p.Y);

        RectangleF rect = new(
            new(minX, minY),
            new(maxX - minX, maxY - minY));

        rect.X *= width;
        rect.Y *= height;
        rect.Width *= width;
        rect.Height *= height;

        rect.Inflate(BORDER_SIZE, BORDER_SIZE);

        rect.X = MathF.Max(rect.X, 0);
        rect.Y = MathF.Max(rect.Y, 0);

        if (rect.X + rect.Width > width)
            rect.Width = width - rect.X;

        if (rect.Y + rect.Height > height)
            rect.Height = height - rect.Y;

        return Rectangle.Round(rect);
    }

    private static Bitmap DownloadBitmap(string url)
    {
        using HttpClient httpClient = new();
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        using HttpResponseMessage response = httpClient.Send(request);

        return new(response.Content.ReadAsStream());
    }

    private static void DrawPath(Graphics g,
        List<PointF> list, float width, float height, float radius)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            float shade = (float)i / list.Count;
            int step = 255 - (int)(255 * shade);
            Color redShade = Color.FromArgb(255, step, 0, 0);

            using SolidBrush brush = new(
                i == 0
                ? Color.White
                : redShade);

            PointF p = list[i];
            g.FillEllipse(brush,
                width * p.X, height * p.Y, radius, radius);
        }
    }
}
