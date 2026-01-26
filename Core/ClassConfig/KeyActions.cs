using Microsoft.Extensions.Logging;

using System;

namespace Core;

public class KeyActions
{
    public KeyAction[] Sequence { get; set; } =
        Array.Empty<KeyAction>();

    public virtual void InitBinds(ILogger logger,
        RequirementFactory factory)
    {
        for (int i = 0; i < Sequence.Length; i++)
        {
            KeyAction keyAction = Sequence[i];

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