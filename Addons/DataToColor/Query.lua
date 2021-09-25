local Load = select(2, ...)
local DataToColor = unpack(Load)
local Range = DataToColor.Libs.RangeCheck

-- Global table of all items player has
local items = {}
local itemsPlaceholderComparison = {}
local enchantedItemsList = {}

-- Discover player's direction in radians (360 degrees = 2π radians)
function DataToColor:GetPlayerFacing()
    return GetPlayerFacing() or 0
end

-- Use Astrolabe function to get current player position
function DataToColor:GetCurrentPlayerPosition()
    local map = C_Map.GetBestMapForUnit(DataToColor.C.unitPlayer)
    if map ~= nil then
        local position = C_Map.GetPlayerMapPosition(map, DataToColor.C.unitPlayer)
        return position:GetXY()
    else
        return
    end
end

-- Base 2 converter for up to 24 boolean values to a single pixel square.
function DataToColor:Base2Converter()
    -- 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384
    return
    DataToColor:MakeIndexBase2(DataToColor:targetCombatStatus(), 0) +
    DataToColor:MakeIndexBase2(DataToColor:GetEnemyStatus(), 1) +
    DataToColor:MakeIndexBase2(DataToColor:deadOrAlive(), 2) +
    DataToColor:MakeIndexBase2(DataToColor:checkTalentPoints(), 3) +
    DataToColor:MakeIndexBase2(DataToColor:isTradeRange(), 4) +
    DataToColor:MakeIndexBase2(DataToColor:targetHostile(), 5) +
    DataToColor:MakeIndexBase2(DataToColor:IsPetVisible(), 6) +
    DataToColor:MakeIndexBase2(DataToColor:mainhandEnchantActive(), 7) +
    DataToColor:MakeIndexBase2(DataToColor:offhandEnchantActive(), 8) +
    DataToColor:MakeIndexBase2(DataToColor:GetInventoryBroken(), 9) +
    DataToColor:MakeIndexBase2(DataToColor:IsPlayerFlying(), 10) +
    DataToColor:MakeIndexBase2(DataToColor:IsPlayerSwimming(), 11) +
    DataToColor:MakeIndexBase2(DataToColor:petHappy(), 12) +
    DataToColor:MakeIndexBase2(DataToColor:hasAmmo(), 13) +
    DataToColor:MakeIndexBase2(DataToColor:playerCombatStatus(), 14) +
    DataToColor:MakeIndexBase2(DataToColor:IsTargetOfTargetPlayer(), 15) +
    DataToColor:MakeIndexBase2(DataToColor:IsAutoRepeatSpellOn(DataToColor.C.Spell.AutoShot), 16) +
    DataToColor:MakeIndexBase2(DataToColor:ProcessExitStatus(), 17) +
    DataToColor:MakeIndexBase2(DataToColor:IsPlayerMounted(), 18) +
    DataToColor:MakeIndexBase2(DataToColor:IsAutoRepeatSpellOn(DataToColor.C.Spell.Shoot), 19) +
    DataToColor:MakeIndexBase2(DataToColor:IsCurrentSpell(6603), 20) + -- AutoAttack enabled
    DataToColor:MakeIndexBase2(DataToColor:targetIsNormal(), 21)+
    DataToColor:MakeIndexBase2(DataToColor:IsTagged(), 22)
end

function DataToColor:getAuraMaskForClass(func, unitId, table)
    local num = 0
    for k, v in pairs(table) do
        for i = 1, 10 do
            local b = func(unitId, i)
            if b == nil then
                break
            end
            if string.find(b, v) then
                num = num + DataToColor:MakeIndexBase2(1, k)
                break
            end
        end
    end
    return num
end

-- Grabs current targets name
function DataToColor:GetTargetName(partition)
    if UnitExists(DataToColor.C.unitTarget) then
        local target = GetUnitName(DataToColor.C.unitTarget)
        target = DataToColor:StringToASCIIHex(target)
        if partition < 3 then
            return tonumber(string.sub(target, 0, 6))
        else if target > 999999 then
                return tonumber(string.sub(target, 7, 12))
            end
        end
    end
    return 0
end

