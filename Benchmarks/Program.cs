using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;

using SharedLib.Logging;

// Check if running PathingAPI benchmark
if (args.Length > 0 && args[0] == "--pather-benchmark")
{
    string baseUrl = args.Length > 1 ? args[1] : "http://localhost:5001";
    int iterations = args.Length > 2 && int.TryParse(args[2], out int iter) ? iter : 2;
    string label = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : "spot-astar";

    // --engines SpotAStar,Navmesh -> run the suite per engine + comparison md
    string[]? engines = null;
    for (int i = 1; i < args.Length - 1; i++)
    {
        if (args[i] == "--engines")
        {
            engines = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    Log.Logger = new LoggerConfiguration()
        .WriteTo.File(new ExpressionTemplate(LogOutputTemplates.Minimal),
            path: "benchmark_out.log",
            rollingInterval: RollingInterval.Day)
        .WriteTo.Console(new ExpressionTemplate(LogOutputTemplates.Minimal, theme: TemplateTheme.Literate))
        .CreateLogger();

    Log.Information($"Running PathingAPI benchmark against {baseUrl}...\n");

    if (engines != null)
    {
        await Benchmarks.PathingAPIBenchmark.RunEngineComparison(baseUrl, iterations, engines, Log.Logger);
    }
    else
    {
        await Benchmarks.PathingAPIBenchmark.RunBenchmark(baseUrl, iterations, logger: Log.Logger, label: label);
    }

    Log.CloseAndFlush();
}
else
{
    // Run BenchmarkDotNet suite
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
