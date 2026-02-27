----------------------------------------------------------------------------
--  DataToColor
----------------------------------------------------------------------------

-- Trigger between emitting game data and frame location data
local SETUP_SEQUENCE = false
-- Total number of data frames generated
local NUMBER_OF_FRAMES = 114
-- Set number of pixel rows
local FRAME_ROWS = 1
-- Size of data squares in px. Varies based on rounding errors as well as dimension size. Use as a guideline, but not 100% accurate.
local CELL_SIZE = 1 -- 1-9
-- Spacing in px between data squares.
local CELL_SPACING = 1 -- 0 or 1

local GLOBAL_TIME_CELL = NUMBER_OF_FRAMES - 2

-- Dont modify values below

if not Enum or not Enum.PowerType then
	PowerType = {
		Mana = 0,
		Rage = 1,
		Focus = 2,
		Energy = 3,
		ComboPoints = 4,
		Runes = 5,
		RunicPower = 6,
		SoulShards = 7,
		LunarPower = 8,
		HolyPower = 9,
		Alternate = 10,
		Maelstrom = 11,
		Chi = 12,
		Insanity = 13,
		Obsolete = 14,
		Obsolete2 = 15,
		ArcaneCharges = 16,
		Fury = 17,
		Pain = 18,
		NumPowerTypes = 19
	}
else
	PowerType = Enum.PowerType
end
-- End of compatibility layer

local Load = select(2, ...)
local DataToColor = unpack(Load)

local band = bit.band
local rshift = bit.rshift
local floor = math.floor
local max = math.max

local strjoin = strjoin
local strfind = strfind
local sub = string.sub
local len = string.len
local upper = string.upper
local byte = string.byte
local debugstack = debugstack
local ceil = ceil
local floor = floor
local GetTime = GetTime

local UIParent = UIParent
local BackdropTemplateMixin = BackdropTemplateMixin
local C_Map = C_Map

local GetNetStats = GetNetStats

local CreateFrame = CreateFrame
local GetAddOnMetadata = GetAddOnMetadata or C_AddOns.GetAddOnMetadata

local UIErrorsFrame = UIErrorsFrame
local DEFAULT_CHAT_FRAME = DEFAULT_CHAT_FRAME

local HasAction = HasAction
local GetSpellBookItemName = GetSpellBookItemName
local GetNumTalentTabs = GetNumTalentTabs
local GetNumTalents = GetNumTalents
local GetTalentInfo = GetTalentInfo
local GetNumSpellTabs = GetNumSpellTabs
local IsSpellKnown = IsSpellKnown

local GetPlayerFacing = GetPlayerFacing
local GetUnitSpeed = GetUnitSpeed
local UnitLevel = UnitLevel
local UnitLevelSafe = DataToColor.UnitLevelSafe
local UnitHealthMax = UnitHealthMax
local UnitHealth = UnitHealth
local UnitPowerMax = UnitPowerMax
local UnitPower = UnitPower

local GetContainerNumFreeSlots = DataToColor.GetContainerNumFreeSlots
local GetContainerItemInfo = DataToColor.GetContainerItemInfo
local GetRuneCooldown = GetRuneCooldown
local GetRuneType = GetRuneType

local UnitBuff = UnitBuff
local UnitDebuff = UnitDebuff
local UnitXP = UnitXP
local UnitXPMax = UnitXPMax
local UnitExists = UnitExists
local UnitGUID = UnitGUID
local UnitClassification = UnitClassification

local GetMoney = GetMoney

local GetContainerNumSlots = DataToColor.GetContainerNumSlots
local GetComboPoints = GetComboPoints

local NUM_BAG_SLOTS = NUM_BAG_SLOTS

local ContainerIDToInventoryID = DataToColor.ContainerIDToInventoryID
local GetContainerItemLink = DataToColor.GetContainerItemLink
local PickupContainerItem = DataToColor.PickupContainerItem
local GetInventoryItemLink = GetInventoryItemLink
local DeleteCursorItem = DeleteCursorItem
local GetMerchantItemLink = GetMerchantItemLink
local GetItemInfo = GetItemInfo
local GetCoinTextureString = GetCoinTextureString
local UseContainerItem = DataToColor.UseContainerItem

local GetNumLootItems = GetNumLootItems

-- Calculate item flags bitmask for bag item
-- Bit 0 (1): IsTradable - can be mailed/traded
-- Bit 1 (2): IsSoulbound - already bound to player
-- Bit 2 (4): IsLocked - item is locked
-- Bit 3 (8): HasNoValue - cannot be vendored
local function GetItemFlags(bagId, slot, itemLink)
    if not itemLink then return 0 end

    local flags = 0

    -- Get container item info (isBound is 11th, isLocked is 3rd, hasNoValue is 9th)
    local _, _, isLocked, _, _, _, _, _, hasNoValue, _, isBound = GetContainerItemInfo(bagId, slot)

    -- Check bind type from GetItemInfo (14th return)
    -- bindType: 1=BoP, 2=BoE, 3=BoU, 4=Quest
    local _, _, _, _, _, _, _, _, _, _, _, _, _, bindType = GetItemInfo(itemLink)

    -- Bit 0: IsTradable (not bound AND not BoP AND not Quest)
    if not isBound and (not bindType or (bindType ~= 1 and bindType ~= 4)) then
        flags = flags + 1
    end

    -- Bit 1: IsSoulbound
    if isBound then
        flags = flags + 2
    end

    -- Bit 2: IsLocked
    if isLocked then
        flags = flags + 4
    end

    -- Bit 3: HasNoValue
    if hasNoValue then
        flags = flags + 8
    end

    return flags
end

-- initialization
local globalTick = 0

DataToColor.DATA_CONFIG = {
    ACCEPT_PARTY_REQUESTS = false, -- O
    DECLINE_PARTY_REQUESTS = false, -- O
    AUTO_REPAIR_ITEMS = true, -- O
    AUTO_RESURRECT = true,
    AUTO_SELL_GREY_ITEMS = true
}

local FRAME_CHANGE_RATE = 5
local initPhase = 2 * FRAME_CHANGE_RATE

-- How often item frames change
local ITEM_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the actionbar frames change
local ACTION_BAR_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the gossip frames change
local GOSSIP_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the spellbook frames change
local SPELLBOOK_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the spellbook frames change
local TALENT_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the spellbook frames change
local COMBAT_LOG_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the check network latency
local LATENCY_ITERATION_FRAME_CHANGE_RATE = 200 -- 500ms * refresh rate in ms
-- How often the lastLoot return from Closed to Corpse
local LOOT_RESET_RATE = FRAME_CHANGE_RATE
-- How often the Player Buff / target Debuff frames change
local AURA_DURATION_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE
-- How often the ChatHistory change
local CHAT_ITERATION_FRAME_CHANGE_RATE = FRAME_CHANGE_RATE

-- Timers
DataToColor.globalTime = 0
DataToColor.lastLoot = 0
DataToColor.lastLootResetStart = 0

DataToColor.map = DataToColor.GetBestMapForUnit(DataToColor.C.unitPlayer)
DataToColor.uiMapId = 0
DataToColor.uiErrorMessage = 0
DataToColor.uiErrorMessageTime = 0
DataToColor.gcdExpirationTime = 0

DataToColor.lastAutoShot = 0
DataToColor.lastMainHandMeleeSwing = 0
DataToColor.lastDamageDoneTime = 0
DataToColor.lastCastEvent = 0
DataToColor.lastCastSpellId = 0
DataToColor.lastCastGCD = 0

