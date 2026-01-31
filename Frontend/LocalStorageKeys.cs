namespace Frontend;

/// <summary>
/// Single source of truth for localStorage key names used across the application.
/// Keep in sync with wwwroot/script/storage.js
/// </summary>
public static class LocalStorageKeys
{
    public const string MailRecipient = "mailRecipient";
    public const string MailExcludedItems = "mail_excluded_items";
    public const string CdnRegion = "cdn_region";
}
