using Microsoft.Extensions.Logging;

using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Rectangle = System.Drawing.Rectangle;

namespace Core.Discord;

/// <summary>
/// Sends Discord webhook notifications for in-game events:
/// chat messages, player deaths, and stuck detection.
/// Subscribes to ChatReader and SessionStat events on construction.
/// </summary>
public sealed class DiscordNotificationService : IDisposable
{
    // Win32 constants for screen capture
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// How many seconds the bot must be stuck before sending a notification.
    /// </summary>
    private const int STUCK_THRESHOLD_SECONDS = 30;

    /// <summary>
    /// How often to check if the bot is stuck (milliseconds).
    /// </summary>
    private const int STUCK_CHECK_INTERVAL_MS = 5000;

    private readonly ILogger<DiscordNotificationService> logger;
    private readonly ChatReader chatReader;
    private readonly SessionStat sessionStat;
    private readonly DiscordConfig config;
    private readonly HttpClient httpClient;
    private readonly CancellationTokenSource cts;
    private readonly Timer? stuckCheckTimer;

    private bool disposed;
    private bool stuckNotificationSent;

    /// <summary>
    /// The name of the last player who whispered us.
    /// Used by the bot service for /reply functionality.
    /// </summary>
    public string? LastWhisperFrom { get; private set; }

    public DiscordNotificationService(
        ILogger<DiscordNotificationService> logger,
        ChatReader chatReader,
        SessionStat sessionStat,
        DataConfig dataConfig,
        CancellationTokenSource cts)
    {
        this.logger = logger;
        this.chatReader = chatReader;
        this.sessionStat = sessionStat;
        this.cts = cts;

        config = DiscordConfig.Load(dataConfig.Root);
        httpClient = new HttpClient();

        if (config.Enabled && config.HasWebhook)
        {
            // Subscribe to chat messages for forwarding to Discord
            chatReader.Messages.CollectionChanged += OnChatMessageReceived;

            // Subscribe to player death events
            sessionStat.OnDeath += OnPlayerDeath;

            // Periodically check if the bot is stuck
            stuckCheckTimer = new Timer(
                CheckStuckStatus, null,
                STUCK_CHECK_INTERVAL_MS, STUCK_CHECK_INTERVAL_MS);

            // Send a startup notification with screenshot
            if (config.NotifyOnStartup)
            {
                _ = SendStartupNotificationAsync();
            }

            logger.LogInformation("Discord notifications enabled");
        }
        else
        {
            logger.LogInformation("Discord notifications disabled (check discord_config.json)");
        }
    }

    /// <summary>
    /// Sends a startup notification with screenshot to confirm the bot is online.
    /// </summary>
    private async Task SendStartupNotificationAsync()
    {
        try
        {
            // Brief delay to let the application fully initialize before capturing
            await Task.Delay(3000, cts.Token);

            using MultipartFormDataContent content = new();
            AddPayload(content,
                "\ud83d\ude80 **Bot Started!**\nDiscord notifications are active.");
            AttachScreenshot(content, "startup");

            await PostWebhookAsync(content, "startup notification");
        }
        catch (OperationCanceledException) { /* Shutdown requested */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord startup notification");
        }
    }

    /// <summary>
    /// Handles new chat messages from the addon.
    /// Filters by type based on user preferences before forwarding to Discord.
    /// </summary>
    private void OnChatMessageReceived(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null)
            return;

