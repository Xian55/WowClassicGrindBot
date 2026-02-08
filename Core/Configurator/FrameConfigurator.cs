using Game;

using Microsoft.Extensions.Logging;

using SharedLib;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

using System;
using System.Threading;

namespace Core;

public sealed class FrameConfigurator : IDisposable
{
    private enum Stage
    {
        Reset,
        DetectRunningGame,
        CheckGameWindowLocation,
        EnterConfigMode,
        WaitEnterConfigMode,
        ValidateMetaSize,
        CreateDataFrames,
        ReturnNormalMode,
        WaitReturnNormalMode,
        UpdateReader,
        ValidateData,
        Done
    }

    private Stage stage = Stage.Reset;

    private const int MAX_HEIGHT = 25; // this one just arbitrary number for sanity check
    private const int INTERVAL = 500;
    private const int MAX_WAIT_RETRIES = 10;

    private readonly ILogger<FrameConfigurator> logger;
    private readonly WowProcess process;
    private readonly IWowScreen screen;
    private readonly WowProcessInput input;
    private readonly AddonConfigurator addonConfigurator;
    private readonly Wait wait;
    private readonly IAddonDataProvider reader;

    private Thread? screenshotThread;
    private CancellationTokenSource cts = new();

    public DataFrameMeta DataFrameMeta { get; private set; } = DataFrameMeta.Empty;

    public DataFrame[] DataFrames { get; private set; } = Array.Empty<DataFrame>();

    public bool Saved { get; private set; }
    public bool AddonNotVisible { get; private set; }

    public string ImageBase64 { private set; get; } = "iVBORw0KGgoAAAANSUhEUgAAAAUAAAAFCAYAAACNbyblAAAAHElEQVQI12P4//8/w38GIAXDIBKE0DHxgljNBAAO9TXL0Y4OHwAAAABJRU5ErkJggg==";

    private Rectangle screenRect = Rectangle.Empty;
    private Size size = Size.Empty;
    private int waitRetryCount;

    public event Action? OnUpdate;

    public FrameConfigurator(ILogger<FrameConfigurator> logger, Wait wait,
        WowProcess process, IAddonDataProvider reader,
        IWowScreen screen, WowProcessInput input,
        AddonConfigurator addonConfigurator)
    {
        this.logger = logger;
        this.wait = wait;
        this.process = process;
        this.reader = reader;
        this.screen = screen;
        this.input = input;
        this.addonConfigurator = addonConfigurator;
    }

    public void Dispose()
    {
        cts.Cancel();
    }

    private void ManualConfigThread()
    {
        screen.Enabled = true;

        while (!cts.Token.IsCancellationRequested)
        {
            DoConfig(false);

            OnUpdate?.Invoke();
            cts.Token.WaitHandle.WaitOne(INTERVAL);
            wait.Update();
        }
        screenshotThread = null;

        screen.Enabled = false;
    }