DataToColor.lastCastStartTime = 0
DataToColor.lastCastEndTime = 0
DataToColor.CastNum = 0

DataToColor.targetChanged = true

DataToColor.autoFollow = false
DataToColor.moving = false
DataToColor.channeling = false

DataToColor.playerGUID = UnitGUID(DataToColor.C.unitPlayer)
DataToColor.petGUID = UnitGUID(DataToColor.C.unitPet)

DataToColor.corpseInRange = 0

DataToColor.softInteractGuid = nil

local bagCache = {}

DataToColor.equipmentQueue = DataToColor.TimedQueue:new(ITEM_ITERATION_FRAME_CHANGE_RATE, nil)
DataToColor.bagQueue = DataToColor.TimedQueue:new(ITEM_ITERATION_FRAME_CHANGE_RATE, nil)
DataToColor.inventoryQueue = DataToColor.TimedQueue:new(ITEM_ITERATION_FRAME_CHANGE_RATE, nil)
DataToColor.gossipQueue = DataToColor.TimedQueue:new(GOSSIP_ITERATION_FRAME_CHANGE_RATE, 0)
DataToColor.spellBookQueue = DataToColor.TimedQueue:new(SPELLBOOK_ITERATION_FRAME_CHANGE_RATE, nil)
DataToColor.talentQueue = DataToColor.TimedQueue:new(TALENT_ITERATION_FRAME_CHANGE_RATE, nil)

DataToColor.actionBarCostQueue = DataToColor.struct:new(ACTION_BAR_ITERATION_FRAME_CHANGE_RATE)
DataToColor.actionBarCooldownQueue = DataToColor.struct:new(ACTION_BAR_ITERATION_FRAME_CHANGE_RATE)

DataToColor.eligibleKillCredit = {}

DataToColor.CombatDamageDoneQueue = DataToColor.TimedQueue:new(COMBAT_LOG_ITERATION_FRAME_CHANGE_RATE, 0)
DataToColor.CombatDamageTakenQueue = DataToColor.TimedQueue:new(COMBAT_LOG_ITERATION_FRAME_CHANGE_RATE, 0)
DataToColor.CombatCreatureDiedQueue = DataToColor.TimedQueue:new(COMBAT_LOG_ITERATION_FRAME_CHANGE_RATE, 0)
DataToColor.CombatMissTypeQueue = DataToColor.TimedQueue:new(COMBAT_LOG_ITERATION_FRAME_CHANGE_RATE, 0)
DataToColor.EnemySummonQueue = DataToColor.TimedQueue:new(COMBAT_LOG_ITERATION_FRAME_CHANGE_RATE, 0)

-- Unified TextQueue for UTF-8 text transfer (totem names, target names, chat, etc.)
DataToColor.TextQueue = DataToColor.TimedQueue:new(CHAT_ITERATION_FRAME_CHANGE_RATE, nil)
local textQueueHead = 0
local textQueueCurrentEntry = nil

-- Text command types (must match C# TextCommand enum)
local TEXT_CMD_CHAT_WHISPER = 0
local TEXT_CMD_CHAT_SAY = 1
local TEXT_CMD_CHAT_YELL = 2
local TEXT_CMD_CHAT_EMOTE = 3
local TEXT_CMD_CHAT_PARTY = 4
local TEXT_CMD_TARGET_NAME = 5
local TEXT_CMD_TOTEM_NAME = 6

-- Export for other files
DataToColor.TextCommand = {
    ChatWhisper = TEXT_CMD_CHAT_WHISPER,
    ChatSay = TEXT_CMD_CHAT_SAY,
    ChatYell = TEXT_CMD_CHAT_YELL,
    ChatEmote = TEXT_CMD_CHAT_EMOTE,
    ChatParty = TEXT_CMD_CHAT_PARTY,
    TargetName = TEXT_CMD_TARGET_NAME,
    TotemName = TEXT_CMD_TOTEM_NAME,
}

-- Pre-allocated free-list pool to avoid table allocation on push
local TEXT_ENTRY_POOL_SIZE = 16
local textEntryFreeList = {}
local textEntryFreeCount = TEXT_ENTRY_POOL_SIZE

-- Initialize pool at load time - all entries start free
for i = 1, TEXT_ENTRY_POOL_SIZE do
    textEntryFreeList[i] = { cmd = 0, text = "", len = 0 }
end

-- Acquire entry from pool (returns new table if pool exhausted)
local function acquireTextEntry()
    if textEntryFreeCount > 0 then
        local entry = textEntryFreeList[textEntryFreeCount]
        textEntryFreeList[textEntryFreeCount] = nil
        textEntryFreeCount = textEntryFreeCount - 1
        return entry
    end
    -- Pool exhausted - allocate new (rare case)
    return { cmd = 0, text = "", len = 0 }
end

-- Release entry back to pool
local function releaseTextEntry(entry)
    entry.text = ""
    entry.len = 0
    textEntryFreeCount = textEntryFreeCount + 1
    textEntryFreeList[textEntryFreeCount] = entry
end

-- Generic push to TextQueue (memory-efficient)
function DataToColor:PushText(command, text)
    if not text or text == "" then return end

    -- Fast path: most strings won't have emoji
    local finalText = text
    if not DataToColor:IsUTF8Safe(text) then
        finalText = DataToColor:FilterUTF8(text)
    end

    local len = #finalText
    if len > 0 then
        local entry = acquireTextEntry()
        entry.cmd = command
        entry.text = finalText
        entry.len = len
        DataToColor.TextQueue:push(entry)
    end
end

-- Convenience functions
function DataToColor:PushTotemName(name)
    DataToColor:PushText(TEXT_CMD_TOTEM_NAME, name)
end

function DataToColor:PushTargetName(name)
    DataToColor:PushText(TEXT_CMD_TARGET_NAME, name)
end

function DataToColor:PushChatMessage(command, author, msg)
    -- Strip realm from author (e.g., "PlayerName-Realm" -> "PlayerName")
    local dashPos = author:find('-')
    if dashPos then
        author = author:sub(1, dashPos - 1)
    end
    DataToColor:PushText(command, author .. ' ' .. msg)
end

DataToColor.playerPetSummons = {}

DataToColor.playerBuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
DataToColor.playerDebuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
DataToColor.targetBuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
DataToColor.targetDebuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
DataToColor.focusBuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)

DataToColor.customTrigger1 = {}

DataToColor.sessionKillCount = 0

function DataToColor:RegisterSlashCommands()
    DataToColor:RegisterChatCommand('dc', 'StartSetup')
    DataToColor:RegisterChatCommand('dccpu', 'GetCPUImpact')
    DataToColor:RegisterChatCommand('dcflush', 'FushState')
    DataToColor:RegisterChatCommand('dcbindings', 'SetDefaultBindings')
    DataToColor:RegisterChatCommand('dcactions', 'CreateSecureButtons')
    DataToColor:RegisterChatCommand('dccache', 'ToggleBitCache')
end

function DataToColor:ToggleBitCache()
    if DataToColor.BitCache and DataToColor.BitCache.toggle then
        DataToColor.BitCache.toggle()
    else
        DataToColor:Print("BitCache not available")
    end
end

function DataToColor:StartSetup()
    if not SETUP_SEQUENCE then
        SETUP_SEQUENCE = true
        DataToColor:Print("Config mode")
    else
        SETUP_SEQUENCE = false
        DataToColor:Print("Normal mode")
    end
end

function DataToColor:Print(...)
    DEFAULT_CHAT_FRAME:AddMessage(strjoin('', '|cff00b3ff', 'DataToColor:|r ', ...))
