local Load = select(2, ...)
local DataToColor = unpack(Load)
local Range = DataToColor.Libs.RangeCheck

local bit = bit
local band = bit.band
local pcall = pcall
local next = next

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
local GetSpellPowerCost = GetSpellPowerCost or function(spellID)
    local cost, powerType = select(4, GetSpellInfo(spellID)), select(5, GetSpellInfo(spellID))
    return { cost = cost, powerType = powerType }
end
local GetSpellBaseCooldown = GetSpellBaseCooldown
local GetInventoryItemLink = GetInventoryItemLink
local IsSpellInRange = IsSpellInRange
local GetSpellInfo = GetSpellInfo
local GetActionCooldown = GetActionCooldown
local IsUsableItem = IsUsableItem or C_Item.IsUsableItem
local IsUsableAction = IsUsableAction
local IsCurrentAction = IsCurrentAction
local IsAutoRepeatAction = IsAutoRepeatAction

local IsUsableSpell = IsUsableSpell or C_Spell.IsUsableSpell

local GetNumSkillLines = GetNumSkillLines
local GetSkillLineInfo = GetSkillLineInfo

local UnitIsGhost = UnitIsGhost
local C_DeathInfo = C_DeathInfo
local UnitAttackSpeed = UnitAttackSpeed
local UnitRangedDamage = UnitRangedDamage
local UnitBuff = UnitBuff

local GameMenuFrame = GameMenuFrame
local LootFrame = LootFrame
local ChatEdit_GetActiveWindow = ChatEdit_GetActiveWindow
local MailFrame = MailFrame
local IsBagOpen = IsBagOpen
local CharacterFrame = CharacterFrame
local SpellBookFrame = SpellBookFrame
local FriendsFrame = FriendsFrame

local HasPetUI = HasPetUI

-- bits

local UnitAffectingCombat = UnitAffectingCombat
local GetWeaponEnchantInfo = GetWeaponEnchantInfo
local UnitIsDead = UnitIsDead
local UnitIsPlayer = UnitIsPlayer
local UnitName = UnitName
local UnitIsDeadOrGhost = UnitIsDeadOrGhost
local UnitCharacterPoints = UnitCharacterPoints or function(unit)
  if not UnitExists(unit) then
    return 0
  end
  if UnitIsUnit(unit, "pet") then
    return GetUnspentTalentPoints(false, true)
  elseif UnitIsUnit(unit, "player") then
    return GetUnspentTalentPoints(false)
  else
    return 0
  end
end
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

local IsAutoRepeatSpell = IsAutoRepeatSpell
local IsCurrentSpell = IsCurrentSpell
local UnitIsVisible = UnitIsVisible
local GetPetHappiness = GetPetHappiness

local ammoSlot = GetInventorySlotInfo("AmmoSlot")

local spellRangeNameCache = {}
local spellRangeUnitNameCache = {}

local cachedPetName = nil
local petNameDirty = true

local cachedShapeshiftForm = 0
local shapeshiftDirty = true

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

function DataToColor:IsChatInputActive()
    return ChatEdit_GetActiveWindow() ~= nil
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
        (DataToColor:UnitIsTapDenied(DataToColor.C.unitTarget) and 2 or 0) ^ 22 +
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
        (DataToColor:UnitIsTapDenied(DataToColor.C.unitmouseover) and 2 or 0) ^ 13 +
        (DataToColor:IsUnitHostile(DataToColor.C.unitPlayer, DataToColor.C.unitmouseover) and 2 or 0) ^ 14 +
        (UnitIsPlayer(DataToColor.C.unitmouseover) and 2 or 0) ^ 15 +
        (DataToColor:IsUnitsTargetIsPlayerOrPet(DataToColor.C.unitmouseover, DataToColor.C.unitmouseovertarget) and 2 or 0) ^ 16 +
        (UnitPlayerControlled(DataToColor.C.unitmouseover) and 2 or 0) ^ 17 +
        (UnitPlayerControlled(DataToColor.C.unitTarget) and 2 or 0) ^ 18 +
        (DataToColor.autoFollow and 2 or 0) ^ 19 +
        (GameMenuFrame:IsShown() and 2 or 0) ^ 20 +
        (IsFlying() and 2 or 0) ^ 21 +
        (DataToColor:PlayerIsMoving() and 2 or 0) ^ 22 +
        (DataToColor:PetIsDefensive() and 2 or 0) ^ 23
