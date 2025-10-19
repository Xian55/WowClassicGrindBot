local Load = select(2, ...)
local DataToColor = unpack(Load)

local band = bit.band
local floor = math.floor

local UIErrorsFrame = UIErrorsFrame
local CombatLogGetCurrentEventInfo = CombatLogGetCurrentEventInfo
local GetSpellInfo = GetSpellInfo
local GetSpellBaseCooldown = GetSpellBaseCooldown
local GetTime = GetTime
local GetGossipOptions = DataToColor.GetGossipOptions
local HasAction = HasAction
local CanMerchantRepair = CanMerchantRepair
local GetRepairAllCost = GetRepairAllCost
local GetMoney = GetMoney
local RepairAllItems = RepairAllItems
local UnitRangedDamage = UnitRangedDamage

local DeclineGroup = DeclineGroup
local AcceptGroup = AcceptGroup
local StaticPopup_Hide = StaticPopup_Hide

local UnitGUID = UnitGUID
local UnitIsDeadOrGhost = UnitIsDeadOrGhost
local UnitIsGhost = UnitIsGhost
local C_Map = C_Map
local DEFAULT_CHAT_FRAME = DEFAULT_CHAT_FRAME
local RepopMe = RepopMe
local RetrieveCorpse = RetrieveCorpse
local GetCorpseRecoveryDelay = GetCorpseRecoveryDelay

local UnitIsTapDenied = UnitIsTapDenied

local ContainerIDToInventoryID = DataToColor.ContainerIDToInventoryID
local NUM_BAG_SLOTS = NUM_BAG_SLOTS

local CAST_SENT = 999997
local CAST_START = 999998
local CAST_SUCCESS = 999999

local MERCHANT_SHOW_V = 9999999
local MERCHANT_CLOSED_V = 9999998

DataToColor.GOSSIP_START = 69
DataToColor.GOSSIP_END = 9999994

local som_spellId = 0

local ignoreErrorList = {
    "ERR_ABILITY_COOLDOWN",
    "ERR_OUT_OF_RAGE",
    "ERR_NO_ATTACK_TARGET",
    "ERR_OUT_OF_MANA",
    "ERR_SPELL_FAILED_SHAPESHIFT_FORM_S",
    "ERR_GENERIC_NO_TARGET",
    "ERR_ATTACK_PREVENTED_BY_MECHANIC_S",
    "ERR_ATTACK_STUNNED",
    "ERR_NOEMOTEWHILERUNNING"
}
local ignoreErrorListMessages = {}


local errorList = {
    "ERR_BADATTACKFACING", --1 "You are facing the wrong way!"
    "ERR_SPELL_FAILED_S", --2 -- like a printf
    "SPELL_FAILED_OUT_OF_RANGE", --3 "Out of range"
    "ERR_BADATTACKPOS", --4 "You are too far away!"
    "ERR_AUTOFOLLOW_TOO_FAR", --5 "Target is too far away."
    "SPELL_FAILED_MOVING", --6 "Can't do that while moving"
    "ERR_SPELL_COOLDOWN", --7 "Spell is not ready yet."
    "ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS", --8 "Another action is in progress"
    "SPELL_FAILED_STUNNED", -- 9 "Can't do that while stunned"
    "SPELL_FAILED_INTERRUPTED", -- 10 "Interrupted"
    "SPELL_FAILED_ITEM_NOT_READY", -- 11 "Item is not ready yet"
    "SPELL_FAILED_TRY_AGAIN", -- 12 "Failed attempt"
    "SPELL_FAILED_NOT_READY", -- 13 "Not yet recovered"
    "SPELL_FAILED_TARGETS_DEAD", -- 14 "Your target is dead"
    "ERR_LOOT_LOCKED", -- 15 "Someone is already looting that corpse."
    "ERR_ATTACK_PACIFIED", -- 16 "Can't attack while pacified."
    -- "ERR_USE_LOCKED_WITH_SPELL_S" -- 17 "Requires %s"
    "SPELL_FAILED_LINE_OF_SIGHT" -- 18
}
local spellFailedErrors = {
    SPELL_FAILED_UNIT_NOT_INFRONT = 1,
    SPELL_FAILED_MOVING = 6,
    SPELL_FAILED_STUNNED = 9,
    ERR_SPELL_OUT_OF_RANGE = 3,
    SPELL_FAILED_LINE_OF_SIGHT = 18
}

