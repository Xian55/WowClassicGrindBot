using Core.Goals;
using Core.GOAP;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Database;
using SharedLib;
using Game;
using WinAPI;
using Microsoft.Extensions.Configuration;

namespace Core
{
    public sealed class BotController : IBotController, IDisposable
    {
        private readonly WowProcess wowProcess;
        private readonly ILogger logger;
        private readonly IPPather pather;

        public string SelectedClassFilename { get; set; } = String.Empty;
        public string? SelectedPathFilename { get; set; }

        public DataConfig DataConfig { get; set; }
        public AddonReader AddonReader { get; set; }

        public Thread? screenshotThread { get; set; }

        private const int screenshotTickMs = 150;
        private readonly DateTime lastScreenshot = default;

        public Thread addonThread { get; set; }
        public Thread? botThread { get; set; }

        public GoapAgent? GoapAgent { get; set; }
        public RouteInfo? RouteInfo { get; set; }

        private GoalThread? actionThread;

        public WowScreen WowScreen { get; set; }
        public WowProcessInput WowProcessInput { get; set; }

        public ConfigurableInput? ConfigurableInput { get; set; }

        private NpcNameFinder npcNameFinder;

        private AreaDB areaDb;

        private IAddonDataProvider addonDataProvider;

        public ClassConfiguration? ClassConfig { get; set; }

        private INodeFinder minimapNodeFinder;
        public IImageProvider? MinimapImageFinder { get; set; }

        public ActionBarPopulator? ActionBarPopulator { get; set; }

        private bool Enabled = true;

        public event EventHandler? ProfileLoaded;
        public event EventHandler<bool>? StatusChanged;

        public BotController(ILogger logger, IPPather pather, DataConfig dataConfig, IConfiguration configuration)
        {
            this.logger = logger;
            this.pather = pather;
            this.DataConfig = dataConfig;
            this.areaDb = new AreaDB(logger, dataConfig);

            updatePlayerPostion.Start();
            wowProcess = new WowProcess();
            WowScreen = new WowScreen(logger, wowProcess);
            WowProcessInput = new WowProcessInput(logger, wowProcess);

            var frames = DataFrameConfiguration.LoadFrames();

            var scad = new StartupConfigAddonData();
            configuration.GetSection(StartupConfigAddonData.Position).Bind(scad);
            if (scad.Mode == "Network")
            {
                logger.LogInformation("Using NetworkedAddonDataProvider");
                addonDataProvider = new NetworkedAddonDataProvider(logger, scad.myPort, scad.connectTo, scad.connectPort);
            }
            else
            {
                logger.LogInformation("Using AddonDataProvider");
                addonDataProvider = new AddonDataProvider(WowScreen, frames);
            }

            AddonReader = new AddonReader(logger, DataConfig, areaDb, addonDataProvider);

            minimapNodeFinder = new MinimapNodeFinder(WowScreen, new PixelClassifier());
            MinimapImageFinder = minimapNodeFinder as IImageProvider;

            addonThread = new Thread(AddonRefreshThread);
            addonThread.Start();

            // wait for addon to read the wow state
            var sw = new Stopwatch();
            sw.Start();
            while (AddonReader.PlayerReader.Sequence == 0 || !Enum.GetValues(typeof(PlayerClassEnum)).Cast<PlayerClassEnum>().Contains(AddonReader.PlayerReader.PlayerClass))
            {
                if (sw.ElapsedMilliseconds > 5000)
                {
                    logger.LogWarning("There is a problem with the addon, I have been unable to read the player class. Is it running ?");
                    sw.Restart();
                }
                Thread.Sleep(100);
            }

            logger.LogDebug($"Woohoo, I have read the player class. You are a {AddonReader.PlayerReader.PlayerClass}.");

            npcNameFinder = new NpcNameFinder(logger, WowScreen, WowProcessInput);
            WowScreen.AddDrawAction(npcNameFinder.ShowNames);

            //ActionFactory = new GoalFactory(AddonReader, logger, wowProcess, npcNameFinder);

            screenshotThread = new Thread(ScreenshotRefreshThread);
            screenshotThread.Start();
        }

        public void AddonRefreshThread()
        {
            while (this.AddonReader.Active && this.Enabled)
            {
                this.AddonReader.AddonRefresh();
                this.GoapAgent?.UpdateWorldState();
                System.Threading.Thread.Sleep(10);
            }
            this.logger.LogInformation("Addon thread stoppped!");
        }

        Stopwatch updatePlayerPostion = new Stopwatch();

        public void ScreenshotRefreshThread()
        {
            var nodeFound = false;
            while (this.Enabled)
            {
                if ((DateTime.Now - lastScreenshot).TotalMilliseconds > screenshotTickMs)
                {
                    this.WowScreen.UpdateScreenshot();
                    this.npcNameFinder.Update();
                    this.WowScreen.PostProcess();
                }

                if (ClassConfig != null && this.ClassConfig.Mode == Mode.AttendedGather)
                {
                    nodeFound = this.minimapNodeFinder.Find(nodeFound) != null;
                }

                if (updatePlayerPostion.ElapsedMilliseconds > 500)
                {
                    this.pather.DrawSphere(new Core.PPather.SphereArgs
                    {
                        Colour = AddonReader.PlayerReader.PlayerBitValues.PlayerInCombat ? 1 : !string.IsNullOrEmpty(AddonReader.PlayerReader.Target)? 6: 2,
                        Name = "Player",
                        MapId = this.AddonReader.PlayerReader.ZoneId,
                        Spot = this.AddonReader.PlayerReader.PlayerLocation
                    });
                    updatePlayerPostion.Reset();
                    updatePlayerPostion.Restart();

                    if(RouteInfo != null)
                        RouteInfo.CurrentArea = areaDb?.CurrentArea;
                }

                Thread.Sleep(10);
            }
            this.logger.LogInformation("Screenshot thread stoppped!");
        }

