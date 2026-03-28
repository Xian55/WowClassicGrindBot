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

    Log.Logger = new LoggerConfiguration()
        .WriteTo.File(new ExpressionTemplate(LogOutputTemplates.Minimal),
            path: "benchmark_out.log",
            rollingInterval: RollingInterval.Day)
        .WriteTo.Console(new ExpressionTemplate(LogOutputTemplates.Minimal, theme: TemplateTheme.Literate))
        .CreateLogger();

    Log.Information($"Running PathingAPI benchmark against {baseUrl}...\n");
    await Benchmarks.PathingAPIBenchmark.RunBenchmark(baseUrl, iterations, logger: Log.Logger);

    Log.CloseAndFlush();
}
else
{
    // Run BenchmarkDotNet suite
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
