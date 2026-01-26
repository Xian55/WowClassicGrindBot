using Microsoft.Extensions.Logging;

using System;
using System.Collections.ObjectModel;
using System.Text;

namespace Core;

public enum ChatMessageType
{
    Whisper,
    Say,
    Yell,
    Emote,
    Party
}

public readonly record struct ChatMessageEntry(DateTime Time, ChatMessageType Type, string Author, string Message);

public sealed class ChatReader : IReader
{
    private const int cMsg = 98;
    private const int cMeta = 99;

    private readonly ILogger<ChatReader> logger;

    // 12 character name
    // 1 space
    // 256 maximum message length
    private readonly StringBuilder sb = new(12 + 1 + 256);

    public ObservableCollection<ChatMessageEntry> Messages { get; } = new();

    private int _head;

    public ChatReader(ILogger<ChatReader> logger)
    {
        this.logger = logger;
    }

    public void Update(IAddonDataProvider reader)
    {
        int meta = reader.GetInt(cMeta);
        if (meta == 0)
        {
            _head = 0;
            return;
        }

        ChatMessageType type = (ChatMessageType)(meta / 1000000);
        int length = meta % 1000000 / 1000;
        int head = meta % 1000;

        if (_head != head)
            _head = head;
        else if (_head == head)
            return;

        string part = reader.GetString(cMsg);

        sb.Append(part);

        if (head + 2 < length)
            return;

        string text = sb.ToString().ToLowerInvariant();
        sb.Clear();

        int firstSpaceIdx = text.AsSpan().IndexOf(' ');
        if (firstSpaceIdx == -1)
        {
            logger.LogError($"Malformed payload: {text}");
            return;
        }

        string author = text.AsSpan(0, firstSpaceIdx).ToString();
        string msg = text.AsSpan(firstSpaceIdx + 1).ToString();

        ChatMessageEntry entry = new(DateTime.Now, type, author, msg);
        Messages.Add(entry);
        logger.LogInformation(entry.ToString());
    }
}