        foreach (ChatMessageEntry entry in e.NewItems)
        {
            // Track the last whisperer for /reply support
            if (entry.Type == ChatMessageType.Whisper)
            {
                LastWhisperFrom = entry.Author;
            }

            // Only send notifications for enabled message types
            if (config.ShouldNotify(entry.Type))
            {
                _ = SendNotificationAsync(entry);
            }
        }
    }

    /// <summary>
    /// Handles player death events from SessionStat.
    /// </summary>
    private void OnPlayerDeath()
    {
        _ = SendDeathNotificationAsync();
    }

    /// <summary>
    /// Periodically checks if the bot is stuck and sends a one-time notification.
    /// Resets when the bot is no longer stuck.
    /// </summary>
    private void CheckStuckStatus(object? state)
    {
        int stuckSeconds = sessionStat.StuckSeconds;

        if (stuckSeconds >= STUCK_THRESHOLD_SECONDS && !stuckNotificationSent)
        {
            stuckNotificationSent = true;
            _ = SendStuckNotificationAsync(stuckSeconds);
        }
        else if (stuckSeconds == 0 && stuckNotificationSent)
        {
            // Bot recovered from being stuck - reset for next occurrence
            stuckNotificationSent = false;
        }
    }

    /// <summary>
    /// Sends a stuck notification with optional screenshot to the Discord webhook.
    /// </summary>
    private async Task SendStuckNotificationAsync(int stuckSeconds)
    {
        if (cts.IsCancellationRequested)
            return;

        try
        {
            string messageContent =
                $"\u26a0\ufe0f **BOT STUCK** for {stuckSeconds} seconds!";

            using MultipartFormDataContent content = new();
            AddPayload(content, messageContent);
            AttachScreenshot(content, "stuck");

            await PostWebhookAsync(content, "stuck notification");
        }
        catch (OperationCanceledException) { /* Shutdown requested */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord stuck notification");
        }
    }

    /// <summary>
    /// Sends a death notification with optional screenshot to the Discord webhook.
    /// </summary>
    private async Task SendDeathNotificationAsync()
    {
        if (cts.IsCancellationRequested)
            return;

        try
        {
            string messageContent =
                $"\u2620\ufe0f **YOU DIED!**\nTotal deaths this session: {sessionStat.Deaths}";

            using MultipartFormDataContent content = new();
            AddPayload(content, messageContent);
            AttachScreenshot(content, "death");

            await PostWebhookAsync(content, "death notification");
        }
        catch (OperationCanceledException) { /* Shutdown requested */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord death notification");
        }
    }

    /// <summary>
    /// Sends a chat message notification with optional screenshot to the Discord webhook.
    /// </summary>
    private async Task SendNotificationAsync(ChatMessageEntry entry)
    {
        if (cts.IsCancellationRequested)
            return;

        try
        {
            string emoji = entry.Type switch
            {
                ChatMessageType.Whisper => "\ud83d\udcac",  // speech bubble
                ChatMessageType.Say => "\ud83d\udde3\ufe0f", // speaking head
                ChatMessageType.Yell => "\ud83d\udce2",      // loudspeaker
                ChatMessageType.Emote => "\ud83c\udfad",     // performing arts
                ChatMessageType.Party => "\ud83d\udc65",     // busts in silhouette
                ChatMessageType.Guild => "\ud83d\udee1\ufe0f", // shield
                _ => "\u2709\ufe0f"                           // envelope
            };

            string typeLabel = entry.Type.ToString().ToUpperInvariant();
            string messageContent =
                $"{emoji} **{typeLabel}** from **{entry.Author}**\n> {entry.Message}";

            using MultipartFormDataContent content = new();
            AddPayload(content, messageContent);

            if (config.IncludeScreenshot)
            {
                AttachScreenshot(content, "screenshot");
            }

            await PostWebhookAsync(content, $"{entry.Type} from {entry.Author}");
        }
        catch (OperationCanceledException) { /* Shutdown requested */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord notification");
        }
    }

    /// <summary>
    /// Sends a test message to verify the webhook URL works.
    /// Called from the Discord configuration UI.
    /// </summary>
    public async Task SendTestNotification()
    {
        if (!config.Enabled || string.IsNullOrEmpty(config.WebhookUrl))
        {
            logger.LogWarning("Cannot send test: Discord notifications are disabled");
            return;
        }

        try
        {
            using MultipartFormDataContent content = new();
            AddPayload(content,
                "\u2705 **WoW Bot Connected!**\nDiscord notifications are working.");

            if (config.IncludeScreenshot)
            {
                AttachScreenshot(content, "test_screenshot");
            }

            await PostWebhookAsync(content, "test message");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord test message");
        }
    }

    // ── Helper Methods ───────────────────────────────────────────────

    /// <summary>
    /// Adds the JSON payload (message text + username) to the multipart content.
    /// </summary>
    private static void AddPayload(MultipartFormDataContent content, string message)
    {
        var payload = new { content = message, username = "WoW Bot Alert" };
        string json = JsonSerializer.Serialize(payload);
        content.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");
    }

    /// <summary>
    /// Captures a screenshot and attaches it to the multipart content.
    /// Silently skips if capture fails.
    /// </summary>
    private void AttachScreenshot(MultipartFormDataContent content, string prefix)
    {
        try
        {
            byte[] bytes = CaptureScreenshot();
            if (bytes.Length > 0)
            {
                ByteArrayContent imageContent = new(bytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(imageContent, "file", $"{prefix}_{DateTime.Now:HHmmss}.jpg");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to capture screenshot for {Prefix}", prefix);
        }
    }

    /// <summary>
    /// Posts the multipart content to the configured webhook URL and logs the result.
    /// </summary>
    private async Task PostWebhookAsync(MultipartFormDataContent content, string description)
    {
        HttpResponseMessage response = await httpClient.PostAsync(
            config.WebhookUrl, content, cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            logger.LogWarning("Discord webhook returned {StatusCode}: {Body}",
                response.StatusCode, body);
        }
        else
        {
            logger.LogInformation("Discord {Description} sent", description);
        }
    }

    /// <summary>
    /// Captures the primary screen as a JPEG byte array.
    /// </summary>
    private static byte[] CaptureScreenshot()
    {
        try
        {
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);

            using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));

            using MemoryStream ms = new();
            bitmap.Save(ms, ImageFormat.Jpeg);
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
        stuckCheckTimer?.Dispose();
        chatReader.Messages.CollectionChanged -= OnChatMessageReceived;
        sessionStat.OnDeath -= OnPlayerDeath;
        httpClient.Dispose();
    }
}
