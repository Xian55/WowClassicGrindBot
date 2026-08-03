local Load = select(2, ...)
local DataToColor = unpack(Load)

local CreateFrame = CreateFrame
local UIParent = UIParent
local C_Timer = C_Timer

local GetMoney = GetMoney
local GetSpellInfo = GetSpellInfo
local GetCoinTextureString = GetCoinTextureString
local gmatch = string.gmatch
local tonumber = tonumber
local pairs = pairs
local wipe = wipe

-- Deliberately not captured at file scope: the trainer API is absent on some clients,
-- and a nil local would raise on call instead of degrading to TRAINER_NO_MATCH.

-- Reported on the trainer cell. Both sit above any spell id and below
-- QUEUE_COUNT_MARKER, and are only ever read on this cell.
local TRAINER_NO_MATCH = 16776001
local TRAINER_NO_MONEY = 16776002

-- One purchase per tick: BuyTrainerService renumbers the service list, so the
-- next index is only trustworthy after TRAINER_UPDATE has been processed.
local ITERATION_INTERVAL = 0.3
local ITERATION_COUNT = 60

local tFrame = CreateFrame("Frame", nil, UIParent)
tFrame:RegisterEvent("TRAINER_SHOW")
tFrame:RegisterEvent("TRAINER_CLOSED")

-- spellId -> true, filled by TAdd
local mWanted = {}
-- spellId -> localized name, spellId -> rank subtext. Resolved once per run so the
-- match works on a localized client: GetSpellInfo answers for spells the player does
-- not know yet, which is exactly the case here.
local mName = {}
local mRank = {}

local mBought = {}
local mBoughtCount = 0
local mRunning = false
local mSawMatch = false
local mSawUnaffordable = false
-- Buy everything the trainer offers instead of only the wanted ids.
local mTrainAll = false
local mTicker
local mTicks = 0

local function ReportAndStop()
    if mTicker then
        mTicker:Cancel()
        mTicker = nil
    end

    if not mRunning then return end
    mRunning = false

    -- Reason first, then the batch. Nothing bought is still a completed visit, so the
    -- header is sent even when the count is zero - it is the only completion signal.
    if mBoughtCount == 0 then
        if mSawMatch and mSawUnaffordable then
            DataToColor:Print("Trainer: cannot afford any wanted spell")
            DataToColor.trainerQueue:push(TRAINER_NO_MONEY)
        elseif not mSawMatch then
            DataToColor:Print("Trainer: teaches none of the wanted spells")
            DataToColor.trainerQueue:push(TRAINER_NO_MATCH)
        end
    else
        DataToColor:Print("Trainer: learned " .. mBoughtCount .. " spell(s)")
    end

    DataToColor.trainerQueue:push(DataToColor.QUEUE_COUNT_MARKER + mBoughtCount)
    for i = 1, mBoughtCount do
        DataToColor.trainerQueue:push(mBought[i])
    end
end

-- The trainer UI is a separate Blizzard addon and MoP grants class spells on level up
-- instead of selling them, so neither the API nor a non-empty list can be assumed.
-- Returning 0 here reports TRAINER_NO_MATCH rather than raising: an error would take
-- out the ticker and leave the bot waiting for a completion that never arrives.
local function NumServices()
    if not GetNumTrainerServices then return 0 end
    return GetNumTrainerServices() or 0
end

-- Matches a trainer service against one wanted spell id.
-- Ranked clients (som/tbc/wrath) carry "Rank N" in both subtexts, so name plus rank
-- identifies the exact rank. Cata and MoP dropped ranks and answer an empty subtext,
-- where the name alone is unambiguous.
local function ServiceMatches(serviceName, serviceRank, spellId)
    if serviceName ~= mName[spellId] then
        return false
    end

    local rank = mRank[spellId]
    if rank and rank ~= "" then
        return rank == (serviceRank or "")
    end

    return true
end

