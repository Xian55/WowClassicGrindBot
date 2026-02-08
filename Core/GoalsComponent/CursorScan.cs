using Game;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core.Goals;

public sealed partial class CursorScan : IDisposable
{
    private readonly ILogger<CursorScan> logger;
    private readonly CancellationToken token;
    private readonly IWowScreen screen;
    private readonly IMouseInput mouseInput;
    private readonly Wait wait;

    private readonly CursorClassifier classifier;

    public CursorScan(ILogger<CursorScan> logger,
        CancellationTokenSource cts,
        IWowScreen screen,
        IMouseInput mouseInput,
        Wait wait)
    {
        this.logger = logger;
        token = cts.Token;
        this.screen = screen;
        this.mouseInput = mouseInput;
        this.wait = wait;

        classifier = new();
    }

    public void Dispose()
    {
        classifier.Dispose();
    }

    /// <summary>
    /// Checks if the current cursor matches any of the specified types without moving the mouse.
    /// </summary>
    /// <param name="targetCursors">The cursor types to match against.</param>
    /// <param name="foundCursor">The cursor type that was found, if any.</param>
    /// <returns>True if current cursor matches any target type, false otherwise.</returns>
    public bool TryMatchCurrent(ReadOnlySpan<CursorType> targetCursors, out CursorType foundCursor)
    {
        classifier.Classify(out CursorType current, out _);

        for (int i = 0; i < targetCursors.Length; i++)
        {
            if (current == targetCursors[i])
            {
                foundCursor = current;
                return true;
            }
        }

        foundCursor = CursorType.None;
        return false;
    }

    /// <summary>
    /// Scans in a spiral pattern from the screen center looking for the specified cursor type.
    /// </summary>
    /// <param name="targetCursor">The cursor type to find.</param>
    /// <param name="foundPosition">The position where the cursor was found, if any.</param>
    /// <param name="stepSize">Pixels between scan points (default 20).</param>
    /// <param name="maxRadius">Maximum distance from center to scan (default 200).</param>
    /// <returns>True if cursor type was found, false otherwise.</returns>
    public bool Find(CursorType targetCursor, out Point foundPosition, int stepSize = 20, int maxRadius = 200)
    {
        screen.GetRectangle(out Rectangle screenRect);
        Point center = new(screenRect.Width / 2, screenRect.Height / 2);

        return FindFrom(targetCursor, center, out foundPosition, stepSize, maxRadius);
    }

