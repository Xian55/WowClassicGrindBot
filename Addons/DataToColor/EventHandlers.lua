local Load = select(2, ...)
local DataToColor = unpack(Load)

local ignoreErrorList = {
    "ERR_ABILITY_COOLDOWN",
    "ERR_OUT_OF_RAGE",
    "ERR_NO_ATTACK_TARGET",
    "ERR_OUT_OF_MANA",
    "ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS", 
    "ERR_SPELL_COOLDOWN", 
    "ERR_SPELL_FAILED_SHAPESHIFT_FORM_S",
    "ERR_GENERIC_NO_TARGET",
    "ERR_ATTACK_PREVENTED_BY_MECHANIC_S",
    "ERR_ATTACK_STUNNED",
    "ERR_NOEMOTEWHILERUNNING",
}

local errorList = {
    "ERR_BADATTACKFACING", --1
    "ERR_SPELL_FAILED_S", --2
    "ERR_SPELL_OUT_OF_RANGE", --3
    "ERR_BADATTACKPOS", --4
    "ERR_AUTOFOLLOW_TOO_FAR", --5
};

function DataToColor:RegisterEvents()
    DataToColor:RegisterEvent("UI_ERROR_MESSAGE", 'OnUIErrorMessage')
    DataToColor:RegisterEvent("COMBAT_LOG_EVENT_UNFILTERED", 'OnCombatEvent')
    DataToColor:RegisterEvent('LOOT_CLOSED','OnLootClosed')
    DataToColor:RegisterEvent('BAG_UPDATE','OnBagUpdate')
    DataToColor:RegisterEvent('MERCHANT_SHOW','OnMerchantShow')
    DataToColor:RegisterEvent('PLAYER_TARGET_CHANGED', 'OnPlayerTargetChanged')
end

function DataToColor:OnUIErrorMessage(event, messageType, message)
    local errorName = GetGameMessageInfo(messageType)

    local foundMessage=false;
    for i = 1, table.getn(ignoreErrorList), 1 do
        if ignoreErrorList[i]==errorName then
            foundMessage=true;
            UIErrorsFrame:AddMessage(message, 0.7, 0.7, 0.7) -- show as grey messasge
        end
    end

    if not foundMessage then
        for i = 1, table.getn(errorList), 1 do
            if errorList[i]==errorName then
                DataToColor.uiErrorMessage = i;
                foundMessage=true;
                UIErrorsFrame:AddMessage(message, 0, 1, 0) -- show as green messasge
            end
        end
    end

    if not foundMessage then
        UIErrorsFrame:AddMessage(message, 0, 0, 1) -- show as blue message (unknown message)
    end
end

function DataToColor:OnCombatEvent(event)
    local timestamp, eventType, _, sourceGUID, sourceName, _, _, destGUID, destName, _, _, spellId, spellName, spellSchool = CombatLogGetCurrentEventInfo();
    --print(CombatLogGetCurrentEventInfo())
    if eventType=="SPELL_PERIODIC_DAMAGE" then
        DataToColor.lastCombatCreature=0;
    elseif string.find(sourceGUID, "Creature") then
        DataToColor.lastCombatCreature = DataToColor:getGuidFromUUID(sourceGUID);
        DataToColor.lastCombatDamageDealerCreature = DataToColor.lastCombatCreature;
        --print(sourceGUID.." "..lastCombatCreature);
    else
        DataToColor.lastCombatCreature=0;
        --print("Other "..eventType);
    end

    if eventType=="UNIT_DIED" then
        if string.find(destGUID, "Creature") then
            DataToColor.lastCombatCreatureDied = DataToColor:getGuidFromUUID(destGUID);
            --print("v_killing blow " .. destGUID .. " " .. lastCombatCreatureDied .. " " .. destName)
        else
            --print("i_killing blow " .. destGUID .. " " .. destName)
        end
    end
end

function DataToColor:OnLootClosed(event)
    DataToColor.lastLoot = DataToColor.globalTime
    DataToColor.inventoryChanged = true
    --DataToColor:Print("OnLootClosed:"..DataToColor.lastLoot)
end

function DataToColor:OnBagUpdate(event, containerID)
    DataToColor.inventoryChanged = true
    --DataToColor:Print("OnBagUpdate "..containerID)
end

function DataToColor:OnMerchantShow(event)
    
    TotalPrice = 0
    for myBags = 0,4 do
        for bagSlots = 1, GetContainerNumSlots(myBags) do
            CurrentItemLink = GetContainerItemLink(myBags, bagSlots)
                if CurrentItemLink then
                    _, _, itemRarity, _, _, _, _, _, _, _, itemSellPrice = GetItemInfo(CurrentItemLink)
                    _, itemCount = GetContainerItemInfo(myBags, bagSlots)
                    if itemRarity == 0 and itemSellPrice ~= 0 then
                        TotalPrice = TotalPrice + (itemSellPrice * itemCount);
                        DataToColor:Print("Selling: "..itemCount.." "..CurrentItemLink.." for "..GetCoinTextureString(itemSellPrice * itemCount));
                        UseContainerItem(myBags, bagSlots)
                    end
                end
        end
    end
    if TotalPrice ~= 0 then
        DataToColor:Print("Total Price for all items: " .. GetCoinTextureString(TotalPrice))
    else
        DataToColor:Print("No grey items were sold.")
    end
