using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core
{
    public class ActionBarPopulator
    {
        struct ActionBarSource {
            public string Name;
            public string Key;
            public bool Item;
            public string Requirement;
        }

        private readonly ILogger logger;
        private readonly ClassConfiguration config;
        private readonly AddonReader addonReader;
        private readonly ExecGameCommand execGameCommand;

        public ActionBarPopulator(ILogger logger, ClassConfiguration config, AddonReader addonReader, ExecGameCommand execGameCommand)
        {
            this.logger = logger;
            this.config = config;
            this.addonReader = addonReader;
            this.execGameCommand = execGameCommand;
        }

        private readonly List<ActionBarSource> sources = new List<ActionBarSource>();


        public async Task Execute()
        {
            CollectKeyActions();
            await Run();
        }

        private void CollectKeyActions()
        {
            sources.Clear();

            config.Adhoc.Sequence.ForEach(k => AddUnique(k));
            config.Parallel.Sequence.ForEach(k => AddUnique(k));
            config.Pull.Sequence.ForEach(k => AddUnique(k));
            config.Combat.Sequence.ForEach(k => AddUnique(k));
            config.NPC.Sequence.ForEach(k => AddUnique(k));

            ResolveConsumables();

            sources.Sort((a, b) => a.Key.CompareTo(b.Key));
        }


        private void AddUnique(KeyAction a)
        {
            if (!KeyReader.KeyMapping.ContainsKey(a.Key)) return;
            if (sources.Any(i => i.Key == a.Key)) return;

            var source = new ActionBarSource
            {
                Name = a.Name,
                Key = a.Key,
                Item = false,
                Requirement = a.Requirement
            };

            sources.Add(source);
        }

        private async Task Run()
        {
            foreach(var a in sources)
            {
                var content = ScriptBuilder(a);
                await execGameCommand.Run(content);
            }
        }


        #region Consumable

        private void ResolveConsumables()
        {
            ReplaceIfExists("Water", 
                addonReader.BagReader.HighestQuantityOfWaterId().ToString());

            ReplaceIfExists("Food",
                addonReader.BagReader.HighestQuantityOfFoodId().ToString());
        }

        private void ReplaceIfExists(string key, string val)
        {
            int index = sources.FindIndex(i => i.Name == key);
            if (index != -1)
            {
                var item = sources[index];
                item.Item = true;
                item.Name = val;
                sources[index] = item;
            }
        }

        #endregion


        private static string ScriptBuilder(ActionBarSource a)
        {
            string nameOrId = $"\"{a.Name}\"";
            if (int.TryParse(a.Name, out int id))
            {
                nameOrId = id.ToString();
            }

            string func = GetFunction(a);
            string slot = GetActionBarSlotHotkey(a);
            return $"/run {func}({nameOrId})PlaceAction({slot})ClearCursor()--";
        }

        private static string GetFunction(ActionBarSource a)
        {
            if (a.Item)
                return "PickupItem";

            if (char.IsLower(a.Name[0]))
                return "PickupMacro";

            return "PickupSpellBookItem";
        }
        
        private static string GetActionBarSlotHotkey(ActionBarSource a)
        {
            if(a.Key.StartsWith(KeyReader.BR))
                return CalculateActionNumber(a.Key, KeyReader.BR, KeyReader.BRIdx);

            if (a.Key.StartsWith(KeyReader.BL))
                return CalculateActionNumber(a.Key, KeyReader.BL, KeyReader.BLIdx);

            // "-" "=" keys
            if (KeyReader.ActionBarSlotMap.ContainsKey(a.Key))
                return CalculateActionNumber(KeyReader.ActionBarSlotMap[a.Key].ToString(), "", 0);

            return CalculateActionNumber(a.Key, "", 0);
        }

        private static string CalculateActionNumber(string key, string prefix, int offset)
        {
            if (!string.IsNullOrEmpty(prefix))
                key = key.Replace(prefix, "");

            if (int.TryParse(key, out int hotkey))
            {
                if (hotkey == 0)
                    return (offset + 10).ToString();

                return (offset + hotkey).ToString();
            }

            return key;
        }

    }
}