local specialErrorS = {}

local errorListMessages = {}

function DataToColor:RegisterEvents()
    DataToColor:RegisterEvent("UI_ERROR_MESSAGE", 'OnUIErrorMessage')
    DataToColor:RegisterEvent("COMBAT_LOG_EVENT_UNFILTERED", 'UnfilteredCombatEvent')
    DataToColor:RegisterEvent("UNIT_SPELLCAST_SENT", 'OnUnitSpellCastSent')
    DataToColor:RegisterEvent("UNIT_SPELLCAST_SUCCEEDED", 'OnUnitSpellCastSucceeded')
    DataToColor:RegisterEvent("UNIT_SPELLCAST_FAILED", 'OnUnitSpellCastFailed')
    DataToColor:RegisterEvent("UNIT_SPELLCAST_CHANNEL_START", 'OnUnitSpellCastChannelStart')
    DataToColor:RegisterEvent("UNIT_SPELLCAST_CHANNEL_STOP", 'OnUnitSpellCastChannelStop')
    --DataToColor:RegisterEvent("UNIT_SPELLCAST_FAILED_QUIET", 'OnUnitSpellCastFailed')
    DataToColor:RegisterEvent('LOOT_READY', 'OnLootReady')
    DataToColor:RegisterEvent('LOOT_CLOSED', 'OnLootClosed')
    DataToColor:RegisterEvent('BAG_UPDATE', 'OnBagUpdate')
    DataToColor:RegisterEvent('BAG_CLOSED', 'OnBagUpdate')
    DataToColor:RegisterEvent('MERCHANT_SHOW', 'OnMerchantShow')
    DataToColor:RegisterEvent('MERCHANT_CLOSED', 'OnMerchantClosed')
    DataToColor:RegisterEvent('PLAYER_TARGET_CHANGED', 'OnPlayerTargetChanged')
    DataToColor:RegisterEvent('PLAYER_EQUIPMENT_CHANGED', 'OnPlayerEquipmentChanged')
    DataToColor:RegisterEvent('GOSSIP_SHOW', 'OnGossipShow') -- defined in Versions.lua
    DataToColor:RegisterEvent('SPELLS_CHANGED', 'OnSpellsChanged')
    DataToColor:RegisterEvent('ACTIONBAR_SLOT_CHANGED', 'ActionbarSlotChanged')
    DataToColor:RegisterEvent('CORPSE_IN_RANGE', 'CorpseInRangeEvent')
    DataToColor:RegisterEvent('CORPSE_OUT_OF_RANGE', 'CorpseOutOfRangeEvent')
    DataToColor:RegisterEvent('CHAT_MSG_OPENING', 'ChatMessageOpeningEvent')
    DataToColor:RegisterEvent('UNIT_PET', 'OnPetChanged')

    DataToColor:RegisterEvent('ZONE_CHANGED', 'OnZoneChanged')
    DataToColor:RegisterEvent('ZONE_CHANGED_INDOORS', 'OnZoneChanged')
    DataToColor:RegisterEvent('ZONE_CHANGED_NEW_AREA', 'OnZoneChanged')

    DataToColor:RegisterEvent('PLAYER_REGEN_ENABLED', 'OnLeftCombat')

    DataToColor:RegisterEvent('AUTOFOLLOW_BEGIN', 'AutoFollowBegin')
    DataToColor:RegisterEvent('AUTOFOLLOW_END', 'AutoFollowEnd')

    DataToColor:RegisterEvent('PLAYER_STARTED_MOVING', 'PlayerStartedMoving')
    DataToColor:RegisterEvent('PLAYER_STOPPED_MOVING', 'PlayerStoppedMoving')

    DataToColor:RegisterEvent('CHAT_MSG_WHISPER', 'OnMessageWhisper')
    DataToColor:RegisterEvent('CHAT_MSG_SAY', 'OnMessageSay')
    DataToColor:RegisterEvent('CHAT_MSG_YELL', 'OnMessageYell')
    DataToColor:RegisterEvent('CHAT_MSG_EMOTE', 'OnMessageEmote')
    DataToColor:RegisterEvent('CHAT_MSG_TEXT_EMOTE', 'OnMessageEmote')
    DataToColor:RegisterEvent('CHAT_MSG_PARTY', 'OnMessageParty')
    DataToColor:RegisterEvent('CHAT_MSG_PARTY_LEADER', 'OnMessageParty')

    -- allows to use the addon with older client version
    pcall(function()
        DataToColor:RegisterEvent("PLAYER_SOFT_INTERACT_CHANGED", "OnPlayerSoftInteractChanged")
    end)

    -- Season of mastery / vanilla
    if WOW_PROJECT_ID == WOW_PROJECT_CLASSIC then
        DataToColor:RegisterEvent('UNIT_SPELLCAST_START', 'SoM_OnCastStart')
        DataToColor:RegisterEvent('UNIT_SPELLCAST_SUCCEEDED', 'SoM_OnCastSuccess')
        DataToColor:RegisterEvent('UNIT_SPELLCAST_FAILED', 'SoM_OnCastFailed')
    end

    for i = 1, #ignoreErrorList do
        local text = _G[ignoreErrorList[i]]
        ignoreErrorListMessages[text] = i
    end

    for i = 1, #errorList do
        local text = _G[errorList[i]]
        errorListMessages[text] = i
    end

    for key, value in pairs(spellFailedErrors) do
        local text = _G[key]
        errorListMessages[text] = value
    end

    specialErrorS[strsplit('%s', ERR_USE_LOCKED_WITH_SPELL_S, 2)] = 17
