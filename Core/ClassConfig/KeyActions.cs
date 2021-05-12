using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Core
{
    public class KeyActions
    {
        public List<KeyAction> Sequence { get; } = new List<KeyAction>();

        public void Initialise(AddonReader addonReader, RequirementFactory requirementFactory, ILogger logger)
        {
            Sequence.ForEach(i => i.Initialise(addonReader, requirementFactory, logger));
        }
    }
}