end

function DataToColor:error(msg)
    DataToColor:log("|cff0000ff" .. msg .. "|r")
    DataToColor:log(msg)
    DataToColor:log(debugstack())
    error(msg)
end

-- This function runs when addon is initialized/player logs in
function DataToColor:OnInitialize()
    DataToColor:CreateConstants()
    DataToColor:InitStorage()
    DataToColor:SetupRequirements()
    DataToColor:CreateFrames()
    DataToColor:RegisterSlashCommands()
    DataToColor:RegisterEvents()

    UIErrorsFrame:UnregisterEvent("UI_ERROR_MESSAGE")
end

function DataToColor:OnEnteringWorld()
    local version = GetAddOnMetadata('DataToColor', 'Version')
    DataToColor:Print("Welcome. Using " .. version)
    DataToColor:InitializeErrorLists()

    DataToColor:PopulateSpellBookInfo()

    DataToColor:InitUpdateQueues()
    DataToColor:InitTrigger(DataToColor.customTrigger1)

    DataToColor.Libs.RangeCheck:activate()

    -- Ensure UIErrorsFrame stays disabled after loading screens
    UIErrorsFrame:UnregisterEvent("UI_ERROR_MESSAGE")

    -- Initialize event-driven bit caching
    DataToColor:RegisterBitCacheEvents()

    -- Initialize event-driven aura caching
    DataToColor:RegisterAuraCacheEvents()

    -- Deferred auto-setup of bindings (1 second delay to ensure everything is ready)
    DataToColor:ScheduleAutoSetup()
end

function DataToColor:SetupRequirements()
    -- Gameplay settings
    DataToColor.SafeSetCVar("autoInteract", 1)
    DataToColor.SafeSetCVar("autoLootDefault", 1)
    DataToColor.SafeSetCVar("UnitNameNPC", 1)                  -- NPC Names: Enabled

    -- Camera settings (Interface -> Camera / Mouse)
    DataToColor.SafeSetCVar("cameraSmoothStyle", 2)           -- Camera Following Style: Always
    DataToColor.SafeSetCVar("cameraSmoothTrackingStyle", 2)   -- Click-to-Move Camera Style: Always
    DataToColor.SafeSetCVar("cameraFollowSpeed", 100)          -- Auto-Follow Speed: Max

    -- Graphics settings required for pixel reading
    DataToColor.SafeSetCVar("ffxAntiAliasingMode", 0)  -- Anti-Aliasing: None
    DataToColor.SafeSetCVar("vsync", 0)                 -- Vertical Sync: Disabled
    DataToColor.SafeSetCVar("renderScale", 1)           -- Render Scale: 100%
    DataToColor.SafeSetCVar("ffxGlow", 0)               -- Disable glow effect

    -- Display calibration
    DataToColor.SafeSetCVar("Contrast", 50)
    DataToColor.SafeSetCVar("Brightness", 50)
    DataToColor.SafeSetCVar("Gamma", 1)
end

function DataToColor:CreateConstants()
    for i = 1, 4 do
        DataToColor.C.unitPartyNames[i] = DataToColor.C.unitParty .. i
        DataToColor.C.unitPartyPetNames[i] = DataToColor.C.unitPartyNames[i] .. DataToColor.C.unitPet
    end
end

function DataToColor:Reset()
    DataToColor.S.playerSpellBookName = {}
    DataToColor.S.playerSpellBookId = {}
    DataToColor.S.playerSpellBookIdHighest = {}
    DataToColor.S.playerSpellBookIconId = {}

    DataToColor.playerGUID = UnitGUID(DataToColor.C.unitPlayer)
    DataToColor.petGUID = UnitGUID(DataToColor.C.unitPet)
    DataToColor.map = DataToColor.GetBestMapForUnit(DataToColor.C.unitPlayer)

    DataToColor.eligibleKillCredit = {}

    DataToColor.globalTime = 0
    DataToColor.lastLoot = 0
    DataToColor.uiErrorMessage = 0
    DataToColor.uiErrorMessageTime = 0
    DataToColor.gcdExpirationTime = 0

    DataToColor.lastAutoShot = 0
    DataToColor.lastMainHandMeleeSwing = 0
    DataToColor.lastCastEvent = 0
    DataToColor.lastCastSpellId = 0
    DataToColor.lastCastGCD = 0

    DataToColor.lastCastStartTime = 0
    DataToColor.CastNum = 0

    DataToColor.corpseInRange = 0

    DataToColor.sessionKillCount = 0

    DataToColor.softInteractGuid = nil

    globalTick = 0

    bagCache = {}

    DataToColor.actionBarCooldownQueue = DataToColor.struct:new(ACTION_BAR_ITERATION_FRAME_CHANGE_RATE)

    DataToColor:InvalidateCurrentActionCache()
    DataToColor:InvalidateActionUseableCache()
    DataToColor:InvalidateDurabilityCache()
    DataToColor:InvalidatePetNameCache()
    DataToColor:InvalidateShapeshiftCache()

    DataToColor.playerBuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
    DataToColor.playerDebuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
    DataToColor.targetBuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
    DataToColor.targetDebuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)
    DataToColor.focusBuffTime = DataToColor.struct:new(AURA_DURATION_ITERATION_FRAME_CHANGE_RATE)

    DataToColor.playerPetSummons = {}

    -- Reinitialize bit cache
    if DataToColor.BitCache and DataToColor.BitCache.reinitialize then
        DataToColor.BitCache.reinitialize()
    end

    -- Refresh aura caches
    if DataToColor.AuraCache and DataToColor.AuraCache.refresh then
        DataToColor.AuraCache.refresh()
    end
end

function DataToColor:Update()
    DataToColor.globalTime = DataToColor.globalTime + 1
    if DataToColor.globalTime > (256 * 256 * 256 - 1) then
        -- overflow wont trigger init state at backend
        DataToColor.globalTime = initPhase
    end
end

function DataToColor:ClearAllQueues()
    -- Clear TimedQueues
    DataToColor.equipmentQueue:clear()
    DataToColor.bagQueue:clear()
    DataToColor.inventoryQueue:clear()
    DataToColor.gossipQueue:clear()
    DataToColor.spellBookQueue:clear()
    DataToColor.talentQueue:clear()
    DataToColor.CombatDamageDoneQueue:clear()
    DataToColor.CombatDamageTakenQueue:clear()
    DataToColor.CombatCreatureDiedQueue:clear()
    DataToColor.CombatMissTypeQueue:clear()
    DataToColor.EnemySummonQueue:clear()
    DataToColor.TextQueue:clear()
    DataToColor.bindingQueue:clear()
    DataToColor.actionBarTextureQueue:clear()
    DataToColor.actionBarMacroQueue:clear()
end

function DataToColor:FushState()
    DataToColor.targetChanged = true

    DataToColor:Reset()
    DataToColor:ClearAllQueues()

    DataToColor:PopulateSpellBookInfo()
    DataToColor:InitUpdateQueues()

    DataToColor:Print('Flush State')
end

function DataToColor:ConsumeChanges()
    if DataToColor.targetChanged then
        DataToColor.targetChanged = false
    end
end

function DataToColor:InitUpdateQueues()
    DataToColor:InitEquipmentQueue()
    DataToColor:InitBagQueue()

    DataToColor:InitInventoryQueue(4)
    DataToColor:InitInventoryQueue(3)
    DataToColor:InitInventoryQueue(2)
    DataToColor:InitInventoryQueue(1)
    DataToColor:InitInventoryQueue(0)

    DataToColor:InitActionBarCostQueue()
    DataToColor:InitSpellBookQueue()
    DataToColor:InitTalentQueue()
    DataToColor:InitBindingQueue()
    DataToColor:InitActionBarTextureQueue()
    DataToColor:InitActionBarMacroQueue()