end

function DataToColor:OnUIErrorMessage(_, _, message)
    if ignoreErrorListMessages[message] then
        UIErrorsFrame:AddMessage(message, 0.7, 0.7, 0.7) -- show as grey message
        return
    end

    local code = errorListMessages[message] or 0
    if code > 0 then
        DataToColor.uiErrorMessage = code
        DataToColor.uiErrorMessageTime = DataToColor.globalTime
        --UIErrorsFrame:AddMessage(code .. " " .. message, 0, 1, 0) -- show as green message
        UIErrorsFrame:AddMessage(message, 0, 1, 0) -- show as green message
        return
    else
        for i, v in pairs(specialErrorS) do
            if string.find(message, i) then
                DataToColor.uiErrorMessage = v
                DataToColor.uiErrorMessageTime = DataToColor.globalTime
                UIErrorsFrame:AddMessage(message, 0, 1, 0) -- show as green message
                return
            end
        end
    end

    UIErrorsFrame:AddMessage(message, 0, 0, 1) -- show as blue message (unknown message)
end

local watchedSpells = {
    [DataToColor.C.Spell.AutoShotId] = function()
        --DataToColor:Print("Auto Shot detected")
        DataToColor.lastAutoShot = DataToColor.globalTime
    end
}

local swing_reset_spells = {
    --[[ Maul ]]
    [132136] = true,
    --[[ Raptor Strike ]]
    [132223] = true,
    --[[ Cleave ]]
    [132338] = true,
    --[[ Heroic Strike ]]
    [132282] = true,
    --[[ Slam ]]
    [132340] = true,
    --[[ Runic Strike]]
    [237518] = true
}

local miss_type = {
    ["ABSORB"] = 1,
    ["BLOCK"] = 2,
    ["DEFLECT"] = 3,
    ["DODGE"] = 4,
    ["EVADE"] = 5,
    ["IMMUNE"] = 6,
    ["MISS"] = 7,
    ["PARRY"] = 8,
    ["REFLECT"] = 9,
    ["RESIST"] = 10
}