    /// <summary>
    /// Scans in a spiral pattern from a given center point looking for the specified cursor type.
    /// </summary>
    /// <param name="targetCursor">The cursor type to find.</param>
    /// <param name="center">The center point to start scanning from.</param>
    /// <param name="foundPosition">The position where the cursor was found, if any.</param>
    /// <param name="stepSize">Pixels between scan points (default 20).</param>
    /// <param name="maxRadius">Maximum distance from center to scan (default 200).</param>
    /// <returns>True if cursor type was found, false otherwise.</returns>
    [SkipLocalsInit]
    public bool FindFrom(CursorType targetCursor, Point center, out Point foundPosition, int stepSize = 20, int maxRadius = 200)
    {
        screen.GetRectangle(out Rectangle screenRect);

        // Spiral state: direction vectors
        int x = 0, y = 0;
        int dx = 1, dy = 0;
        int stepsInDirection = 1;
        int stepsTaken = 0;
        int directionChanges = 0;

        if (logger.IsEnabled(LogLevel.Debug))
            LogScanStart(logger, targetCursor.ToStringF(), center, stepSize, maxRadius);

        while (Math.Max(Math.Abs(x), Math.Abs(y)) * stepSize <= maxRadius)
        {
            if (token.IsCancellationRequested)
            {
                foundPosition = default;
                return false;
            }

            Point scanPoint = new(center.X + x * stepSize, center.Y + y * stepSize);

            if (screenRect.Contains(scanPoint))
            {
                mouseInput.SetCursorPos(scanPoint);
                wait.Update();

                classifier.Classify(out CursorType cls, out double similarity);
                if (cls == targetCursor)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                        LogScanFound(logger, targetCursor.ToStringF(), scanPoint, similarity);
                    foundPosition = scanPoint;
                    return true;
                }
            }

            // Move in spiral pattern: right -> down -> left -> up -> right
            x += dx;
            y += dy;
            stepsTaken++;

            if (stepsTaken >= stepsInDirection)
            {
                stepsTaken = 0;
                // Rotate direction 90 degrees clockwise
                int temp = dx;
                dx = -dy;
                dy = temp;
                directionChanges++;
                if (directionChanges % 2 == 0)
                    stepsInDirection++;
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
            LogScanNotFound(logger, targetCursor.ToStringF());
        foundPosition = default;
        return false;
    }

    /// <summary>
    /// Scans in a spiral pattern from the screen center looking for any of the specified cursor types.
    /// </summary>
    /// <param name="targetCursors">The cursor types to find (any match succeeds).</param>
    /// <param name="foundCursor">The cursor type that was found, if any.</param>
    /// <param name="foundPosition">The position where the cursor was found, if any.</param>
    /// <param name="stepSize">Pixels between scan points (default 20).</param>
    /// <param name="maxRadius">Maximum distance from center to scan (default 200).</param>
    /// <returns>True if any cursor type was found, false otherwise.</returns>
    public bool FindAny(ReadOnlySpan<CursorType> targetCursors, out CursorType foundCursor, out Point foundPosition, int stepSize = 20, int maxRadius = 200)
    {
        screen.GetRectangle(out Rectangle screenRect);
        Point center = new(screenRect.Width / 2, screenRect.Height / 2);

        return FindAnyFrom(targetCursors, center, out foundCursor, out foundPosition, stepSize, maxRadius);
    }

    /// <summary>
    /// Scans in a spiral pattern from a given center point looking for any of the specified cursor types.
    /// </summary>
    /// <param name="targetCursors">The cursor types to find (any match succeeds).</param>
    /// <param name="center">The center point to start scanning from.</param>
    /// <param name="foundCursor">The cursor type that was found, if any.</param>
    /// <param name="foundPosition">The position where the cursor was found, if any.</param>
    /// <param name="stepSize">Pixels between scan points (default 20).</param>
    /// <param name="maxRadius">Maximum distance from center to scan (default 200).</param>
    /// <returns>True if any cursor type was found, false otherwise.</returns>
    [SkipLocalsInit]
    public bool FindAnyFrom(ReadOnlySpan<CursorType> targetCursors, Point center, out CursorType foundCursor, out Point foundPosition, int stepSize = 20, int maxRadius = 200)
    {
        screen.GetRectangle(out Rectangle screenRect);

        // Spiral state: direction vectors
        int x = 0, y = 0;
        int dx = 1, dy = 0;
        int stepsInDirection = 1;
        int stepsTaken = 0;
        int directionChanges = 0;

        while (Math.Max(Math.Abs(x), Math.Abs(y)) * stepSize <= maxRadius)
        {
            if (token.IsCancellationRequested)
            {
                foundCursor = CursorType.None;
                foundPosition = default;
                return false;
            }

            Point scanPoint = new(center.X + x * stepSize, center.Y + y * stepSize);

            if (screenRect.Contains(scanPoint))
            {
                mouseInput.SetCursorPos(scanPoint);
                wait.Update();

                classifier.Classify(out CursorType cls, out double similarity);

                for (int i = 0; i < targetCursors.Length; i++)
                {
                    if (cls == targetCursors[i])
                    {
                        if (logger.IsEnabled(LogLevel.Information))
                            LogScanFound(logger, cls.ToStringF(), scanPoint, similarity);
                        foundCursor = cls;
                        foundPosition = scanPoint;
                        return true;
                    }
                }
            }

            // Move in spiral pattern: right -> down -> left -> up -> right
            x += dx;
            y += dy;
            stepsTaken++;

            if (stepsTaken >= stepsInDirection)
            {
                stepsTaken = 0;
                // Rotate direction 90 degrees clockwise
                int temp = dx;
                dx = -dy;
                dy = temp;
                directionChanges++;
                if (directionChanges % 2 == 0)
                    stepsInDirection++;
            }
        }

        foundCursor = CursorType.None;
        foundPosition = default;
        return false;
    }

    #region Logging

    [LoggerMessage(
        EventId = 0180,
        Level = LogLevel.Debug,
        Message = "Cursor scan start: searching for {cursorType} from {center} (step={stepSize}, maxRadius={maxRadius})")]
    static partial void LogScanStart(ILogger logger, string cursorType, Point center, int stepSize, int maxRadius);

    [LoggerMessage(
        EventId = 0181,
        Level = LogLevel.Information,
        Message = "Cursor scan found: {cursorType} at {position} (similarity={similarity:F1}%)")]
    static partial void LogScanFound(ILogger logger, string cursorType, Point position, double similarity);

    [LoggerMessage(
        EventId = 0182,
        Level = LogLevel.Debug,
        Message = "Cursor scan complete: {cursorType} not found")]
    static partial void LogScanNotFound(ILogger logger, string cursorType);

    #endregion
}