end

function DataToColor:InitEquipmentQueue()
    -- ammo slot till tabard
    for eqNum = 0, 19 do
        DataToColor.equipmentQueue:push(eqNum)
    end

    -- backpacks
    for i = 1, NUM_BAG_SLOTS do
        local invID = ContainerIDToInventoryID(i)
        DataToColor.equipmentQueue:push(invID)
    end
end

function DataToColor:InitInventoryQueue(containerID)
    if containerID >= 0 and containerID <= 4 then
        for i = 1, GetContainerNumSlots(containerID) do
            if DataToColor:BagSlotChanged(containerID, i) then
                DataToColor.inventoryQueue:push(containerID * 1000 + i)
            end
        end
    end
end

function DataToColor:BagSlotChanged(container, slot)
    local _, count, _, _, _, _, link = GetContainerItemInfo(container, slot)
    local id = link and tonumber(link:match("item:(%d+)")) or 0

    if not id then
        count = 0
        id = 0
    end

    local index = container * 1000 + slot
    local cache = bagCache[index]
    if cache then
        if cache.id ~= id or cache.count ~= count then
            cache.id = id
            cache.count = count
            return true
        end
    else
        bagCache[index] = { id = id, count = count }
        return true
    end
    return false
end

function DataToColor:InitBagQueue(min, max)
    min = min or 0
    max = max or 4
    for bag = min, max do
        DataToColor.bagQueue:push(bag)
    end
end

function DataToColor:InitActionBarCostQueue()
    for slot = 1, DataToColor.C.MAX_ACTIONBAR_SLOT do
        if HasAction(slot) then
            DataToColor:populateActionbarCost(slot)
        end
    end
end

function DataToColor:PopulateSpellBookInfo()
    local numLoaded = 0
    local bookType = "spell"

    -- prepare destination tables
    local S = DataToColor.S
    S.playerSpellBookId, S.playerSpellBookName,
    S.playerSpellBookIconToId, S.playerSpellBookIdHighest = {}, {}, {}, {}

    --------------------------------------------------------------------
    -- Helper to extract base spell name without rank suffix
    -- "Frostbolt (Rank 3)" -> "Frostbolt"
    --------------------------------------------------------------------
    local function GetBaseSpellName(name)
        return name:match("^(.-)%s*%(") or name
    end

    --------------------------------------------------------------------
    -- Helper to record one spell safely
    -- Groups by base spell name (not texture) to handle spells that
    -- share icons but are different abilities (e.g., Overpower vs Sword Specialization)
    --------------------------------------------------------------------
    local function RecordSpell(id, name, texture)
        if not (id and name and texture) then return end
        -- prefer number over file path name
        texture = DataToColor:NormalizeTexture(texture)
        S.playerSpellBookId[id] = true
        S.playerSpellBookName[texture] = name
        S.playerSpellBookIconToId[texture] = id
        -- Group by base spell name, not texture
        -- This ensures different spells with the same icon are all sent
        local baseName = GetBaseSpellName(name)
        local highest = S.playerSpellBookIdHighest[baseName]
        if not highest or id > highest then
            S.playerSpellBookIdHighest[baseName] = id
        end
        numLoaded = numLoaded + 1
    end

    --------------------------------------------------------------------
    -- Classic-era clients: no GetNumSpellTabs
    --------------------------------------------------------------------
    if not GetNumSpellTabs then
        local i = 1
        while true do
            local name, rank, id

            if GetSpellBookItemName then
                name, rank, id = GetSpellBookItemName(i, bookType)
            elseif GetSpellName then
                name, rank = GetSpellName(i, bookType)
            end

            if not name then break end
            id = id or (GetSpellID and GetSpellID(i, bookType))
            local texture = GetSpellBookItemTexture and GetSpellBookItemTexture(i, bookType)
            RecordSpell(id, name, texture)
            i = i + 1
        end

    --------------------------------------------------------------------
    -- Cataclysm and later: tab-based spellbook
    --------------------------------------------------------------------
    else
        for tab = 1, GetNumSpellTabs() do
            local offset, numSlots = select(3, GetSpellTabInfo(tab))
            for i = offset + 1, offset + numSlots do
                local slotType, id = GetSpellBookItemInfo(i, bookType)
                if slotType == "SPELL" and id and (not IsSpellKnown or IsSpellKnown(id)) then
                    local name = GetSpellBookItemName(i, bookType)
                    local texture = GetSpellBookItemTexture(i, bookType)
                    RecordSpell(id, name, texture)
                end
            end
        end
    end

    --DataToColor:Print(("Loaded %d spells"):format(numLoaded))

    DataToColor:PopulateSpellInRangeNames()
end

function DataToColor:InitSpellBookQueue()
    for _, id in pairs(DataToColor.S.playerSpellBookIdHighest) do
        DataToColor.spellBookQueue:push(id)
    end
end

function DataToColor:InitTalentQueue()
    for tab = 1, GetNumTalentTabs(false, false) do
        for i = 1, GetNumTalents(tab) do
            local _, _, tier, column, currentRank = GetTalentInfo(tab, i)
            if currentRank > 0 then
                --                     1-3 +         1-11 +          1-4 +         1-5
                local hash = tab * 1000000 + tier * 10000 + column * 10 + currentRank
                DataToColor.talentQueue:push(hash)
                --DataToColor:Print("talentQueue tab: ", tab, " | tier: ", tier, " | column: ", column, " | rank: ", currentRank, " | hash: ", hash)
            end
        end
    end
end

function DataToColor:InitTrigger(t)
    for i = 0, 23 do
        t[i] = 0
    end
end

