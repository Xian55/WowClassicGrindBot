using Core.GOAP;

using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;

namespace Core.Goals;

public static class CastState_Extension
{
    public static string ToStringF(this WaitForGatheringGoal.CastState value)
        => value switch
        {
            WaitForGatheringGoal.CastState.None =>
                nameof(WaitForGatheringGoal.CastState.None),
            WaitForGatheringGoal.CastState.Casting =>
                nameof(WaitForGatheringGoal.CastState.Casting),
            WaitForGatheringGoal.CastState.Failed =>
                nameof(WaitForGatheringGoal.CastState.Failed),
            WaitForGatheringGoal.CastState.Abort =>
                nameof(WaitForGatheringGoal.CastState.Abort),
            WaitForGatheringGoal.CastState.Success =>
                nameof(WaitForGatheringGoal.CastState.Success),
            WaitForGatheringGoal.CastState.WaitUserInput =>
                nameof(WaitForGatheringGoal.CastState.WaitUserInput),
            _ => throw new System.NotImplementedException(),
        };
}

public partial class WaitForGatheringGoal : GoapGoal
{
    public override float Cost => 17;

    private const int Timeout = 5000;

    private readonly ILogger logger;
    private readonly Wait wait;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly Stopwatch stopWatch;

    public enum CastState
    {
        None,
        Casting,
        Failed,
        Abort,
        Success,
        WaitUserInput,
    }

    private CastState state;
    private int lastKnownCast;

    public WaitForGatheringGoal(ILogger logger, Wait wait,
        PlayerReader playerReader, AddonBits bits,
        StopMoving stopMoving)
        : base(nameof(WaitForGatheringGoal))
    {
        this.logger = logger;
        this.wait = wait;
        this.playerReader = playerReader;
        this.bits = bits;
        this.stopMoving = stopMoving;
        this.stopWatch = new();

        AddPrecondition(GoapKey.gathering, true);
    }

    public override void OnEnter()
    {
        stopMoving.Stop();
        wait.Update();

        wait.While(bits.Falling);

        LogOnEnter(logger);
    }

    public override void OnExit()
    {
        state = CastState.None;
        lastKnownCast = 0;

        LogState(logger, state.ToStringF());

        stopWatch.Reset();
        stopWatch.Stop();
    }

    public override void Update()
    {
        switch (state)
        {
            case CastState.None:
                CheckCastStarted(false);
                break;
            case CastState.Casting:
                if (!playerReader.IsCasting())
                {
                    wait.Update();
                    if (playerReader.LastUIError == UI_ERROR.ERR_SPELL_FAILED_S)
                    {
                        state = CastState.Failed;
                        LogFailed(logger, state.ToStringF(), Timeout);
                    }
                    else
                    {
                        if (Array.BinarySearch(GatherSpells.Mining, lastKnownCast) < 0)
                        {
                            state = CastState.WaitUserInput;
                            LogSuccessMining(logger, CastState.Success.ToStringF(), state.ToStringF(), Timeout);
                            stopWatch.Restart();
                            wait.Update();
                        }
                        else
                        {
                            state = CastState.Success;
                            LogState(logger, state.ToStringF());
                        }
                    }
                }
                break;
            case CastState.Failed:
                stopWatch.Restart();
                state = CastState.WaitUserInput;
                LogFailed(logger, state.ToStringF(), Timeout);
                wait.Update();
                break;
            case CastState.Success:
            case CastState.Abort:
                SendGoapEvent(new GoapStateEvent(GoapKey.gathering, false));
                break;
            case CastState.WaitUserInput:
                CheckCastStarted(true);

                if (stopWatch.ElapsedMilliseconds > Timeout)
                {
                    SendGoapEvent(new GoapStateEvent(GoapKey.gathering, false));
                }
                break;
        }

        wait.Update();
    }

    private void CheckCastStarted(bool restartTimer)
    {
        if (playerReader.IsCasting() &&
            (Array.BinarySearch(GatherSpells.Herbalism, playerReader.CastSpellId.Value) >= 0 ||
            Array.BinarySearch(GatherSpells.Mining, playerReader.CastSpellId.Value) >= 0))
        {
            lastKnownCast = playerReader.CastSpellId.Value;
            state = CastState.Casting;

            LogState(logger, state.ToStringF());

            if (restartTimer)
            {
                stopWatch.Reset();
                stopWatch.Stop();
            }
        }

        if (bits.Falling())
        {
            state = CastState.Abort;
            LogState(logger, state.ToStringF());
        }
    }


    #region Logging

    [LoggerMessage(
        EventId = 0101,
        Level = LogLevel.Information,
        Message = "{state}")]
    static partial void LogState(ILogger logger, string state);

    [LoggerMessage(
        EventId = 0102,
        Level = LogLevel.Warning,
        Message = "Waiting indefinitely for [Gathering cast to start] or [Press Jump to Abort]")]
    static partial void LogOnEnter(ILogger logger);

    [LoggerMessage(
        EventId = 0103,
        Level = LogLevel.Error,
        Message = "{state} -- Waiting(max {Timeout} ms) for [Gathering cast to start] or [Press Jump to Abort]")]
    static partial void LogFailed(ILogger logger, string state, int Timeout);

    [LoggerMessage(
        EventId = 0104,
        Level = LogLevel.Information,
        Message = "{success} -> {state} Waiting(max {Timeout} ms) for [More Mining cast] or [Press Jump to Abort]")]
    static partial void LogSuccessMining(ILogger logger, string success, string state, int Timeout);

    #endregion
}