function DataToColor:CastingInfoSpellId()
    local _, _, texture, _, _, _, _, _, spellID = CastingInfo()
    if spellID ~= nil then
        return spellID
    end
    if texture ~= nil then -- temp fix for tbc
        return texture
    end

    _, _, texture, _, _, _, _, spellID = ChannelInfo()
    if spellID ~= nil then
        return spellID
    end
    if texture ~= nil then -- temp fix for tbc
        return texture
    end

    return 0
end

--


function DataToColor:getUnitXP(unit)
    return UnitXP(unit)
end

function DataToColor:getUnitXPMax(unit)
    return UnitXPMax(unit)
end

-- Finds maximum amount of health player can have
function DataToColor:getHealthMax(unit)
    return UnitHealthMax(unit)
end
-- Finds axact amount of health player current has
function DataToColor:getHealthCurrent(unit)
    return UnitHealth(unit)
end

-- Finds maximum amount of mana a character can store
function DataToColor:getManaMax(unit)
    return UnitPowerMax(unit)
end

-- Finds exact amount of mana player is storing
function DataToColor:getManaCurrent(unit)
    return UnitPower(unit)
end

-- Finds player current level
function DataToColor:getPlayerLevel()
    return UnitLevel(DataToColor.C.unitPlayer)
end

function DataToColor:getTargetLevel()
    return UnitLevel(DataToColor.C.unitTarget)
end

-- Finds the total amount of money.
function DataToColor:getMoneyTotal()
    return GetMoney()
end

function DataToColor:targetHostile()
    local hostile = UnitReaction(DataToColor.C.unitPlayer, DataToColor.C.unitTarget)
    if hostile ~= nil and hostile <= 4 then
        return 1
    end
    return 0
end

local ammoSlot = GetInventorySlotInfo("AmmoSlot")
function DataToColor:hasAmmo()
    local ammoCount = GetInventoryItemCount(DataToColor.C.unitPlayer, ammoSlot)
    if ammoCount > 0 then
        return 1
    end
    return 0
end

function DataToColor:getRange()
    if UnitExists(DataToColor.C.unitTarget) then
        local min, max = Range:GetRange(DataToColor.C.unitTarget)
        if max == nil then
            max = 99
        end
        return min * 100000 + max * 100
    end
    return 0
end

function DataToColor:isTradeRange()
    if UnitExists(DataToColor.C.unitTarget) then
        if CheckInteractDistance(DataToColor.C.unitTarget, 2) then
            return 1
        end
    end
    return 0
end

function DataToColor:targetNpcId()
    local _, _, _, _, _, npcID, guid = strsplit('-', UnitGUID(DataToColor.C.unitTarget) or '')
    if npcID ~= nil then
        return tonumber(npcID)
    end
    return 0
end

function DataToColor:getGuid(src)
    local _, _, _, _, _, npcID, spawnUID = strsplit('-', UnitGUID(src) or '')
    if npcID ~= nil then
        return DataToColor:uniqueGuid(npcID, spawnUID)
    end
    return 0
end

function DataToColor:getGuidFromUUID(uuid)
    local _, _, _, _, _, npcID, spawnUID = strsplit('-', uuid or '')
    return DataToColor:uniqueGuid(npcID, spawnUID)
end

function DataToColor:uniqueGuid(npcId, spawn)
    local spawnEpochOffset = bit.band(tonumber(string.sub(spawn, 5), 16), 0x7fffff)
    local spawnIndex = bit.band(tonumber(string.sub(spawn, 1, 5), 16), 0xffff8)

    local dd = date("*t", spawnEpochOffset)
    local num = 
    DataToColor:sum24(
        dd.day +
        dd.hour +
        dd.min +
        dd.sec +
        npcId +
        spawnIndex
    )
    return tonumber(num, 16)
end

