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
