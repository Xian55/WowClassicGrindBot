using Newtonsoft.Json;

using System.IO;

namespace Core.Discord;

public sealed class DiscordConfig
{
    private const string ConfigFileName = "discord_config.json";

    [JsonIgnore]
    private string? _rootPath;

    // Webhook settings (for notifications)
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public bool NotifyOnWhisper { get; set; } = true;
    public bool NotifyOnSay { get; set; } = true;
    public bool NotifyOnYell { get; set; } = true;
    public bool NotifyOnEmote { get; set; } = false;
    public bool NotifyOnParty { get; set; } = false;
    public bool NotifyOnGuild { get; set; } = false;
    public bool IncludeScreenshot { get; set; } = true;

    // Bot settings (for commands)
    public string BotToken { get; set; } = string.Empty;
    public bool BotEnabled { get; set; } = false;
    public ulong CommandChannelId { get; set; } = 0;

    // Auto-logout settings
    public int MaxDeaths { get; set; } = 0; // 0 = disabled
    public bool AutoLogoutOnMaxDeaths { get; set; } = false;
    public int MaxStuckSeconds { get; set; } = 30; // 0 = disabled
    public bool AutoLogoutOnStuck { get; set; } = false;

    [JsonIgnore]
    public bool HasBotToken => !string.IsNullOrWhiteSpace(BotToken);

    [JsonIgnore]
    public bool HasWebhook => !string.IsNullOrWhiteSpace(WebhookUrl);

    public static DiscordConfig Load(string rootPath)
    {
        string path = Path.Combine(rootPath, ConfigFileName);

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

    public void Save()
    {
        if (string.IsNullOrEmpty(_rootPath))
            return;

        string path = Path.Combine(_rootPath, ConfigFileName);
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(path, json);
    }

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