function DataToColor:UnfilteredCombatEvent()
    DataToColor:OnCombatEvent(CombatLogGetCurrentEventInfo())
end

local COMBATLOG_OBJECT_TYPE_NPC = COMBATLOG_OBJECT_TYPE_NPC
local COMBATLOG_OBJECT_TYPE_PLAYER_OR_PET = COMBATLOG_OBJECT_TYPE_PLAYER + COMBATLOG_OBJECT_TYPE_PET


local playerDamageTakenEvents = {
    SWING_DAMAGE = true,
    SPELL_DAMAGE = true
}

local playerSpellCastSuccess = {
    SPELL_CAST_SUCCESS = true
}

local playerSpellCastStarted = {
    SPELL_CAST_START = true
}

local playerSpellCastFinished = {
    SPELL_CAST_SUCCESS = true,
    SPELL_CAST_FAILED = true
}

local playerSpellFailed = {
    SPELL_CAST_FAILED = true
}

local playerDamageDone = {
    SWING_DAMAGE = true,
    RANGE_DAMAGE = true,
    SPELL_DAMAGE = true
}

local playerDamageMiss = {
    SWING_MISSED = true,
    RANGE_MISSED = true,
    SPELL_MISSED = true
}

local playerMeleeSwing = {
    SWING_DAMAGE = true,
    SWING_MISSED = true
}

local playerSummon = {
    SPELL_SUMMON = true
}

local unitDied = {
    UNIT_DIED = true
}