-- Function to mass generate all of the initial frames for the pixel reader
function DataToColor:CreateFrames()
    local valueCache = {}
    local frames = {}
    local updateCount = {}

    -- This function is able to pass numbers in range 0 to 16777215
    -- r,g,b are integers in range 0-255
    -- then we turn them into 0-1 range
    local function int(self, i)
        return band(rshift(i, 16), 255) / 255, band(rshift(i, 8), 255) / 255, band(i, 255) / 255, 1
    end

    -- This function is able to pass numbers in range 0 to 9.99999 (6 digits)
    -- converting them to a 6-digit integer.
    local function float(self, f)
        return int(self, floor(f * 100000))
    end

    local function Pixel(func, value, slot)
        if valueCache[slot] ~= value then
            valueCache[slot] = value
            local frame = frames[slot]
            frame:SetBackdropColor(func(self, value))

            updateCount[slot] = updateCount[slot] + 1
            return true
        end
        return false
    end

    local function UpdateGlobalTime()
        Pixel(int, DataToColor.globalTime, GLOBAL_TIME_CELL)
    end

    local function IdxToRadix(input)
        if input == 1 then
            return 10000
        elseif input == 2 then
            return 100
        elseif input == 3 then
            return 1
        end
        return 0
    end

    -- Precomputed static value for cell 46 (race/class/version never change)
    local raceClassVersionCell = DataToColor.C.CHARACTER_RACE_ID * 10000
        + DataToColor.C.CHARACTER_CLASS_ID * 100
        + DataToColor.ClientVersion

    -- MiniMap settings cache (cells 16-17), recomputed every ~200 ticks
    local miniMapCache1 = 0
    local miniMapCache2 = 0
    local miniMapCacheTick = -999

    -- areSpellsInRange throttle (cell 40)
    local spellsInRangeCache = 0
    local spellsInRangeTick = -999

    -- UnitsTargetAsNumber throttle (cell 59)
    local unitsTargetCache = 0
    local unitsTargetTick = -999

    local function updateFrames()
        if not SETUP_SEQUENCE and globalTick >= initPhase then
            -- Ensure globalTime is past the C# FullReset threshold (Value <= 3)
            -- so queue data is processed immediately when rendering starts.
            -- Without this, the first queue items only get ~1 frame of C# visibility.
            if DataToColor.globalTime < initPhase then
                DataToColor.globalTime = initPhase
            end

            Pixel(int, 0, 0)
            -- The final data square, reserved for additional metadata.
            Pixel(int, 2000001, NUMBER_OF_FRAMES - 1)

            local x, y = DataToColor:GetPosition()
            Pixel(float, x * 10, 1)
            Pixel(float, y * 10, 2)

            Pixel(float, GetPlayerFacing() or 0, 3)
            Pixel(int, DataToColor.map or 0, 4) -- MapUIId
            local playerLevel = UnitLevel(DataToColor.C.unitPlayer)
            Pixel(int, playerLevel, 5)

            local cx, cy = DataToColor:GetCorpsePosition()
            Pixel(float, cx * 10, 6)
            Pixel(float, cy * 10, 7)

            -- Boolean variables
            -- Use event-driven cached versions (reduces API calls from ~46 to ~5 per frame)
            Pixel(int, DataToColor:Bits1Cached(), 8)
            Pixel(int, DataToColor:Bits2Cached(), 9)

            Pixel(int, UnitHealthMax(DataToColor.C.unitPlayer), 10)
            Pixel(int, UnitHealth(DataToColor.C.unitPlayer), 11)

            Pixel(int, UnitPowerMax(DataToColor.C.unitPlayer, nil), 12) -- either mana, rage, energy
            Pixel(int, UnitPower(DataToColor.C.unitPlayer, nil), 13) -- either mana, rage, energy

            if DataToColor.C.CHARACTER_CLASS_ID == 6 then -- death Knight
                local bloodRunes = 0
                local unholyRunes = 0
                local frostRunes = 0
                local deathRunes = 0
                local numRunes = 0

                for index = 1, 6 do
                    local startTime = GetRuneCooldown(index)
                    if startTime == 0 then
                        numRunes = numRunes + 1
                        local runeType = GetRuneType(index)
                        if runeType == 1 then
                            bloodRunes = bloodRunes + 1
                        elseif runeType == 2 then
                            frostRunes = frostRunes + 1
                        elseif runeType == 3 then
                            unholyRunes = unholyRunes + 1
                        elseif runeType == 4 then
                            deathRunes = deathRunes + 1
                        end
                    end
                end

                bloodRunes  = bloodRunes + deathRunes
                unholyRunes = unholyRunes + deathRunes
                frostRunes  = frostRunes + deathRunes

                Pixel(int, numRunes, 14)
                Pixel(int, bloodRunes * 100 + frostRunes * 10 + unholyRunes, 15)
            else
                Pixel(int, UnitPowerMax(DataToColor.C.unitPlayer, PowerType.Mana), 14)
                Pixel(int, UnitPower(DataToColor.C.unitPlayer, PowerType.Mana), 15)
            end

            if globalTick - miniMapCacheTick >= 200 then
                miniMapCache1 = DataToColor:MiniMapSettings1()
                miniMapCache2 = DataToColor:MiniMapSettings2()
                miniMapCacheTick = globalTick
            end
            Pixel(int, miniMapCache1, 16)
            Pixel(int, miniMapCache2, 17)

            if DataToColor.targetChanged then
                DataToColor.targetBuffTime:forcedReset()
            end

            Pixel(int, UnitHealthMax(DataToColor.C.unitTarget), 18)
            Pixel(int, UnitHealth(DataToColor.C.unitTarget), 19)

            -- 20
            local bagNum = DataToColor.bagQueue:shift(globalTick)
            if bagNum then
                local freeSlots, bagType = GetContainerNumFreeSlots(bagNum)
                -- BagType + Index + FreeSpace + BagSlots
                if Pixel(int, (bagType or 0) * 1000000 + bagNum * 100000 + freeSlots * 1000 + GetContainerNumSlots(bagNum), 20) then
                    --DataToColor:Print("bagQueue bagType:", bagType or 0, " | bagNum: ", bagNum, " | freeSlots: ", freeSlots, " | BagSlots: ", GetContainerNumSlots(bagNum), " | tick: ", globalTick)
                end
            else
                Pixel(int, 0, 20)
            end

            -- 21 22
            local bagSlotNum = DataToColor.inventoryQueue:shift(globalTick)
            if bagSlotNum then
                bagNum = floor(bagSlotNum / 1000)
                bagSlotNum = bagSlotNum - (bagNum * 1000)

                local texture, itemCount, locked, quality, readable, lootable, link = GetContainerItemInfo(bagNum, bagSlotNum)
                local itemID = link and tonumber(link:match("item:(%d+)")) or 0

                -- 0-4 bagNum + 1-21 itenNum + 1-1000 quantity
                if Pixel(int, bagNum * 1000000 + bagSlotNum * 10000 + (itemCount or 0), 21) then
                    --DataToColor:Print("inventoryQueue: ", bagNum, " ", bagSlotNum, " -> id: ", itemID or 0, " c:", itemCount or 0)
                end

                -- flags * 1000000 + itemId (max itemId ~270000, flags 0-15)
                local flags = GetItemFlags(bagNum, bagSlotNum, link)
                Pixel(int, flags * 1000000 + (itemID or 0), 22)
            else
                Pixel(int, 0, 21)
                Pixel(int, 0, 22)
            end

            -- 23 24
            local equipmentSlot = DataToColor.equipmentQueue:shift(globalTick) or 0

            -- TODO map new slot to old
            -- should be calculated
            local slot = equipmentSlot
            if slot >= 30 then
                slot = slot - 11
            end
            Pixel(int, slot, 23)
            local itemId = DataToColor:equipSlotItemId(equipmentSlot)
            Pixel(int, itemId, 24)
            --DataToColor:Print("equipmentQueue ", equipmentSlot, " slot -> ", slot, " -> ", itemId)

            Pixel(int, DataToColor:isCurrentActionCached(1), 25)
            Pixel(int, DataToColor:isCurrentActionCached(2), 26)
            Pixel(int, DataToColor:isCurrentActionCached(3), 27)
            Pixel(int, DataToColor:isCurrentActionCached(4), 28)
            Pixel(int, DataToColor:isCurrentActionCached(5), 29)

            -- Safety: periodic forced invalidation every ~1 second
            if globalTick % 60 == 0 then
                DataToColor:InvalidateActionUseableCache()
            end
            Pixel(int, DataToColor:isActionUseableCached(1), 30)
            Pixel(int, DataToColor:isActionUseableCached(2), 31)
            Pixel(int, DataToColor:isActionUseableCached(3), 32)
            Pixel(int, DataToColor:isActionUseableCached(4), 33)
            Pixel(int, DataToColor:isActionUseableCached(5), 34)

            local costMeta, costValue = DataToColor.actionBarCostQueue:getTimed(globalTick)
            if costMeta and costValue then
                if DataToColor.actionBarCostQueue:removeWhenExpired(costMeta, globalTick) then
                    --DataToColor:Print("actionBarCostQueue: ", costMeta, " ", costValue)
                end
            end
            Pixel(int, costMeta or 0, 35)
            Pixel(int, costValue or 0, 36)

            local actionSlot, expireTime = DataToColor.actionBarCooldownQueue:getTimed(globalTick)
            if actionSlot then
                DataToColor.actionBarCooldownQueue:setDirtyAfterTime(actionSlot, globalTick)

                local duration = max(0, floor((expireTime - GetTime()) * 10))
                --if duration > 0 then
                --    DataToColor:Print("actionBarCooldownQueue: ", actionSlot, " ", duration, " ", expireTime - GetTime())
                --end
                Pixel(int, actionSlot * 100000 + duration, 37)

                if duration == 0 then
                    DataToColor.actionBarCooldownQueue:removeWhenExpired(actionSlot, globalTick)
                    --DataToColor:Print("actionBarCooldownQueue: ", actionSlot, " expired")
                end
            else
                Pixel(int, 0, 37)
            end

            Pixel(int, UnitHealthMax(DataToColor.C.unitPet), 38)
            Pixel(int, UnitHealth(DataToColor.C.unitPet), 39)

            if globalTick - spellsInRangeTick >= 5 then
                spellsInRangeCache = DataToColor:areSpellsInRange()
                spellsInRangeTick = globalTick
            end
            Pixel(int, spellsInRangeCache, 40)
            Pixel(int, DataToColor:getAuraMaskForClass(UnitBuff, DataToColor.C.unitPlayer, DataToColor.S.playerBuffs), 41)
            Pixel(int, DataToColor:getAuraMaskForClass(UnitDebuff, DataToColor.C.unitTarget, DataToColor.S.targetDebuffs), 42)

            -- Amount of money in coppers
            Pixel(int, GetMoney() % 1000000, 44) -- Represents amount of money held (in copper)
            Pixel(int, floor(GetMoney() / 1000000), 45) -- Represents amount of money held (in gold) 

            Pixel(int, raceClassVersionCell, 46)
            Pixel(int, DataToColor.uiErrorMessageTime, 47)
            Pixel(int, DataToColor:shapeshiftForm(), 48) -- Shapeshift id https://wowwiki.fandom.com/wiki/API_GetShapeshiftForm
            Pixel(int, DataToColor:getRange(), 49) -- Represents minRange-maxRange ex. 0-5 5-15

            Pixel(int, UnitXP(DataToColor.C.unitPlayer), 50)
            Pixel(int, UnitXPMax(DataToColor.C.unitPlayer), 51)
            Pixel(int, DataToColor.uiErrorMessage, 52) -- Last UI Error message
            DataToColor.uiErrorMessage = 0

            Pixel(int, DataToColor:CastingInfoSpellId(DataToColor.C.unitPlayer), 53)                                                                                                                                                                               -- SpellId being cast
            Pixel(int, DataToColor:getAvgEquipmentDurabilityCached() * 100 + ((DataToColor.C.CHARACTER_CLASS_ID == 2 and UnitPower(DataToColor.C.unitPlayer, PowerType.HolyPower) or GetComboPoints(DataToColor.C.unitPlayer, DataToColor.C.unitTarget)) or 0), 54)                                                                                                                                                                                                                                                -- for paladin holy power or combo points

            local playerBuffCount = DataToColor:populateAuraTimer(UnitBuff, DataToColor.C.unitPlayer, DataToColor.playerBuffTime)
            local playerDebuffCount = DataToColor:populateAuraTimer(UnitDebuff, DataToColor.C.unitPlayer, DataToColor.playerDebuffTime)
            local targetDebuffCount = DataToColor:populateAuraTimer(UnitDebuff, DataToColor.C.unitTarget, DataToColor.targetDebuffTime)
            local targetBuffCount = DataToColor:populateAuraTimer(UnitBuff, DataToColor.C.unitTarget, DataToColor.targetBuffTime)
            local focusBuffCount = DataToColor:populateAuraTimer(UnitBuff, DataToColor.C.unitFocus, DataToColor.focusBuffTime)

            -- player/target buff and debuff counts
            -- playerdebuff count cannot be higher than 16
            -- formula playerDebuffCount + playerBuffCount + targetDebuffCount + targetBuffCount
            Pixel(int, min(16, playerDebuffCount) * 1000000 + playerBuffCount * 10000 + targetDebuffCount * 100 + targetBuffCount, 55)

            if DataToColor.targetChanged then
                local targetLevel = UnitLevelSafe(DataToColor.C.unitTarget, playerLevel)
                Pixel(int, targetLevel * 100 + DataToColor.C.unitClassification[UnitClassification(DataToColor.C.unitTarget)], 43)
                Pixel(int, DataToColor:NpcId(DataToColor.C.unitTarget), 56) -- target id
                Pixel(int, DataToColor:getGuidFromUnit(DataToColor.C.unitTarget), 57)
            end

            Pixel(int, DataToColor:CastingInfoSpellId(DataToColor.C.unitTarget), 58) -- SpellId being cast by target

            if globalTick - unitsTargetTick >= 5 then
                unitsTargetCache =
                    10 * DataToColor:UnitsTargetAsNumber(DataToColor.C.unitmouseover, DataToColor.C.unitmouseovertarget) +
                    DataToColor:UnitsTargetAsNumber(DataToColor.C.unitTarget, DataToColor.C.unitTargetTarget)
                unitsTargetTick = globalTick
            end
            Pixel(int, unitsTargetCache, 59)

            Pixel(int, DataToColor.lastAutoShot, 60)
            Pixel(int, DataToColor.lastMainHandMeleeSwing, 61)
            Pixel(int, DataToColor.lastCastEvent, 62)
            Pixel(int, DataToColor.lastCastSpellId, 63)

            Pixel(int, DataToColor.CombatCreatureDiedQueue:shift(globalTick) or 0, 66)
            Pixel(int, DataToColor.CombatDamageDoneQueue:shift(globalTick) or 0, 64)
            Pixel(int, DataToColor.CombatDamageTakenQueue:shift(globalTick) or 0, 65)
            Pixel(int, DataToColor.CombatMissTypeQueue:shift(globalTick) or 0, 67)

            Pixel(int, DataToColor:getGuidFromUnit(DataToColor.C.unitPet), 68)
            Pixel(int, DataToColor:getGuidFromUnit(DataToColor.C.unitPetTarget), 69)
            Pixel(int, DataToColor.CastNum, 70)

            Pixel(int, DataToColor.spellBookQueue:shift(globalTick) or 0, 71)

            Pixel(int, DataToColor.talentQueue:shift(globalTick) or 0, 72)

            local gossipNum = DataToColor.gossipQueue:shift(globalTick)
            if gossipNum then
                --DataToColor:Print("gossipQueue: ", gossipNum)
                Pixel(int, gossipNum, 73)
            end

            Pixel(int, DataToColor:CustomTrigger(DataToColor.customTrigger1), 74)
            Pixel(int, DataToColor:getMeleeAttackSpeed(DataToColor.C.unitPlayer), 75)

            -- 76 rem cast time
            local remainCastTime = floor(DataToColor.lastCastEndTime - GetTime() * 1000)
            Pixel(int, max(0, remainCastTime), 76)

            if UnitExists(DataToColor.C.unitFocus) then
                Pixel(int, DataToColor:getGuidFromUnit(DataToColor.C.unitFocus), 77)
                Pixel(int, DataToColor:getGuidFromUnit(DataToColor.C.unitFocusTarget), 78)
            end

            local textureId, expireTime = DataToColor.playerBuffTime:getTimed(globalTick)
            if textureId then
                DataToColor.playerBuffTime:setDirtyAfterTime(textureId, globalTick)

                local durationSec = max(0, ceil(expireTime - GetTime()))
                --DataToColor:Print("player buff update  ", textureId, " ", durationSec)
                Pixel(int, textureId, 79)
                Pixel(int, durationSec, 80)

                if durationSec == 0 then
                    DataToColor.playerBuffTime:removeWhenExpired(textureId, globalTick)
                    --DataToColor:Print("player buff expired ", textureId, " ", durationSec)
                end
            else
                Pixel(int, 0, 79)
                Pixel(int, 0, 80)
            end

            if UnitExists(DataToColor.C.unitTarget) then
                textureId, expireTime = DataToColor.targetDebuffTime:getTimed(globalTick)
            else
                textureId, expireTime = DataToColor.targetDebuffTime:getForced(globalTick)
                expireTime = GetTime()
            end

            if textureId then
                DataToColor.targetDebuffTime:setDirtyAfterTime(textureId, globalTick)

                local durationSec = max(0, ceil(expireTime - GetTime()))
                --DataToColor:Print("target debuff update ", textureId, " ", durationSec)
                Pixel(int, textureId, 81)
                Pixel(int, durationSec, 82)

                if durationSec == 0 then
                    DataToColor.targetDebuffTime:removeWhenExpired(textureId, globalTick)
                    --DataToColor:Print("target debuff expired ", textureId, " ", durationSec)
                end
            else
                Pixel(int, 0, 81)
                Pixel(int, 0, 82)
            end

            if UnitExists(DataToColor.C.unitTarget) then
                textureId, expireTime = DataToColor.targetBuffTime:getTimed(globalTick)
            else
                textureId, expireTime = DataToColor.targetBuffTime:getForced(globalTick)
                expireTime = GetTime()
            end

            if textureId then
                DataToColor.targetBuffTime:setDirtyAfterTime(textureId, globalTick)

                local durationSec = max(0, ceil(expireTime - GetTime()))
                --DataToColor:Print("target buff update ", textureId, " ", durationSec)
                Pixel(int, textureId, 83)
                Pixel(int, durationSec, 84)

                if durationSec == 0 then
                    DataToColor.targetBuffTime:removeWhenExpired(textureId, globalTick)
                    --DataToColor:Print("target buff expired ", textureId, " ", durationSec)
                end
            else
                Pixel(int, 0, 83)
                Pixel(int, 0, 84)
            end

            if UnitExists(DataToColor.C.unitFocus) then
                textureId, expireTime = DataToColor.focusBuffTime:getTimed(globalTick)
            else
                textureId, expireTime = DataToColor.focusBuffTime:getForced(globalTick)
                expireTime = GetTime()
            end

            if textureId then
                DataToColor.focusBuffTime:setDirtyAfterTime(textureId, globalTick)

                local durationSec = max(0, ceil(expireTime - GetTime()))
                --DataToColor:Print("focus buff update ", textureId, " ", durationSec)
                Pixel(int, textureId, 92)
                Pixel(int, durationSec, 93)

                if durationSec == 0 then
                    DataToColor.focusBuffTime:removeWhenExpired(textureId, globalTick)
                    --DataToColor:Print("focus buff expired ", textureId, " ", durationSec)
                end
            else
                Pixel(int, 0, 92)
                Pixel(int, 0, 93)
            end

            local mouseoverLevel = UnitLevelSafe(DataToColor.C.unitmouseover, playerLevel)
            Pixel(int, mouseoverLevel * 100 + DataToColor.C.unitClassification[UnitClassification(DataToColor.C.unitmouseover)], 85)

            Pixel(int, DataToColor:NpcId(DataToColor.C.unitmouseover), 86)
            Pixel(int, DataToColor:getGuidFromUnit(DataToColor.C.unitmouseover), 87)

            Pixel(int, DataToColor:getUnitRangedDamage(DataToColor.C.unitPlayer), 88)

            Pixel(int, UnitHealthMax(DataToColor.C.unitFocus), 89)
            Pixel(int, UnitHealth(DataToColor.C.unitFocus), 90)
            Pixel(int, DataToColor:getAuraMaskForClass(UnitBuff, DataToColor.C.unitFocus, DataToColor.S.playerBuffs), 91)

            -- 94 last cast GCD
            Pixel(int, DataToColor.lastCastGCD, 94)

            -- 95 gcd
            local gcd = floor((DataToColor.gcdExpirationTime - GetTime()) * 1000)
            Pixel(int, max(0, gcd), 95)

            if globalTick % LATENCY_ITERATION_FRAME_CHANGE_RATE == 0 then
                local _, _, lagHome, lagWorld = GetNetStats()

                -- artificially increase lagWorld to avoid skipping timers
                lagWorld = max(lagWorld, 10)

                local lag = min(max(lagHome, lagWorld), 9999)
                local SpellQueueWindow = min(tonumber(DataToColor.SafeGetCVar(DataToColor.C.SpellQueueWindow, "0")) or 0, 999)

                Pixel(int, 10000 * SpellQueueWindow + lag, 96)
            end

            -- Timers
            if DataToColor.lastLoot == DataToColor.C.Loot.Closed and
                DataToColor.globalTime - DataToColor.lastLootResetStart >= LOOT_RESET_RATE then
                DataToColor.lastLoot = DataToColor.C.Loot.Corpse
            end
            local lootItemCount = GetNumLootItems()
            Pixel(int, lootItemCount * 10 + DataToColor.lastLoot, 97)

            -- TextQueue output (UTF-8 text transfer for totem names, chat, etc.)
            local textEntry = DataToColor.TextQueue:peek()
            if not textEntry then
                Pixel(int, 0, 98)
                Pixel(int, 0, 99)
                textQueueHead = 0
                textQueueCurrentEntry = nil
            else
                if textQueueCurrentEntry ~= textEntry then
                    textQueueCurrentEntry = textEntry
                    textQueueHead = 1
                end

                -- Pack 3 UTF-8 bytes into data cell (no allocation)
                local data = DataToColor:PackUTF8Bytes(textEntry.text, textQueueHead)

                -- Pack metadata: cmd(4 bits) | length(10 bits) | offset(10 bits)
                -- meta = cmd * 2^20 + len * 2^10 + offset
                local meta = textEntry.cmd * 1048576 + textEntry.len * 1024 + (textQueueHead - 1)

                Pixel(int, data, 98)
                Pixel(int, meta, 99)

                textQueueHead = textQueueHead + 3
                if textQueueHead > textEntry.len then
                    -- Release entry back to pool before shifting
                    local completed = DataToColor.TextQueue:shift(globalTick)
                    if completed then
                        releaseTextEntry(completed)
                    end
                    textQueueHead = 0
                    textQueueCurrentEntry = nil
                end
            end

            Pixel(int, DataToColor:Bits3Cached(), 100)

            Pixel(int, DataToColor:getGuidFromUUID(DataToColor.softInteractGuid), 101)
            Pixel(int, DataToColor:getNpcIdFromUUID(DataToColor.softInteractGuid), 102)
            Pixel(int, DataToColor:getTypeFromUUID(DataToColor.softInteractGuid), 103)

            -- player debuff
            textureId, expireTime = DataToColor.playerDebuffTime:getTimed(globalTick)
            if textureId then
                DataToColor.playerDebuffTime:setDirtyAfterTime(textureId, globalTick)

                local durationSec = max(0, ceil(expireTime - GetTime()))
                --DataToColor:Print("player debuff update  ", textureId, " ", durationSec)
                Pixel(int, textureId, 104)
                Pixel(int, durationSec, 105)

                if durationSec == 0 then
                    DataToColor.playerDebuffTime:removeWhenExpired(textureId, globalTick)
                    --DataToColor:Print("player debuff expired ", textureId, " ", durationSec)
                end
            else
                Pixel(int, 0, 104)
                Pixel(int, 0, 105)
            end


            -- Key bindings queue (slot 106)
            Pixel(int, DataToColor.bindingQueue:shift(globalTick) or 0, 106)

            -- Action bar texture queue (slot 107)
            Pixel(int, DataToColor.actionBarTextureQueue:shift(globalTick) or 0, 107)

            -- Action bar macro queue (slot 108)
            Pixel(int, DataToColor.actionBarMacroQueue:shift(globalTick) or 0, 108)

            Pixel(int, DataToColor.lastDamageDoneTime, 109)

            -- Enemy summons (totems, pets summoned by hostile NPCs)
            Pixel(int, DataToColor.EnemySummonQueue:shift(globalTick) or 0, 110)

            local _, playerRunSpeed = GetUnitSpeed(DataToColor.C.unitPlayer)
            Pixel(float, playerRunSpeed or 0, 111)

            UpdateGlobalTime()
            -- NUMBER_OF_FRAMES - 1 reserved for validation

            DataToColor:ConsumeChanges()

            DataToColor:HandlePlayerInteractionEvents()

            DataToColor:Update()
        elseif not SETUP_SEQUENCE then
            if globalTick < initPhase then
                for i = 1, NUMBER_OF_FRAMES - 1 do
                    Pixel(int, 0, i)
                    updateCount[i] = 0
                end
            end
            UpdateGlobalTime()
        end

        if SETUP_SEQUENCE then
            -- Emits meta data in data square index 0 concerning our estimated cell size, number of rows, and the numbers of frames
            Pixel(int, CELL_SPACING * 10000000 + CELL_SIZE * 100000 + 1000 * FRAME_ROWS + NUMBER_OF_FRAMES, 0)
            -- Assign pixel squares a value equivalent to their respective indices.
            for i = 1, NUMBER_OF_FRAMES - 1 do
                Pixel(int, i, i)
                updateCount[i] = 0
            end
        end

        globalTick = globalTick + 1
    end

    local function genFrame(name, x, y)
        local f = CreateFrame("Frame", name, UIParent, BackdropTemplateMixin and "BackdropTemplate") or CreateFrame("Frame", name, UIParent)

        local xx = x * floor(CELL_SIZE + CELL_SPACING)
        local yy = floor(-y * (CELL_SIZE + CELL_SPACING))
        --DataToColor:Print(name, " ", xx, " ", yy)

        f:SetPoint("TOPLEFT", xx, yy)
        f:SetHeight(CELL_SIZE)
        f:SetWidth(CELL_SIZE)
        f:SetBackdrop({
            bgFile = "Interface\\AddOns\\DataToColor\\white.tga",
            insets = { top = 0, left = 0, bottom = 0, right = 0 },
        })
        f:SetFrameStrata("TOOLTIP")
        f:SetBackdropColor(0, 0, 0, 1)
        return f
    end

    -- background frame
    local backgroundframe = genFrame("frame_bg", 0, 0)
    backgroundframe:SetHeight(FRAME_ROWS * (CELL_SIZE + CELL_SPACING))
    backgroundframe:SetWidth(ceil(NUMBER_OF_FRAMES / FRAME_ROWS) * (CELL_SIZE + CELL_SPACING))
    backgroundframe:SetFrameStrata("FULLSCREEN_DIALOG")
    backgroundframe:SetBackdropColor(0, 0, 0, 1)

    for frame = 0, NUMBER_OF_FRAMES - 1 do
        -- those are grid coordinates (1,2,3,4 by  1,2,3,4 etc), not pixel coordinates
        local y = frame % FRAME_ROWS
        local x = floor(frame / FRAME_ROWS)
        frames[frame] = genFrame("frame_" .. tostring(frame), x, y)
        valueCache[frame] = -1
        updateCount[frame] = 0
    end

    backgroundframe:SetScript("OnUpdate", updateFrames)

    local function DumpCallCount(maxRow)
        print("Frame        count  val --- globalTick: " .. globalTick)

        local tbl = {}
        local function byUpdateCountDesc(a, b)
            return a[2] > b[2]
        end

        for k, v in pairs(updateCount) do
            table.insert(tbl, { k, v })
        end

        table.sort(tbl, byUpdateCountDesc)

        maxRow = tonumber(maxRow) or 5

        local c = 0
        for k, v in ipairs(tbl) do
            if c >= maxRow then break end
            print(string.format("%03d  %010d  %s", v[1], v[2], valueCache[v[1]]))
            c = c + 1
        end
    end

    --C_Timer.After(10, DumpCallCount)

    DataToColor:RegisterChatCommand('dcdump', DumpCallCount)
