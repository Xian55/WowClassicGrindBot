using Discord;
using Discord.WebSocket;

using Game;

using Microsoft.Extensions.Logging;

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Discord;

/// <summary>
/// Interactive Discord bot that responds to text commands in a designated channel.
/// Also runs a timed auto-reload thread to prevent WoW addon memory buildup.
///
/// Supported commands:
///   !status    - Bot status, session stats, player info
///   !stop      - Stops the bot
///   !start     - Starts the bot
///   !screenshot- Captures and sends a screenshot
///   !say       - Sends a /say message in-game
///   !whisper   - Sends a /whisper to a player
///   !reply     - Replies to the last whisperer
///   !guild     - Sends a guild chat message
///   !reload    - Triggers a /reload in-game
///   !logout    - Logs the character out (/camp)
///   !hearth    - Uses Hearthstone
/// </summary>
public sealed class DiscordBotService : IDisposable
{
    // Win32 constants for screen capture
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly ILogger<DiscordBotService> logger;
    private readonly IBotController botController;
    private readonly PlayerReader playerReader;
    private readonly BagReader bagReader;
    private readonly LevelTracker levelTracker;
    private readonly SessionStat sessionStat;
    private readonly WowProcessInput wowInput;
    private readonly DiscordNotificationService notificationService;
    private readonly DiscordConfig config;
    private readonly CancellationTokenSource cts;

    private DiscordSocketClient? client;
    private bool disposed;

    public DiscordBotService(
        ILogger<DiscordBotService> logger,
        IBotController botController,
        PlayerReader playerReader,
        BagReader bagReader,
        LevelTracker levelTracker,
        SessionStat sessionStat,
        WowProcessInput wowInput,
        DiscordNotificationService notificationService,
        DataConfig dataConfig,
        CancellationTokenSource cts)
    {
        this.logger = logger;
        this.botController = botController;
        this.playerReader = playerReader;
        this.bagReader = bagReader;
        this.levelTracker = levelTracker;
        this.sessionStat = sessionStat;
        this.wowInput = wowInput;
        this.notificationService = notificationService;
        this.cts = cts;

        config = DiscordConfig.Load(dataConfig.Root);

        // Start the interactive Discord bot if configured
        if (config.BotEnabled && config.HasBotToken)
        {
            _ = InitializeAsync();
        }
        else
        {
            logger.LogInformation("Discord bot commands disabled (check discord_config.json)");
        }

    }

    /// <summary>
    /// Connects to Discord and begins listening for messages.
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            DiscordSocketConfig socketConfig = new()
            {
                GatewayIntents = GatewayIntents.Guilds |
                                 GatewayIntents.GuildMessages |
                                 GatewayIntents.MessageContent
            };

            client = new DiscordSocketClient(socketConfig);

            client.Log += OnLog;
            client.Ready += OnReady;
            client.MessageReceived += OnMessageReceived;

