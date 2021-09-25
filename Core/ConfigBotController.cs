using Core.GOAP;
using System;
using System.Collections.Generic;
using System.Threading;
using Core.Session;
using SharedLib;
using Game;

namespace Core
{
    public class ConfigBotController : IBotController
    {
        public DataConfig DataConfig { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public AddonReader AddonReader { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Thread? screenshotThread { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Thread addonThread { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Thread? botThread { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public GoalFactory ActionFactory { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public GoapAgent? GoapAgent { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public RouteInfo? RouteInfo { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public WowScreen WowScreen { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public WowProcessInput WowProcessInput { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ConfigurableInput? ConfigurableInput { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IGrindSession GrindSession { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string SelectedClassFilename { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string? SelectedPathFilename { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool IsBotActive => false;

        public IImageProvider? MinimapImageFinder { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ClassConfiguration? ClassConfig { get => null; set => throw new NotImplementedException(); }

        public ActionBarPopulator? ActionBarPopulator { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public ExecGameCommand ExecGameCommand { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public event EventHandler? ProfileLoaded;
        public event EventHandler<bool>? StatusChanged;

        public void Shutdown()
        {

        }

        public void StopBot()
        {
            StatusChanged?.Invoke(this, false);
            throw new NotImplementedException();
        }

        public void ToggleBotStatus()
        {
            throw new NotImplementedException();
        }

        public void LoadClassProfile(string classFilename)
        {
            ProfileLoaded?.Invoke(this, EventArgs.Empty);
            throw new NotImplementedException();
        }

        public List<string> ClassFileList()
        {
            throw new NotImplementedException();
        }

        public List<string> PathFileList()
        {
            throw new NotImplementedException();
        }

        public void LoadPathProfile(string pathFilename)
        {
            ProfileLoaded?.Invoke(this, EventArgs.Empty);
            throw new NotImplementedException();
        }

        public void OverrideClassConfig(ClassConfiguration classConfiguration)
        {
            throw new NotImplementedException();
        }
    }
}