function DataToColor:OnCombatEvent(...)
    local _, subEvent, _, sourceGUID, _, sourceFlags, _, destGUID, _, destFlags, _, spellId, spellName, _ = ...
    --print(...)

    if playerDamageTakenEvents[subEvent] and
        band(destFlags, COMBATLOG_OBJECT_TYPE_PLAYER_OR_PET) and
        strlen(sourceGUID) > 0 and
        (destGUID == DataToColor.playerGUID or
        destGUID == DataToColor.petGUID or
        DataToColor.playerPetSummons[destGUID]) then
        --DataToColor:Print("Damage Taken ", sourceGUID)

        local targetGuid = UnitGUID(DataToColor.C.unitTarget)
        if targetGuid == sourceGUID and not UnitIsTapDenied(DataToColor.C.unitTarget) and DataToColor.eligibleKillCredit[sourceGUID] == nil then
            DataToColor.eligibleKillCredit[sourceGUID] = true
            --DataToColor:Print("Kill Credit added(take): ", sourceGUID)
        end

        DataToColor.CombatDamageTakenQueue:push(DataToColor:getGuidFromUUID(sourceGUID))
    end

    if sourceGUID == DataToColor.playerGUID then
        if playerSpellCastSuccess[subEvent] then

            -- Fix SoM
            if spellId == 0 or spellId == nil then
                spellId = som_spellId
            end

            if watchedSpells[spellId] then watchedSpells[spellId]() end

            local _, _, icon = GetSpellInfo(spellId)
            if swing_reset_spells[icon] then
                --DataToColor:Print("Special Melee Swing detected ", spellId)
                DataToColor.lastMainHandMeleeSwing = DataToColor.globalTime
            end
        end

        if playerSpellCastStarted[subEvent] then
            DataToColor.lastCastEvent = CAST_START
            DataToColor.uiErrorMessageTime = DataToColor.globalTime

            -- Fix SoM
            if spellId == 0 or spellId == nil then
                spellId = som_spellId
            end

            DataToColor.lastCastSpellId = spellId

            local _, gcdMS = GetSpellBaseCooldown(spellId)
            gcdMS = gcdMS or 0
            DataToColor.lastCastGCD = gcdMS
            --DataToColor:Print(subEvent, " ", spellId, " ", gcdMS)
        end

        if playerSpellCastFinished[subEvent] then

            -- Fix Som
            if spellId == 0 or spellId == nil then
                spellId = som_spellId
            end

            DataToColor.lastCastSpellId = spellId

            if playerSpellFailed[subEvent] then
                --local lastCastEvent = DataToColor.lastCastEvent
                local failedMessage = select(15, ...)
                DataToColor.lastCastEvent = errorListMessages[failedMessage] or 0
                DataToColor.uiErrorMessage = DataToColor.lastCastEvent
                DataToColor.uiErrorMessageTime = DataToColor.globalTime
                --DataToColor:Print(subEvent, " ", lastCastEvent, " -> ", DataToColor.lastCastEvent, " ", failedMessage, " ", spellId)
            else
                DataToColor.lastCastEvent = CAST_SUCCESS
                --DataToColor:Print(subEvent, " ", spellId)
                DataToColor.uiErrorMessageTime = DataToColor.globalTime

                local hasGCD = true

                local _, gcdMS = GetSpellBaseCooldown(spellId)
                if gcdMS == 0 then
                    hasGCD = false
                end

                local _, _, _, castTime = GetSpellInfo(spellId)
                castTime = castTime or 0

                if castTime > 0 then
                    hasGCD = false
                end

                if spellId == DataToColor.C.Spell.ShootId then
                    hasGCD = true
                end

                if hasGCD then
                    if spellId == DataToColor.C.Spell.ShootId then
                        castTime = floor(UnitRangedDamage(DataToColor.C.unitPlayer) * 1000)
                    else
                        castTime = gcdMS
                    end

                    DataToColor.gcdExpirationTime = GetTime() + (castTime / 1000)
                    DataToColor.lastCastGCD = castTime
                    --DataToColor:Print(subEvent, " ", spellName, " ", spellId, " ", castTime)
                else
                    --DataToColor:Print(subEvent, " ", spellName, " ", spellId, " has no GCD")
                    DataToColor.lastCastGCD = 0
                end
            end
        end

        -- matches SWING_ RANGE_ SPELL_ but not SPELL_PERIODIC
        if playerDamageDone[subEvent] or playerDamageMiss[subEvent] then
            --DataToColor:Print(subEvent, " ", destGUID)

            local targetGuid = UnitGUID(DataToColor.C.unitTarget)
            if targetGuid == destGUID and not UnitIsTapDenied(DataToColor.C.unitTarget) and DataToColor.eligibleKillCredit[destGUID] == nil then
                DataToColor.eligibleKillCredit[destGUID] = true
                --DataToColor:Print("Kill Credit added(done): ", destGUID)
            end

            DataToColor.CombatDamageDoneQueue:push(DataToColor:getGuidFromUUID(destGUID))

            if playerDamageMiss[subEvent] then
                local missType = select(-2, ...)
                if type(missType) == "boolean" then -- some spells has 3 args like Charge Stun
                    missType = select(-3, ...)
                end
                DataToColor.CombatMissTypeQueue:push(miss_type[missType])
                --DataToColor:Print(subEvent, " ", missType, " ", miss_type[missType])
            end
        end

        if playerMeleeSwing[subEvent] then
            local _, _, _, _, _, _, _, _, _, isOffHand = select(12, ...)
            if not isOffHand then
                --DataToColor:Print("Normal Main Hand Melee Swing detected")
                DataToColor.lastMainHandMeleeSwing = DataToColor.globalTime
            end
        end

        if playerSummon[subEvent] then
            local guid = DataToColor:getGuidFromUUID(destGUID)
            DataToColor.playerPetSummons[guid] = true
            DataToColor.playerPetSummons[destGUID] = true
            --DataToColor:Print("Summoned Pet added: ", destGUID)
        end
    end

    if DataToColor.playerPetSummons[sourceGUID] then
        if playerDamageDone[subEvent] then
            DataToColor.CombatDamageDoneQueue:push(DataToColor:getGuidFromUUID(destGUID))
        end
    end

    if unitDied[subEvent] then
        if band(destFlags, COMBATLOG_OBJECT_TYPE_NPC) > 0 and DataToColor.eligibleKillCredit[destGUID] then
            DataToColor.CombatCreatureDiedQueue:push(DataToColor:getGuidFromUUID(destGUID))
            DataToColor.lastLoot = DataToColor.C.Loot.Corpse
            DataToColor.sessionKillCount = DataToColor.sessionKillCount + 1
            --DataToColor:Print(subEvent, " ", destGUID, " ", DataToColor:getGuidFromUUID(destGUID))
        elseif destGUID == DataToColor.playerGUID then
            DataToColor.CombatCreatureDiedQueue:push(16777215)
            --DataToColor:Print(subEvent, " player Death ", destGUID, " 16777215")
        elseif DataToColor.playerPetSummons[destGUID] then
            local guid = DataToColor:getGuidFromUUID(destGUID)
            DataToColor.playerPetSummons[guid] = nil
            DataToColor.playerPetSummons[destGUID] = nil
            --DataToColor:Print("Summoned Pet removed: ", destGUID)
        else
            --DataToColor:Print(subEvent, " ignored ", destGUID)
        end
    end