function DataToColor:actionbarCost(slot)
    if HasAction(slot) then
        local actionName, _
        local actionType, id = GetActionInfo(slot)
        if actionType == 'macro' then _, _ , id = GetMacroSpell(id) end
        if actionType == 'item' then
            actionName = GetItemInfo(id)
        elseif actionType == 'spell' or (actionType == 'macro' and id) then
            actionName = GetSpellInfo(id)
        end
        if actionName then
            local cost = 0
            local type = 0
            local costTable = GetSpellPowerCost(actionName)
            if costTable ~= nil then
                for key, costInfo in pairs(costTable) do
                    cost = costInfo.cost
                    type = costInfo.type
                    break
                end
            end
            --DataToColor:Print(button:GetName(), actionType, (GetSpellLink(id)), actionName, type, cost, id)
            return DataToColor.C.MAX_POWER_TYPE * type + DataToColor.C.MAX_ACTION_IDX * slot + cost
        end
    end
    return DataToColor.C.MAX_ACTION_IDX * slot
end

-- A function used to check which items we have.
-- Find item IDs on wowhead in the url: e.g: http://www.wowhead.com/item=5571/small-black-pouch. Best to confirm ingame if possible, though.
function DataToColor:itemName(bag, slot)
    local item
    local itemCount
    _, itemCount, _, _, _, _, _ = GetContainerItemInfo(bag, slot)
    -- If no item in the slot, returns nil. We assign this as zero for sake of pixel reading.
    if GetContainerItemLink(bag, slot) == nil then
        item = 0
        -- Formatting to isolate the ID in the ItemLink
    else _, _, item = string.find(GetContainerItemLink(bag, slot), DataToColor.C.ItemPattern)
        item = string.gsub(item, 'm:', '')
    end
    if item == nil then item = 0
    end
    if(itemCount ~= nil and itemCount > 0) then 
        if (itemCount>100) then itemCount=100 end
        item = item + itemCount * 100000
    end
    -- Sets global variable to current list of items
    items[(bag * 16) + slot] = item
    return tonumber(item)
end

function DataToColor:itemInfo(bag, slot)
    local itemCount
    _, itemCount, _, _, _, _, _ = GetContainerItemInfo(bag, slot)
    local value=0
    if itemCount ~= nil and itemCount > 0 then 
        local isSoulBound = C_Item.IsBound(ItemLocation:CreateFromBagAndSlot(bag,slot))
        if isSoulBound == true then value=1 end
    else
        value=2
    end
    return value
end

-- Returns item id from specific index in global items table
function DataToColor:returnItemFromIndex(index)
    return items[index]
end

function DataToColor:enchantedItems()
    if DataToColor:ValuesAreEqual(items, itemsPlaceholderComparison) then
    end
end



function DataToColor:equipName(slot)
    local equip
    if GetInventoryItemLink(DataToColor.C.unitPlayer, slot) == nil then
        equip = 0
    else _, _, equip = string.find(GetInventoryItemLink(DataToColor.C.unitPlayer, slot), DataToColor.C.ItemPattern)
        equip = string.gsub(equip, 'm:', '')
    end
    if equip == nil then
        equip = 0
    end
    return tonumber(equip)
end
-- -- Function to tell if a spell is on cooldown and if the specified slot has a spell assigned to it
-- -- Slot ID information can be found on WoW Wiki. Slots we are using: 1-12 (main action bar), Bottom Right Action Bar maybe(49-60), and  Bottom Left (61-72)

function DataToColor:areSpellsInRange()
    local inRange = 0
    for i = 1, table.getn(DataToColor.S.spellInRangeList), 1 do
        local isInRange = IsSpellInRange(DataToColor.S.spellInRangeList[i], DataToColor.C.unitTarget)
        if isInRange==1 then
            inRange = inRange + (2 ^ (i - 1))
        end
    end
    return inRange
end

function DataToColor:isActionUseable(min,max)
    local isUsableBits = 0
    -- Loops through main action bar slots 1-12
    local start, isUsable, notEnough
    for i = min, max do
        start = GetActionCooldown(i)
        isUsable, notEnough = IsUsableAction(i)
        if start == 0 and isUsable == true and notEnough == false then
            isUsableBits = isUsableBits + (2 ^ (i - min))
        end
    end
    return isUsableBits
end

-- Function to tell how many bag slots we have in each bag
function DataToColor:bagSlots(bag)
    return GetContainerNumSlots(bag)
end

