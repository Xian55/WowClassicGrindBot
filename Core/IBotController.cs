using Core.GOAP;
using System.Collections.Generic;
using System.Threading;
using Core.Session;
using SharedLib;
using Game;

namespace Core
{
    public interface IBotController
    {
        DataConfig DataConfig { get; set; }
        AddonReader AddonReader { get; set; }
        Thread? screenshotThread { get; set; }
        Thread addonThread { get; set; }
        Thread? botThread { get; set; }
        //GoalFactory ActionFactory { get; set; }
        GoapAgent? GoapAgent { get; set; }
        RouteInfo? RouteInfo { get; set; }
        WowScreen WowScreen { get; set; }
        WowProcessInput WowProcessInput { get; set; }
        ConfigurableInput? ConfigurableInput { get; set; }
        ClassConfiguration? ClassConfig { get; set; }
        IImageProvider? MinimapImageFinder { get; set; }

        ExecGameCommand ExecGameCommand { get; set; }

        ActionBarPopulator? ActionBarPopulator { get; set; }
        public IGrindSession GrindSession { get; set; }

        string SelectedClassFilename { get; set; }
        string? SelectedPathFilename { get; set; }

        event System.EventHandler? ProfileLoaded;
        event System.EventHandler<bool> StatusChanged;

        void ToggleBotStatus();
        void StopBot();

        void Shutdown();

        bool IsBotActive { get; }

        List<string> ClassFileList();

        void LoadClassProfile(string classFilename);

        List<string> PathFileList();

        void LoadPathProfile(string pathFilename);

        void OverrideClassConfig(ClassConfiguration classConfiguration);
    }
}