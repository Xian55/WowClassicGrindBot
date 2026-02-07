using Core.Goals;

using Game;

using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Numerics;

namespace Core;

public sealed partial class KeyAction
{
    public float Cost { get; set; } = 18;
    public string Name { get; set; } = string.Empty;
    public bool HasCastBar
    {
        get => features[ActionMask.HasCastBar];
        set => features[ActionMask.HasCastBar] = value;
    }
    public ConsoleKey ConsoleKey { get; set; }
    public ModifierKey Modifier { get; set; } = ModifierKey.None;
    public bool HasModifier => Modifier != ModifierKey.None;
    public string Key { get; set; } = string.Empty;
    public BindingID BindingID { get; set; }
    public int Slot { get; set; }
    public int SlotIndex { get; private set; }
    public int SpellId { get; set; }

    public string MacroText { get; set; } = string.Empty;
    public Func<string> Macro { get; set; } = () => "";

    public int PressDuration { get; set; } = InputDuration.DefaultPress;

    // enabled by default
    public BitVector32 features = new(
        ActionMask.Log |
        ActionMask.BeforeCastDismount
        );

    public Form? Form { get; init; }
    public Form FormValue => HasForm ? Form!.Value : Core.Form.None;
    public bool HasForm => Form.HasValue;

    public int Cooldown { get; set; } = CastingHandler.SPELL_QUEUE;

    private int _charge;
    public int Charge { get; set; } = 1;
    public SchoolMask School { get; set; } = SchoolMask.None;
    public int MinComboPoints { get; set; }

    public string Requirement { get; set; } = string.Empty;
    public List<string> Requirements { get; } = [];
    public Requirement[] RequirementsRuntime { get; set; } = [];

    public string Interrupt { get; set; } = string.Empty;
    public List<string> Interrupts { get; } = [];
    public Requirement[] InterruptsRuntime { get; set; } = [];

    public bool WhenUsable
    {
        get => features[ActionMask.WhenUsable];
        set => features[ActionMask.WhenUsable] = value;
    }

    public bool ResetOnNewTarget
    {
        get => features[ActionMask.ResetOnNewTarget];
        set => features[ActionMask.ResetOnNewTarget] = value;
    }

    public bool Log
    {
        get => features[ActionMask.Log];
        set => features[ActionMask.Log] = value;
    }

    public bool BaseAction
    {
        get => features[ActionMask.BaseAction];
        set => features[ActionMask.BaseAction] = value;
    }

    public bool Item
    {
        get => features[ActionMask.Item];
        set => features[ActionMask.Item] = value;
    }

    public int BeforeCastDelay { get; set; }
    public int BeforeCastMaxDelay { get; set; }

    public bool BeforeCastStop
    {
        get => features[ActionMask.BeforeCastStop];
        set => features[ActionMask.BeforeCastStop] = value;
    }
    public bool BeforeCastDismount
    {
        get => features[ActionMask.BeforeCastDismount];
        set => features[ActionMask.BeforeCastDismount] = value;
    }

    public bool BeforeCastFaceTarget
    {
        get => features[ActionMask.BeforeCastFaceTarget];
        set => features[ActionMask.BeforeCastFaceTarget] = value;
    }

    public int AfterCastDelay { get; set; }
    public int AfterCastMaxDelay { get; set; }
    public bool AfterCastWaitMeleeRange
    {
        get => features[ActionMask.AfterCastWaitMeleeRange];
        set => features[ActionMask.AfterCastWaitMeleeRange] = value;
    }
    public bool AfterCastWaitBuff
    {
        get => features[ActionMask.AfterCastWaitBuff];
        set => features[ActionMask.AfterCastWaitBuff] = value;
    }
    public bool AfterCastWaitBag
    {
        get => features[ActionMask.AfterCastWaitBag];
        set => features[ActionMask.AfterCastWaitBag] = value;
    }
    public bool AfterCastWaitSwing
    {
        get => features[ActionMask.AfterCastWaitSwing];
        set => features[ActionMask.AfterCastWaitSwing] = value;
    }
    public bool AfterCastWaitCastbar
    {
        get => features[ActionMask.AfterCastWaitCastbar];
        set => features[ActionMask.AfterCastWaitCastbar] = value;
    }
    public bool AfterCastWaitCombat
    {
        get => features[ActionMask.AfterCastWaitCombat];
        set => features[ActionMask.AfterCastWaitCombat] = value;
    }
    public bool AfterCastWaitGCD
    {
        get => features[ActionMask.AfterCastWaitGCD];
        set => features[ActionMask.AfterCastWaitGCD] = value;
    }
    public bool AfterCastAuraExpected
    {
        get => features[ActionMask.AfterCastAuraExpected];
        set => features[ActionMask.AfterCastAuraExpected] = value;
    }

    public bool CancelOnInterrupt
    {
        get => features[ActionMask.CancelOnInterrupt];
        set => features[ActionMask.CancelOnInterrupt] = value;
    }

    public bool UseMount
    {
        get => features[ActionMask.UseMount];
        set => features[ActionMask.UseMount] = value;
    }

    public int AfterCastStepBack { get; set; }

    public string InCombat { get; set; } = "false";

    public bool? UseWhenTargetIsCasting { get; set; }

    public string PathFilename { get; set; } = string.Empty;
    public Vector3[] Path { get; set; } = [];

    public int ConsoleKeyFormHash { private set; get; }

    private DateTime LastClicked = DateTime.UtcNow.AddDays(-1);

    private static int LastKey;
    private static DateTime LastKeyTime;

