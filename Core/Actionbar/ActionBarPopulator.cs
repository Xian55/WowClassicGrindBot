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
    private readonly AddonConfig addonConfig;
    private readonly BagReader bagReader;
    private readonly EquipmentReader equipmentReader;
    private readonly ExecGameCommand execGameCommand;

    public ActionBarPopulator(ILogger<ActionBarPopulator> logger,
        ClassConfiguration config, AddonConfigurator addonConfigurator,
        BagReader bagReader, EquipmentReader equipmentReader,
        ExecGameCommand execGameCommand)
    {
        this.logger = logger;

        this.config = config;
        this.addonConfig = addonConfigurator.Config;
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
                logger.LogWarning("Unable to populate {ActionName} -> '{Name}' is not valid Name or ID!",
                    absi.KeyAction.Name, absi.Name);
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

    private bool ScriptBuilder(ActionBarSlotItem abs, out string content)
    {
        int actionSlot = abs.KeyAction.SlotIndex + 1;

        // For items, use PickupItem with item ID
        if (abs.IsItem)
        {
            if (int.TryParse(abs.Name, out int itemId) && itemId > 0)
            {
                content = $"/run PickupItem({itemId})PlaceAction({actionSlot})ClearCursor()--";
                return true;
            }
            content = "";
            return false;
        }

        // For macros (lowercase names), use PickupMacro
        if (char.IsLower(abs.Name[0]))
        {
            content = $"/run PickupMacro(\"{abs.Name}\")PlaceAction({actionSlot})ClearCursor()--";
            return true;
        }

        // For spells, use addon's PS() function which searches spellbook by name prefix
        // This handles ranked spells like "Immolate(Rank 9)" by matching "Immolate"
        content = $"/run {addonConfig.Title}:PS(\"{abs.Name}\",{actionSlot})";
        return true;
    }

    /// <summary>
    /// Places a single KeyAction on the action bar.
    /// Handles spells, macros, items, food, drink, and trinkets.
    /// </summary>
    public bool Place(KeyAction keyAction)
    {
        if (keyAction.Slot == 0 || string.IsNullOrEmpty(keyAction.Name))
            return false;

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

        var item = new ActionBarSlotItem(name, keyAction, isItem);
        if (ScriptBuilder(item, out string content))
        {
            execGameCommand.Run(content);
            return true;
        }

        logger.LogWarning("Unable to place {ActionName} -> '{Name}' is not valid!", keyAction.Name, name);
        return false;
    }
}
