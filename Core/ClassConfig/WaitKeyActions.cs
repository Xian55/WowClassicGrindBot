using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;

using static Core.Requirement;
using static Core.RequirementFactory;

namespace Core;

public sealed partial class WaitKeyActions : KeyActions
{
    public float FoodDrinkCost { get; set; } = 4.09f;

    public bool AutoGenerateWaitForFoodAndDrink { get; set; } = true;

    public void AddWaitKeyActionsForFoodOrDrink(ILogger logger,
        List<(string, KeyActions)> allKeyActions)
    {
        foreach ((string _, KeyActions actions) in allKeyActions)
        {
            foreach (KeyAction action in actions.Sequence)
            {
                if (action.Name.Equals(Food,
                    StringComparison.InvariantCultureIgnoreCase))
                {
                    AddNewKeyAction(logger,
                        action.Name,
                        $"{Food} {SymbolAnd} " +
                        $"{HealthP} < 100");
                }
                else if (action.Name.Equals(Drink,
                    StringComparison.InvariantCultureIgnoreCase))
                {
                    AddNewKeyAction(logger,
                        action.Name,
                        $"{Drink} {SymbolAnd} " +
                        $"{ManaP} < 100");
                }
            }
        }
    }

    private void AddNewKeyAction(ILogger logger,
        string keyActionName, string requirement)
    {
        string newActionName = $"{keyActionName} Buff";

        KeyAction waitAction = new()
        {
            Cost = FoodDrinkCost,
            Name = newActionName,
            Requirement = requirement,
            // Appended after InitBinds has already run for this section, so it never
            // picks the flag up from KeyRequired the way the declared entries do.
            KeyOptional = true
        };

        KeyAction[] keyActions = Sequence;

        int newSize = keyActions.Length + 1;
        Array.Resize(ref keyActions, newSize);

        keyActions[^1] = waitAction;

        Sequence = keyActions;

        LogAddedWait(logger, nameof(WaitKeyActions), newActionName, keyActionName);
    }

    #region logging

    [LoggerMessage(
        EventId = 0016,
        Level = LogLevel.Information,
        Message = "[{typeName}] Added {newActionName} to await {keyActionName}")]
    static partial void LogAddedWait(ILogger logger, string typeName, string newActionName, string keyActionName);

    #endregion

}