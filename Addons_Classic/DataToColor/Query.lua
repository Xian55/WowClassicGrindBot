local Load = select(2, ...)
local DataToColor = unpack(Load)
local Range = DataToColor.Libs.RangeCheck
Range:activate()

local bit = bit
local band = bit.band
local pcall = pcall

local floor = math.floor

local tonumber = tonumber
local sub = string.sub
local find = string.find
local upper = string.upper
local byte = string.byte
local strsplit = strsplit

local C_Map = C_Map
local UnitExists = UnitExists
local GetUnitName = GetUnitName
local UnitReaction = UnitReaction
local UnitIsFriend = UnitIsFriend
local GetInventorySlotInfo = GetInventorySlotInfo
local GetInventoryItemCount = GetInventoryItemCount
local CheckInteractDistance = CheckInteractDistance
local UnitGUID = UnitGUID

local GetActionInfo = GetActionInfo
local GetMacroSpell = GetMacroSpell
local GetSpellPowerCost = GetSpellPowerCost
local GetSpellBaseCooldown = GetSpellBaseCooldown
local GetInventoryItemLink = GetInventoryItemLink
local IsSpellInRange = IsSpellInRange
local GetSpellInfo = GetSpellInfo
local GetActionCooldown = GetActionCooldown
local IsUsableAction = IsUsableAction
local GetActionTexture = GetActionTexture
local IsCurrentAction = IsCurrentAction
local IsAutoRepeatAction = IsAutoRepeatAction

local IsUsableSpell = IsUsableSpell

local GetNumSkillLines = GetNumSkillLines
local GetSkillLineInfo = GetSkillLineInfo

local UnitIsGhost = UnitIsGhost
local C_DeathInfo = C_DeathInfo
local UnitAttackSpeed = UnitAttackSpeed
local UnitRangedDamage = UnitRangedDamage

local GameMenuFrame = GameMenuFrame
local LootFrame = LootFrame
local ChatFrame1EditBox = ChatFrame1EditBox

local HasPetUI = HasPetUI

-- bits

local UnitAffectingCombat = UnitAffectingCombat
local GetWeaponEnchantInfo = GetWeaponEnchantInfo
local UnitIsDead = UnitIsDead
local UnitIsPlayer = UnitIsPlayer
local UnitName = UnitName
local UnitIsDeadOrGhost = UnitIsDeadOrGhost
local UnitCharacterPoints = UnitCharacterPoints
local UnitPlayerControlled = UnitPlayerControlled
local GetShapeshiftForm = GetShapeshiftForm
local GetShapeshiftFormInfo = GetShapeshiftFormInfo
local GetInventoryItemBroken = GetInventoryItemBroken
local GetInventoryItemDurability = GetInventoryItemDurability
local GetInventoryItemID = GetInventoryItemID
local UnitOnTaxi = UnitOnTaxi
local IsSwimming = IsSwimming
local IsFalling = IsFalling
local IsFlying = IsFlying
local IsIndoors = IsIndoors
local IsStealthed = IsStealthed
local GetMirrorTimerInfo = GetMirrorTimerInfo
local IsMounted = IsMounted
local IsInGroup = IsInGroup

local UnitIsTapDenied = UnitIsTapDenied
local IsAutoRepeatSpell = IsAutoRepeatSpell
local IsCurrentSpell = IsCurrentSpell
local UnitIsVisible = UnitIsVisible
local GetPetHappiness = GetPetHappiness

local ammoSlot = GetInventorySlotInfo("AmmoSlot")

-- Use Astrolabe function to get current player position
function DataToColor:GetPosition()
    if not DataToColor.map then
        return 0, 0
    end

    local pos = C_Map.GetPlayerMapPosition(DataToColor.map, DataToColor.C.unitPlayer)
    if pos then
        return pos:GetXY()
    end
    return 0, 0
end