        public bool IsBotActive => actionThread == null ? false : actionThread.Active;

        public void ToggleBotStatus()
        {
            if (actionThread != null)
            {
                if (!actionThread.Active)
                {
                    this.pather.DrawLines();

                    actionThread.Active = true;
                    botThread = new Thread(() => Task.Factory.StartNew(() => BotThread()));
                    botThread.Start();
                }
                else
                {
                    actionThread.Active = false;
                }

                StatusChanged?.Invoke(this, actionThread.Active);
            }
        }

        public async Task BotThread()
        {
            if (this.actionThread != null)
            {
                while (this.actionThread.Active && this.Enabled)
                {
                    await actionThread.GoapPerformGoal();
                }
            }

            await new StopMoving(WowProcessInput, AddonReader.PlayerReader).Stop();
            logger.LogInformation("Stopped!");
        }

        public bool InitialiseFromFile(string classFile, string? pathFile)
        {
            try
            {
                ClassConfig = ReadClassConfiguration(classFile, pathFile);
            }
            catch(Exception e)
            {
                logger.LogError(e.Message);
                return false;
            }

            Initialize(ClassConfig);

            return true;
        }

        private void Initialize(ClassConfiguration config)
        {
            ConfigurableInput = new ConfigurableInput(logger, wowProcess, config);
            ActionBarPopulator = new ActionBarPopulator(logger, wowProcess, ConfigurableInput, config, AddonReader);

            var blacklist = config.Mode != Mode.Grind ? new NoBlacklist() : (IBlacklist)new Blacklist(AddonReader.PlayerReader, config.NPCMaxLevels_Above, config.NPCMaxLevels_Below, config.Blacklist, logger);

            var actionFactory = new GoalFactory(logger, AddonReader, ConfigurableInput, DataConfig, npcNameFinder, pather);
            var availableActions = actionFactory.CreateGoals(config, blacklist);
            RouteInfo = actionFactory.RouteInfo;

            this.GoapAgent = new GoapAgent(logger, wowProcess, ConfigurableInput, AddonReader.PlayerReader, availableActions, blacklist, config, AddonReader.BagReader);

            this.actionThread = new GoalThread(logger, ConfigurableInput, AddonReader.PlayerReader, GoapAgent);

            // hookup events between actions
            availableActions.ToList().ForEach(a =>
            {
                a.ActionEvent += this.actionThread.OnActionEvent;
                a.ActionEvent += GoapAgent.OnActionEvent;

                // tell other action about my actions
                availableActions.ToList().ForEach(b =>
                {
                    if (b != a) { a.ActionEvent += b.OnActionEvent; }
                });
            });
        }

        private ClassConfiguration ReadClassConfiguration(string classFilename, string? pathFilename)
        {
            if(!classFilename.ToLower().Contains(AddonReader.PlayerReader.PlayerClass.ToString().ToLower()))
            {
                throw new Exception("Not allowed to load other class profile!");
            }

            var requirementFactory = new RequirementFactory(AddonReader.PlayerReader, AddonReader.BagReader, AddonReader.equipmentReader,  logger);

            ClassConfiguration classConfig;
            var classFilePath = Path.Join(DataConfig.Class, classFilename);
            if (File.Exists(classFilePath))
            {
                classConfig = JsonConvert.DeserializeObject<ClassConfiguration>(File.ReadAllText(classFilePath));
                classConfig.Initialise(DataConfig, AddonReader, requirementFactory, logger, pathFilename);

                logger.LogDebug($"Loaded `{classFilename}` with Path Profile `{classConfig.PathFilename}`.");

                return classConfig;
            }

            throw new ArgumentOutOfRangeException($"Class config file not found {classFilename}");
        }

        public void Dispose()
        {
            WowScreen.Dispose();
            addonDataProvider?.Dispose();
        }

        public void StopBot()
        {
            if (actionThread != null)
            {
                actionThread.Active = false;
                this.GoapAgent?.AvailableGoals.ToList().ForEach(goal => goal.OnActionEvent(this, new ActionEventArgs(GoapKey.abort, true)));

                StatusChanged?.Invoke(this, actionThread.Active);
            }
        }

        public void Shutdown()
        {
            this.Enabled = false;
        }

        public void LoadClassProfile(string classFilename)
        {
            StopBot();
            if(InitialiseFromFile(classFilename, SelectedPathFilename))
            {
                SelectedClassFilename = classFilename;
            }

            ProfileLoaded?.Invoke(this, EventArgs.Empty);
        }

        public List<string> ClassFileList()
        {
            DirectoryInfo directory = new DirectoryInfo(DataConfig.Class);
            var list = directory.GetFiles().Select(i => i.Name).ToList();
            list.Sort(new NaturalStringComparer());
            list.Insert(0, String.Empty);
            return list;
        }

        public List<string> PathFileList()
        {
            var root = DataConfig.Path;

            var files = Directory.EnumerateFiles(root, "*.json*", SearchOption.AllDirectories)
                .Select(path => path.Replace(root, "")).ToList();

            files.Sort(new NaturalStringComparer());
            files.Insert(0, "Use Class Profile Default");
            return files;
        }

        public void LoadPathProfile(string pathFilename)
        {
            StopBot();
            if(InitialiseFromFile(SelectedClassFilename, pathFilename))
            {
                SelectedPathFilename = pathFilename;
            }

            ProfileLoaded?.Invoke(this, EventArgs.Empty);
        }

        public void OverrideClassConfig(ClassConfiguration classConfiguration)
        {
            this.ClassConfig = classConfiguration;
            Initialize(this.ClassConfig);
        }
    }
}