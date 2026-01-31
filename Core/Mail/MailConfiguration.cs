using System.Collections.Frozen;
using System.Linq;

using Newtonsoft.Json;

namespace Core;

/// <summary>
/// Configuration template for automated mail sending functionality.
/// Items meeting quality threshold (excluding listed items) and excess gold
/// can be automatically sent to a designated recipient when visiting a mailbox.
/// This is a pure settings template - whether mail is enabled is controlled by
/// ClassConfiguration.Mail bool property.
/// </summary>
public sealed class MailConfiguration
{
    /// <summary>
    /// Environment variable name for the mail recipient.
    /// Set this environment variable to avoid storing the recipient name in config files.
    /// </summary>
    public const string RecipientEnvVar = "MAIL_RECIPIENT";

    /// <summary>
    /// Character name to send mail to.
    /// Can be left empty if using the MAIL_RECIPIENT environment variable.
    /// </summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>
    /// Minimum gold to keep on character (in copper).
    /// Gold above this threshold will be sent.
    /// Default: 0 (send all gold). Example: 10000 = 1 gold.
    /// </summary>
    public int MinimumGoldToKeep { get; set; } = 10000;

    /// <summary>
    /// Minimum item quality to send.
    /// 0 = Grey (Poor), 1 = White (Common), 2 = Green (Uncommon),
    /// 3 = Blue (Rare), 4 = Epic, 5 = Legendary.
    /// Default: 0 (send grey and above).
    /// </summary>
    public int MinimumItemQuality { get; set; }

    /// <summary>
    /// Whether to send gold above the MinimumGoldToKeep threshold.
    /// </summary>
    public bool SendGold { get; set; } = true;

    /// <summary>
    /// Whether to send items meeting the quality threshold.
    /// </summary>
    public bool SendItems { get; set; } = true;

    /// <summary>
    /// Item IDs to never mail (e.g., Hearthstone = 6948).
    /// These items will be excluded even if they meet the quality threshold.
    /// </summary>
    public int[] ExcludedItemIds { get; set; } = [];

    /// <summary>
    /// Cached FrozenSet of excluded item IDs for efficient lookups.
    /// Lazily initialized from ExcludedItemIds.
    /// </summary>
    [JsonIgnore]
    public FrozenSet<int> ExcludedItemIdSet => field ??=
        ExcludedItemIds.Length > 0 ? ExcludedItemIds.ToFrozenSet() : FrozenSet<int>.Empty;

    public override string ToString()
    {
        return $"MailConfiguration {{ SendGold = {SendGold}, SendItems = {SendItems}, MinimumGoldToKeep = {MinimumGoldToKeep}, MinimumItemQuality = {MinimumItemQuality}, ExcludedItemIds = [{string.Join(", ", ExcludedItemIds)}] }}";
    }
}