-- Finds passed in string to return profession level
function DataToColor:GetProfessionLevel(skill)
    local numskills = GetNumSkillLines()
    for c = 1, numskills do
        local skillname, _, _, skillrank = GetSkillLineInfo(c)
        if(skillname == skill) then
            return tonumber(skillrank)
        end
    end
    return 0
end

-- Returns zone name
function DataToColor:GetZoneName(partition)
    local zone = DataToColor:StringToASCIIHex(GetZoneText())
    if zone and tonumber(string.sub(zone, 7, 12)) ~= nil then
        -- Returns first 3 characters of zone
        if partition < 3 then
            return tonumber(string.sub(zone, 0, 6))
            -- Returns characters 4-6 of zone
        elseif partition >= 3 then
            return tonumber(string.sub(zone, 7, 12))
        end
    end
    -- Fail safe to prevent nil
    return 1
end

function DataToColor:GetBestMap()
    return C_Map.GetBestMapForUnit(DataToColor.C.unitPlayer) or 0
end 

-- Game time on a 24 hour clock
function DataToColor:GameTime()
    local hours, minutes = GetGameTime()
    hours = (hours * 100) + minutes
    return hours
end

function DataToColor:GetGossipIcons()
    -- Checks if we have options available
    local option = GetGossipOptions()
    -- Checks if we have an active quest in the gossip window
    local activeQuest = GetGossipActiveQuests()
    -- Checks if we have a quest that we can pickup
    local availableQuest = GetGossipAvailableQuests()
    local gossipCode
    -- Code 0 if no gossip options are available
    if option == nil and activeQuest == nil and availableQuest == nil then
        gossipCode = 0
        -- Code 1 if only non quest gossip options are available
    elseif option ~= nil and activeQuest == nil and availableQuest == nil then
        gossipCode = 1
        -- Code 2 if only quest gossip options are available
    elseif option == nil and (activeQuest ~= nil or availableQuest) ~= nil then
        gossipCode = 2
        -- Code 3 if both non quest gossip options are available
    elseif option ~= nil and (activeQuest ~= nil or availableQuest) ~= nil then
        gossipCode = 3
        -- -- Error catcher
    else
        gossipCode = 0
    end
    return gossipCode
end

--return the x and y of corpse and resurrect the player if he is on his corpse
--the x and y is 0 if not dead
--runs the RetrieveCorpse() function to ressurrect
function DataToColor:CorpsePosition()
    if UnitIsGhost(DataToColor.C.unitPlayer) then
        local map = C_Map.GetBestMapForUnit(DataToColor.C.unitPlayer)
        if C_DeathInfo.GetCorpseMapPosition(map) ~= nil then
            return C_DeathInfo.GetCorpseMapPosition(map):GetXY()
        end
    end

    return 0, 0
end

function DataToColor:ComboPoints()
    return GetComboPoints(DataToColor.C.unitPlayer, DataToColor.C.unitTarget) or 0
end

-----------------------------------------------------------------
-- Boolean functions --------------------------------------------
-- Only put functions here that are part of a boolean sequence --
-- Sew BELOW for examples ---------------------------------------
-----------------------------------------------------------------

function DataToColor:mainhandEnchantActive() 
    local hasMainHandEnchant = GetWeaponEnchantInfo()
    return hasMainHandEnchant and 1 or 0
end

function DataToColor:offhandEnchantActive() 
    local _, _, _, _, hasOffHandEnchant = GetWeaponEnchantInfo()
    return hasOffHandEnchant and 1 or 0
end

-- Finds if player or target is in combat
function DataToColor:targetCombatStatus()
    return UnitAffectingCombat(DataToColor.C.unitTarget) and 1 or 0
end

-- Checks if target is dead. Returns 1 if target is dead, nil otherwise (converts to 0)
function DataToColor:GetEnemyStatus()
    return UnitIsDead(DataToColor.C.unitTarget) and 1 or 0
end

function DataToColor:targetIsNormal()
    local classification = UnitClassification(DataToColor.C.unitTarget)
    if classification == DataToColor.C.unitNormal then
        if (UnitIsPlayer(DataToColor.C.unitTarget)) then
            return 0
        end

        if UnitName(DataToColor.C.unitPet) == UnitName(DataToColor.C.unitTarget) then
            return 0
        end

        return 1
        -- if target is not in combat, return 1 for bitmask
    else
        return 0
    end