end

function DataToColor:OnUnitSpellCastSent(event, unit, target, castGUID, spellId)
    --print(event, unit, target, castGUID, spellId)
    if unit ~= DataToColor.C.unitPlayer then return end

    DataToColor.lastCastEvent = CAST_SENT
    DataToColor.uiErrorMessageTime = DataToColor.globalTime
    DataToColor.lastCastSpellId = spellId
end

function DataToColor:OnUnitSpellCastSucceeded(event, unit, castGUID, spellId)
    --print(event, unit, castGUID, spellId)
    if unit ~= DataToColor.C.unitPlayer then return end

    DataToColor.lastCastEvent = CAST_SUCCESS
    DataToColor.uiErrorMessageTime = DataToColor.globalTime
    DataToColor.lastCastSpellId = spellId
end

function DataToColor:OnUnitSpellCastFailed(event, unit, castGUID, spellId)
    --print(event, unit, castGUID, spellId)
    if unit ~= DataToColor.C.unitPlayer then return end

    DataToColor.lastCastEvent = DataToColor.uiErrorMessage
    DataToColor.uiErrorMessageTime = DataToColor.globalTime
    DataToColor.lastCastSpellId = spellId
end

function DataToColor:OnUnitSpellCastChannelStart(event, unit, castGUID, spellID)
    if unit ~= DataToColor.C.unitPlayer then return end
    DataToColor.channeling = true
end

function DataToColor:OnUnitSpellCastChannelStop(event, unit, castGUID, spellID)
    if unit ~= DataToColor.C.unitPlayer then return end
    DataToColor.channeling = false
end

function DataToColor:SoM_OnCastSuccess(event, unitTarget, castGuid, spellId)
    if unitTarget ~= DataToColor.C.unitPlayer then return end
    som_spellId = spellId or 0
end

function DataToColor:SoM_OnCastStart(event, unitTarget, castGuid, spellId)
    if unitTarget ~= DataToColor.C.unitPlayer then return end
    som_spellId = spellId or 0
end

function DataToColor:SoM_OnCastFailed(event, unitTarget, castGuid, spellId)
    if unitTarget ~= DataToColor.C.unitPlayer then return end
    som_spellId = spellId or 0
end

function DataToColor:OnLootReady(autoloot)
    DataToColor.lastLoot = DataToColor.C.Loot.Ready
    --DataToColor:Print("OnLootReady:"..DataToColor.lastLoot)
end

function DataToColor:OnLootClosed(event)
    DataToColor.lastLoot = DataToColor.C.Loot.Closed
    DataToColor.lastLootResetStart = DataToColor.globalTime
    --DataToColor:Print("OnLootClosed:"..DataToColor.lastLoot)
end

