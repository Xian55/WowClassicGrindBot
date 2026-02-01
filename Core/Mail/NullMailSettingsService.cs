using System.Collections.Frozen;
using System.Collections.Generic;

namespace Core;

public sealed class NullMailSettingsService : IMailSettingsService
{
    public void SetRecipient(string recipient) { }

    public void AddExclusion(int itemId) { }

    public void RemoveExclusion(int itemId) { }

    public IReadOnlySet<int> GetExclusions() => FrozenSet<int>.Empty;

    public void SetExclusions(IEnumerable<int> itemIds) { }
}
