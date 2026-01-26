using Core.Goals;

using Microsoft.Extensions.Logging;

using SharedLib.Extensions;

using System.Numerics;

namespace Core;

public sealed partial class MountHandler : IMountHandler
{
    private const int DISTANCE_TO_MOUNT = 40;

    private const int MIN_DISTANCE_TO_INTERRUPT_CAST = 60;

    private readonly ILogger<MountHandler> logger;
    private readonly ConfigurableInput input;
    private readonly ClassConfiguration classConfig;
    private readonly Wait wait;
    private readonly ActionBarBits<IUsableAction> usableAction;
    private readonly ActionBarCooldownReader cooldownReader;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly IBlacklist targetBlacklist;

    public MountHandler(ILogger<MountHandler> logger, ConfigurableInput input,
        ClassConfiguration classConfig, AddonBits bits, Wait wait,
        PlayerReader playerReader, ActionBarBits<IUsableAction> usableAction,
        ActionBarCooldownReader cooldownReader,
        StopMoving stopMoving,
        IBlacklist targetBlacklist)
    {
        this.logger = logger;
        this.classConfig = classConfig;
        this.input = input;
        this.wait = wait;
        this.usableAction = usableAction;
        this.cooldownReader = cooldownReader;
        this.playerReader = playerReader;
        this.bits = bits;
        this.stopMoving = stopMoving;
        this.targetBlacklist = targetBlacklist;
    }

    public bool CanMount()
    {
        return
            !IsMounted() &&
            !bits.Indoors() &&
            !bits.Combat() &&
            !bits.Swimming() &&
            !bits.Falling() &&
            usableAction.Is(classConfig.Mount) &&
            cooldownReader.Get(classConfig.Mount) == 0;
    }

    public void MountUp()
    {
        wait.While(bits.Falling);

        stopMoving.Stop();
        wait.Update();

        input.PressMount();
        wait.Update();

        float e = wait.Until(
            playerReader.DoubleNetworkLatency,
            CastDetected);

        LogCastStarted(logger, e);

        wait.Update();

        e = wait.Until(
            playerReader.RemainCastMs + playerReader.DoubleNetworkLatency,
            MountedOrNotCastingOrValidTargetOrEnteredCombat);

        LogCastEnded(logger, e);

        if (HasValidTarget())
        {
            LogIsMounted(logger, bits.Mounted());
            return;
        }

        wait.Fixed(playerReader.NetworkLatency);
        LogIsMounted(logger, bits.Mounted());
    }

    public bool ShouldMount(Vector3 targetW)
    {
        Vector3 playerW = playerReader.WorldPos;
        float distance = playerW.WorldDistanceXYTo(targetW);
        return distance > DISTANCE_TO_MOUNT;
    }

    public static bool ShouldMount(float totalDistance)
    {
        return totalDistance > DISTANCE_TO_MOUNT;
    }

    public void Dismount()
    {
        input.PressDismount();
        wait.Update();

        LogIsMounted(logger, bits.Mounted());
    }

    public bool IsMounted()
    {
        return bits.Mounted();
    }

    private bool CastDetected() =>
        bits.Mounted() || playerReader.IsCasting();

    private bool MountedOrNotCastingOrValidTargetOrEnteredCombat() =>
        bits.Mounted() ||
        !playerReader.IsCasting() ||
        HasValidTarget() ||
        bits.Combat();

    private bool HasValidTarget() =>
        bits.Target() && bits.Target_Alive() && !targetBlacklist.Is() &&
        playerReader.MinRange() < MIN_DISTANCE_TO_INTERRUPT_CAST;


    #region Logging

    [LoggerMessage(
        EventId = 0110,
        Level = LogLevel.Information,
        Message = "Cast started {elapsed}ms")]
    static partial void LogCastStarted(ILogger logger, float elapsed);

    [LoggerMessage(
        EventId = 0111,
        Level = LogLevel.Information,
        Message = "Cast ended {elapsed}ms")]
    static partial void LogCastEnded(ILogger logger, float elapsed);

    [LoggerMessage(
        EventId = 0112,
        Level = LogLevel.Information,
        Message = "Mounted ? {mounted}")]
    static partial void LogIsMounted(ILogger logger, bool mounted);

    #endregion
}
