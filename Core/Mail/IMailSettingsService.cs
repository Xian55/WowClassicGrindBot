using System.Collections.Generic;

namespace Core;

public interface IMailSettingsService
{
    void SetRecipient(string recipient);
    void AddExclusion(int itemId);
    void RemoveExclusion(int itemId);
    IReadOnlySet<int> GetExclusions();
    void SetExclusions(IEnumerable<int> itemIds);
}
