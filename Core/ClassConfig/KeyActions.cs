using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;

namespace Core;

public class KeyActions
{
    public KeyAction[] Sequence { get; set; } =
        Array.Empty<KeyAction>();

    /// <summary>
    /// False for sections whose goals act on Requirements alone and never press the
    /// entry's key - NPC, Flee and Wait. Propagated onto each KeyAction because
    /// InitSlot also runs later from KeyAction.Init, which has no view of the section.
    /// </summary>
    [JsonIgnore]
    public bool KeyRequired { get; init; } = true;

    public virtual void InitBinds(ILogger logger,
        RequirementFactory factory)
    {
        for (int i = 0; i < Sequence.Length; i++)
        {
            KeyAction keyAction = Sequence[i];

            keyAction.KeyOptional = !KeyRequired;
            keyAction.InitSlot(logger);
            factory.InitAutoBinds(keyAction);
        }
    }

    public void Init(ILogger logger, bool globalLog,
        PlayerReader playerReader, RecordInt globalTime,
        RequirementFactory factory)
    {
        for (int i = 0; i < Sequence.Length; i++)
        {
            KeyAction keyAction = Sequence[i];

            keyAction.Init(logger, globalLog, playerReader, globalTime);
            factory.Init(keyAction);
        }
    }
}