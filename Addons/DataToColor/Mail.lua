--[[
    Mail.lua

    Automated mail sending functionality for DataToColor addon.
    Handles sending items (filtered by quality) and excess gold to a designated recipient.

    Uses a tick-based state machine for reliable multi-batch mail sending.

    API (called from C#):
    - SMC(recipient, minGoldToKeep, minQuality, sendGold): Set mail config
    - AEI(ids): Add excluded item IDs (comma-separated string, paginated)
    - SMS(): Start mail sending using stored config
]]

local Load = select(2, ...)
local DataToColor = unpack(Load)

-- Early exit if mail functionality is not enabled
-- Can be configured via DATA_CONFIG or left always available
if DataToColor.DATA_CONFIG and DataToColor.DATA_CONFIG.MAIL_ENABLED == false then
    return
end

-- Cache global functions at file scope for performance
local CreateFrame = CreateFrame
local UIParent = UIParent
local C_Timer = C_Timer
local floor = math.floor

local GetContainerNumSlots = DataToColor.GetContainerNumSlots
local GetContainerItemInfo = DataToColor.GetContainerItemInfo
local GetContainerItemLink = DataToColor.GetContainerItemLink
local PickupContainerItem = DataToColor.PickupContainerItem

local GetNumFriends = DataToColor.GetNumFriends
local GetFriendInfo = DataToColor.GetFriendInfo

local GetItemInfo = GetItemInfo
local GetMoney = GetMoney
local SendMail = SendMail
local SetSendMailMoney = SetSendMailMoney
local ClearSendMail = ClearSendMail
local ClickSendMailItemButton = ClickSendMailItemButton

local tonumber = tonumber
local wipe = wipe
local gmatch = string.gmatch
local GetSendMailItem = GetSendMailItem
local MailFrameTab_OnClick = MailFrameTab_OnClick
local SendMailSubjectEditBox = SendMailSubjectEditBox

-- Mail state constants (matching C# MailReader)
-- Note: Opened/Closed states are handled by the MailFrameShown bit, not gossip
local MAIL_SENDING          = 9999988
local MAIL_SEND_SUCCESS     = 9999987
local MAIL_SEND_FAILED      = 9999986
local MAIL_FINISHED         = 9999985

-- State machine states
local STATE_IDLE = 0
local STATE_SCANNING = 1
local STATE_ATTACHING = 2
local STATE_SENDING = 3

-- Maximum items per mail
local ATTACHMENTS_MAX_SEND = 12

-- Delay between mail sends (seconds) - prevents server-side rate limiting
local MAIL_SEND_DELAY = 1.0

-- Random delay range for item attachments (seconds) - makes attachment look natural
local ITEM_ATTACH_DELAY_MIN = 0.01  -- 10ms
local ITEM_ATTACH_DELAY_MAX = 0.10  -- 100ms

-- Cache math.random at file scope
local random = math.random

-- Generate random delay between min and max
local function RandomAttachDelay()
    return ITEM_ATTACH_DELAY_MIN + random() * (ITEM_ATTACH_DELAY_MAX - ITEM_ATTACH_DELAY_MIN)
end

-- Special recipient keywords
local KEYWORD_RANDOM_FRIEND = "UseRandomFriendList"

-- Gets a random friend name from friend list, or nil if list is empty
local function GetRandomFriend()
    local numFriends = GetNumFriends()
    if numFriends == 0 then
        return nil
    end

    -- Pick random friend (online or offline)
    local randomIndex = random(1, numFriends)
    local name = GetFriendInfo(randomIndex)
    return name
end

-- Resolves special recipient keywords to actual character names
local function ResolveRecipient(recipientName)
    if recipientName == KEYWORD_RANDOM_FRIEND then
        return GetRandomFriend()
    end
    return recipientName
end

-- Internal state
local mFrame = CreateFrame("Frame", nil, UIParent)

-- State machine
local mState = STATE_IDLE

-- Mail sending parameters (set by SMC - SetMailConfig)
local mRecipient = ""
local mMinGoldToKeep = 0
local mMinQuality = 0
local mSendGold = true
local mExcludedItems = {}

-- Current mail state
local mGoldToSend = false
local mGoldAttached = false

-- Reusable table for item scanning to avoid allocations
local mMailableItems = {}

-- State for item attachment
local mAttachIndex = 0

-- Forward declaration for Tick
local Tick

-- Checks if an item should be excluded from mailing
local function IsExcludedItem(itemId)
    return mExcludedItems[itemId] == true
end

-- Scans bags for mailable items (quality >= minQuality, not excluded, not bound)
local function ScanForMailableItems()
    wipe(mMailableItems)

    for bagId = 0, 4 do
        local numSlots = GetContainerNumSlots(bagId)
        for slot = 1, numSlots do
            local itemLink = GetContainerItemLink(bagId, slot)
            if itemLink then
                local itemId = tonumber(itemLink:match("item:(%d+)"))
                if itemId and not IsExcludedItem(itemId) then
                    -- Check if item is already bound (11th return from GetContainerItemInfo)
                    local _, _, _, _, _, _, _, _, _, _, isBound = GetContainerItemInfo(bagId, slot)
                    if not isBound then
                        -- Check item properties: quality (3rd), bindType (14th)
                        local _, _, quality, _, _, _, _, _, _, _, _, _, _, bindType = GetItemInfo(itemLink)
                        -- Skip BoP (1) and Quest items (4) - these cannot be mailed
                        if quality and quality >= mMinQuality and (not bindType or (bindType ~= 1 and bindType ~= 4)) then
                            mMailableItems[#mMailableItems + 1] = {
                                bag = bagId,
                                slot = slot,
                                itemId = itemId,
                                quality = quality
                            }
                        end
                    end
                end
            end
        end
    end

    return #mMailableItems > 0
end

-- Count actual items in mail attachment slots using WoW API
local function CountActualAttachments()
    local count = 0
    for i = 1, ATTACHMENTS_MAX_SEND do
        local name = GetSendMailItem(i)
        if name then
            count = count + 1
        end
    end
    return count
end

-- Find first empty attachment slot (1-12), returns nil if all full
local function FindFirstEmptySlot()
    for i = 1, ATTACHMENTS_MAX_SEND do
        if not GetSendMailItem(i) then
            return i
        end
    end
    return nil
end

-- Calculates and attaches excess gold
local function AttachGold()
    if mGoldAttached then
        return false
    end

    local currentGold = GetMoney() -- in copper
    local excess = currentGold - mMinGoldToKeep

    if excess > 0 then
        SetSendMailMoney(excess)
        mGoldAttached = true
        --DataToColor:Print("Mail: Attached " .. floor(excess / 10000) .. " gold")
        return true
    end

    return false
end

-- Gets subject from UI edit box, or returns fallback if empty
local function GetSubject(fallback)
    local text = SendMailSubjectEditBox:GetText()
    if text and text ~= "" then
        return text
    end
    return fallback or "Items"
end

-- Main tick function - advances state machine
Tick = function()
    -- STATE_IDLE: Do nothing
    if mState == STATE_IDLE then
        return
    end

    -- STATE_SCANNING: Scan bags for items
    if mState == STATE_SCANNING then
        ClearSendMail()
        mGoldAttached = false
        ScanForMailableItems()

        if #mMailableItems == 0 then
            -- No items left, check if we need to send gold
            if mGoldToSend then
                mGoldToSend = false
                if AttachGold() then
                    mState = STATE_SENDING
                    C_Timer.After(0.1, function()
                        DataToColor.gossipQueue:push(MAIL_SENDING)
                        --DataToColor:Print("Mail: Sending gold to " .. mRecipient)
                        SendMail(mRecipient, "Gold", "")
                    end)
                    return
                end
            end
            -- All done
            mState = STATE_IDLE
            DataToColor.gossipQueue:push(MAIL_FINISHED)
            --DataToColor:Print("Mail: Finished sending all mail")
            return
        end

        -- Have items to attach
        mAttachIndex = 0
        mState = STATE_ATTACHING
        C_Timer.After(0, Tick)  -- Start attaching next frame
        return
    end

    -- STATE_ATTACHING: Attach items one at a time
    if mState == STATE_ATTACHING then
        -- Count current attachments
        local attached = CountActualAttachments()

        -- Calculate max affordable items (30 copper per attachment slot)
        local currentMoney = GetMoney()
        local maxAffordable = floor(currentMoney / 30)

        -- Check affordability limit BEFORE max attachment limit
        if attached >= maxAffordable then
            if attached == 0 then
                -- Can't afford even 1 item
                --DataToColor:Print("Mail: Cannot afford any items (" .. currentMoney .. "c)")
                if mGoldToSend then
                    mGoldToSend = false
                    if AttachGold() then
                        mState = STATE_SENDING
                        DataToColor.gossipQueue:push(MAIL_SENDING)
                        SendMail(mRecipient, "Gold", "")
                        return
                    end
                end
                mState = STATE_IDLE
                DataToColor.gossipQueue:push(MAIL_FINISHED)
                return
            end

            -- Send what we can afford
            if mGoldToSend and #mMailableItems <= attached then
                AttachGold()
                mGoldToSend = false
            end
            mState = STATE_SENDING
            DataToColor.gossipQueue:push(MAIL_SENDING)
            --DataToColor:Print("Mail: Sending " .. attached .. " items (affordability limit)")
            SendMail(mRecipient, GetSubject("Items"), "")
            return
        end

        if attached >= ATTACHMENTS_MAX_SEND then
            -- Mail is full, send it
            -- Attach gold if this is the last batch
            if #mMailableItems <= ATTACHMENTS_MAX_SEND and mGoldToSend then
                AttachGold()
                mGoldToSend = false
            end
            mState = STATE_SENDING
            DataToColor.gossipQueue:push(MAIL_SENDING)
            --DataToColor:Print("Mail: Sending mail to " .. mRecipient .. " (" .. attached .. " items)")
            SendMail(mRecipient, GetSubject("Items"), "")
            return
        end

        -- Try to attach next item
        mAttachIndex = mAttachIndex + 1
        if mAttachIndex > #mMailableItems then
            -- No more items in this scan
            if attached > 0 or mGoldToSend then
                -- Attach gold with last batch
                if mGoldToSend then
                    AttachGold()
                    mGoldToSend = false
                end
                mState = STATE_SENDING
                -- Use "Gold" subject only for gold-only mails; items auto-populate subject in UI
                local subject = (attached == 0) and "Gold" or GetSubject("Items")
                DataToColor.gossipQueue:push(MAIL_SENDING)
                --DataToColor:Print("Mail: Sending mail to " .. mRecipient .. " (" .. attached .. " items)")
                SendMail(mRecipient, subject, "")
            else
                -- Nothing to send
                mState = STATE_IDLE
                DataToColor.gossipQueue:push(MAIL_FINISHED)
                --DataToColor:Print("Mail: Finished sending all mail")
            end
            return
        end

        -- Attach this item
        local item = mMailableItems[mAttachIndex]
        PickupContainerItem(item.bag, item.slot)

        C_Timer.After(0, function()
            -- Only continue if we're still in ATTACHING state
            if mState ~= STATE_ATTACHING then
                return
            end

            local slotIndex = FindFirstEmptySlot()
            if slotIndex then
                ClickSendMailItemButton(slotIndex)
            end
            -- Continue with random delay
            C_Timer.After(RandomAttachDelay(), Tick)
        end)
        return
    end

    -- STATE_SENDING: Waiting for event, do nothing in tick
end

-- Event handler for MAIL_SEND_SUCCESS
local function OnMailSendSuccess()
    if mState ~= STATE_SENDING then
        return
    end

    --DataToColor:Print("Mail: Send success!")
    DataToColor.gossipQueue:push(MAIL_SEND_SUCCESS)
    mGoldAttached = false

    -- Go back to scanning for next batch
    mState = STATE_SCANNING
    C_Timer.After(MAIL_SEND_DELAY, Tick)
end

-- Event handler for MAIL_FAILED
local function OnMailSendFailed()
    --DataToColor:Print("Mail: Send failed!")
    DataToColor.gossipQueue:push(MAIL_SEND_FAILED)
    mState = STATE_IDLE
end

-- Register events
mFrame:RegisterEvent("MAIL_SHOW")
mFrame:RegisterEvent("MAIL_CLOSED")
mFrame:RegisterEvent("MAIL_SEND_SUCCESS")
mFrame:RegisterEvent("MAIL_FAILED")

mFrame:SetScript("OnEvent", function(self, event, ...)
    if event == "MAIL_SHOW" then
        -- Mail frame opened - tracked via MailFrameShown bit, not gossip
        --DataToColor:Print("Mail: Mailbox opened")
    elseif event == "MAIL_CLOSED" then
        -- Only reset if we were in the middle of something
        if mState ~= STATE_IDLE then
            --DataToColor:Print("Mail: Mailbox closed while sending")
            mState = STATE_IDLE
        end
        -- Mail frame closed - tracked via MailFrameShown bit, not gossip
    elseif event == "MAIL_SEND_SUCCESS" then
        OnMailSendSuccess()
    elseif event == "MAIL_FAILED" then
        OnMailSendFailed()
    end
end)

--[[
    API Functions (called via /run from C#)
    Short names to stay within WoW's 255 character command limit
]]

-- SMC: Set Mail Config
-- Sets mail configuration (called once before starting)
-- Parameters:
--   recipient: Character name to send mail to (max 12 chars)
--   minGoldToKeep: Minimum gold to keep (in copper). Gold above this will be sent.
--   minQuality: Minimum item quality to send (0=grey, 1=white, 2=green, 3=blue, 4=epic)
--   sendGold: Whether to send gold (1=true, 0=false)
function DataToColor:SMC(recipient, minGoldToKeep, minQuality, sendGold)
    local resolvedRecipient = ResolveRecipient(recipient)
    mRecipient = resolvedRecipient or ""
    mMinGoldToKeep = minGoldToKeep or 0
    mMinQuality = minQuality or 0
    mSendGold = (sendGold ~= 0)
    wipe(mExcludedItems)  -- Reset excluded items when config changes
end

-- AEI: Add Excluded Items
-- Adds item IDs to exclusion list (called 0-N times, paginated)
-- Parameters:
--   ids: Comma-separated string of item IDs to exclude (e.g., "6948,12345,67890")
function DataToColor:AEI(ids)
    if not ids or ids == "" then return end
    for id in gmatch(ids, "(%d+)") do
        local itemId = tonumber(id)
        if itemId then
            mExcludedItems[itemId] = true
        end
    end
end

-- SMS: Start Mail Sending
-- Begins the mail sending process using stored config from SMC/AEI calls
function DataToColor:SMS()
    if mState ~= STATE_IDLE then
        --DataToColor:Print("Mail: Already sending")
        return
    end

    if mRecipient == "" then
        --DataToColor:Print("Mail: No recipient set")
        DataToColor.gossipQueue:push(MAIL_SEND_FAILED)
        return
    end

    MailFrameTab_OnClick(nil, 2)  -- Switch to Send Mail tab

    mGoldToSend = mSendGold
    mGoldAttached = false
    mState = STATE_SCANNING

    -- Push MAIL_SENDING immediately to signal a fresh operation has started
    -- This allows C# to distinguish new operations from stale MAIL_FINISHED states
    DataToColor.gossipQueue:push(MAIL_SENDING)

    --DataToColor:Print("Mail: Starting to send to " .. mRecipient)

    Tick()
end

-- Legacy function for backward compatibility
-- Can be removed once all C# code uses the new API
function DataToColor:StartMailSending(recipient, minGoldToKeep, minQuality, excludedIds, sendItems, sendGold)
    -- Use the new API internally
    DataToColor:SMC(recipient, minGoldToKeep, minQuality, sendGold and 1 or 0)

    -- Parse excluded IDs
    if excludedIds and excludedIds ~= "" then
        DataToColor:AEI(excludedIds)
    end

    -- Start sending
    if sendItems ~= false then
        DataToColor:SMS()
    elseif sendGold then
        -- Gold only mode
        mGoldToSend = true
        mState = STATE_SCANNING
        Tick()
    end
end