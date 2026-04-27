using System.Collections.Generic;

namespace Core;

/// <summary>
/// Per-spell cast event log.
/// <para>
/// The addon's global <c>lastCastEvent</c> cell only ever holds the most
/// recent event; if two events for two different spells fire between bot
/// polls, the second overwrites the first and the bot misses important
/// failure codes (e.g. ERR_BADATTACKFACING right before a successful
/// retry). This reader drains the addon's per-tick event queue (cell 113
/// encoded value + cell 114 monotonic seq) and keeps the latest event
/// keyed by spell ID, so consumers can ask "what was the last event for
/// spell X" without contamination from other spells.
/// </para>
/// </summary>
public sealed class CastEventReader : IReader
{
    private const int cEncoded = 113;
    private const int cSeq = 114;

    // Short codes the addon publishes for the cast-event sentinels — must
    // match EVENT_CODE_CAST_SENT/START/SUCCESS in EventHandlers.lua.
    private const int CODE_CAST_SENT = 251;
    private const int CODE_CAST_START = 252;
    private const int CODE_CAST_SUCCESS = 253;

    private readonly Dictionary<int, UI_ERROR> latestBySpellId = [];

    private int lastSeq;

    public void Update(IAddonDataProvider reader)
    {
        // Guard against a stale/short Data array — same bounds-safety pattern
        // as ActionBarCastTimeReader. Without this a cached frame_config that
        // doesn't include cells 113/114 yet would crash the addon thread.
        if ((uint)cSeq >= (uint)reader.Data.Length)
            return;

        int seq = reader.GetInt(cSeq);
        if (seq == lastSeq)
            return;

        lastSeq = seq;

        int encoded = reader.GetInt(cEncoded);
        if (encoded == 0)
            return;

        int spellId = encoded % 65536;
        int rawCode = encoded / 65536;
        if (spellId == 0 || rawCode == 0)
            return;

        UI_ERROR ev = MapEventCode(rawCode);
        latestBySpellId[spellId] = ev;
    }

    public void Reset()
    {
        latestBySpellId.Clear();
        lastSeq = 0;
    }

    /// <summary>
    /// Returns the most recently published event for the given spell, or
    /// <see cref="UI_ERROR.NONE"/> if the bot has never seen one.
    /// </summary>
    public UI_ERROR Latest(int spellId)
    {
        if (spellId == 0)
            return UI_ERROR.NONE;

        return latestBySpellId.TryGetValue(spellId, out UI_ERROR ev)
            ? ev
            : UI_ERROR.NONE;
    }

    /// <summary>
    /// Reads the latest event AND clears it, so each call observes a
    /// distinct event. Useful inside a per-cast verification loop where we
    /// want to react exactly once to BADATTACKFACING / OUT_OF_RANGE etc.
    /// </summary>
    public bool TryConsume(int spellId, out UI_ERROR ev)
    {
        if (spellId == 0)
        {
            ev = UI_ERROR.NONE;
            return false;
        }

        if (latestBySpellId.TryGetValue(spellId, out ev) && ev != UI_ERROR.NONE)
        {
            latestBySpellId[spellId] = UI_ERROR.NONE;
            return true;
        }

        ev = UI_ERROR.NONE;
        return false;
    }

    private static UI_ERROR MapEventCode(int rawCode) => rawCode switch
    {
        CODE_CAST_SENT => UI_ERROR.CAST_SENT,
        CODE_CAST_START => UI_ERROR.CAST_START,
        CODE_CAST_SUCCESS => UI_ERROR.CAST_SUCCESS,
        // Errors 1..18 in the addon's errorList line up 1:1 with the
        // small UI_ERROR enum values on this side (UI_ERROR.cs).
        _ => (UI_ERROR)rawCode
    };
}