    public static int LastKeyClicked()
    {
        const int SECONDS_TO_SHOW_AS_ACTIVE = 2;

        return (DateTime.UtcNow - LastKeyTime).TotalSeconds > SECONDS_TO_SHOW_AS_ACTIVE
            ? (int)ConsoleKey.NoName
            : LastKey;
    }

    private RecordInt globalTime = null!;
    private int canRunTime;
    private bool canRun;

    private int canBeInterruptedTime;
    private bool canBeInterrupted;

    public void InitSlot(ILogger logger)
    {
        if (!KeyReader.ReadKey(logger, this) && !BaseAction)
        {
            LogInputNoValidKey(logger, Name, Key, ConsoleKey);
        }
        else if (Slot == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
                LogInputNonActionbar(logger, Name, Key, Modifier.ToPrefix(), ConsoleKey);
        }
    }

    public void Init(
        ILogger logger, bool globalLog,
        PlayerReader playerReader,
        RecordInt globalTime)
    {
        this.globalTime = globalTime;
        this.canRunTime = globalTime.Value;
        this.canBeInterruptedTime = globalTime.Value;

        if (!globalLog)
            Log = false;

        ResetCharges();

        InitSlot(logger);
        if (Slot > 0)
        {
            this.SlotIndex = Stance.ToSlot(this, playerReader) - 1;
            LogInputActionbar(logger, Name, Key, Slot, SlotIndex);
        }

        if (HasForm)
        {
            if (logger.IsEnabled(LogLevel.Information))
                LogFormRequired(logger, Name, FormValue.ToStringF());
        }

        ConsoleKeyFormHash = ((int)FormValue * 1000) + (int)ConsoleKey;

        if (!string.IsNullOrEmpty(Requirement))
        {
            Requirements.Add(Requirement);
        }

        if (!string.IsNullOrEmpty(Interrupt))
        {
            Interrupts.Add(Interrupt);
        }

        if (!string.IsNullOrEmpty(PathFilename))
        {
            LogPath(logger, Name, PathFilename);
        }

        if (BeforeCastMaxDelay < BeforeCastDelay)
            BeforeCastMaxDelay = BeforeCastDelay;

        if (AfterCastMaxDelay < AfterCastDelay)
            AfterCastMaxDelay = AfterCastDelay;
    }

    public int GetRemainingCooldown()
    {
        return Math.Max(Cooldown - SinceLastClickMs, 0);
    }

    public bool OnCooldown()
    {
        return GetRemainingCooldown() > 0;
    }

    public void SetClicked(double offset = 0)
    {
        LastKey = ConsoleKeyFormHash;
        LastKeyTime = LastClicked = DateTime.UtcNow.AddMilliseconds(offset);
    }

    public int SinceLastClickMs =>
        (int)(DateTime.UtcNow - LastClicked).TotalMilliseconds;

    public void ResetCooldown()
    {
        LastClicked = DateTime.UtcNow.AddDays(-1);
    }

    public int GetChargeRemaining()
    {
        return _charge;
    }

    public void ConsumeCharge()
    {
        if (Charge <= 1)
            return;

        _charge--;
        if (_charge > 0)
        {
            ResetCooldown();
        }
        else
        {
            ResetCharges();
            SetClicked();
        }
    }

    public void ResetCharges()
    {
        _charge = Charge;
    }

    public bool CanRun()
    {
        if (canRunTime == globalTime.Value)
            return canRun;

        canRunTime = globalTime.Value;

        ReadOnlySpan<Requirement> span = RequirementsRuntime;
        for (int i = 0; i < span.Length; i++)
        {
            if (!span[i].HasRequirement())
                return canRun = false;
        }

        return canRun = true;
    }

    public bool CanBeInterrupted()
    {
        if (canBeInterruptedTime == globalTime.Value)
            return canBeInterrupted;

        canBeInterruptedTime = globalTime.Value;

        ReadOnlySpan<Requirement> span = InterruptsRuntime;
        for (int i = 0; i < span.Length; i++)
        {
            if (!span[i].HasRequirement())
                return canBeInterrupted = false;
        }

        return canBeInterrupted = true;
    }

    #region Logging

    [LoggerMessage(
        EventId = 0001,
        Level = LogLevel.Information,
        Message = "[{name,-17}] Path: {path}")]
    static partial void LogPath(ILogger logger, string name, string path);

    [LoggerMessage(
        EventId = 0002,
        Level = LogLevel.Information,
        Message = "[{name,-17}] Required Form: {form}")]
    static partial void LogFormRequired(ILogger logger, string name, string form);

    [LoggerMessage(
        EventId = 0003,
        Level = LogLevel.Information,
        Message = "[{name,-17}] Actionbar Key:{key} -> Actionbar:{slot} -> Index:{slotIndex}")]
    static partial void LogInputActionbar(ILogger logger, string name, string key, int slot, int slotIndex);

    [LoggerMessage(
        EventId = 0004,
        Level = LogLevel.Information,
        Message = "[{name,-17}] Non Actionbar {key} -> {modifier}{consoleKey}")]
    static partial void LogInputNonActionbar(ILogger logger, string name, string key, string modifier, ConsoleKey consoleKey);

    [LoggerMessage(
        EventId = 0005,
        Level = LogLevel.Warning,
        Message = "[{name,-17}] has no valid Key={key} or ConsoleKey={consoleKey}")]
    static partial void LogInputNoValidKey(ILogger logger, string name, string key, ConsoleKey consoleKey);

    [LoggerMessage(
        EventId = 0006,
        Level = LogLevel.Information,
        Message = "[{name,-17}] Update {type} cost to {newCost} from {oldCost}")]
    static partial void LogPowerCostChange(ILogger logger, string name, string type, int newCost, int oldCost);

    #endregion
}