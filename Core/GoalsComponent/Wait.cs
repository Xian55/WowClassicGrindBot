using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using static System.Diagnostics.Stopwatch;

namespace Core;

public sealed class Wait
{
    private readonly ManualResetEventSlim globalTime;
    private readonly CancellationToken token;

    public Wait(ManualResetEventSlim globalTime, CancellationTokenSource cts)
    {
        this.globalTime = globalTime;
        this.token = cts.Token;
    }

    public void Update()
    {
        globalTime.Wait();
        globalTime.Reset();
    }

    public void Update(CancellationToken token = default)
    {
        try
        {
            globalTime.Wait(token);
        }
        catch (OperationCanceledException) { }

        globalTime.Reset();
    }

    public bool Update(int timeoutMs)
    {
        bool result = globalTime.Wait(timeoutMs);
        if (!result)
        {
            return result;
        }

        globalTime.Reset();
        return result;
    }

    public void Fixed(int durationMs)
    {
        token.WaitHandle.WaitOne(durationMs);
    }

    [SkipLocalsInit]
    public bool Till(int timeoutMs, Func<bool> interrupt)
    {
        long start = GetTimestamp();
        while (GetElapsedTime(start).TotalMilliseconds < timeoutMs)
        {
            if (interrupt())
                return false;

            Update();
        }

        return true;
    }

    [SkipLocalsInit]
    public float Until(int timeoutMs, Func<bool> interrupt)
    {
        long start = GetTimestamp();
        float elapsedMs;
        while ((elapsedMs = (float)GetElapsedTime(start).TotalMilliseconds) < timeoutMs)
        {
            if (interrupt())
                return elapsedMs;

            Update();
        }

        return -elapsedMs;
    }

    public float UntilCount(int count, Func<bool> interrupt)
    {
        long start = GetTimestamp();
        for (int i = 0; i < count; i++)
        {
            if (interrupt())
                return (float)GetElapsedTime(start).TotalMilliseconds;

            Update();
        }
        return -(float)GetElapsedTime(start).TotalMilliseconds;
    }

    [SkipLocalsInit]
    public float Until(int timeoutMs, CancellationToken token)
    {
        long start = GetTimestamp();
        float elapsedMs;
        while ((elapsedMs = (float)GetElapsedTime(start).TotalMilliseconds) < timeoutMs)
        {
            if (token.IsCancellationRequested)
                return elapsedMs;

            Update();
        }

        return -elapsedMs;
    }

    [SkipLocalsInit]
    public float Until(int timeoutMs, Func<bool> interrupt, Action repeat)
    {
        long start = GetTimestamp();
        float elapsedMs;
        while ((elapsedMs = (float)GetElapsedTime(start).TotalMilliseconds) < timeoutMs)
        {
            repeat.Invoke();
            if (interrupt())
                return elapsedMs;

            Update();
        }

        return -elapsedMs;
    }

    [SkipLocalsInit]
    public float UntilWithoutRepeat(int timeoutMs, Func<bool> interrupt, Action repeat)
    {
        long start = GetTimestamp();
        float elapsedMs;
        while ((elapsedMs = (float)GetElapsedTime(start).TotalMilliseconds) < timeoutMs)
        {
            if (interrupt())
                return elapsedMs;
            else
                repeat.Invoke();

            Update();
        }

        return -elapsedMs;
    }

    [SkipLocalsInit]
    public float AfterEquals<T>(int timeoutMs, int updateCount, Func<T> func, Action? repeat = null)
    {
        long start = GetTimestamp();
        float elapsedMs;
        while ((elapsedMs = (float)GetElapsedTime(start).TotalMilliseconds) < timeoutMs)
        {
            T initial = func();

            repeat?.Invoke();

            for (int i = 0; i < updateCount; i++)
                Update();

            if (EqualityComparer<T>.Default.Equals(initial, func()))
                return elapsedMs;
        }

        return -elapsedMs;
    }

    public void While(Func<bool> condition, CancellationToken token = default)
    {
        while (!token.IsCancellationRequested && condition())
        {
            Update(token);
        }
    }
}
