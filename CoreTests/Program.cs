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

namespace CoreTests;

internal sealed class Program
{
    private static Microsoft.Extensions.Logging.ILogger logger;
    private static ILoggerFactory loggerFactory;

    private static CancellationTokenSource cts;
    private static WowProcess process;
    private static IWowScreen screen;

    private static bool LogOverallTimes;
    private static bool LogEachUpdate = true;
    private static bool UseGpu = true;
    private static bool UseDxgi;
    private static int delay = 150;

    private static readonly Dictionary<string, Action<string[]>> suites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["npc"] = Test_NPCNameFinder,
        ["input"] = Test_Input,
        ["cursor-grab"] = Test_CursorGrabber,
        ["cursor-compare"] = Test_CursorCompare,
        ["minimap"] = Test_MinimapNodeFinder,
        ["find-target"] = Test_FindTargetByCursor,
        ["pather"] = Test_PPather,
    };

    public static void Main(string[] args)
    {
        var logConfig = new LoggerConfiguration()
            .WriteTo.File("names.log")
            .WriteTo.Debug()
            .WriteTo.Console()
            .CreateLogger();

        Log.Logger = logConfig;
        logger = new SerilogLoggerProvider(Log.Logger).CreateLogger(nameof(Program));

        loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders().AddSerilog();
        });

        List<string> remaining = [];
        for (int a = 0; a < args.Length; a++)
        {
            switch (args[a].ToLowerInvariant())
            {
                case "--log-times":
                    LogOverallTimes = true;
                    break;
                case "--no-gpu":
                    UseGpu = false;
                    break;
                case "--delay" when a + 1 < args.Length && int.TryParse(args[a + 1], out int d):
                    delay = d;
                    a++;
                    break;
                case "--no-log-update":
                    LogEachUpdate = false;
                    break;
                case "--dxgi":
                    UseDxgi = true;
                    break;
                default:
                    remaining.Add(args[a]);
                    break;
            }
        }

        // its expected to have at least 2 DataFrame
        DataFrame[] mockFrames =
        [
            new DataFrame(0, 0, 0),
            new DataFrame(1, 0, 0),
        ];

        cts = new CancellationTokenSource();
        process = new(cts, Options.Create<StartupConfigPid>(new() { Id = -1 }));
        screen = UseDxgi
            ? new WowScreenDXGI(loggerFactory.CreateLogger<WowScreenDXGI>(), process, mockFrames)
            : new WowScreenWGC(loggerFactory.CreateLogger<WowScreenWGC>(), process, mockFrames);

        if (remaining.Count > 0 && suites.TryGetValue(remaining[0], out Action<string[]> suite))
        {
            string[] suiteArgs = remaining.GetRange(1, remaining.Count - 1).ToArray();
            Log.Logger.Information("Suite: {Suite} | Args: {Args}", remaining[0], string.Join(", ", suiteArgs));
            suite(suiteArgs);
        }
        else
        {
            Log.Logger.Information("Available suites: {Suites}", string.Join(", ", suites.Keys));
            Log.Logger.Information("Global flags: --log-times, --no-gpu, --no-log-update, --dxgi, --delay <ms>");
            Log.Logger.Information("Example: dotnet run -c Release -- --log-times npc enemy neutral 10000");
        }

        Log.CloseAndFlush();
        Environment.Exit(0);
    }

    private static void Test_NPCNameFinder(string[] args)
    {
        NpcNames types = NpcNames.None;
        int count = 100;

        foreach (string arg in args)
        {
            if (int.TryParse(arg, out int n))
                count = n;
            else if (Enum.TryParse(arg, true, out NpcNames flag))
                types |= flag;
        }

        if (types == NpcNames.None)
            types = NpcNames.Friendly | NpcNames.Neutral;

        using Test_NpcNameFinder test = new(logger, process, screen, loggerFactory, types, UseGpu, LogEachUpdate);
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
            Array.Sort(sample);
            Array.Sort(captures);
            Array.Sort(updates);

            Log.Logger.Information($"overall | n: {count} | avg: {sample.Average():F2} | min: {sample.Min():F2} | p50: {Percentile(sample, 0.50):F2} | p95: {Percentile(sample, 0.95):F2} | p99: {Percentile(sample, 0.99):F2} | max: {sample.Max():F2}");
            Log.Logger.Information($"capture | n: {count} | avg: {captures.Average():F2} | min: {captures.Min():F2} | p50: {Percentile(captures, 0.50):F2} | p95: {Percentile(captures, 0.95):F2} | p99: {Percentile(captures, 0.99):F2} | max: {captures.Max():F2}");
            Log.Logger.Information($"updates | n: {count} | avg: {updates.Average():F2} | min: {updates.Min():F2} | p50: {Percentile(updates, 0.50):F2} | p95: {Percentile(updates, 0.95):F2} | p99: {Percentile(updates, 0.99):F2} | max: {updates.Max():F2}");
        }
    }

    private static void Test_Input(string[] args)
    {
        Test_Input test = new(logger, cts, process, screen, loggerFactory);
        test.Mouse_Movement();
        test.Mouse_Clicks();
        test.SendText();
    }

    private static void Test_CursorGrabber(string[] args)
    {
        using CursorClassifier classifier = new();
        int i = 5;
        while (i > 0)
        {
            Thread.Sleep(1000);

            classifier.Classify(out CursorType cursorType, out _);
            Log.Logger.Information($"{cursorType.ToString()}");

            i--;
        }
    }

    private static void Test_CursorCompare(string[] args)
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
            Log.Logger.Information($"{cursorType.ToString()} {similarity} {times[i]:F6}ms");
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

    private static void Test_MinimapNodeFinder(string[] args)
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

    private static void Test_FindTargetByCursor(string[] args)
    {
        ReadOnlySpan<CursorType> cursorType = [CursorType.Vendor];

        NpcNames types = NpcNames.None;
        int count = 2;

        foreach (string arg in args)
        {
            if (int.TryParse(arg, out int n))
                count = n;
            else if (Enum.TryParse(arg, true, out NpcNames flag))
                types |= flag;
        }

        if (types == NpcNames.None)
            types = NpcNames.Friendly | NpcNames.Neutral;

        using Test_NpcNameFinder test = new(logger, process, screen, loggerFactory, types, UseGpu, LogEachUpdate);
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

    private static void Test_PPather(string[] args)
    {
        string expansion = args.Length > 0 ? args[0] : "SoM";
        PPatherV2.PPatherV2 pPather = new(logger, DataConfig.Load(expansion));
    }

    private static double Percentile(double[] sorted, double p)
    {
        double index = p * (sorted.Length - 1);
        int lower = (int)index;
        double fraction = index - lower;

        if (lower + 1 < sorted.Length)
            return sorted[lower] + fraction * (sorted[lower + 1] - sorted[lower]);

        return sorted[lower];
    }
}