end

function DataToColor:delete(items)
    for b = 0, 4 do
        for s = 1, GetContainerNumSlots(b) do
            local n = GetContainerItemLink(b, s)
            if n then
                for i = 1, #items, 1 do
                    if strfind(n, items[i]) then
                        DataToColor:Print("Delete: ", items[i])
                        PickupContainerItem(b, s)
                        DeleteCursorItem()
                    end
                end
            end
        end
    end
end

function DataToColor:sell(items)
    if not UnitExists(DataToColor.C.unitTarget) then
        DataToColor:Print("Merchant is not targetted.")
        return
    end

    local item = GetMerchantItemLink(1)
    if not item then
        DataToColor:Print("Merchant is not open to sell to, please approach and open.")
        return
    end

    DataToColor:Print("Selling items...")
    DataToColor:OnMerchantShow()
    local TotalPrice = 0

    for b = 0, 4 do
        for s = 1, GetContainerNumSlots(b) do
            local CurrentItemLink = GetContainerItemLink(b, s)
            if CurrentItemLink then
                for i = 1, #items, 1 do
                    if strfind(CurrentItemLink, items[i]) then
                        local _, _, itemRarity, _, _, _, _, _, _, _, itemSellPrice = GetItemInfo(CurrentItemLink)
                        if (itemRarity < 2) then
                            local _, itemCount = GetContainerItemInfo(b, s)
                            TotalPrice = TotalPrice + (itemSellPrice * itemCount)
                            DataToColor:Print("Selling: ", itemCount, " ", CurrentItemLink,
                                " for ", GetCoinTextureString(itemSellPrice * itemCount))
                            UseContainerItem(b, s)
                        else
                            DataToColor:Print("Item is not gray or common, not selling it: ", items[i])
                        end
                    end
                end
            end
        end
    end

    if TotalPrice ~= 0 then
        DataToColor:Print("Total Price for all items: ", GetCoinTextureString(TotalPrice))
    else
        DataToColor:Print("No grey items were sold.")
    end
end

-- Place Spell on action bar by searching spellbook for name prefix
-- Usage: /run DataToColor:PS("Immolate",1)
-- Handles ranked spells like "Immolate(Rank 9)" by matching prefix
function DataToColor:PS(name, slot)
    local bookType = "spell"
    local best = nil
    for i = 1, 500 do
        local n = GetSpellBookItemName(i, bookType)
        if not n then break end
        if n == name or n:find("^" .. name .. "[%s%(]") then
            best = i
        elseif best then
            break
        end
    end
    if best then
        PickupSpellBookItem(best, bookType)
        PlaceAction(slot)
        ClearCursor()
        return true
    end
    return false
end