end

function DataToColor:Bits3()
    return
        (UnitExists(DataToColor.C.unitSoftInteract) and 1 or 0) +
        (UnitIsDead(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 1 +
        (UnitIsDeadOrGhost(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 2 +
        (UnitIsPlayer(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 3 +
        (DataToColor:UnitIsTapDenied(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 4 +
        (UnitAffectingCombat(DataToColor.C.unitSoftInteract) and 2 or 0) ^ 5 +
        (DataToColor:IsUnitHostile(DataToColor.C.unitPlayer, DataToColor.C.unitSoftInteract) and 2 or 0) ^ 6 +
        (DataToColor.channeling and 2 or 0) ^ 7 +
        (LootFrame:IsShown() and 2 or 0) ^ 8 +
        (DataToColor:IsChatInputActive() and 2 or 0) ^ 9 +
        (DataToColor:SoftTargetInteractEnabled() and 2 or 0) ^ 10 +
        (MailFrame:IsShown() and 2 or 0) ^ 11 +
        (DataToColor:AnyBagOpen() and 2 or 0) ^ 12 +
        (DataToColor:CharacterFrameOpen() and 2 or 0) ^ 13 +
        (DataToColor:SpellBookFrameOpen() and 2 or 0) ^ 14 +
        (DataToColor:FriendsFrameOpen() and 2 or 0) ^ 15
end

function DataToColor:CustomTrigger(t)
    local v = t[0] or 0
    for i = 1, 23 do
        v = v + ((t[i] or 0) ^ i)
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

-- Uses next() directly to avoid pairs() iterator allocation
-- Uses cached aura data to avoid UnitBuff/UnitDebuff string allocations
function DataToColor:getAuraMaskForClass(func, unitId, tbl)
    local mask = 0
    -- Determine if we're looking for buffs or debuffs based on the function
    local isBuff = (func == UnitBuff)
    local k, v = next(tbl)
    while k do
        for i = 1, 24 do
            local name, texture = DataToColor:GetCachedAuraInfo(isBuff, unitId, i)
            if not name then
                break
            end
            if v[texture] or find(name, v[1]) then
                mask = mask + (2 ^ k)
                break
            end
        end
        k, v = next(tbl, k)
    end
    return mask
end

-- Uses cached aura data to avoid UnitBuff/UnitDebuff string allocations
function DataToColor:populateAuraTimer(func, unitId, queue)
    local count = 0

    self._existingAuras = self._existingAuras or {}
    local existingAuras = self._existingAuras

    -- Clear using next() to avoid pairs() iterator allocation
    local k = next(existingAuras)
    while k do
        local nextK = next(existingAuras, k)
        existingAuras[k] = nil
        k = nextK
    end

    -- Determine if we're looking for buffs or debuffs based on the function
    local isBuff = (func == UnitBuff)

    for i = 1, 40 do
        local name, texture, duration, expirationTime = DataToColor:GetCachedAuraInfo(isBuff, unitId, i)
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
    -- Uses next() directly to avoid pairs() iterator allocation
    if queue then
        local t = queue:getTable()
        local k = next(t)
        while k do
            if not existingAuras[k] then
                --DataToColor:Print(k, " remove unlimited")
                queue:set(k, GetTime())
            end
            k = next(t, k)
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

function DataToColor:PopulateSpellInRangeNames()
    local S = DataToColor.S
    for i = 1, #S.spellInRangeTarget do
        local spellIconId = S.spellInRangeTarget[i]
        local spellId = S.playerSpellBookIconToId[spellIconId] or spellIconId
        spellRangeNameCache[i] = GetSpellInfo(spellId)
    end
    for i = 1, #S.spellInRangeUnit do
        local data = S.spellInRangeUnit[i]
        local spellId = S.playerSpellBookIconToId[data[1]]
        if spellId then
            spellRangeUnitNameCache[i] = GetSpellInfo(spellId)
        end
    end
end

function DataToColor:InvalidatePetNameCache()
    petNameDirty = true
end

function DataToColor:areSpellsInRange()
    local inRange = 0
    local targetCount = #DataToColor.S.spellInRangeTarget
    for i = 1, targetCount do
        local spellName = spellRangeNameCache[i]
        if spellName then
            if IsSpellInRange(spellName, DataToColor.C.unitTarget) == 1 then
                inRange = inRange + (2 ^ (i - 1))
            end
        end
    end

    for i = 1, #DataToColor.S.spellInRangeUnit do
        local data = DataToColor.S.spellInRangeUnit[i]
        local spellName = spellRangeUnitNameCache[i]
        local unit = data[2]
        if spellName and IsSpellInRange(spellName, unit) == 1 then
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

local function NormalizeUsable(usable, notEnough)
  -- Classic/vanilla style: usable = 1/nil, notEnough = 1/nil
  if usable == 1 then usable = true end
  if usable == nil then usable = false end

  if notEnough == 1 then notEnough = true end
  if notEnough == nil then notEnough = false end

  return usable, notEnough
end

local function GetActionSpellGcdMs(spellId)
  if not spellId then return 0 end
  local base = select(2, GetSpellBaseCooldown(spellId))
  return base or 1500 -- legacy fallback you already used
end

-- /dump DataToColor:isActionUseable(75, 75)
function DataToColor:isActionUseable(min, max)
  local isUsableBits = 0
  for slot = min, max do
    local start, duration, enabled = GetActionCooldown(slot)

    local actionType, id, _ = GetActionInfo(slot)

    local usable, notEnough = false, false
    local gcdMs = 0

    if actionType == "spell" then
      usable, notEnough = IsUsableSpell(id)
      usable, notEnough = NormalizeUsable(usable, notEnough)

      if DataToColor.S.playerSpellBookId and DataToColor.S.playerSpellBookId[id] then
        gcdMs = GetActionSpellGcdMs(id)
      end

    elseif actionType == "item" then
      usable, notEnough = IsUsableItem(id)
      usable, notEnough = NormalizeUsable(usable, notEnough)

      -- items don't use spell GCD the same way; keep gcdMs=0

    elseif actionType == "macro" then
      local macroSpell = GetMacroSpell(id)
      if macroSpell then
        usable, notEnough = IsUsableSpell(macroSpell)
        usable, notEnough = NormalizeUsable(usable, notEnough)

        if DataToColor.S.playerSpellBookId and DataToColor.S.playerSpellBookId[macroSpell] then
          gcdMs = GetActionSpellGcdMs(macroSpell)
        end
      else
        local macroItemName = GetMacroItem(id)
        if macroItemName then
          usable, notEnough = IsUsableItem(macroItemName)
          usable, notEnough = NormalizeUsable(usable, notEnough)
        else
          local u, ne = IsUsableAction(slot)
          usable, notEnough = NormalizeUsable(u, ne)
        end
      end

    else
      local u, ne = IsUsableAction(slot)
      usable, notEnough = NormalizeUsable(u, ne)
    end

    -- 'Red question mark' guard (134400) stays
    local texture = DataToColor:GetActionTexture(slot)

    if texture ~= 134400 and start == 0 and usable and not notEnough then
      isUsableBits = isUsableBits + (2 ^ (slot - min))
    end

    if enabled == 1 and start ~= 0 and (duration * 1000) > gcdMs and not DataToColor.actionBarCooldownQueue:exists(slot) then
      DataToColor.actionBarCooldownQueue:set(slot, start + duration)
    end
  end
  return isUsableBits
end

-- isActionUseable cache (cells 30-34)
local actionUseableCache = { 0, 0, 0, 0, 0 }
local actionUseableDirty = true

local function RebuildActionUseableCache()
    actionUseableCache[1] = DataToColor:isActionUseable(1, 24)
    actionUseableCache[2] = DataToColor:isActionUseable(25, 48)
    actionUseableCache[3] = DataToColor:isActionUseable(49, 72)
    actionUseableCache[4] = DataToColor:isActionUseable(73, 96)
    actionUseableCache[5] = DataToColor:isActionUseable(97, 120)
    actionUseableDirty = false
end

function DataToColor:isActionUseableCached(chunk)
    if actionUseableDirty then
        RebuildActionUseableCache()
    end
    return actionUseableCache[chunk]
end

function DataToColor:InvalidateActionUseableCache()
    actionUseableDirty = true
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

-- isCurrentAction cache (cells 25-29)
local currentActionCache = { 0, 0, 0, 0, 0 }
local currentActionDirty = true

local function RebuildCurrentActionCache()
    currentActionCache[1] = DataToColor:isCurrentAction(1, 24)
    currentActionCache[2] = DataToColor:isCurrentAction(25, 48)
    currentActionCache[3] = DataToColor:isCurrentAction(49, 72)
    currentActionCache[4] = DataToColor:isCurrentAction(73, 96)
    currentActionCache[5] = DataToColor:isCurrentAction(97, 120)
    currentActionDirty = false
end

function DataToColor:isCurrentActionCached(chunk)
    if currentActionDirty then
        RebuildCurrentActionCache()
    end
    return currentActionCache[chunk]
end

function DataToColor:InvalidateCurrentActionCache()
    currentActionDirty = true
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

-- Equipment durability cache (cell 54)
local cachedDurability = 0
local durabilityDirty = true

function DataToColor:getAvgEquipmentDurabilityCached()
    if durabilityDirty then
        cachedDurability = DataToColor:getAvgEquipmentDurability()
        durabilityDirty = false
    end
    return cachedDurability
end

function DataToColor:InvalidateDurabilityCache()
    durabilityDirty = true
end

-----------------------------------------------------------------
-- Boolean functions --------------------------------------------
-- Only put functions here that are part of a boolean sequence --
-- Sew BELOW for examples ---------------------------------------
-----------------------------------------------------------------

function DataToColor:shapeshiftForm()
    if shapeshiftDirty then
        local index = GetShapeshiftForm(false)
        if not index or index == 0 then
            cachedShapeshiftForm = 0
        else
            local _, _, _, spellId = GetShapeshiftFormInfo(index)
            cachedShapeshiftForm = DataToColor.S.playerAuraMap[spellId] or index
        end
        shapeshiftDirty = false
    end
    return cachedShapeshiftForm
end

function DataToColor:InvalidateShapeshiftCache()
    shapeshiftDirty = true
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
    local targetName = UnitName(unittarget)
    if not targetName then return 2 end                                             -- target has no target

    local unitName = UnitName(unit)
    if DataToColor.C.CHARACTER_NAME == unitName then return 0 end                   -- targeting self

    if petNameDirty then
        cachedPetName = UnitName(DataToColor.C.unitPet)
        petNameDirty = false
    end

    if cachedPetName and cachedPetName == targetName then return 4 end              -- targetting my pet
    if DataToColor.playerPetSummons[UnitGUID(unittarget)] then return 4 end
    if DataToColor.C.CHARACTER_NAME == targetName then return 1 end                 -- targetting me
    if cachedPetName and unitName == cachedPetName and targetName then return 5 end
    if IsInGroup() and DataToColor:UnitTargetsPartyOrPet(targetName) then return 6 end
    return 3
end

function DataToColor:UnitTargetsPartyOrPet(targetName)
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

function DataToColor:AnyBagOpen()
    for i = 0, NUM_BAG_SLOTS do
        if IsBagOpen(i) then
            return true
        end
    end
    return false
end

function DataToColor:CharacterFrameOpen()
    return CharacterFrame and CharacterFrame:IsShown() or false
end

function DataToColor:SpellBookFrameOpen()
    return SpellBookFrame and SpellBookFrame:IsShown() or false
end

function DataToColor:FriendsFrameOpen()
    return FriendsFrame and FriendsFrame:IsShown() or false
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

--------------------------------------------------------------------------------
-- UTF-8 Text Encoding for TextQueue
-- Packs UTF-8 bytes into 24-bit pixel values for transfer to C#
--------------------------------------------------------------------------------

local lshift = bit.lshift
local bor = bit.bor

-- Pack 3 UTF-8 bytes into 24-bit value (no allocation)
-- Returns integer: byte1 << 16 | byte2 << 8 | byte3
function DataToColor:PackUTF8Bytes(str, offset)
    local b1 = byte(str, offset) or 0
    local b2 = byte(str, offset + 1) or 0
    local b3 = byte(str, offset + 2) or 0
    return bor(lshift(b1, 16), lshift(b2, 8), b3)
end

-- Check if string contains 4-byte UTF-8 sequences (emoji)
-- Returns true if string is safe (no 4-byte chars), false otherwise
-- This avoids allocation - just scans the string
function DataToColor:IsUTF8Safe(str)
    local i = 1
    local len = #str
    while i <= len do
        local b = byte(str, i)
        if b < 0x80 then
            i = i + 1
        elseif b < 0xE0 then
            i = i + 2
        elseif b < 0xF0 then
            i = i + 3
        else
            -- 4-byte sequence found (emoji, etc.)
            return false
        end
    end
    return true
end

-- Pre-allocated filter buffer (reused across calls)
local filterBuffer = {}
local filterBufferSize = 0

-- Filter out 4-byte UTF-8 sequences (emoji) - ONLY call if IsUTF8Safe() returns false
-- Reuses pre-allocated buffer to minimize allocations
function DataToColor:FilterUTF8(str)
    -- Wipe only used portion of buffer
    for j = 1, filterBufferSize do
        filterBuffer[j] = nil
    end
    filterBufferSize = 0

    local i = 1
    local len = #str
    local sub = string.sub
    while i <= len do
        local b = byte(str, i)
        if b < 0x80 then
            filterBufferSize = filterBufferSize + 1
            filterBuffer[filterBufferSize] = sub(str, i, i)
            i = i + 1
        elseif b < 0xE0 then
            filterBufferSize = filterBufferSize + 1
            filterBuffer[filterBufferSize] = sub(str, i, i + 1)
            i = i + 2
        elseif b < 0xF0 then
            filterBufferSize = filterBufferSize + 1
            filterBuffer[filterBufferSize] = sub(str, i, i + 2)
            i = i + 3
        else
            -- 4-byte sequence (emoji) - skip
            i = i + 4
        end
    end
    return table.concat(filterBuffer)
end

function DataToColor:MiniMapSettings1()
    local zoom = Minimap:GetZoom() or 0
    local zoomlevels = Minimap:GetZoomLevels() or 0
    local rotateMinimap = (GetCVar("rotateMinimap") == "1") and 1 or 0
    local width = floor(Minimap:GetWidth() or 0)

    -- Layout:
    -- bits 0-2  : zoom (0-7)
    -- bits 3-5  : zoomlevels (0-7)
    -- bit  6    : rotateMinimap
    -- bits 7-16 : width (0-1023)
    return bit.bor(
        band(zoom, 0x7),
        bit.lshift(band(zoomlevels, 0x7), 3),
        bit.lshift(rotateMinimap, 6),
        bit.lshift(band(width, 0x3FF), 7)
    )
end

function DataToColor:MiniMapSettings2()
    local screenW = GetScreenWidth()
    local screenH = GetScreenHeight()
    local left = Minimap:GetLeft() or 0
    local top = Minimap:GetTop() or 0
    local width = floor((Minimap:GetWidth() or 0) + 0.5)

    local offsetRight = floor(screenW - (left + width))
    local offsetTop = floor(screenH - top)

    -- Layout (bit-packed):
    -- bits 0-11  : offsetRight (0-4095)
    -- bits 12-23 : offsetTop (0-4095)
    return bit.bor(
        band(offsetRight, 0xFFF),
        bit.lshift(band(offsetTop, 0xFFF), 12)
    )
end