end

function DataToColor:OnPlayerTargetChanged(event)
    DataToColor.targetChanged = true
end

DataToColor.playerInteractIterator = 0

DATA_CONFIG = {
    ACCEPT_PARTY_REQUESTS = false, -- O
    DECLINE_PARTY_REQUESTS = false, -- O
    AUTO_REPAIR_ITEMS = true, -- O
    AUTO_LEARN_TALENTS = false, -- O
    AUTO_TRAIN_SPELLS = false, -- O
    AUTO_RESURRECT = true,
    SELL_WHITE_ITEMS = true
}

-- List of talents that will be trained
local talentList = {
    "Improved Frostbolt",
    "Ice Shards",
    "Frostbite",
    "Piercing Ice",
    "Improved Frost Nova",
    "Shatter",
    "Arctic Reach",
    "Ice Block",
    "Ice Barrier",
    "Winter's Chill",
    "Frost Channeling",
    "Frost Warding",
    "Elemental Precision",
    "Permafrost",
    "Improved Fireball",
    "Improved Fire Blast"
}

local CORPSE_RETRIEVAL_DISTANCE = 40

-----------------------------------------------------------------------------
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-----------------------------------------------------------------------------
function DataToColor:HandlePlayerInteractionEvents()
    -- Handles group accept/decline
    if DATA_CONFIG.ACCEPT_PARTY_REQUESTS or DATA_CONFIG.DECLINE_PARTY_REQUESTS then
        DataToColor:HandlePartyInvite()
    end
    -- Handles item repairs when talking to item repair NPC
    if DATA_CONFIG.AUTO_REPAIR_ITEMS then
        DataToColor:RepairItems()
    end
    -- Handles learning talents, only works after level 10
    if DATA_CONFIG.AUTO_LEARN_TALENTS then
        DataToColor:LearnTalents()
    end
    -- Handles train new spells and talents
    if DATA_CONFIG.AUTO_TRAIN_SPELLS then
        DataToColor:CheckTrainer()  
    end
    -- Resurrect player
    if DATA_CONFIG.AUTO_RESURRECT then
        DataToColor:ResurrectPlayer()
    end

    DataToColor:IncrementIterator();
end

-- Declines/Accepts Party Invites.
function DataToColor:HandlePartyInvite()
    -- Declines party invite if configured to decline
    if DATA_CONFIG.DECLINE_PARTY_REQUESTS then
        DeclineGroup()
    else if DATA_CONFIG.ACCEPT_PARTY_REQUESTS then
            AcceptGroup()
        end
    end
    -- Hides the party invite pop-up regardless of whether we accept it or not
    StaticPopup_Hide("PARTY_INVITE")
end

-- Repairs items if they are broken
function DataToColor:RepairItems()
    if CanMerchantRepair() and GetRepairAllCost() > 0 then
        if GetMoney() >= GetRepairAllCost() then
            RepairAllItems()
        end
    end
end

-- Automatically learns predefined talents
function DataToColor:LearnTalents()
    if UnitCharacterPoints(DataToColor.C.unitPlayer) > 0 then
        -- Grabs global list of talents we want to learn
        for i = 0, table.getn(talentList), 1 do
            -- Iterates through each talent tab (e.g. "Arcane, Fire, Frost")
            for j = 0, 3, 1 do
                -- Loops through all of the talents in each individual tab
                for k = 1, GetNumTalents(j), 1 do
                    -- Grabs API info of a specified talent index
                    local name, iconPath, tier, column, currentRank, maxRank, isExceptional, meetsPrereq, previewRank, meetsPreviewPrereq = GetTalentInfo(j, k)
                    local tabId, tabName, tabPointsSpent, tabDescription, tabIconTexture = GetTalentTabInfo(j)
                    local _, _, isLearnable = GetTalentPrereqs(j, k)
                    -- DEFAULT_CHAT_FRAME:AddMessage("hello" .. tier)
                    -- Runs API call to learn specified talent. Skips over it if we already have the max rank.
                    if name == talentList[i] and currentRank ~= maxRank and meetsPrereq then
                        -- Checks if we have spent enough points in the prior tiers in order to purchase talent. Otherwie moves on to next possible spell
                        if tabPointsSpent ~= nil and tabPointsSpent >= (tier * 5) - 5 then
                            LearnTalent(j, k)
                            return
                        end
                    end
                end
            end
        end
    end
end