-- Base 2 converter for up to 24 boolean values to a single pixel square.
function DataToColor:Bits1()
    -- 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384

    local mainHandEnchant, _, _, _, offHandEnchant = GetWeaponEnchantInfo()

    return
        (UnitAffectingCombat(DataToColor.C.unitTarget) and 1 or 0) +
        (UnitIsDead(DataToColor.C.unitTarget) and 2 or 0) ^ 1 +
        (UnitIsDeadOrGhost(DataToColor.C.unitPlayer) and 2 or 0) ^ 2 +
        (UnitCharacterPoints(DataToColor.C.unitPlayer) > 0 and 2 or 0) ^ 3 +
        (UnitExists(DataToColor.C.unitmouseover) and 2 or 0) ^ 4 +
        (DataToColor:IsUnitHostile(DataToColor.C.unitPlayer, DataToColor.C.unitTarget) and 2 or 0) ^ 5 +
        (UnitIsVisible(DataToColor.C.unitPet) and not UnitIsDead(DataToColor.C.unitPet) and 2 or 0) ^ 6 +
        (mainHandEnchant and 2 or 0) ^ 7 +
        (offHandEnchant and 2 or 0) ^ 8 +
        (DataToColor:GetInventoryBroken() ^ 9) +
        (UnitOnTaxi(DataToColor.C.unitPlayer) and 2 or 0) ^ 10 +
        (IsSwimming() and 2 or 0) ^ 11 +
        (DataToColor:PetHappy() and 2 or 0) ^ 12 +
        (DataToColor:HasAmmo() and 2 or 0) ^ 13 +
        (UnitAffectingCombat(DataToColor.C.unitPlayer) and 2 or 0) ^ 14 +
        (DataToColor:IsUnitsTargetIsPlayerOrPet(DataToColor.C.unitTarget, DataToColor.C.unitTargetTarget) and 2 or 0) ^ 15 +
        (IsAutoRepeatSpell(DataToColor.C.Spell.AutoShotId) and 2 or 0) ^ 16 +
        (UnitExists(DataToColor.C.unitTarget) and 2 or 0) ^ 17 +
        (IsMounted() and 2 or 0) ^ 18 +
        (IsAutoRepeatSpell(DataToColor.C.Spell.ShootId) and 2 or 0) ^ 19 +
        (IsCurrentSpell(DataToColor.C.Spell.AttackId) and 2 or 0) ^ 20 +
        (UnitIsPlayer(DataToColor.C.unitTarget) and 2 or 0) ^ 21 +
        (UnitIsTapDenied(DataToColor.C.unitTarget) and 2 or 0) ^ 22 +
        (IsFalling() and 2 or 0) ^ 23
end

function DataToColor:Bits2()
    local type, _, _, scale = GetMirrorTimerInfo(2)
    return
        (type == DataToColor.C.MIRRORTIMER.BREATH and scale < 0 and 1 or 0) +
        (DataToColor.corpseInRange ^ 1) +
        (IsIndoors() and 2 or 0) ^ 2 +
        (UnitExists(DataToColor.C.unitFocus) and 2 or 0) ^ 3 +
        (UnitAffectingCombat(DataToColor.C.unitFocus) and 2 or 0) ^ 4 +
        (UnitExists(DataToColor.C.unitFocusTarget) and 2 or 0) ^ 5 +
        (UnitAffectingCombat(DataToColor.C.unitFocusTarget) and 2 or 0) ^ 6 +
        (DataToColor:IsUnitHostile(DataToColor.C.unitPlayer, DataToColor.C.unitFocusTarget) and 2 or 0) ^ 7 +
        (UnitIsDead(DataToColor.C.unitmouseover) and 2 or 0) ^ 8 +
        (UnitIsDead(DataToColor.C.unitPetTarget) and 2 or 0) ^ 9 +
        (IsStealthed() and 2 or 0) ^ 10 +
        (UnitIsTrivial(DataToColor.C.unitTarget) and 2 or 0) ^ 11 +
        (UnitIsTrivial(DataToColor.C.unitmouseover) and 2 or 0) ^ 12 +
        (UnitIsTapDenied(DataToColor.C.unitmouseover) and 2 or 0) ^ 13 +
        (DataToColor:IsUnitHostile(DataToColor.C.unitPlayer, DataToColor.C.unitmouseover) and 2 or 0) ^ 14 +
        (UnitIsPlayer(DataToColor.C.unitmouseover) and 2 or 0) ^ 15 +
        (DataToColor:IsUnitsTargetIsPlayerOrPet(DataToColor.C.unitmouseover, DataToColor.C.unitmouseovertarget) and 2 or 0) ^ 16 +
        (UnitPlayerControlled(DataToColor.C.unitmouseover) and 2 or 0) ^ 17 +
        (UnitPlayerControlled(DataToColor.C.unitTarget) and 2 or 0) ^ 18 +
        (DataToColor.autoFollow and 2 or 0) ^ 19 +
        (GameMenuFrame:IsShown() and 2 or 0) ^ 20 +
        (IsFlying() and 2 or 0) ^ 21 +
        (DataToColor.moving and 2 or 0) ^ 22 +
        (DataToColor:PetIsDefensive() and 2 or 0) ^ 23