function DataToColor:OnBagUpdate(event, containerID)
    if containerID >= 0 and containerID <= NUM_BAG_SLOTS then
        DataToColor.bagQueue:push(containerID)
        DataToColor:InitInventoryQueue(containerID)

        if containerID >= 1 then
            local invID = ContainerIDToInventoryID(containerID)
            --DataToColor:Print("OnBagUpdate "..containerID.." invID "..invID)
            DataToColor.equipmentQueue:push(invID)
        end
    end
    --DataToColor:Print("OnBagUpdate "..containerID)
end

function DataToColor:OnMerchantShow(event)
    DataToColor.gossipQueue:push(MERCHANT_SHOW_V)
end

function DataToColor:OnMerchantClosed(event)
    DataToColor.gossipQueue:push(MERCHANT_CLOSED_V)
end

function DataToColor:OnPlayerTargetChanged(event)
    DataToColor.targetChanged = true
end

function DataToColor:OnPlayerEquipmentChanged(event, equipmentSlot, hasCurrent)
    DataToColor.equipmentQueue:push(equipmentSlot)
    --local c = hasCurrent and 1 or 0
    --DataToColor:Print("OnPlayerEquipmentChanged "..equipmentSlot.." -> "..c)
end

--[[
function DataToColor:OnGossipShow(event)
    local options = GetGossipOptions()
    if not options then
        return
    end

    DataToColor.gossipQueue:push(GOSSIP_START)

    local count = #options
    for i, v in pairs(options) do
        local hash = 10000 * count + 100 * i + DataToColor.C.GossipIcon[v.icon]
        --DataToColor:Print(i .. " " .. v.icon .. " " .. DataToColor.C.GossipIcon[v.icon] .. " " .. v.name .. " " .. hash)
        DataToColor.gossipQueue:push(hash)
    end
    DataToColor.gossipQueue:push(GOSSIP_END)
end
]]--

function DataToColor:OnSpellsChanged(event)
    DataToColor:InitTalentQueue()
    DataToColor:InitSpellBookQueue()
    DataToColor:InitActionBarCostQueue()
end

function DataToColor:ActionbarSlotChanged(event, slot)
    if slot and slot <= DataToColor.C.MAX_ACTIONBAR_SLOT and HasAction(slot) then
        DataToColor:populateActionbarCost(slot)
    end
end

function DataToColor:CorpseInRangeEvent(event)
    DataToColor.corpseInRange = 2
end

function DataToColor:CorpseOutOfRangeEvent(event)
    DataToColor.corpseInRange = 0
end

function DataToColor:ChatMessageOpeningEvent(event, ...)
    local _, playerName, _, _, playerName2 = ...
    local function isempty(s)
        return s == nil or s == ''
    end

    if isempty(playerName) and isempty(playerName2) then
        DataToColor.lastCastEvent = CAST_SUCCESS
        DataToColor.uiErrorMessage = CAST_SUCCESS
        DataToColor.uiErrorMessageTime = DataToColor.globalTime
    end
end

function DataToColor:OnPetChanged(event, unit)
    if unit == DataToColor.C.unitPlayer then
        DataToColor.petGUID = UnitGUID(DataToColor.C.unitPet)
    end
end

function DataToColor:OnZoneChanged(event)
    DataToColor.map = C_Map.GetBestMapForUnit(DataToColor.C.unitPlayer)
end

function DataToColor:OnLeftCombat()
    DataToColor.eligibleKillCredit = {}
end

function DataToColor:AutoFollowBegin()
    DataToColor.autoFollow = true
end

function DataToColor:AutoFollowEnd()
    DataToColor.autoFollow = false
end

function DataToColor:PlayerStartedMoving()
    DataToColor.moving = true
end

function DataToColor:PlayerStoppedMoving()
    DataToColor.moving = false
end

function DataToColor:OnMessageWhisper(event, msg, author)
    AddMessageToQueue(0, msg, author)
end

function DataToColor:OnMessageSay(event, msg, author)
    AddMessageToQueue(1, msg, author)
end

function DataToColor:OnMessageYell(event, msg, author)
    AddMessageToQueue(2, msg, author)