    private bool DoConfig(bool auto)
    {
        switch (stage)
        {
            case Stage.Reset:
                screenRect = Rectangle.Empty;
                size = Size.Empty;
                ResetConfigState();

                stage++;
                break;
            case Stage.DetectRunningGame:
                if (process.IsRunning)
                {
                    if (auto)
                    {
                        logger.LogInformation(
                            "Found WowProcess with pid={Id} {ProcessName}",
                            process.Id, process.ProcessName);
                    }
                    stage++;
                }
                else
                {
                    if (auto)
                    {
                        logger.LogWarning("WowProcess no longer running!");
                        return false;
                    }
                    stage--;
                }
                break;
            case Stage.CheckGameWindowLocation:
                screen.GetRectangle(out screenRect);
                if (screenRect.Location.X < 0 || screenRect.Location.Y < 0)
                {
                    logger.LogWarning("Client window outside of the visible area of the screen {Location}", screenRect.Location);
                    stage = Stage.Reset;

                    if (auto)
                    {
                        return false;
                    }
                }
                else
                {
                    AddonNotVisible = false;
                    stage++;

                    if (auto)
                    {
                        logger.LogInformation("Client window: {ScreenRect}", screenRect);
                    }
                }
                break;
            case Stage.EnterConfigMode:
                if (auto)
                {
                    Version? version = addonConfigurator.GetInstallVersion();
                    if (version == null)
                    {
                        stage = Stage.Reset;
                        logger.LogError("Addon is not installed!");
                        return false;
                    }
                    logger.LogInformation("Addon installed! Version: {Version}", version);

                    logger.LogInformation("Enter configuration mode.");
                    input.SetForegroundWindow();
                    wait.Fixed(INTERVAL);
                    ToggleInGameConfiguration();
                    waitRetryCount = 0;
                    stage = Stage.WaitEnterConfigMode;
                }
                else
                {
                    // Manual mode: just check if already in config mode
                    DataFrameMeta temp = GetDataFrameMeta();
                    if (DataFrameMeta == DataFrameMeta.Empty && temp != DataFrameMeta.Empty)
                    {
                        DataFrameMeta = temp;
                        stage = Stage.ValidateMetaSize;
                        logger.LogInformation("{DataFrameMeta}", DataFrameMeta);
                    }
                }
                break;
            case Stage.WaitEnterConfigMode:
                {
                    wait.Update();
                    DataFrameMeta temp = GetDataFrameMeta();
                    if (DataFrameMeta == DataFrameMeta.Empty && temp != DataFrameMeta.Empty)
                    {
                        DataFrameMeta = temp;
                        stage = Stage.ValidateMetaSize;
                        logger.LogInformation("{DataFrameMeta}", DataFrameMeta);
                    }
                    else
                    {
                        waitRetryCount++;
                        if (waitRetryCount >= MAX_WAIT_RETRIES)
                        {
                            logger.LogError("Timeout waiting for config mode!");
                            stage = Stage.Reset;
                            if (auto) return false;
                        }
                    }
                }
                break;
            case Stage.ValidateMetaSize:
                size = DataFrameMeta.EstimatedSize(screenRect);
                if (!size.IsEmpty &&
                    size.Width <= screenRect.Size.Width &&
                    size.Height <= screenRect.Size.Height &&
                    size.Height <= MAX_HEIGHT)
                {
                    stage++;
                }
                else
                {
                    logger.LogWarning("Addon Rect({Size}) size issue. Either too small or too big!", size);
                    stage = Stage.Reset;

                    if (auto)
                        return false;
                }
                break;
            case Stage.CreateDataFrames:

                Size addonSize = size;
                var cropped = screen.ScreenImage.Clone(cropSize);
                void cropSize(IImageProcessingContext x)
                {
                    x.Crop(addonSize.Width, addonSize.Height);
                }

                if (!auto)
                {
                    ImageBase64 = cropped.ToBase64String(JpegFormat.Instance);
                }

                DataFrames = FrameConfig.CreateFrames(DataFrameMeta, cropped);
                if (DataFrames.Length == DataFrameMeta.Count)
                {
                    stage++;
                }
                else
                {
                    logger.LogWarning("DataFrameMeta and FrameConfig dosen't match Frames: ({FrameCount}) != Meta: ({MetaCount})", DataFrames.Length, DataFrameMeta.Count);
                    stage = Stage.Reset;

                    if (auto)
                        return false;
                }

                break;
            case Stage.ReturnNormalMode:
                if (auto)
                {
                    logger.LogInformation("Exit configuration mode.");
                    input.SetForegroundWindow();
                    ToggleInGameConfiguration();
                    waitRetryCount = 0;
                    stage = Stage.WaitReturnNormalMode;
                }
                else
                {
                    // Manual mode: just check if already in normal mode
                    DataFrameMeta temp = GetDataFrameMeta();
                    if (temp == DataFrameMeta.Empty)
                    {
                        logger.LogDebug(temp.ToString());
                        stage = Stage.UpdateReader;
                    }
                }
                break;
            case Stage.WaitReturnNormalMode:
                {
                    wait.Update();
                    DataFrameMeta temp = GetDataFrameMeta();
                    if (temp == DataFrameMeta.Empty)
                    {
                        logger.LogDebug(temp.ToString());
                        stage = Stage.UpdateReader;
                    }
                    else
                    {
                        waitRetryCount++;
                        if (waitRetryCount >= MAX_WAIT_RETRIES)
                        {
                            logger.LogError("Unable to return normal mode!");
                            ResetConfigState();
                            return false;
                        }
                    }
                }
                break;
            case Stage.UpdateReader:
                reader.InitFrames(DataFrames);
                wait.Update();
                wait.Update();
                reader.UpdateData();
                stage++;
                break;
            case Stage.ValidateData:
                if (TryResolveRaceAndClass(out UnitRace race, out UnitClass @class, out ClientVersion clientVersion))
                {
                    if (auto)
                    {
                        if (logger.IsEnabled(LogLevel.Information))
                            logger.LogInformation("Found {ClientVersion} {Race} {Class}!", clientVersion.ToStringF(), race.ToStringF(), @class.ToStringF());
                    }

                    stage++;
                }
                else
                {
                    logger.LogError("Unable to identify ClientVersion UnitRace and UnitClass!");
                    stage = Stage.Reset;

                    if (auto)
                        return false;
                }
                break;
            case Stage.Done:
                return false;
            default:
                break;
        }

        return true;
    }


