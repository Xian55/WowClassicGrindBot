using Discord;
using Discord.WebSocket;

using Game;

using Microsoft.Extensions.Logging;

using System;
using System.Drawing;
using System.IO;
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
    private readonly SessionStat sessionStat;
    private readonly WowProcessInput wowInput;
    private readonly DiscordNotificationService notificationService;
    private readonly DiscordConfig config;
    private readonly CancellationTokenSource cts;

    private DiscordSocketClient? client;
    private bool disposed;

    // ── Auto-Reload ──────────────────────────────────────────────────

    /// <summary>
    /// Interval between automatic /reload commands (prevents addon memory buildup).
    /// </summary>
    private const int AUTO_RELOAD_INTERVAL_MS = 30 * 60 * 1000; // 30 minutes

    private readonly Thread autoReloadThread;
    private DateTime lastReloadTime = DateTime.MinValue;
    private bool reloadInProgress;

    public DiscordBotService(
        ILogger<DiscordBotService> logger,
        IBotController botController,
        PlayerReader playerReader,
        SessionStat sessionStat,
        WowProcessInput wowInput,
        DiscordNotificationService notificationService,
        DataConfig dataConfig,
        CancellationTokenSource cts)
    {
        this.logger = logger;
        this.botController = botController;
        this.playerReader = playerReader;
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

        // Start timed auto-reload regardless of bot settings
        autoReloadThread = new Thread(AutoReloadThread);
        autoReloadThread.Priority = ThreadPriority.BelowNormal;
        autoReloadThread.Start();
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
                    await HandleWhisperCommand(message.Channel, args);
                    break;
                case "!reply":
                    await HandleReplyCommand(message.Channel, args);
                    break;
                case "!guild":
                    await HandleGuildCommand(message.Channel, args);
                    break;
                case "!reload":
                    await HandleReloadCommand(message.Channel);
                    break;
                case "!logout":
                    await HandleLogoutCommand(message.Channel);
                    break;
                case "!hearth":
                    await HandleHearthCommand(message.Channel);
                    break;
                default:
                    await message.Channel.SendMessageAsync(
                        "Unknown command. Available: !status, !stop, !start, " +
                        "!screenshot, !say, !whisper, !reply, !guild, !reload, " +
                        "!logout, !hearth");
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

        string msg =
            $"**Bot Status:** {status}\n" +
            $"**Profile:** {className}\n" +
            $"**Level:** {playerReader.Level.Value}\n" +
            $"**HP:** {playerReader.HealthPercent()}%\n" +
            $"**Mana:** {playerReader.ManaPercent()}%\n" +
            $"**Session:** {sessionStat.Minutes} min\n" +
            $"**Kills:** {sessionStat.Kills}\n" +
            $"**Deaths:** {sessionStat.Deaths}";

        await channel.SendMessageAsync(msg);
    }

    private async Task HandleStopCommand(ISocketMessageChannel channel)
    {
        if (!botController.IsBotActive)
        {
            await channel.SendMessageAsync("Bot is already stopped.");
            return;
        }

        botController.Shutdown();
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
        ExecuteAutoReload();
        await channel.SendMessageAsync("Reload triggered.");
    }

    private async Task HandleLogoutCommand(ISocketMessageChannel channel)
    {
        if (botController.IsBotActive)
        {
            botController.Shutdown();
        }

        SendGameChat("/camp");
        await channel.SendMessageAsync("Logout command sent (/camp).");
    }

    private async Task HandleHearthCommand(ISocketMessageChannel channel)
    {
        if (botController.IsBotActive)
        {
            botController.Shutdown();
        }

        SendGameChat("/use Hearthstone");
        await channel.SendMessageAsync("Using Hearthstone...");
    }

    // ── Chat Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Sends a chat command to WoW by typing it into the chat box
    /// using clipboard paste for reliability with special characters.
    /// </summary>
    private void SendGameChat(string command)
    {
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
    }

    // ── Auto-Reload Thread ───────────────────────────────────────────

    /// <summary>
    /// Background thread that periodically triggers /reload to prevent
    /// WoW addon memory buildup from Lua string interning.
    /// </summary>
    private void AutoReloadThread()
    {
        logger.LogInformation(
            "Auto-reload started (interval: {Interval} minutes)",
            AUTO_RELOAD_INTERVAL_MS / 60000);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                cts.Token.WaitHandle.WaitOne(AUTO_RELOAD_INTERVAL_MS);
                if (cts.IsCancellationRequested) break;
                if (reloadInProgress) continue;

                logger.LogInformation("Triggering scheduled auto-reload...");
                ExecuteAutoReload();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in auto-reload thread");
            }
        }

        logger.LogInformation("Auto-reload stopped");
    }

    /// <summary>
    /// Executes a /reload by stopping the bot, sending the command,
    /// waiting for the reload to complete, then restarting the bot.
    /// </summary>
    private void ExecuteAutoReload()
    {
        if (reloadInProgress) return;
        reloadInProgress = true;

        try
        {
            bool wasRunning = botController.IsBotActive;

            if (wasRunning)
            {
                botController.Shutdown();
                Thread.Sleep(2000);
            }

            // Send /reload to WoW
            wowInput.SetForegroundWindow();
            Thread.Sleep(50);
            wowInput.PressRandom(ConsoleKey.Enter, 50);
            Thread.Sleep(100);
            wowInput.SendText("/reload");
            Thread.Sleep(100);
            wowInput.PressRandom(ConsoleKey.Enter, 50);

            // Wait for reload to complete
            Thread.Sleep(10000);

            lastReloadTime = DateTime.UtcNow;

            if (wasRunning)
            {
                botController.ToggleBotStatus();
                logger.LogInformation("Auto-reload complete, bot restarted");
            }
            else
            {
                logger.LogInformation("Auto-reload complete");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during auto-reload");
        }
        finally
        {
            reloadInProgress = false;
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
