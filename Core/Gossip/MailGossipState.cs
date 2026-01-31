namespace Core;

/// <summary>
/// Mail operation states tracked via gossip queue.
/// Note: Mailbox open/closed UI state is tracked via AddonBits.MailFrameShown() bit.
/// </summary>
public enum MailGossipState
{
    None,
    Sending,
    ItemAttached,
    SendSuccess,
    SendFailed,
    Finished
}

public static class MailGossipStateExtensions
{
    public static string ToStringF(this MailGossipState state) => state switch
    {
        MailGossipState.None => nameof(MailGossipState.None),
        MailGossipState.Sending => nameof(MailGossipState.Sending),
        MailGossipState.ItemAttached => nameof(MailGossipState.ItemAttached),
        MailGossipState.SendSuccess => nameof(MailGossipState.SendSuccess),
        MailGossipState.SendFailed => nameof(MailGossipState.SendFailed),
        MailGossipState.Finished => nameof(MailGossipState.Finished),
        _ => nameof(MailGossipState.None)
    };
}
