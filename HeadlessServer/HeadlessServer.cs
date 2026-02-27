using CommandLine;

using Core;

using Game;

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
    private readonly KeyBindingsReader keyBindingsReader;
    private readonly WowProcessInput wowInput;
    private readonly Wait wait;

    public HeadlessServer(ILogger<HeadlessServer> logger,
        IBotController botController,
        IAddonReader addonReader,
        ActionBarCostReader actionBarCostReader,
        SpellBookReader spellBookReader,
        BagReader bagReader,
        KeyBindingsReader keyBindingsReader,
        WowProcessInput wowInput,
        Wait wait)
    {
        this.logger = logger;
        this.botController = botController;
        this.addonReader = addonReader;
        this.actionBarCostReader = actionBarCostReader;
        this.spellBookReader = spellBookReader;
        this.bagReader = bagReader;
        this.keyBindingsReader = keyBindingsReader;
        this.wowInput = wowInput;
        this.wait = wait;
    }

    public void Run(ParserResult<RunOptions> options)
    {
        InitState();

        botController.LoadClassProfile(options.Value.ClassConfig!);

        botController.ToggleBotStatus();
    }

    public bool RunLoadOnly(ParserResult<RunOptions> options)
    {
        return botController.LoadClassProfile(options.Value.ClassConfig!);
    }

    private void InitState()
    {
        addonReader.FullReset();
        wowInput.PressFlushKey();

        const int CELL_UPDATE_TICK = 5 * 2;

        int actionbarCost;
        int spellBook;
        int bag;
        int keyBindings;

        long startTime = GetTimestamp();
        do
        {
            actionbarCost = actionBarCostReader.Count;
            spellBook = spellBookReader.Count;
            bag = bagReader.BagItems.Count;
            keyBindings = keyBindingsReader.Count;

            for (int i = 0; i < CELL_UPDATE_TICK; i++)
                wait.Update();

            if (actionbarCost != actionBarCostReader.Count ||
                spellBook != spellBookReader.Count ||
                bag != bagReader.BagItems.Count ||
                keyBindings != keyBindingsReader.Count)
            {
                LogInitStateStatus(logger, actionbarCost, spellBook, bag, keyBindings);
            }
        } while (
            actionbarCost != actionBarCostReader.Count ||
            spellBook != spellBookReader.Count ||
            bag != bagReader.BagItems.Count ||
            keyBindings != keyBindingsReader.Count);

        LogInitStateEnd(logger, (float)GetElapsedTime(startTime).TotalSeconds);
    }

    #region Logging

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Actionbar: {actionbar,3} | SpellBook: {spellBook,3} | Bag: {bag,3} | Bindings: {keyBindings,3}")]
    static partial void LogInitStateStatus(ILogger logger, int actionbar, int spellbook, int bag, int keyBindings);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "InitState {elapsedSec}sec")]
    static partial void LogInitStateEnd(ILogger logger, float elapsedSec);

    #endregion

}
