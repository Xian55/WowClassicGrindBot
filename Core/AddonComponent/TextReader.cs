using System;
using System.Collections.ObjectModel;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Core;

/// <summary>
/// Command types for text queue. Must match Lua TextCommand values.
/// </summary>
public enum TextCommand
{
    ChatWhisper = 0,
    ChatSay = 1,
    ChatYell = 2,
    ChatEmote = 3,
    ChatParty = 4,
    TargetName = 5,
    TotemName = 6,
}

public enum ChatMessageType
{
    Whisper,
    Say,
    Yell,
    Emote,
    Party
}

public readonly record struct ChatMessageEntry(DateTime Time, ChatMessageType Type, string Author, string Message);

/// <summary>
/// Reads UTF-8 text from addon cells 98-99 using a unified encoding format.
/// Supports totem names, target names, and chat messages.
/// </summary>
public sealed class TextReader : IReader
{
    private const int CELL_TEXT_DATA = 98;
    private const int CELL_TEXT_META = 99;
    private const int MAX_BUFFER_SIZE = 1024;

    private readonly ILogger<TextReader> logger;
    private readonly byte[] buffer = new byte[MAX_BUFFER_SIZE];

    private int lastOffset = -1;
    private int currentLength;
    private TextCommand currentCommand;

    /// <summary>Event raised when a totem name is received.</summary>
    public event Action<string>? TotemNameReceived;

    /// <summary>Event raised when a target name is received.</summary>
    public event Action<string>? TargetNameReceived;

    /// <summary>Event raised when a chat message is received.</summary>
    public event Action<ChatMessageType, string, string>? ChatMessageReceived;

    /// <summary>The last totem name received from the addon.</summary>
    public string LastTotemName { get; private set; } = string.Empty;

    /// <summary>The last target name received from the addon.</summary>
    public string LastTargetName { get; private set; } = string.Empty;

    /// <summary>Collection of received chat messages for UI binding.</summary>
    public ObservableCollection<ChatMessageEntry> Messages { get; } = [];

    public TextReader(ILogger<TextReader> logger)
    {
        this.logger = logger;
    }

    public void Reset()
    {
        lastOffset = -1;
        currentLength = 0;
        LastTotemName = string.Empty;
        LastTargetName = string.Empty;
    }

    public void Update(IAddonDataProvider reader)
    {
        int meta = reader.GetInt(CELL_TEXT_META);
        if (meta == 0)
        {
            lastOffset = -1;
            return;
        }

        // Decode metadata: cmd(4 bits) | length(10 bits) | offset(10 bits)
        TextCommand cmd = (TextCommand)(meta >> 20);
        int length = (meta >> 10) & 0x3FF;
        int offset = meta & 0x3FF;

        // Skip if same position (already processed)
        if (offset == lastOffset)
            return;

        lastOffset = offset;

        // Decode 3 UTF-8 bytes from data cell
        int data = reader.GetInt(CELL_TEXT_DATA);
        buffer[offset] = (byte)((data >> 16) & 0xFF);
        if (offset + 1 < length)
            buffer[offset + 1] = (byte)((data >> 8) & 0xFF);
        if (offset + 2 < length)
            buffer[offset + 2] = (byte)(data & 0xFF);

        currentCommand = cmd;
        currentLength = length;

        // Check if message is complete (offset + 3 >= length)
        if (offset + 3 >= length)
        {
            ProcessCompleteMessage();
        }
    }

    private void ProcessCompleteMessage()
    {
        string text = Encoding.UTF8.GetString(buffer, 0, currentLength);

        switch (currentCommand)
        {
            case TextCommand.TotemName:
                LastTotemName = text;
                TotemNameReceived?.Invoke(text);
                logger.LogInformation("Totem name received: {Name}", text);
                break;

            case TextCommand.TargetName:
                LastTargetName = text;
                TargetNameReceived?.Invoke(text);
                logger.LogDebug("Target name received: {Name}", text);
                break;

            case TextCommand.ChatWhisper:
            case TextCommand.ChatSay:
            case TextCommand.ChatYell:
            case TextCommand.ChatEmote:
            case TextCommand.ChatParty:
                ProcessChatMessage(text);
                break;
        }

        lastOffset = -1;
    }

    private void ProcessChatMessage(string text)
    {
        int spaceIdx = text.IndexOf(' ');
        if (spaceIdx == -1)
        {
            logger.LogWarning("Malformed chat message: {Text}", text);
            return;
        }

        string author = text[..spaceIdx];
        string message = text[(spaceIdx + 1)..];

        ChatMessageType chatType = currentCommand switch
        {
            TextCommand.ChatWhisper => ChatMessageType.Whisper,
            TextCommand.ChatSay => ChatMessageType.Say,
            TextCommand.ChatYell => ChatMessageType.Yell,
            TextCommand.ChatEmote => ChatMessageType.Emote,
            TextCommand.ChatParty => ChatMessageType.Party,
            _ => ChatMessageType.Say
        };

        ChatMessageEntry entry = new(DateTime.Now, chatType, author, message);
        Messages.Add(entry);

        ChatMessageReceived?.Invoke(chatType, author, message);

        logger.LogInformation(entry.ToString());
    }

    /// <summary>
    /// Clears the last totem name. Call after successfully targeting the totem.
    /// </summary>
    public void ClearTotemName()
    {
        LastTotemName = string.Empty;
    }
}
