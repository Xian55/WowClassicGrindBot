using Core;

using Game;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Extensions.Logging;

using SharedLib;
using SharedLib.NpcFinder;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

#pragma warning disable 0162

namespace CoreTests;

internal sealed class Program
{
    private static Microsoft.Extensions.Logging.ILogger logger;
    private static ILoggerFactory loggerFactory;

    private static CancellationTokenSource cts;
    private static WowProcess process;
    private static WowScreenWGC screen;

    private const bool LogOverallTimes = false;
    private const bool UseGpu = true;
    private const int delay = 150;

    public static void Main()
    {
        var logConfig = new LoggerConfiguration()
            .WriteTo.File("names.log")
            .WriteTo.Debug()
            .CreateLogger();

        Log.Logger = logConfig;
        logger = new SerilogLoggerProvider(Log.Logger).CreateLogger(nameof(Program));


        //var a = new PPatherV2.PPatherV2(logger, DataConfig.Load("tbc"));
        //return;

        loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders().AddSerilog();
        });

        //PPatherV2.PPatherV2 pPather = new(logger, DataConfig.Load(ClientVersion.SoM.ToStringF()));
        //Environment.Exit(0);
        //return;


        // its expected to have at least 2 DataFrame 
        DataFrame[] mockFrames =
        [
            new DataFrame(0, 0, 0),
            new DataFrame(1, 0, 0),
        ];

        cts = new CancellationTokenSource();
        process = new(cts, Options.Create<StartupConfigPid>(new() { Id = -1 }));
        //screen = new WowScreenDXGI(loggerFactory.CreateLogger<WowScreenDXGI>(), process, mockFrames);
        screen = new WowScreenWGC(loggerFactory.CreateLogger<WowScreenWGC>(), process, mockFrames);

        Test_NPCNameFinder();
        //Test_Input();
        //Test_CursorGrabber();
        //Test_CursorCompare();
        //Test_MinimapNodeFinder();
        //Test_FindTargetByCursor();

        Log.CloseAndFlush();
        Environment.Exit(0);
    }

    private static void Test_NPCNameFinder()
    {
        //NpcNames types = NpcNames.Enemy;
        //NpcNames types = NpcNames.Corpse;
        //NpcNames types = NpcNames.Neutral;
        NpcNames types = NpcNames.Enemy | NpcNames.Neutral;
        //NpcNames types = NpcNames.Enemy | NpcNames.Neutral | NpcNames.NamePlate;
        //NpcNames types = NpcNames.Friendly | NpcNames.Neutral;

        using Test_NpcNameFinder test = new(logger, process, screen, loggerFactory, types, UseGpu);
        int count = 100;
        int i = 0;

        long timestamp = Stopwatch.GetTimestamp();
        double[] sample = new double[count];

        double[] captures = new double[count];
        double[] updates = new double[count];

        Log.Logger.Information($"running {count} samples...");

        screen.Enabled = true;

        while (i < count)
        {
            if (LogOverallTimes)
                timestamp = Stopwatch.GetTimestamp();

            (captures[i], updates[i]) = test.Execute(delay);

            if (LogOverallTimes)
                sample[i] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            i++;
            Thread.Sleep(4);
        }

        screen.Enabled = false;

        if (LogOverallTimes)
        {
            Log.Logger.Information($"overall | sample: {count:D4} | avg: {sample.Average():F2} | min: {sample.Min():F2} | max: {sample.Max():000.000} | total: {sample.Sum():F2}");

            double meanCapture = captures.Average();
            double stdCapture = Math.Sqrt(captures.Select(t => Math.Pow(t - meanCapture, 2)).Average());
            double thresholdCapture = meanCapture + stdCapture;
            List<double> fCaptures = captures.Where(v => v <= thresholdCapture).ToList();

            Log.Logger.Information($"capture | sample: {fCaptures.Count:D4} | avg: {fCaptures.Average():F2} | min: {fCaptures.Min():F2} | max: {fCaptures.Max():000.000} | total: {fCaptures.Sum():F2} | std: {stdCapture:000.00} | thres: {thresholdCapture:F4}ms");

            double meanUpdate = updates.Average();
            double stdUpdate = Math.Sqrt(updates.Select(t => Math.Pow(t - meanCapture, 2)).Average());
            double thresholdUpdate = meanUpdate + stdUpdate;
            List<double> fUpdates = updates.Where(v => v <= thresholdUpdate).ToList();

            Log.Logger.Information($"updates | sample: {fUpdates.Count:D4} | avg: {fUpdates.Average():F2} | min: {fUpdates.Min():F2} | max: {fUpdates.Max():000.000} | total: {fUpdates.Sum():F2} | std: {stdUpdate:000.00} | thres: {thresholdUpdate:F4}ms");
        }
    }

    private static void Test_Input()
    {
        Test_Input test = new(logger, cts, process, screen, loggerFactory);
        test.Mouse_Movement();
        test.Mouse_Clicks();
        test.SendText();
    }

    private static void Test_CursorGrabber()
    {
        using CursorClassifier classifier = new();
        int i = 5;
        while (i > 0)
        {
            Thread.Sleep(1000);

            classifier.Classify(out CursorType cursorType, out _);
            Log.Logger.Information($"{cursorType.ToStringF()}");

            i--;
        }
    }

    private static void Test_CursorCompare()
    {
        using CursorClassifier classifier = new();
        const int count = 50;
        int i = 0;

        Span<double> times = stackalloc double[count];

        while (i < count)
        {
            Thread.Sleep(100);

            long startTime = Stopwatch.GetTimestamp();

            classifier.Classify(out CursorType cursorType, out double similarity);

            times[i] = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            Log.Logger.Information($"{cursorType.ToStringF()} {similarity} {times[i]:F6}ms");
            i++;
        }

        double sum = 0;
        double max = double.MinValue;
        double min = double.MaxValue;

        for (i = 0; i < times.Length - 1; i++)
        {
            double val = times[i];
            sum += val;
            if (val > max) max = val;
            else if (val < min) min = val;
        }

        Log.Logger.Information($"min:{min:F6} | max: {max:F5} | avg:{(sum / count):F6}");
    }

    private static void Test_MinimapNodeFinder()
    {
        void nodeEvent(object sender, MinimapNodeEventArgs e)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("[{X},{Y}] {Amount}", e.X, e.Y, e.Amount);
        }

        Test_MinimapNodeFinder test = new(logger, screen, nodeEvent);

        int count = 100;
        int i = 0;

        long timestamp = Stopwatch.GetTimestamp();
        double[] sample = new double[count];

        Log.Logger.Information($"running {count} samples...");

        screen.MinimapEnabled = true;

        while (i < count)
        {
            if (LogOverallTimes)
                timestamp = Stopwatch.GetTimestamp();

            test.Execute();

            if (LogOverallTimes)
                sample[i] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            i++;
            Thread.Sleep(delay);
        }

        screen.MinimapEnabled = false;

        if (LogOverallTimes)
            Log.Logger.Information($"sample: {count} | avg: {sample.Average():F2} | min: {sample.Min():F2} | max: {sample.Max():F2} | total: {sample.Sum()}");
    }

    private static void Test_FindTargetByCursor()
    {
        //CursorType cursorType = CursorType.Kill;
        ReadOnlySpan<CursorType> cursorType = [CursorType.Vendor];

        //NpcNames types = NpcNames.Enemy;
        //NpcNames types = NpcNames.Corpse;
        //NpcNames types = NpcNames.Enemy | NpcNames.Neutral;
        NpcNames types = NpcNames.Friendly | NpcNames.Neutral;

        using Test_NpcNameFinder test = new(logger, process, screen, loggerFactory, types, UseGpu);

        int count = 2;
        int i = 0;

        screen.Enabled = true;

        while (i < count)
        {
            test.Execute(delay);
            if (test.Execute_FindTargetBy(cursorType))
            {
                break;
            }

            i++;
            Thread.Sleep(delay);
        }

        screen.Enabled = false;
    }
}