    private void ResetConfigState()
    {
        screenRect = Rectangle.Empty;
        size = Size.Empty;

        AddonNotVisible = true;
        stage = Stage.Reset;
        Saved = false;

        DataFrameMeta = DataFrameMeta.Empty;
        DataFrames = Array.Empty<DataFrame>();

        reader.InitFrames(DataFrames);
        wait.Update();

        logger.LogDebug("ResetConfigState");
    }

    private DataFrameMeta GetDataFrameMeta()
    {
        return FrameConfig.GetMeta(screen.ScreenImage[0, 0]);
    }

    public void ToggleManualConfig()
    {
        if (screenshotThread != null)
        {
            cts.Cancel();
            return;
        }

        ResetConfigState();

        cts.Dispose();
        cts = new();
        screenshotThread = new Thread(ManualConfigThread);
        screenshotThread.Start();
    }

    public bool FinishConfig()
    {
        Version? version = addonConfigurator.GetInstallVersion();
        if (version == null ||
            DataFrames.Length == 0 ||
            DataFrameMeta.Count == 0 ||
            DataFrames.Length != DataFrameMeta.Count ||
            !TryResolveRaceAndClass(out _, out _, out _))
        {
            logger.LogInformation("Frame configuration was incomplete! Please try again, after resolving the previusly mentioned issues...");
            ResetConfigState();
            return false;
        }

        screen.GetRectangle(out Rectangle rect);
        FrameConfig.Save(rect, version, DataFrameMeta, DataFrames);
        logger.LogInformation("Frame configuration was successful! Configuration saved!");
        Saved = true;

        return true;
    }

    public bool StartAutoConfig()
    {
        screen.Enabled = true;

        while (DoConfig(true))
        {
            wait.Update();
        }

        screen.Enabled = false;

        return FinishConfig();
    }

    public static void DeleteConfig()
    {
        FrameConfig.Delete();
    }

    private void ToggleInGameConfiguration()
    {
        // Press SHIFT-PAGEUP to trigger CUSTOM_CONFIG (/dc)
        input.PressRandomWithModifier(ConsoleKey.PageUp, ModifierKey.Shift, 50);
    }

    public bool TryResolveRaceAndClass(out UnitRace race, out UnitClass @class, out ClientVersion version)
    {
        if (reader.Data.Length < 46)
        {
            race = 0;
            @class = 0;
            version = 0;
            return false;
        }

        int value = reader.GetInt(46);

        // RACE_ID * 10000 + CLASS_ID * 100 + ClientVersion
        race = (UnitRace)(value / 10000);
        @class = (UnitClass)(value / 100 % 100);
        version = (ClientVersion)(value % 100);

        return Enum.IsDefined(race) && Enum.IsDefined(@class) && Enum.IsDefined(version) &&
            race != UnitRace.None && @class != UnitClass.None && version != ClientVersion.None;
    }
}