            await client.LoginAsync(TokenType.Bot, config.BotToken);
            await client.StartAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Discord bot");
        }
    }

    private Task OnLog(LogMessage msg)
    {
        logger.LogDebug("Discord.Net: {Message}", msg.ToString());
        return Task.CompletedTask;
    }

    private Task OnReady()
    {
        logger.LogInformation("Discord bot connected as {User}", client?.CurrentUser);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Routes incoming Discord messages to the appropriate command handler.
    /// Only processes messages in the configured command channel.
    /// </summary>
    private async Task OnMessageReceived(SocketMessage message)
    {
        // Ignore bot messages and messages from wrong channels
        if (message.Author.IsBot)
            return;

        if (config.CommandChannelId != 0 && message.Channel.Id != config.CommandChannelId)
            return;

        string content = message.Content.Trim();
        if (!content.StartsWith('!'))
            return;

        // Split command and arguments: "!whisper PlayerName hello" -> ["!whisper", "PlayerName hello"]
        string[] parts = content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();
        string args = parts.Length > 1 ? parts[1] : string.Empty;

        try
        {
            switch (command)
            {
                case "!status":
                    await HandleStatusCommand(message.Channel);
                    break;
                case "!stop":
                    await HandleStopCommand(message.Channel);
                    break;
                case "!start":
                    await HandleStartCommand(message.Channel);
                    break;
                case "!screenshot":
                case "!ss":
                    await HandleScreenshotCommand(message.Channel);
                    break;
                case "!say":
                    await HandleSayCommand(message.Channel, args);
                    break;
                case "!whisper":
                case "!w":
                    await HandleWhisperCommand(message.Channel, args);
                    break;
                case "!reply":
                case "!r":
                    await HandleReplyCommand(message.Channel, args);
                    break;
                case "!guild":
                case "!g":
                    await HandleGuildCommand(message.Channel, args);
                    break;
                case "!reload":
                    await HandleReloadCommand(message.Channel);
                    break;
                case "!logout":
                case "!camp":
                    await HandleLogoutCommand(message.Channel);
                    break;
                case "!hearth":
                    await HandleHearthCommand(message.Channel);
                    break;
                case "!help":
                    await HandleHelpCommand(message.Channel);
                    break;
                default:
                    await HandleHelpCommand(message.Channel);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling Discord command: {Command}", command);
            await message.Channel.SendMessageAsync($"Error: {ex.Message}");
        }
    }

    // ── Command Handlers ─────────────────────────────────────────────

    private async Task HandleStatusCommand(ISocketMessageChannel channel)
    {
        string status = botController.IsBotActive ? "Running" : "Stopped";
        string className = botController.ClassConfig?.FileName ?? "None";

        // Sum free slots across all general-purpose bags
        int freeSlots = bagReader.Bags
            .Where(b => b.BagType == BagType.Unspecified)
            .Sum(b => b.FreeSlot);
        int totalSlots = bagReader.Bags
            .Where(b => b.BagType == BagType.Unspecified)
            .Sum(b => b.SlotCount);

        // Format time to level
        TimeSpan ttl = levelTracker.TimeToLevel;
        string timeToLevel = ttl > TimeSpan.Zero
            ? $"{(int)ttl.TotalHours}h {ttl.Minutes}m"
            : "N/A";

        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle("Bot Status")
            .WithColor(botController.IsBotActive
                ? new global::Discord.Color(0x2E, 0xCC, 0x71)   // green when running
                : new global::Discord.Color(0xE7, 0x4C, 0x3C))  // red when stopped
            .AddField("Status", status, true)
            .AddField("Profile", className, true)
            .AddField("Level", playerReader.Level.Value.ToString(), true)
            .AddField("XP Progress", $"{playerReader.PlayerXpPercent}%", true)
            .AddField("Time to Level", timeToLevel, true)
            .AddField("Bag Space", $"{freeSlots}/{totalSlots} free", true)
            .AddField("HP / Mana", $"{playerReader.HealthPercent()}% / {playerReader.ManaPercent()}%", true)
            .AddField("Kills / Deaths", $"{sessionStat.Kills} / {sessionStat.Deaths}", true)
            .AddField("Session", $"{sessionStat.Minutes} min", true)
            .WithTimestamp(DateTimeOffset.Now);

        await channel.SendMessageAsync(embed: embed.Build());
    }

    private async Task HandleStopCommand(ISocketMessageChannel channel)
    {
        if (!botController.IsBotActive)
        {
            await channel.SendMessageAsync("Bot is already stopped.");
            return;
        }

        botController.ToggleBotStatus();
        await channel.SendMessageAsync("Bot stopped.");
    }

    private async Task HandleStartCommand(ISocketMessageChannel channel)
    {
        if (botController.IsBotActive)
        {
            await channel.SendMessageAsync("Bot is already running.");
            return;
        }

        botController.ToggleBotStatus();
        await channel.SendMessageAsync("Bot started.");
    }

    private async Task HandleScreenshotCommand(ISocketMessageChannel channel)
    {
        byte[] screenshotBytes = CaptureScreenshot();
        if (screenshotBytes.Length == 0)
        {
            await channel.SendMessageAsync("Failed to capture screenshot.");
            return;
        }

        logger.LogInformation("Sending screenshot to Discord");
        using MemoryStream ms = new(screenshotBytes);
        await channel.SendFileAsync(ms, "screenshot.jpg", "Current screen:");
    }

    private async Task HandleSayCommand(ISocketMessageChannel channel, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await channel.SendMessageAsync("Usage: !say <message>");
            return;
        }

        SendGameChat($"/say {text}");
        await channel.SendMessageAsync($"Sent /say: {text}");
    }

    private async Task HandleWhisperCommand(ISocketMessageChannel channel, string args)
    {
        // Expected format: "PlayerName message text"
        string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await channel.SendMessageAsync("Usage: !whisper <player> <message>");
            return;
        }

        string playerName = parts[0];
        string message = parts[1];

        SendGameChat($"/whisper {playerName} {message}");
        await channel.SendMessageAsync($"Whispered to {playerName}: {message}");
    }

    private async Task HandleReplyCommand(ISocketMessageChannel channel, string text)
    {
        string? lastWhisperer = notificationService.LastWhisperFrom;
        if (string.IsNullOrEmpty(lastWhisperer))
        {
            await channel.SendMessageAsync("No recent whispers to reply to.");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            await channel.SendMessageAsync("Usage: !reply <message>");
            return;
        }

        SendGameChat($"/whisper {lastWhisperer} {text}");
        await channel.SendMessageAsync($"Replied to {lastWhisperer}: {text}");
    }

    private async Task HandleGuildCommand(ISocketMessageChannel channel, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await channel.SendMessageAsync("Usage: !guild <message>");
            return;
        }

        SendGameChat($"/guild {text}");
        await channel.SendMessageAsync($"Sent to guild: {text}");
    }

    private async Task HandleReloadCommand(ISocketMessageChannel channel)
    {
        SendGameChat("/reload");
        await channel.SendMessageAsync("Reload triggered.");
    }

    private async Task HandleLogoutCommand(ISocketMessageChannel channel)
    {
        if (botController.IsBotActive)
        {
            botController.ToggleBotStatus();
            Thread.Sleep(500);
        }

        SendGameChat("/camp");
        await channel.SendMessageAsync("Logout command sent (/camp).");
    }

    private async Task HandleHearthCommand(ISocketMessageChannel channel)
    {
        if (botController.IsBotActive)
        {
            botController.ToggleBotStatus();
            Thread.Sleep(500);
        }

        SendGameChat("/use Hearthstone");
        await channel.SendMessageAsync("Using Hearthstone...");
    }

    private static async Task HandleHelpCommand(ISocketMessageChannel channel)
    {
        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle("WoW Bot Commands")
            .WithColor(new global::Discord.Color(0x58, 0x65, 0xF2)) // Discord blurple
            .WithDescription(
                "**!start**\nStart the bot\n\n" +
                "**!stop**\nStop the bot\n\n" +
                "**!status**\nGet current bot status (incl. bag space)\n\n" +
                "**!screenshot / !ss**\nTake and send a screenshot\n\n" +
                "**!say <message>**\nSend a /say message in-game\n\n" +
                "**!w <name> <message>**\nWhisper a player\n\n" +
                "**!r <message>**\nReply to last whisper\n\n" +
                "**!g <message>**\nSend guild chat message\n\n" +
                "**!reload**\nReload WoW UI\n\n" +
                "**!hearth**\nUse Hearthstone\n\n" +
                "**!logout / !camp**\nStop bot and logout (/camp)\n\n" +
                "**!help**\nShow this help message");

        await channel.SendMessageAsync(embed: embed.Build());
    }

    // ── Chat Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Sends a chat command to WoW by typing it into the chat box
    /// using clipboard paste for reliability with special characters.
    /// Temporarily stops the bot while typing to prevent input interference.
    /// </summary>
    private void SendGameChat(string command)
    {
        // Stop the bot to prevent it from sending inputs while we type
        bool wasRunning = botController.IsBotActive;
        if (wasRunning)
        {
            botController.ToggleBotStatus();
            Thread.Sleep(1000);
        }

        wowInput.SetForegroundWindow();
        Thread.Sleep(50);

        // Set clipboard and paste to avoid issues with special characters
        wowInput.SetClipboard(command);
        Thread.Sleep(30);

        // Open chat box
        wowInput.PressRandom(ConsoleKey.Enter, 50);
        Thread.Sleep(100);

        // Paste from clipboard (Ctrl+V)
        wowInput.PasteFromClipboard();
        Thread.Sleep(100);

        // Send the message
        wowInput.PressRandom(ConsoleKey.Enter, 50);
        Thread.Sleep(200);

        // Resume the bot if it was running before
        if (wasRunning)
        {
            botController.ToggleBotStatus();
        }
    }

    // ── Screenshot ───────────────────────────────────────────────────

    /// <summary>
    /// Captures the primary screen as a JPEG byte array.
    /// </summary>
    private static byte[] CaptureScreenshot()
    {
        try
        {
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);

            using Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));

            using MemoryStream ms = new();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch
        {
            return [];
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (client != null)
        {
            client.Log -= OnLog;
            client.Ready -= OnReady;
            client.MessageReceived -= OnMessageReceived;
            client.Dispose();
        }
    }
}