end

function DataToColor:Bits3()
    return
        (UnitExists(DataToColor.C.unitSoftInteract) and 1 or 0) +
        (UnitIsDead(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 1 +
        (UnitIsDeadOrGhost(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 2 +
        (UnitIsPlayer(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 3 +
        (UnitIsTapDenied(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 4 +
        (UnitAffectingCombat(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 5 +
        (DataToColor:IsUnitHostile(DataToColor.C.unitPlayer, DataToColor.C.unitSoftInteract) and 2 or 0) ^ 6 +
        (DataToColor.channeling and 2 or 0) ^ 7 +
        (LootFrame:IsShown() and 2 or 0) ^ 8 +
        (ChatFrame1EditBox:IsVisible() and 2 or 0) ^ 9 +
        (DataToColor:SoftTargetInteractEnabled() and 2 or 0) ^ 10
end

function DataToColor:CustomTrigger(t)
    local v = t[0]
    for i = 1, 23 do
        v = v + (t[i] ^ i)
    end
    return v
end

function DataToColor:Set(trigger, input)
    if input == true then input = 1 end
    local v = tonumber(input) or 0
    if v > 0 then v = 1 end
    if trigger >= 0 and trigger <= 23 then
        DataToColor.customTrigger1[trigger] = v
    end
end

function DataToColor:getAuraMaskForClass(func, unitId, table)
    local mask = 0
    for k, v in pairs(table) do
        for i = 1, 24 do
            local name, texture = func(unitId, i)
            if not name then
                break
            end
            if v[texture] or find(name, v[1]) then
                mask = mask + (2 ^ k)
                break
            end
        end
    end
    return mask
end

function DataToColor:populateAuraTimer(func, unitId, queue)
    local count = 0

    self._existingAuras = self._existingAuras or {}
    local existingAuras = self._existingAuras

    for k in pairs(existingAuras) do
        existingAuras[k] = nil
    end

    for i = 1, 40 do
        local name, texture, _, _, duration, expirationTime = func(unitId, i)
        if not name then
            break
        end
        count = i

        if queue then
            existingAuras[texture] = true

            if duration == 0 then
                expirationTime = GetTime() + 14400 -- 4 hours - anything above considered unlimited duration
                --DataToColor:Print(texture, " unlimited aura added ", expirationTime)
            end

            if not queue:exists(texture) then
                queue:set(texture, expirationTime)
                --DataToColor:Print(texture, " aura added ", expirationTime)
            elseif not queue:isDirty(texture) and queue:value(texture) < expirationTime then
                queue:set(texture, expirationTime)
                --DataToColor:Print(texture, " aura updated ", expirationTime)
            end
        end
    end

    -- Remove unlimited duration Auras.
    -- Such as clickable Mounts and Buffs
    if queue then
        for k in queue:iterator() do
            if not existingAuras[k] then
                --DataToColor:Print(k, " remove unlimited")
                queue:set(k, GetTime())
            end
        end
    end

    return count
end

-- Pass in a string to get the upper case ASCII values. Converts any special character with ASCII values below 100
local function StringToASCIIHex(str)
    str = upper(sub(str, 1, min(6, #str)))
    local asciiValue = 0
    for i = 1, #str do
        asciiValue = asciiValue * 100 + min(byte(str, i), 90) -- 90 is Z
    end
    return asciiValue
end

-- Grabs current targets name
function DataToColor:GetTargetName(partition)
    if not UnitExists(DataToColor.C.unitTarget) then
        return 0
    end

    local targetName = StringToASCIIHex(GetUnitName(DataToColor.C.unitTarget))

    if partition >= 3 and targetName > 999999 then
        return targetName % 10 ^ 6
    end

    return floor(targetName / 10 ^ 6)
end

function DataToColor:CastingInfoSpellId(unitId)
    local _, _, _, startTime, endTime, _, _, _, spellID = DataToColor.UnitCastingInfo(unitId)

    if spellID then
        if unitId == DataToColor.C.unitPlayer and startTime ~= DataToColor.lastCastStartTime then
            DataToColor.lastCastStartTime = startTime
            DataToColor.lastCastEndTime = endTime
            DataToColor.CastNum = DataToColor.CastNum + 1
        end
        return spellID
    end

    local _, _, _, startTime, endTime, _, _, spellID = DataToColor.UnitChannelInfo(unitId)
    if spellID then
        if unitId == DataToColor.C.unitPlayer and startTime ~= DataToColor.lastCastStartTime then
            DataToColor.lastCastStartTime = startTime
            DataToColor.lastCastEndTime = endTime
            DataToColor.CastNum = DataToColor.CastNum + 1
        end
        return spellID
    end

    if unitId == DataToColor.C.unitPlayer then
        DataToColor.lastCastEndTime = 0
    end

    return 0
end

--

function DataToColor:getRange()
    local min, max = Range:GetRange(DataToColor.C.unitTarget)
    return (max or 0) * 1000 + (min or 0)
end

function DataToColor:NpcId(unit)
    local guid = UnitGUID(unit) or ""
    local id = guid:match("-(%d+)-[^-]+$")

    if id and not guid:find("^Player") then
        return tonumber(id, 10)
    end
    return 0
end

function DataToColor:getGuidFromUnit(unit)
    if not UnitExists(unit) then
        return 0
    end

    -- Player-4731-02AAD4FF
    -- Creature-0-4488-530-222-19350-000005C0D70
    -- Pet-0-4448-530-222-22123-15004E200E
    return DataToColor:uniqueGuid(select(-2, strsplit('-', UnitGUID(unit))))
end

function DataToColor:getGuidFromUUID(uuid)
    if not uuid then
        return 0
    end
    return DataToColor:uniqueGuid(select(-2, strsplit('-', uuid)))
end

function DataToColor:getNpcIdFromUUID(uuid)
    if not uuid then
        return 0
    end

    local id = uuid:match("-(%d+)-[^-]+$")

    if id and not uuid:find("^Player") then
        return tonumber(id, 10)
    end
    return 0
end

function DataToColor:getTypeFromUUID(uuid)
    if not uuid then
        return 0
    end

    local type = uuid:match("^(.-)-")
    return DataToColor.C.GuidType[type] or 0
end

function DataToColor:uniqueGuid(npcId, spawn)
    local spawnEpochOffset = band(tonumber(sub(spawn, 5), 16), 0x7fffff)
    local spawnIndex = band(tonumber(sub(spawn, 1, 5), 16), 0xffff8)

    return (spawnEpochOffset + spawnIndex + npcId) % 0x1000000
end

local offsetEnumPowerType = 2
function DataToColor:populateActionbarCost(slot)
    local actionType, id = GetActionInfo(slot)
    if actionType == DataToColor.C.ActionType.Macro then
        id = GetMacroSpell(id)
    end

    local found = false

    if id and actionType == DataToColor.C.ActionType.Spell or actionType == DataToColor.C.ActionType.Macro then
        local costTable = GetSpellPowerCost(id)
        if costTable then
            for order, costInfo in ipairs(costTable) do
                -- cost negative means it produces that type of powertype...
                if costInfo.cost > 0 then
                    local meta = 100000 * slot + 10000 * order + costInfo.type + offsetEnumPowerType
                    --print(slot, actionType, order, costInfo.type, costInfo.cost, GetSpellLink(id), meta)
                    DataToColor.actionBarCostQueue:set(meta, costInfo.cost)
                    found = true
                end
            end
        end
    end
    -- default value mana with zero cost
    if found == false then
        DataToColor.actionBarCostQueue:set(100000 * slot + 10000 + offsetEnumPowerType, 0)
    end
end

function DataToColor:equipSlotItemId(slot)
    return GetInventoryItemID(DataToColor.C.unitPlayer, slot) or 0
end

-- -- Function to tell if a spell is on cooldown and if the specified slot has a spell assigned to it
-- -- Slot ID information can be found on WoW Wiki. Slots we are using: 1-12 (main action bar), Bottom Right Action Bar maybe(49-60), and  Bottom Left (61-72)

function DataToColor:areSpellsInRange()
    local inRange = 0
    local targetCount = #DataToColor.S.spellInRangeTarget
    for i = 1, targetCount do
        local spellIconId = DataToColor.S.spellInRangeTarget[i]
        local spellId = DataToColor.S.playerSpellBookIconToId[spellIconId] or spellIconId -- fallback to spellId
        local spellName = GetSpellInfo(spellId)
        if spellName then
            if IsSpellInRange(spellName, DataToColor.C.unitTarget) == 1 then
                inRange = inRange + (2 ^ (i - 1))
            end
        else
            --print(spellId .. " is null")
        end
    end

    for i = 1, #DataToColor.S.spellInRangeUnit do
        local data = DataToColor.S.spellInRangeUnit[i]
        local spellId = DataToColor.S.playerSpellBookIconToId[data[1]]
        local unit = data[2]
        if spellId and IsSpellInRange(GetSpellInfo(spellId), unit) == 1 then
            inRange = inRange + (2 ^ (targetCount + i - 1))
        end
    end

    -- CheckInteractDistance restricted in combat
    if not UnitAffectingCombat(DataToColor.C.unitPlayer) then
        local c = #DataToColor.S.interactInRangeUnit
        for i = 1, c do
            local data = DataToColor.S.interactInRangeUnit[i]
            if CheckInteractDistance(data[1], data[2]) then
                inRange = inRange + (2 ^ (24 - c + i - 1))
            end
        end
    end

    return inRange
end

function DataToColor:isActionUseable(min, max)
    local isUsableBits = 0
    for i = min, max do
        local start, duration, enabled = GetActionCooldown(i)
        local isUsable, notEnough = IsUsableAction(i)
        local texture = GetActionTexture(i)
        local spellName = DataToColor.S.playerSpellBookName[texture]

        if start == 0 and (isUsable == true and notEnough == false or IsUsableSpell(spellName)) and texture ~= 134400 then -- red question mark texture
            isUsableBits = isUsableBits + (2 ^ (i - min))
        end

        local _, spellId = GetActionInfo(i)
        local gcd = 0
        if DataToColor.S.playerSpellBookId[spellId] then
            gcd = select(2, GetSpellBaseCooldown(spellId))
        end

        if enabled == 1 and start ~= 0 and (duration * 1000) > gcd and not DataToColor.actionBarCooldownQueue:exists(i) then
            local expireTime = start + duration
            DataToColor.actionBarCooldownQueue:set(i, expireTime)
        end
    end
    return isUsableBits
end

function DataToColor:isCurrentAction(min, max)
    local isUsableBits = 0
    for i = min, max do
        if IsCurrentAction(i) or IsAutoRepeatAction(i) then
            isUsableBits = isUsableBits + (2 ^ (i - min))
        end
    end
    return isUsableBits
end

-- Finds passed in string to return profession level
function DataToColor:GetProfessionLevel(skillName)
    local max = GetNumSkillLines()
    for c = 1, max do
        local name, _, _, rank = GetSkillLineInfo(c)
        if (name == skillName) then
            return tonumber(rank)
        end
    end
    return 0
end

function DataToColor:GetCorpsePosition()
    if not UnitIsGhost(DataToColor.C.unitPlayer) then
        return 0, 0
    end

    local corpseMap = C_DeathInfo.GetCorpseMapPosition(DataToColor.map)
    if corpseMap then
        return corpseMap:GetXY()
    end
    return 0, 0
end

function DataToColor:getMeleeAttackSpeed(unit)
    local main, off = UnitAttackSpeed(unit)
    return 10000 * floor((off or 0) * 100) + floor((main or 0) * 100)
end

function DataToColor:getUnitRangedDamage(unit)
    local speed = UnitRangedDamage(unit)
    return floor((speed or 0) * 100)
end

function DataToColor:getAvgEquipmentDurability()
    local current = 0
    local max = 0
    for i = 1, 18 do
        local c, m = GetInventoryItemDurability(i)
        current = current + (c or 0)
        max = max + (m or 0)
    end
    return math.max(0, floor((current + 1) * 100 / (max + 1)) - 1) -- 0-99
end

-----------------------------------------------------------------
-- Boolean functions --------------------------------------------
-- Only put functions here that are part of a boolean sequence --
-- Sew BELOW for examples ---------------------------------------
-----------------------------------------------------------------

function DataToColor:shapeshiftForm()
    local index = GetShapeshiftForm(false)
    if not index or index == 0 then
        return 0
    end

    local _, _, _, spellId = GetShapeshiftFormInfo(index)
    local form = DataToColor.S.playerAuraMap[spellId]
    if form then
        return form
    end
    return index
end

function DataToColor:GetInventoryBroken()
    for i = 1, 18 do
        if GetInventoryItemBroken(DataToColor.C.unitPlayer, i) then
            return 2
        end
    end
    return 0
end

function DataToColor:UnitsTargetAsNumber(unit, unittarget)
    if not (UnitName(unittarget)) then return 2 end                              -- target has no target
    if DataToColor.C.CHARACTER_NAME == UnitName(unit) then return 0 end          -- targeting self
    if UnitName(DataToColor.C.unitPet) == UnitName(unittarget) then return 4 end -- targetting my pet
    if DataToColor.playerPetSummons[UnitGUID(unittarget)] then return 4 end
    if DataToColor.C.CHARACTER_NAME == UnitName(unittarget) then return 1 end    -- targetting me
    if UnitName(DataToColor.C.unitPet) == UnitName(unit) and UnitName(unittarget) then
        return 5
    end
    if IsInGroup() and DataToColor:UnitTargetsPartyOrPet(unittarget) then return 6 end
    return 3
end

function DataToColor:UnitTargetsPartyOrPet(unittarget)
    local targetName = UnitName(unittarget)
    if not targetName then return false end

    for i = 1, 4 do
        local partyUnit = DataToColor.C.unitPartyNames[i]
        if UnitExists(partyUnit) and UnitName(partyUnit) == targetName then
            return true
        end

        local petUnit = DataToColor.C.unitPartyPetNames[i]
        if UnitExists(petUnit) and UnitName(petUnit) == targetName then
            return true
        end
    end
    return false
end

function DataToColor:HasAmmo()
    -- After Cataclysm, ammo slot was removed
    if DataToColor:IsClassicPreCata() == false then
        return true
    end

    local count = GetInventoryItemCount(DataToColor.C.unitPlayer, ammoSlot)
    return count > 0
end

function DataToColor:PetHappy()
    -- After Cataclysm, pet always happy :)
    if DataToColor:IsClassicPreCata() == false then
        return true
    end

    return GetPetHappiness() == 3
end

function DataToColor:SoftTargetInteractEnabled()
    local success, value = pcall(GetCVar, DataToColor.C.CVarSoftTargetInteract)
    return success and tonumber(value) == 3
end

-- Returns true if target of our target is us
function DataToColor:IsUnitsTargetIsPlayerOrPet(unit, unittarget)
    local x = DataToColor:UnitsTargetAsNumber(unit, unittarget)
    return x == 1 or x == 4
end

function DataToColor:IsUnitHostile(unit, unittarget)
    return
        UnitExists(unittarget) and
        (UnitReaction(unit, unittarget) or 0) <= 4 and
        not UnitIsFriend(unit, unittarget)
end

function DataToColor:PetIsDefensive()
    if not HasPetUI() then
        return false
    end

    for i = 1, 10 do
        local name, _, _, isActive = GetPetActionInfo(i)
        if isActive and name == DataToColor.C.PET_MODE_DEFENSIVE then
            return true
        end
    end

    return false
end