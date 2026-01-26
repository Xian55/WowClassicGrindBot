using CommandLine;

using Core;

using Microsoft.Extensions.Logging;

using static System.Diagnostics.Stopwatch;

namespace HeadlessServer;

public sealed partial class HeadlessServer
{
    private readonly ILogger<HeadlessServer> logger;
    private readonly IBotController botController;
    private readonly IAddonReader addonReader;
    private readonly ActionBarCostReader actionBarCostReader;
    private readonly SpellBookReader spellBookReader;
    private readonly BagReader bagReader;
    private readonly ExecGameCommand exec;
    private readonly AddonConfigurator addonConfigurator;
    private readonly Wait wait;

    public HeadlessServer(ILogger<HeadlessServer> logger,
        IBotController botController,
        IAddonReader addonReader,
        ActionBarCostReader actionBarCostReader,
        SpellBookReader spellBookReader,
        BagReader bagReader,
        ExecGameCommand exec,
        AddonConfigurator addonConfigurator,
        Wait wait)
    {
        this.logger = logger;
        this.botController = botController;
        this.addonReader = addonReader;
        this.actionBarCostReader = actionBarCostReader;
        this.spellBookReader = spellBookReader;
        this.bagReader = bagReader;
        this.exec = exec;
        this.addonConfigurator = addonConfigurator;
        this.wait = wait;
    }

    public void Run(ParserResult<RunOptions> options)
    {
        InitState();

        botController.LoadClassProfile(options.Value.ClassConfig!);

        botController.ToggleBotStatus();
    }

    public void RunLoadOnly(ParserResult<RunOptions> options)
    {
        botController.LoadClassProfile(options.Value.ClassConfig!);
    }

    private void InitState()
    {
        addonReader.FullReset();
        exec.Run("");
        exec.Run($"/{addonConfigurator.Config.CommandFlush}");

        const int CELL_UPDATE_TICK = 5 * 2;

        int actionbarCost;
        int spellBook;
        int bag;

        long startTime = GetTimestamp();
        do
        {
            actionbarCost = actionBarCostReader.Count;
            spellBook = spellBookReader.Count;
            bag = bagReader.BagItems.Count;

            for (int i = 0; i < CELL_UPDATE_TICK; i++)
                wait.Update();

            if (actionbarCost != actionBarCostReader.Count ||
                spellBook != spellBookReader.Count ||
                bag != bagReader.BagItems.Count)
            {
                LogInitStateStatus(logger, actionbarCost, spellBook, bag);
            }
        } while (
            actionbarCost != actionBarCostReader.Count ||
            spellBook != spellBookReader.Count ||
            bag != bagReader.BagItems.Count);

        LogInitStateEnd(logger, (float)GetElapsedTime(startTime).TotalSeconds);
    }

    #region Logging

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Actionbar: {actionbar,3} | SpellBook: {spellBook,3} | Bag: {bag,3}")]
    static partial void LogInitStateStatus(ILogger logger, int actionbar, int spellbook, int bag);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "InitState {elapsedSec}sec")]
    static partial void LogInitStateEnd(ILogger logger, float elapsedSec);

    #endregion

}
