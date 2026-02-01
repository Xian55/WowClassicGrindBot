using Newtonsoft.Json;

using System.IO;

namespace Core.Discord;

/// <summary>
/// Configuration for Discord webhook notifications and bot commands.
/// Loaded from Json/Discord/discord_config.json relative to the data root.
/// </summary>
public sealed class DiscordConfig
{
    private const string ConfigDirectory = "Discord";
    private const string ConfigFileName = "discord_config.json";

    /// <summary>
    /// Root path used for saving - not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    private string? _rootPath;

    // ── Webhook Settings ─────────────────────────────────────────────

    /// <summary>
    /// Discord webhook URL for sending notifications.
    /// Create one in Discord: Channel Settings > Integrations > Webhooks.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Master toggle for webhook notifications.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether to attach a screenshot with each notification.
    /// </summary>
    public bool IncludeScreenshot { get; set; } = true;

    // ── Notification Triggers ────────────────────────────────────────

    /// <summary>
    /// Send a notification with screenshot when the bot starts up.
    /// </summary>
    public bool NotifyOnStartup { get; set; } = true;

    public bool NotifyOnWhisper { get; set; } = true;
    public bool NotifyOnSay { get; set; } = true;
    public bool NotifyOnYell { get; set; } = true;
    public bool NotifyOnEmote { get; set; }
    public bool NotifyOnParty { get; set; }
    public bool NotifyOnGuild { get; set; }

    // ── Bot Settings ─────────────────────────────────────────────────

    /// <summary>
    /// Discord bot token from the Developer Portal.
    /// Required for interactive bot commands (!status, !stop, etc.).
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Whether the interactive bot is enabled.
    /// </summary>
    public bool BotEnabled { get; set; }

    /// <summary>
    /// Channel ID where the bot listens for commands.
    /// Right-click channel > Copy ID (requires Developer Mode).
    /// </summary>
    public ulong CommandChannelId { get; set; }

    // ── Computed Properties ──────────────────────────────────────────

    [JsonIgnore]
    public bool HasBotToken => !string.IsNullOrWhiteSpace(BotToken);

    [JsonIgnore]
    public bool HasWebhook => !string.IsNullOrWhiteSpace(WebhookUrl);

    // ── Load / Save ──────────────────────────────────────────────────

    /// <summary>
    /// Loads the Discord configuration from disk.
    /// Returns default config if file doesn't exist or is invalid.
    /// </summary>
    public static DiscordConfig Load(string rootPath)
    {
        string directory = Path.Combine(rootPath, ConfigDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ConfigFileName);

        DiscordConfig config;
        if (!File.Exists(path))
        {
            config = new DiscordConfig();
        }
        else
        {
            try
            {
                string json = File.ReadAllText(path);
                config = JsonConvert.DeserializeObject<DiscordConfig>(json) ?? new DiscordConfig();
            }
            catch
            {
                config = new DiscordConfig();
            }
        }

        config._rootPath = rootPath;
        return config;
    }

    /// <summary>
    /// Saves the current configuration to disk.
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_rootPath))
            return;

        string directory = Path.Combine(_rootPath, ConfigDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ConfigFileName);
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Checks whether a given chat message type should trigger a notification
    /// based on the current notification trigger settings.
    /// </summary>
    public bool ShouldNotify(ChatMessageType type)
    {
        return type switch
        {
            ChatMessageType.Whisper => NotifyOnWhisper,
            ChatMessageType.Say => NotifyOnSay,
            ChatMessageType.Yell => NotifyOnYell,
            ChatMessageType.Emote => NotifyOnEmote,
            ChatMessageType.Party => NotifyOnParty,
            ChatMessageType.Guild => NotifyOnGuild,
            _ => false
        };
    }
}