-- The spell id behind a trainer service. Only needed for train-all, where no whitelist
-- named it. The tooltip is the one API that answers it, so a service whose id cannot be
-- resolved is skipped rather than bought blind - every id the bot is told about then
-- corresponds to a real purchase, and the count it waits on stays honest.
local function TrainerServiceSpellId(index)
    if not GameTooltip or not GameTooltip.SetTrainerService or not GameTooltip.GetSpell then
        return 0
    end

    GameTooltip:SetOwner(UIParent, "ANCHOR_NONE")
    GameTooltip:SetTrainerService(index)

    local _, spellId = GameTooltip:GetSpell()

    GameTooltip:Hide()

    return spellId or 0
end

local function Tick()
    if not mRunning then return end

    -- The ticker stops calling once its iterations run out. Reporting here rather than
    -- letting it lapse silently is what stops the bot waiting on a batch that never comes.
    mTicks = mTicks + 1
    if mTicks > ITERATION_COUNT then
        ReportAndStop()
        return
    end

    local count = NumServices()
    if count == 0 or not BuyTrainerService then
        ReportAndStop()
        return
    end

    local money = GetMoney()

    for i = 1, count do
        local serviceName, serviceRank, category = GetTrainerServiceInfo(i)

        if serviceName and category == "available" then
            -- Buy(spellId) returns true when a purchase happened, which ends this tick:
            -- indices shift afterwards, so the list is rescanned next time round.
            local function Buy(spellId)
                mSawMatch = true

                local cost = GetTrainerServiceCost(i) or 0
                if cost > money then
                    mSawUnaffordable = true
                    return false
                end

                BuyTrainerService(i)

                mBoughtCount = mBoughtCount + 1
                mBought[mBoughtCount] = spellId
                -- Learned, so it must not match again on the rescan.
                mWanted[spellId] = nil

                DataToColor:Print("Trainer: learning " .. serviceName ..
                    (serviceRank and serviceRank ~= "" and (" (" .. serviceRank .. ")") or "") ..
                    " for " .. (GetCoinTextureString and GetCoinTextureString(cost) or (cost .. "c")))

                return true
            end

            if mTrainAll then
                local spellId = TrainerServiceSpellId(i)
                if spellId > 0 and Buy(spellId) then
                    return
                end
            else
                for spellId in pairs(mWanted) do
                    if ServiceMatches(serviceName, serviceRank, spellId) and Buy(spellId) then
                        return
                    end
                end
            end
        end
    end

    -- A full pass bought nothing, so nothing further is buyable this visit.
    ReportAndStop()
end

tFrame:SetScript("OnEvent", function(self, event)
    if event == "TRAINER_CLOSED" then
        ReportAndStop()
    end
end)

-- TC: Trainer Clear
-- Drops any whitelist left over from a previous visit.
function DataToColor:TC()
    wipe(mWanted)
    wipe(mName)
    wipe(mRank)
end

-- TAdd: Add wanted spell ids
-- Called 1-N times, paginated to stay under the 255 character chat limit.
-- Parameters:
--   ids: Comma-separated string of spell ids (e.g. "6673,5242,100")
function DataToColor:TAdd(ids)
    if not ids or ids == "" then return end
    for id in gmatch(ids, "(%d+)") do
        local spellId = tonumber(id)
        if spellId then
            mWanted[spellId] = true
        end
    end
end

-- TGo: Start training
-- Buys every wanted spell the trainer offers and the player can afford.
-- Parameters:
--   trainAll: 1 to ignore the wanted list and buy whatever is available and affordable
function DataToColor:TGo(trainAll)
    if mRunning then return end

    wipe(mBought)
    mBoughtCount = 0
    mSawMatch = false
    mSawUnaffordable = false
    mTicks = 0
    mTrainAll = trainAll ~= nil and trainAll ~= 0
    mRunning = true

    -- Unhide everything so a service is never missed because of a leftover filter.
    if SetTrainerServiceTypeFilter then
        SetTrainerServiceTypeFilter("available", 1)
        SetTrainerServiceTypeFilter("unavailable", 1)
        SetTrainerServiceTypeFilter("used", 1)
    end

    for spellId in pairs(mWanted) do
        local name, rank = GetSpellInfo(spellId)
        mName[spellId] = name
        mRank[spellId] = rank or ""
    end

    if mTicker then mTicker:Cancel() end
    mTicker = C_Timer.NewTicker(ITERATION_INTERVAL, Tick, ITERATION_COUNT)

    Tick()
end
