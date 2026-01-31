using Game;

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

public sealed class DiscordNotificationService : IDisposable
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int STUCK_CHECK_INTERVAL_MS = 5000; // Check every 5 seconds

    private readonly ILogger<DiscordNotificationService> logger;
    private readonly ChatReader chatReader;
    private readonly SessionStat sessionStat;
    private readonly DiscordConfig config;
    private readonly HttpClient httpClient;
    private readonly CancellationTokenSource cts;
    private readonly Timer? stuckCheckTimer;
    private readonly IBotController? botController;
    private readonly WowProcessInput? wowInput;

    private bool disposed;
    private bool stuckNotificationSent;
    private bool maxDeathsReached;
    private bool maxStuckReached;

    /// <summary>
    /// The name of the last player who whispered us, for reply functionality
    /// </summary>
    public string? LastWhisperFrom { get; private set; }

    public DiscordNotificationService(
        ILogger<DiscordNotificationService> logger,
        ChatReader chatReader,
        SessionStat sessionStat,
        DataConfig dataConfig,
        CancellationTokenSource cts,
        IBotController? botController = null,
        WowProcessInput? wowInput = null)
    {
        this.logger = logger;
        this.chatReader = chatReader;
        this.sessionStat = sessionStat;
        this.cts = cts;
        this.botController = botController;
        this.wowInput = wowInput;

        config = DiscordConfig.Load(dataConfig.Root);
        httpClient = new HttpClient();

        if (config.Enabled && !string.IsNullOrEmpty(config.WebhookUrl))
        {
            chatReader.Messages.CollectionChanged += OnChatMessageReceived;
            sessionStat.OnDeath += OnPlayerDeath;
            stuckCheckTimer = new Timer(CheckStuckStatus, null, STUCK_CHECK_INTERVAL_MS, STUCK_CHECK_INTERVAL_MS);
            logger.LogInformation("Discord notifications enabled");
        }
        else
        {
            logger.LogInformation("Discord notifications disabled (check discord_config.json)");
        }
    }

    private void OnChatMessageReceived(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null)
            return;

        foreach (ChatMessageEntry entry in e.NewItems)
        {
            logger.LogInformation($"[Discord] Chat message received: Type={entry.Type}, Author={entry.Author}");

            // Track last whisper sender for reply functionality
            if (entry.Type == ChatMessageType.Whisper && !string.IsNullOrEmpty(entry.Author))
            {
                LastWhisperFrom = entry.Author;
            }

            if (config.ShouldNotify(entry.Type))
            {
                logger.LogInformation($"[Discord] Sending notification for {entry.Type} from {entry.Author}");
                _ = SendNotificationAsync(entry);
            }
            else
            {
                logger.LogInformation($"[Discord] Skipping notification for {entry.Type} (disabled in config)");
            }
        }
    }

    private void OnPlayerDeath()
    {
        logger.LogInformation("[Discord] Player death detected, sending notification");
        _ = SendDeathNotificationAsync();

        // Check if max deaths threshold is reached
        if (config.AutoLogoutOnMaxDeaths && config.MaxDeaths > 0 && !maxDeathsReached)
        {
            if (sessionStat.Deaths >= config.MaxDeaths)
            {
                maxDeathsReached = true;
                logger.LogWarning($"Max deaths threshold reached ({sessionStat.Deaths}/{config.MaxDeaths}). Stopping bot and logging out.");
                _ = TriggerAutoLogoutAsync();
            }
        }
    }

    private void CheckStuckStatus(object? state)
    {
        int stuckSeconds = sessionStat.StuckSeconds;

        // Check if max stuck threshold is reached and auto-logout is enabled
        if (config.AutoLogoutOnStuck && config.MaxStuckSeconds > 0 && !maxStuckReached)
        {
            if (stuckSeconds >= config.MaxStuckSeconds)
            {
                maxStuckReached = true;
                logger.LogWarning($"Max stuck threshold reached ({stuckSeconds}/{config.MaxStuckSeconds} seconds). Stopping bot and logging out.");
                _ = TriggerAutoLogoutOnStuckAsync(stuckSeconds);
                return; // Exit early to avoid sending duplicate notifications
            }
        }

        // Send notification if stuck for a while (but not yet at auto-logout threshold)
        int notificationThreshold = config.MaxStuckSeconds > 0 ? config.MaxStuckSeconds : 30;
        if (stuckSeconds >= notificationThreshold && !stuckNotificationSent)
        {
            stuckNotificationSent = true;
            _ = SendStuckNotificationAsync(stuckSeconds);
        }
        else if (stuckSeconds < notificationThreshold && stuckNotificationSent)
        {
            // Reset flag when no longer stuck
            stuckNotificationSent = false;
            maxStuckReached = false;
        }
    }

    private async Task SendStuckNotificationAsync(int stuckSeconds)
    {
        if (cts.IsCancellationRequested)
            return;

        try
        {
            string messageContent = $"\u26a0\ufe0f **BOT STUCK!**\nStuck for {stuckSeconds} seconds";

            using var content = new MultipartFormDataContent();

            var payload = new
            {
                content = messageContent,
                username = "WoW Bot Alert"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            content.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

            try
            {
                byte[] screenshotBytes = CaptureScreenshot();
                if (screenshotBytes.Length > 0)
                {
                    var imageContent = new ByteArrayContent(screenshotBytes);
                    imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    content.Add(imageContent, "file", $"stuck_{DateTime.Now:HHmmss}.jpg");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to capture screenshot for stuck notification");
            }

            var response = await httpClient.PostAsync(config.WebhookUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning($"Discord webhook returned {response.StatusCode}: {responseBody}");
            }
            else
            {
                logger.LogInformation("Discord stuck notification sent");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord stuck notification");
        }
    }

    private async Task SendDeathNotificationAsync()
    {
        if (cts.IsCancellationRequested)
            return;

        try
        {
            string messageContent = $"\u2620\ufe0f **YOU DIED!**\nTotal deaths this session: {sessionStat.Deaths}";

            using var content = new MultipartFormDataContent();

            var payload = new
            {
                content = messageContent,
                username = "WoW Bot Alert"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            content.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

            try
            {
                byte[] screenshotBytes = CaptureScreenshot();
                if (screenshotBytes.Length > 0)
                {
                    var imageContent = new ByteArrayContent(screenshotBytes);
                    imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    content.Add(imageContent, "file", $"death_{DateTime.Now:HHmmss}.jpg");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to capture screenshot for death notification");
            }

            var response = await httpClient.PostAsync(config.WebhookUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning($"Discord webhook returned {response.StatusCode}: {responseBody}");
            }
            else
            {
                logger.LogInformation("Discord death notification sent");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord death notification");
        }
    }

    private async Task SendNotificationAsync(ChatMessageEntry entry)
    {
        if (cts.IsCancellationRequested)
            return;

        try
        {
            string emoji = entry.Type switch
            {
                ChatMessageType.Whisper => "\ud83d\udcac",
                ChatMessageType.Say => "\ud83d\udde3\ufe0f",
                ChatMessageType.Yell => "\ud83d\udce2",
                ChatMessageType.Emote => "\ud83c\udfad",
                ChatMessageType.Party => "\ud83d\udc65",
                ChatMessageType.Guild => "\ud83d\udee1\ufe0f",
                _ => "\u2709\ufe0f"
            };

            string typeLabel = entry.Type.ToString().ToUpperInvariant();
            string messageContent = $"{emoji} **{typeLabel}** from **{entry.Author}**\n> {entry.Message}";

            using var content = new MultipartFormDataContent();

            var payload = new
            {
                content = messageContent,
                username = "WoW Bot Alert"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            content.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

            if (config.IncludeScreenshot)
            {
                try
                {
                    byte[] screenshotBytes = CaptureScreenshot();
                    if (screenshotBytes.Length > 0)
                    {
                        var imageContent = new ByteArrayContent(screenshotBytes);
                        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                        content.Add(imageContent, "file", $"screenshot_{DateTime.Now:HHmmss}.jpg");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to capture screenshot for Discord notification");
                }
            }

            var response = await httpClient.PostAsync(config.WebhookUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning($"Discord webhook returned {response.StatusCode}: {responseBody}");
            }
            else
            {
                logger.LogInformation($"Discord notification sent: {entry.Type} from {entry.Author}");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord notification");
        }
    }

    private byte[] CaptureScreenshot()
    {
        try
        {
            // Capture fresh screenshot directly from primary screen
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to capture fresh screenshot");
            return Array.Empty<byte>();
        }
    }

    public Task SendTestNotification() => SendTestMessageAsync();

    private async Task TriggerAutoLogoutAsync()
    {
        try
        {
            // Send Discord notification first
            string messageContent = $"⚠️ **AUTO-LOGOUT TRIGGERED!**\nMax deaths reached: {sessionStat.Deaths}/{config.MaxDeaths}\nStopping bot and logging out...";

            using var content = new MultipartFormDataContent();

            var payload = new
            {
                content = messageContent,
                username = "WoW Bot Alert"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            content.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

            try
            {
                byte[] screenshotBytes = CaptureScreenshot();
                if (screenshotBytes.Length > 0)
                {
                    var imageContent = new ByteArrayContent(screenshotBytes);
                    imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    content.Add(imageContent, "file", $"auto_logout_{DateTime.Now:HHmmss}.jpg");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to capture screenshot for auto-logout notification");
            }

            var response = await httpClient.PostAsync(config.WebhookUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning($"Discord webhook returned {response.StatusCode}: {responseBody}");
            }

            // Stop the bot if it's running
            if (botController != null && botController.IsBotActive)
            {
                botController.ToggleBotStatus();
                logger.LogInformation("Bot stopped due to max deaths");
            }

            // Send logout command
            if (wowInput != null)
            {
                await Task.Delay(500); // Brief delay before logout

                wowInput.PressRandom(ConsoleKey.Enter, 50);
                await Task.Delay(100);

                wowInput.SetClipboard("/camp");
                wowInput.PasteFromClipboard();
                await Task.Delay(100);

                wowInput.PressRandom(ConsoleKey.Enter, 50);

                logger.LogInformation("Logout command sent (/camp)");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to trigger auto-logout");
        }
    }

    private async Task TriggerAutoLogoutOnStuckAsync(int stuckSeconds)
    {
        try
        {
            // Send Discord notification first
            string messageContent = $"⚠️ **AUTO-LOGOUT TRIGGERED!**\nBot stuck for {stuckSeconds} seconds (threshold: {config.MaxStuckSeconds})\nStopping bot and logging out...";

            using var content = new MultipartFormDataContent();

            var payload = new
            {
                content = messageContent,
                username = "WoW Bot Alert"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            content.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

            try
            {
                byte[] screenshotBytes = CaptureScreenshot();
                if (screenshotBytes.Length > 0)
                {
                    var imageContent = new ByteArrayContent(screenshotBytes);
                    imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    content.Add(imageContent, "file", $"stuck_logout_{DateTime.Now:HHmmss}.jpg");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to capture screenshot for stuck auto-logout notification");
            }

            var response = await httpClient.PostAsync(config.WebhookUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning($"Discord webhook returned {response.StatusCode}: {responseBody}");
            }

            // Stop the bot if it's running
            if (botController != null && botController.IsBotActive)
            {
                botController.ToggleBotStatus();
                logger.LogInformation("Bot stopped due to being stuck too long");
            }

            // Send logout command
            if (wowInput != null)
            {
                await Task.Delay(500); // Brief delay before logout

                wowInput.PressRandom(ConsoleKey.Enter, 50);
                await Task.Delay(100);

                wowInput.SetClipboard("/camp");
                wowInput.PasteFromClipboard();
                await Task.Delay(100);

                wowInput.PressRandom(ConsoleKey.Enter, 50);

                logger.LogInformation("Logout command sent (/camp) due to stuck");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to trigger auto-logout on stuck");
        }
    }

    public async Task SendTestMessageAsync()
    {
        if (!config.Enabled || string.IsNullOrEmpty(config.WebhookUrl))
        {
            logger.LogWarning("Cannot send test message: Discord notifications are disabled");
            return;
        }

        try
        {
            using var content = new MultipartFormDataContent();

            var payload = new
            {
                content = "\u2705 **WowWhisper Connected!**\nDiscord notifications are working.",
                username = "WoW Bot Alert"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            content.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

            if (config.IncludeScreenshot)
            {
                try
                {
                    byte[] screenshotBytes = CaptureScreenshot();
                    if (screenshotBytes.Length > 0)
                    {
                        var imageContent = new ByteArrayContent(screenshotBytes);
                        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                        content.Add(imageContent, "file", $"test_screenshot.jpg");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to capture screenshot for test message");
                }
            }

            var response = await httpClient.PostAsync(config.WebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Discord test message sent successfully");
            }
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning($"Discord webhook test returned {response.StatusCode}: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord test message");
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
