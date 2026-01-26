using Microsoft.Extensions.Logging;

using System.Collections.Generic;

namespace Core;

public sealed class ActionBarPopulator
{
    internal sealed class ActionBarSlotItem
    {
        public string Name { get; }
        public KeyAction KeyAction { get; }
        public bool IsItem { get; }

        public ActionBarSlotItem(string name, KeyAction keyAction, bool isItem)
        {
            Name = name;
            KeyAction = keyAction;
            IsItem = isItem;
        }
    }

    private readonly ILogger<ActionBarPopulator> logger;
    private readonly ClassConfiguration config;
    private readonly BagReader bagReader;
    private readonly EquipmentReader equipmentReader;
    private readonly ExecGameCommand execGameCommand;

    public ActionBarPopulator(ILogger<ActionBarPopulator> logger,
        ClassConfiguration config, BagReader bagReader,
        EquipmentReader equipmentReader, ExecGameCommand execGameCommand)
    {
        this.logger = logger;

        this.config = config;
        this.bagReader = bagReader;
        this.equipmentReader = equipmentReader;
        this.execGameCommand = execGameCommand;
    }

    public void Execute()
    {
        List<ActionBarSlotItem> items = new();

        foreach ((string _, KeyActions keyActions) in config.GetByType<KeyActions>())
        {
            foreach (KeyAction keyAction in keyActions.Sequence)
            {
                AddUnique(items, keyAction);
            }
        }

        items.Sort((a, b) => a.KeyAction.Slot.CompareTo(b.KeyAction.Slot));

        foreach (ActionBarSlotItem absi in items)
        {
            if (ScriptBuilder(absi, out string content))
            {
                execGameCommand.Run(content);
            }
            else
            {
                logger.LogWarning($"Unable to populate " +
                    $"{absi.KeyAction.Name} -> " +
                    $"'{absi.Name}' is not valid Name or ID!");
            }
        }
    }

    private void AddUnique(List<ActionBarSlotItem> items, KeyAction keyAction)
    {
        // not bound to actionbar slot
        if (keyAction.Slot == 0) return;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].KeyAction.SlotIndex == keyAction.SlotIndex)
                return;
        }

        string name = keyAction.Name;
        bool isItem = false;

        if (name.Equals(RequirementFactory.Drink, System.StringComparison.OrdinalIgnoreCase))
        {
            name = bagReader.HighestQuantityOfDrinkItemId().ToString();
            isItem = true;
        }
        else if (name.Equals(RequirementFactory.Food, System.StringComparison.OrdinalIgnoreCase))
        {
            name = bagReader.HighestQuantityOfFoodItemId().ToString();
            isItem = true;
        }
        else if (keyAction.Item)
        {
            if (keyAction.Name == "Trinket 1")
            {
                name = equipmentReader.GetId((int)InventorySlotId.Trinket_1).ToString();
                isItem = true;
            }
            else if (keyAction.Name == "Trinket 2")
            {
                name = equipmentReader.GetId((int)InventorySlotId.Trinket_2).ToString();
                isItem = true;
            }
        }

        items.Add(new(name, keyAction, isItem));
    }

    private static bool ScriptBuilder(ActionBarSlotItem abs, out string content)
    {
        string nameOrId = $"\"{abs.Name}\"";
        if (int.TryParse(abs.Name, out int id))
        {
            nameOrId = id.ToString();
            if (nameOrId == "0")
            {
                content = "";
                return false;
            }
        }

        string func = GetFunction(abs);
        int slot = abs.KeyAction.SlotIndex + 1;
        content = $"/run {func}({nameOrId})PlaceAction({slot})ClearCursor()--";

        return true;
    }

    private static string GetFunction(ActionBarSlotItem a)
    {
        if (a.IsItem)
            return "PickupItem";

        if (char.IsLower(a.Name[0]))
            return "PickupMacro";

        return "PickupSpellBookItem";
    }
}
