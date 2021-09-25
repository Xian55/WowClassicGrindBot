using Serilog;
using Serilog.Events;
using SharedLib;
using Serilog.Extensions.Logging;
using System.Threading;
using Game;
using CommandLine;

namespace Server
{
    public class Program
    {
        public class Options
        {
            [Option('p', "port", Required = false, HelpText = "Listening Port default=9050")]
            public int Port { get; set; }
        }

        private static Microsoft.Extensions.Logging.ILogger logger;

        private static void CreateLogger()
        {
            var logfile = "server_out.log";
            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss:fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(logfile, rollingInterval: RollingInterval.Day);

            Log.Logger = config.CreateLogger();
            logger = new SerilogLoggerProvider(Log.Logger).CreateLogger(nameof(Program));
        }

        private static void WaitForProcess()
        {
            while (WowProcess.Get() == null)
            {
                Log.Information("Unable to find the Wow process is it running ?");
                Thread.Sleep(1000);
            }
        }

        private static void CreateConfig(WowScreen wowScreen)
        {
            // Requirement enabled addon
            var enabledAddon = wowScreen.GetBitmap(1, 1);
            var firstPixel = enabledAddon.GetPixel(0, 0);
            while (firstPixel.ToArgb() != System.Drawing.Color.Black.ToArgb())
            {
                Log.Logger.Information("Addon not visible!");

                Thread.Sleep(1000);
                enabledAddon = wowScreen.GetBitmap(1, 1);
                firstPixel = enabledAddon.GetPixel(0, 0);
            }

            // Enabled Addon visible
            Log.Logger.Information("Addon Visible");

            var dataFrameMeta = DataFrameMeta.Empty;
            while(dataFrameMeta == DataFrameMeta.Empty)
            {
                var metaBmp = wowScreen.GetBitmap(5, 5);
                dataFrameMeta = DataFrameConfiguration.GetMeta(metaBmp);
                Thread.Sleep(1000);

                Log.Logger.Information("Enter Addon Configure mode!");
            }

            wowScreen.GetRectangle(out var rect);
            var size = dataFrameMeta.EstimatedSize(rect);
            var screenshot = wowScreen.GetBitmap(size.Width, size.Height);
            var dataFrames = DataFrameConfiguration.CreateFrames(dataFrameMeta, screenshot);

            while (dataFrames.Count != dataFrameMeta.frames)
            {
                dataFrames = DataFrameConfiguration.CreateFrames(dataFrameMeta, screenshot);
                Thread.Sleep(1000);

                Log.Logger.Information($"Size missmatch {dataFrames.Count} != {dataFrameMeta.frames}");
            }

            DataFrameConfiguration.SaveConfiguration(rect, null, dataFrameMeta, dataFrames);
            Log.Logger.Information("DataFrameConfiguration Saved!");
            Log.Logger.Information("Leave Addon Configure mode!");
            Log.Logger.Information("Please Restart!");

            System.Console.ReadLine();
        }

        static void Main(string[] args)
        {
            int port = 9050;

            Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                if(o.Port != 0)
                    port = o.Port;
            });

            CreateLogger();
            WaitForProcess();

            var wowProcess = new WowProcess();
            var wowScreen = new WowScreen(logger, wowProcess);

            if (!DataFrameConfiguration.Exists())
            {
                Log.Logger.Error("DataFrameConfiguration not exists");
                CreateConfig(wowScreen);
            }
            else
            {
                var frames = DataFrameConfiguration.LoadFrames();
                IDataProvider provider = new DataProvider(logger, wowScreen, frames);
                Server server = new Server(logger, port, provider);
                server.ListenServer();
            }

        }
    }
}