end

-- Checks if we are currently alive or are a ghost/dead.
function DataToColor:deadOrAlive()
    return UnitIsDeadOrGhost(DataToColor.C.unitPlayer) and 1 or 0
end

-- Checks the number of talent points we have available to spend
function DataToColor:checkTalentPoints()
    if UnitCharacterPoints(DataToColor.C.unitPlayer) > 0 then
        return 1
    end
    return 0
end

function DataToColor:shapeshiftForm()
    local form = GetShapeshiftForm(true)
    if form == nil then
        form = 0
    end
    return form
end

function DataToColor:playerCombatStatus()
    return UnitAffectingCombat(DataToColor.C.unitPlayer) and 1 or 0
end

-- Returns the slot in which we have a fully degraded item
function DataToColor:GetInventoryBroken()
    for i = 1, 18 do
        if GetInventoryItemBroken(DataToColor.C.unitPlayer, i) then
            return 1
        end
    end
    return 0
end
-- Checks if we are on a taxi
function DataToColor:IsPlayerFlying()
    return UnitOnTaxi(DataToColor.C.unitPlayer) and 1 or 0
end

function DataToColor:IsPlayerSwimming()
    return IsSwimming() and 1 or 0
end

function DataToColor:IsPlayerMounted()
    return IsMounted() and 1 or 0
end

function DataToColor:IsTargetOfTargetPlayerAsNumber()
    if not(UnitName(DataToColor.C.unitTargetTarget)) then return 2 end -- target has no target
    if DataToColor.C.CHARACTER_NAME == UnitName(DataToColor.C.unitTarget) then return 0 end -- targeting self
    if UnitName(DataToColor.C.unitPet) == UnitName(DataToColor.C.unitTargetTarget) then return 4 end -- targetting my pet
    if DataToColor.C.CHARACTER_NAME == UnitName(DataToColor.C.unitTargetTarget) then return 1 end -- targetting me
    if UnitName(DataToColor.C.unitPet) == UnitName(DataToColor.C.unitTarget) and UnitName(DataToColor.C.unitTargetTarget) ~= nil then return 5 end
    return 3
end

-- Returns true if target of our target is us
function DataToColor:IsTargetOfTargetPlayer()
    local x = DataToColor:IsTargetOfTargetPlayerAsNumber()
    if x==1 or x==4 then return 1 else return 0 end
end

function DataToColor:IsTagged()
    return UnitIsTapDenied(DataToColor.C.unitTarget) and 1 or 0
end

function DataToColor:IsAutoRepeatActionOn(actionSlot)
    return IsAutoRepeatAction(actionSlot) and 1 or 0
end

function DataToColor:IsAutoRepeatSpellOn(spell)
    return IsAutoRepeatSpell(spell) and 1 or 0
end

function DataToColor:IsCurrentSpell(spell)
    return IsCurrentSpell(spell) and 1 or 0
end

function DataToColor:IsCurrentActionOn(actionSlot)
    return IsCurrentAction(actionSlot) and 1 or 0
end

function DataToColor:IsPetVisible()
    if UnitIsVisible(DataToColor.C.unitPet) and not UnitIsDead(DataToColor.C.unitPet) then
        return 1
    end
    return 0
end

function DataToColor:petHappy()
    local happiness, damagePercentage, loyaltyRate = GetPetHappiness()
    -- (1 = unhappy, 2 = content, 3 = happy)
    if happiness ~= nil and happiness == 3 then
        return 1
    end
    return 0
end

-- Returns 0 if target is unskinnable or if we have no target.
function DataToColor:isUnskinnable()
    local creatureType = UnitCreatureType(DataToColor.C.unitTarget)
    -- Demons COULD be included in this list, but there are some skinnable demon dogs.
    if creatureType == DataToColor.C.Humanoid or creatureType == DataToColor.C.Elemental or creatureType == DataToColor.C.Mechanical or creatureType == DataToColor.C.Totem then
        return 1
    else if creatureType ~= nil then
            return 0
        end
    end
    return 1
end
