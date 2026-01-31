using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Core;

public sealed class MailSettingsService : IMailSettingsService
{
    private readonly IBotController botController;

    public MailSettingsService(IBotController botController)
    {
        this.botController = botController;
    }

    public void SetRecipient(string recipient)
    {
        if (botController.ClassConfig == null)
            return;

        // Initialize RuntimeMailConfig if null
        botController.ClassConfig.RuntimeMailConfig ??= new MailConfiguration();
        botController.ClassConfig.RuntimeMailConfig.RecipientName = recipient;
    }

    public void AddExclusion(int itemId)
    {
        if (botController.ClassConfig == null)
            return;

        // Initialize RuntimeMailConfig if null
        botController.ClassConfig.RuntimeMailConfig ??= new MailConfiguration();

        // Get current exclusions and add the new one
        HashSet<int> currentExclusions = [.. botController.ClassConfig.RuntimeMailConfig.ExcludedItemIds];
        currentExclusions.Add(itemId);
        botController.ClassConfig.RuntimeMailConfig.ExcludedItemIds = [.. currentExclusions];

        // Also update persisted config for JSON serialization
        botController.ClassConfig.MailConfig.ExcludedItemIds =
            botController.ClassConfig.GetEffectiveExcludedItemIds();
    }

    public void RemoveExclusion(int itemId)
    {
        if ((botController.ClassConfig?.RuntimeMailConfig) == null)
            return;

        HashSet<int> currentExclusions = [.. botController.ClassConfig.RuntimeMailConfig.ExcludedItemIds];
        currentExclusions.Remove(itemId);
        botController.ClassConfig.RuntimeMailConfig.ExcludedItemIds = [.. currentExclusions];

        // Also update persisted config for JSON serialization
        botController.ClassConfig.MailConfig.ExcludedItemIds =
            botController.ClassConfig.GetEffectiveExcludedItemIds();
    }

    public IReadOnlySet<int> GetExclusions()
    {
        if (botController.ClassConfig == null)
            return FrozenSet<int>.Empty;

        return botController.ClassConfig.GetEffectiveExcludedItemIdSet();
    }

    public void SetExclusions(IEnumerable<int> itemIds)
    {
        if (botController.ClassConfig == null)
            return;

        int[] exclusionArray = itemIds.ToArray();

        // Initialize RuntimeMailConfig if null
        botController.ClassConfig.RuntimeMailConfig ??= new MailConfiguration();
        botController.ClassConfig.RuntimeMailConfig.ExcludedItemIds = exclusionArray;

        // Also update persisted config for JSON serialization
        botController.ClassConfig.MailConfig.ExcludedItemIds = exclusionArray;
    }
}