end

function DataToColor:OnMessageEmote(event, msg, author)
    AddMessageToQueue(3, msg, author)
end

function DataToColor:OnMessageParty(event, msg, author)
    AddMessageToQueue(4, msg, author)
end

function AddMessageToQueue(type, msg, author)
    --print(author, msg)
    --author split '-' MyName-Realm
    local i, length = string.find(author, '-')
    if i ~= nil then
        length = length - 1
    else
        length = string.len(author)
    end
    author = string.sub(author, 1, length)

    msg = author .. ' ' .. msg

    --print(type, string.len(msg), msg)

    DataToColor.ChatQueue:push({ type = type, length = string.len(msg), msg = msg })
end

function DataToColor:OnPlayerSoftInteractChanged(event, old, new)
    DataToColor.softInteractGuid = new
    --print(event, old, "vs", new, DataToColor:getGuidFromUUID(new), DataToColor:getNpcIdFromUUID(new))
end

local CORPSE_RETRIEVAL_DISTANCE = 40

-----------------------------------------------------------------------------
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-----------------------------------------------------------------------------
function DataToColor:HandlePlayerInteractionEvents()
    -- Handles group accept/decline
    if DataToColor.DATA_CONFIG.ACCEPT_PARTY_REQUESTS or DataToColor.DATA_CONFIG.DECLINE_PARTY_REQUESTS then
        DataToColor:HandlePartyInvite()
    end
    -- Handles item repairs when talking to item repair NPC
    if DataToColor.DATA_CONFIG.AUTO_REPAIR_ITEMS then
        DataToColor:RepairItems()
    end
    -- Resurrect player
    if DataToColor.DATA_CONFIG.AUTO_RESURRECT then
        DataToColor:ResurrectPlayer()
    end
end

-- Declines/Accepts Party Invites.
function DataToColor:HandlePartyInvite()
    if DataToColor.globalTime % 500 == 1 then
        -- Declines party invite if configured to decline
        if DataToColor.DATA_CONFIG.DECLINE_PARTY_REQUESTS then
            DeclineGroup()
        else if DataToColor.DATA_CONFIG.ACCEPT_PARTY_REQUESTS then
                AcceptGroup()
            end
        end
        -- Hides the party invite pop-up regardless of whether we accept it or not
        StaticPopup_Hide("PARTY_INVITE")
    end
end

-- Repairs items if they are broken
function DataToColor:RepairItems()
    if DataToColor.globalTime % 25 == 1 then
        local cost = GetRepairAllCost()
        if CanMerchantRepair() and cost > 0 and GetMoney() >= cost then
            RepairAllItems()
        end
    end
end

--the x and y is 0 if not dead
--runs the RetrieveCorpse() function to ressurrect
function DataToColor:ResurrectPlayer()
    if DataToColor.globalTime % 700 == 1 then
        if UnitIsDeadOrGhost(DataToColor.C.unitPlayer) then

            -- Accept Release Spirit immediately after dying
            if not UnitIsGhost(DataToColor.C.unitPlayer) and UnitIsGhost(DataToColor.C.unitPlayer) ~= nil then
                RepopMe()
            end
            if UnitIsGhost(DataToColor.C.unitPlayer) then
                local cX, cY = DataToColor:GetCorpsePosition()
                local x, y = DataToColor:GetPosition()
                -- Waits so that we are in range of specified retrieval distance, and ensures there is no delay timer before attemtping to resurrect
                if cX ~= 0 and cY ~= 0 and
                    math.abs(cX - x) < CORPSE_RETRIEVAL_DISTANCE / 1000 and
                    math.abs(cY - y) < CORPSE_RETRIEVAL_DISTANCE / 1000 and
                    GetCorpseRecoveryDelay() == 0 then
                    DEFAULT_CHAT_FRAME:AddMessage('Attempting to retrieve corpse')
                    -- Accept Retrieve Corpsse when near enough
                    RetrieveCorpse()
                end
            end
        end
    end
end