-- List desired spells and professions to be trained here.
function ValidSpell(spell)
    local spellList = {
        "Conjure Food",
        "Conjure Water",
        "Conjure Mana Ruby",
        "Mana Shield",
        "Arcane Intellect",
        "Fire Blast",
        "Fireball",
        "Frostbolt",
        "Counterspell",
        "Ice Barrier",
        "Evocation",
        "Frost Armor",
        "Frost Nova",
        "Ice Armor",
        "Remove Lesser Curse",
        "Blink",
        "Apprentice Skinning",
        "Journeyman Skinning",
        "Expert Skinning",
        "Artisan Skinning",
        "Apprentice Fishing",
        "Journeyman Fishing"
    }
    -- Loops through all spells to see if we have a matching spells with the one passed in
    for i = 0, table.getn(spellList), 1 do
        if spellList[i] == spell then
            return true
        end
    end
    return false
end

function DataToColor:IncrementIterator()
    DataToColor.playerInteractIterator = DataToColor.playerInteractIterator + 1
end 

-- Used purely for training spells and professions
function DataToColor:CheckTrainer()
    DataToColor.playerInteractIterator = DataToColor.playerInteractIterator + 1
    if DataToColor:Modulo(DataToColor.playerInteractIterator, 30) == 1 then
        -- First checks that the trainer gossip window is open
        -- DEFAULT_CHAT_FRAME:AddMessage(GetTrainerServdiceInfo(1))
        if GetTrainerServiceInfo(1) ~= nil and DATA_CONFIG .AUTO_TRAIN_SPELLS then
            -- LPCONFIG.AUTO_TRAIN_SPELLS = false
            local allAvailableOptions = GetNumTrainerServices()
            local money = GetMoney()
            local level = UnitLevel(DataToColor.C.unitPlayer)
            
            -- Loops through every spell on the list and checks if we
            -- 1) Have the level to train that spell
            -- 2) Have the money want to train that spell
            -- 3) Want to train that spell
            for i = 1, allAvailableOptions, 1 do
                local spell = GetTrainerServiceInfo(i)
                if spell ~= nil and ValidSpell(spell) then
                    if GetTrainerServiceLevelReq(i) <= level then
                        if GetTrainerServiceCost(i) <= money then
                            -- DEFAULT_CHAT_FRAME:AddMessage(" buying spell" .. tostring(i) )
                            BuyTrainerService(i)
                            -- Closes skinning trainer, fishing trainer menu, etc.
                            -- Closes after one profession purchase. Impossible to buy profession skills concurrently.
                            if IsTradeskillTrainer() then
                                CloseTrainer()
                                -- LPCONFIG.AUTO_TRAIN_SPELLS = true
                            end
                            -- DEFAULT_CHAT_FRAME:AddMessage(allAvailableOptions .. tostring(i) )
                            -- if not (allAvailableOptions == i) then
                            -- TrainSpells()
                            return
                            -- end
                            -- An error messages for the rare case where we don't have enough money for a spell but have the level for it.
                        else if GetTrainerServiceCost(i) > money then
                            end
                        end
                    end
                end
            end
            -- DEFAULT_CHAT_FRAME:AddMessage('between')
            -- Automatically closes menu after we have bought all spells we need to buy
            --CloseTrainer()
            -- LPCONFIG.AUTO_TRAIN_SPELLS = true
        end
    end
end

--the x and y is 0 if not dead
--runs the RetrieveCorpse() function to ressurrect
function DataToColor:ResurrectPlayer()
    if DataToColor:Modulo(DataToColor.playerInteractIterator, 700) == 1 then
        if UnitIsDeadOrGhost(DataToColor.C.unitPlayer) then
            
            -- Accept Release Spirit immediately after dying
            if not UnitIsGhost(DataToColor.C.unitPlayer) and UnitIsGhost(DataToColor.C.unitPlayer) ~= nil then
                RepopMe()
            end
            if UnitIsGhost(DataToColor.C.unitPlayer) then
                local map = C_Map.GetBestMapForUnit(DataToColor.C.unitPlayer)
                if C_DeathInfo.GetCorpseMapPosition(map) ~= nil then
                    local cX, cY = C_DeathInfo.GetCorpseMapPosition(map):GetXY()
                    local x, y = DataToColor:GetCurrentPlayerPosition()
                    -- Waits so that we are in range of specified retrieval distance, and ensures there is no delay timer before attemtping to resurrect
                    if math.abs(cX - x) < CORPSE_RETRIEVAL_DISTANCE / 1000 and math.abs(cY - y) < CORPSE_RETRIEVAL_DISTANCE / 1000 and GetCorpseRecoveryDelay() == 0 then
                        DEFAULT_CHAT_FRAME:AddMessage('Attempting to retrieve corpse')
                        -- Accept Retrieve Corpsse when near enough
                        RetrieveCorpse()
                    end
                end
            end
        end
    end
end
