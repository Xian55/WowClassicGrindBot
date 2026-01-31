using System.Collections.Generic;

namespace Core;

public sealed class GossipReader : IReader
{
    private const int cGossip = 73;

    // Mail state constants
    // Note: Opened/Closed states are handled by the MailFrameShown bit in AddonBits
    private const int MAIL_SENDING = 9999988;
    private const int MAIL_SUCCESS = 9999987;
    private const int MAIL_FAILED = 9999986;
    private const int MAIL_FINISHED = 9999985;
    private const int MAIL_ITEM_ATTACHED = 9999984;

    public int Count { private set; get; }
    public Dictionary<Gossip, int> Gossips { get; } = new();

    private int data;

    public bool Ready => Gossips.Count == Count;

    public bool GossipStart() => data == 69;
    public bool GossipEnd() => data == 9999994;

    public bool MerchantWindowOpened() => data == 9999999;

    public bool MerchantWindowClosed() => data == 9999998;

    public bool MerchantWindowSelling() => data == 9999997;

    public bool MerchantWindowSellingFinished() => data == 9999996;

    public bool GossipStartOrMerchantWindowOpened() => GossipStart() || MerchantWindowOpened();

    // Mail states (using gossipQueue slot 73)
    // Note: Mailbox open/closed state is tracked via AddonBits.MailFrameShown()

    public bool MailSending() => data == MAIL_SENDING;

    public bool MailSendSuccess() => data == MAIL_SUCCESS;

    public bool MailSendFailed() => data == MAIL_FAILED;

    public bool MailFinished() => data == MAIL_FINISHED;

    public bool MailItemAttached() => data == MAIL_ITEM_ATTACHED;

    public MailGossipState GetMailState() => data switch
    {
        MAIL_SENDING => MailGossipState.Sending,
        MAIL_SUCCESS => MailGossipState.SendSuccess,
        MAIL_FAILED => MailGossipState.SendFailed,
        MAIL_FINISHED => MailGossipState.Finished,
        MAIL_ITEM_ATTACHED => MailGossipState.ItemAttached,
        _ => MailGossipState.None
    };

    public GossipReader()
    {
    }

    public void Update(IAddonDataProvider reader)
    {
        data = reader.GetInt(cGossip);

        // used for merchant window open state
        if (MerchantWindowClosed() ||
            MerchantWindowOpened() ||
            MerchantWindowSelling() ||
            MerchantWindowSellingFinished() ||
            GossipEnd())
            return;

        // Mail states - don't process as gossip data
        if (MailSending() ||
            MailSendSuccess() ||
            MailSendFailed() ||
            MailFinished() ||
            MailItemAttached())
            return;

        if (data == 0 || GossipStart())
        {
            Count = 0;
            Gossips.Clear();
            return;
        }

        // formula
        // 10000 * count + 100 * index + value
        Count = data / 10000;
        int order = data / 100 % 100;
        Gossip gossip = (Gossip)(data % 100);

        Gossips[gossip] = order;
    }
}
