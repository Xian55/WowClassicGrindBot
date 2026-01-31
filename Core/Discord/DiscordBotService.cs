using Discord;
using Discord.WebSocket;

using Game;

using Microsoft.Extensions.Logging;

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Discord;

public sealed class DiscordBotService : IDisposable
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly ILogger<DiscordBotService> logger;
    private readonly IBotController botController;
    private readonly PlayerReader playerReader;
    private readonly BagReader bagReader;
    private readonly LevelTracker levelTracker;
    private readonly WowProcessInput wowInput;
    private readonly DiscordNotificationService notificationService;
    private readonly DiscordConfig config;
    private readonly CancellationTokenSource cts;

    private DiscordSocketClient? client;
    private bool disposed;
    private bool isConnected;

    public DiscordBotService(
        ILogger<DiscordBotService> logger,
        IBotController botController,
        PlayerReader playerReader,
        BagReader bagReader,
        LevelTracker levelTracker,
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
        this.wowInput = wowInput;
        this.notificationService = notificationService;
        this.cts = cts;

        config = DiscordConfig.Load(dataConfig.Root);

        if (config.BotEnabled && config.HasBotToken)
        {
            _ = InitializeAsync();
        }
        else
        {
            logger.LogInformation("Discord bot commands disabled (check discord_config.json)");
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var socketConfig = new DiscordSocketConfig
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

            logger.LogInformation("Discord bot starting...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Discord bot");
        }
    }

    private Task OnLog(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        logger.Log(level, msg.Exception, "[Discord] {Message}", msg.Message);
        return Task.CompletedTask;
    }

    private Task OnReady()
    {
        isConnected = true;
        logger.LogInformation("Discord bot connected as {Username}", client?.CurrentUser?.Username);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceived(SocketMessage message)
    {
        // Ignore bot messages
        if (message.Author.IsBot)
            return;

        // Check if we should restrict to a specific channel
        if (config.CommandChannelId != 0 && message.Channel.Id != config.CommandChannelId)
            return;

        string content = message.Content.Trim();
        string contentLower = content.ToLowerInvariant();

        // Only process commands starting with !
        if (!contentLower.StartsWith('!'))
            return;

        try
        {
            // Handle commands with arguments first
            if (contentLower.StartsWith("!say "))
            {
                await HandleSayCommand(message, content[5..]);
                return;
            }

            if (contentLower.StartsWith("!w "))
            {
                await HandleWhisperCommand(message, content[3..]);
                return;
            }

            if (contentLower.StartsWith("!r "))
            {
                await HandleReplyCommand(message, content[3..]);
                return;
            }

            if (contentLower.StartsWith("!g "))
            {
                await HandleGuildCommand(message, content[3..]);
                return;
            }

            switch (contentLower)
            {
                case "!start":
                    await HandleStartCommand(message);
                    break;

                case "!stop":
                    await HandleStopCommand(message);
                    break;

                case "!status":
                    await HandleStatusCommand(message);
                    break;

                case "!screenshot":
                case "!ss":
                    await HandleScreenshotCommand(message);
                    break;

                case "!reload":
                    await HandleReloadCommand(message);
                    break;

                case "!hearth":
                    await HandleHearthCommand(message);
                    break;

                case "!logout":
                case "!camp":
                    await HandleLogoutCommand(message);
                    break;

                case "!help":
                    await HandleHelpCommand(message);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling Discord command: {Command}", content);
            await message.Channel.SendMessageAsync($"Error: {ex.Message}");
        }
    }

    private async Task HandleStartCommand(SocketMessage message)
    {
        if (botController.IsBotActive)
        {
            await message.Channel.SendMessageAsync("Bot is already running.");
            return;
        }

        botController.ToggleBotStatus();
        await message.Channel.SendMessageAsync("Bot started!");
        logger.LogInformation("Bot started via Discord command by {User}", message.Author.Username);
    }

    private async Task HandleStopCommand(SocketMessage message)
    {
        if (!botController.IsBotActive)
        {
            await message.Channel.SendMessageAsync("Bot is not running.");
            return;
        }

        botController.ToggleBotStatus();
        await message.Channel.SendMessageAsync("Bot stopped!");
        logger.LogInformation("Bot stopped via Discord command by {User}", message.Author.Username);
    }

    private async Task HandleSayCommand(SocketMessage message, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await message.Channel.SendMessageAsync("Usage: `!say <message>`");
            return;
        }

        try
        {
            // Press Enter to open chat, type /say message, press Enter to send
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard($"/say {text}");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync($"Sent: /say {text}");
            logger.LogInformation("Chat message sent via Discord by {User}: {Text}", message.Author.Username, text);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to send message: {ex.Message}");
            logger.LogError(ex, "Failed to send chat message via Discord");
        }
    }

    private async Task HandleWhisperCommand(SocketMessage message, string args)
    {
        // Parse: <playername> <message>
        int spaceIndex = args.IndexOf(' ');
        if (spaceIndex == -1)
        {
            await message.Channel.SendMessageAsync("Usage: `!w <playername> <message>`");
            return;
        }

        string playerName = args[..spaceIndex].Trim();
        string text = args[(spaceIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(text))
        {
            await message.Channel.SendMessageAsync("Usage: `!w <playername> <message>`");
            return;
        }

        try
        {
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard($"/w {playerName} {text}");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync($"Sent whisper to {playerName}: {text}");
            logger.LogInformation("Whisper sent via Discord by {User} to {Player}: {Text}", message.Author.Username, playerName, text);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to send whisper: {ex.Message}");
            logger.LogError(ex, "Failed to send whisper via Discord");
        }
    }

    private async Task HandleReplyCommand(SocketMessage message, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await message.Channel.SendMessageAsync("Usage: `!r <message>`");
            return;
        }

        string? lastWhisper = notificationService.LastWhisperFrom;
        if (string.IsNullOrEmpty(lastWhisper))
        {
            await message.Channel.SendMessageAsync("No one has whispered you yet.");
            return;
        }

        try
        {
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard($"/w {lastWhisper} {text}");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync($"Replied to {lastWhisper}: {text}");
            logger.LogInformation("Reply sent via Discord by {User} to {Player}: {Text}", message.Author.Username, lastWhisper, text);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to send reply: {ex.Message}");
            logger.LogError(ex, "Failed to send reply via Discord");
        }
    }

    private async Task HandleGuildCommand(SocketMessage message, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await message.Channel.SendMessageAsync("Usage: `!g <message>`");
            return;
        }

        try
        {
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard($"/g {text}");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync($"Sent to guild: {text}");
            logger.LogInformation("Guild message sent via Discord by {User}: {Text}", message.Author.Username, text);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to send guild message: {ex.Message}");
            logger.LogError(ex, "Failed to send guild message via Discord");
        }
    }

    private async Task HandleReloadCommand(SocketMessage message)
    {
        try
        {
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard("/reload");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync("Sent /reload - UI is reloading...");
            logger.LogInformation("UI reload triggered via Discord by {User}", message.Author.Username);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to reload UI: {ex.Message}");
            logger.LogError(ex, "Failed to reload UI via Discord");
        }
    }

    private async Task HandleHearthCommand(SocketMessage message)
    {
        try
        {
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard("/use Hearthstone");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync("Using Hearthstone...");
            logger.LogInformation("Hearthstone used via Discord by {User}", message.Author.Username);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to use Hearthstone: {ex.Message}");
            logger.LogError(ex, "Failed to use Hearthstone via Discord");
        }
    }

    private async Task HandleLogoutCommand(SocketMessage message)
    {
        try
        {
            // Stop the bot first if it's running
            if (botController.IsBotActive)
            {
                botController.ToggleBotStatus();
            }

            wowInput.PressRandom(ConsoleKey.Enter, 50);
            await Task.Delay(100);

            wowInput.SetClipboard("/camp");
            wowInput.PasteFromClipboard();
            await Task.Delay(100);

            wowInput.PressRandom(ConsoleKey.Enter, 50);

            await message.Channel.SendMessageAsync("Logging out... (bot stopped, /camp sent)");
            logger.LogInformation("Logout triggered via Discord by {User}", message.Author.Username);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to logout: {ex.Message}");
            logger.LogError(ex, "Failed to logout via Discord");
        }
    }

    private async Task HandleStatusCommand(SocketMessage message)
    {
        string status = botController.IsBotActive ? "Running" : "Stopped";
        string profile = !string.IsNullOrEmpty(botController.SelectedClassFilename)
            ? botController.SelectedClassFilename
            : "None";

        int level = playerReader.Level.Value;
        int xpPercent = playerReader.PlayerXpPercent;
        TimeSpan ttl = levelTracker.TimeToLevel;
        string timeToLevel = ttl > TimeSpan.Zero
            ? $"{(int)ttl.TotalHours}h {ttl.Minutes}m"
            : "N/A";

        // Calculate bag space
        int totalSlots = bagReader.SlotCount;
        int usedSlots = bagReader.BagItems.Count;
        int freeSlots = totalSlots - usedSlots;
        string bagSpace = $"{freeSlots}/{totalSlots} free";

        var embed = new EmbedBuilder()
            .WithTitle("Bot Status")
            .WithColor(botController.IsBotActive ? global::Discord.Color.Green : global::Discord.Color.Red)
            .AddField("Status", status, true)
            .AddField("Profile", profile, true)
            .AddField("Level", level.ToString(), true)
            .AddField("XP Progress", $"{xpPercent}%", true)
            .AddField("Time to Level", timeToLevel, true)
            .AddField("Bag Space", bagSpace, true)
            .WithTimestamp(DateTimeOffset.Now)
            .Build();

        await message.Channel.SendMessageAsync(embed: embed);
    }

    private async Task HandleScreenshotCommand(SocketMessage message)
    {
        await message.Channel.SendMessageAsync("Taking screenshot...");

        try
        {
            // Capture fresh screenshot directly from primary screen
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            ms.Position = 0;

            await message.Channel.SendFileAsync(ms, $"screenshot_{DateTime.Now:HHmmss}.jpg", "Current game view:");
            logger.LogInformation("Screenshot sent via Discord command by {User}", message.Author.Username);
        }
        catch (Exception ex)
        {
            await message.Channel.SendMessageAsync($"Failed to capture screenshot: {ex.Message}");
        }
    }

    private static async Task HandleHelpCommand(SocketMessage message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("WoW Bot Commands")
            .WithColor(global::Discord.Color.Blue)
            .AddField("!start", "Start the bot", false)
            .AddField("!stop", "Stop the bot", false)
            .AddField("!status", "Get current bot status (incl. bag space)", false)
            .AddField("!screenshot / !ss", "Take and send a screenshot", false)
            .AddField("!say <message>", "Send a /say message in-game", false)
            .AddField("!w <name> <message>", "Whisper a player", false)
            .AddField("!r <message>", "Reply to last whisper", false)
            .AddField("!g <message>", "Send guild chat message", false)
            .AddField("!reload", "Reload WoW UI", false)
            .AddField("!hearth", "Use Hearthstone", false)
            .AddField("!logout / !camp", "Stop bot and logout (/camp)", false)
            .AddField("!help", "Show this help message", false)
            .Build();

        await message.Channel.SendMessageAsync(embed: embed);
    }

    public async Task DisconnectAsync()
    {
        if (client != null && isConnected)
        {
            await client.StopAsync();
            await client.LogoutAsync();
            isConnected = false;
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

            try
            {
                client.StopAsync().GetAwaiter().GetResult();
                client.LogoutAsync().GetAwaiter().GetResult();
            }
            catch { }

            client.Dispose();
        }
    }
}
