----------------------------------------------------------------------------
--  DataToColor - display player position as color
----------------------------------------------------------------------------

local Load = select(2, ...)
local DataToColor = unpack(Load)

-- Trigger between emitting game data and frame location data
SETUP_SEQUENCE = false
-- Exit process trigger
EXIT_PROCESS_STATUS = 0
-- Total number of data frames generated
local NUMBER_OF_FRAMES = 100
-- Set number of pixel rows
local FRAME_ROWS = 1
-- Size of data squares in px. Varies based on rounding errors as well as dimension size. Use as a guideline, but not 100% accurate.
local CELL_SIZE = 1 -- 1-9 
-- Spacing in px between data squares.
local CELL_SPACING = 1 -- 0 or 1
-- Item slot trackers initialization
local itemNum = 0
local equipNum = 0
local actionNum = 1
local bagNum = -1
local globalCounter = 0

-- How often item frames change
local ITEM_ITERATION_FRAME_CHANGE_RATE = 6
-- How often the actionbar frames change
local ACTION_BAR_ITERATION_FRAME_CHANGE_RATE = 5

-- Action bar configuration for which spells are tracked
local MAIN_MIN = 1
local MAIN_MAX = 12
local BOTTOM_LEFT_MIN = 61
local BOTTOM_LEFT_MAX = 72

-- Timers
DataToColor.timeUpdateSec = 0.1
DataToColor.globalTime = 0
DataToColor.lastLoot = 0

DataToColor.frames = nil
DataToColor.r = 0

DataToColor.uiErrorMessage = 0
DataToColor.lastCombatDamageDealerCreature = 0
DataToColor.lastCombatCreature = 0
DataToColor.lastCombatCreatureDied = 0

DataToColor.targetChanged = true
DataToColor.inventoryChanged = true
DataToColor.equipmentChanged = true

DataToColor.updateActionBarCost = true

-- Note: Coordinates where player is standing (max: 10, min: -10)
-- Note: Player direction is in radians (360 degrees = 2π radians)
-- Note: Player health/mana is taken out of 100% (0 - 1)

function DataToColor:RegisterSlashCommands()
    DataToColor:RegisterChatCommand('dc', 'StartSetup')
    DataToColor:RegisterChatCommand('dccpu', 'GetCPUImpact')
    DataToColor:RegisterChatCommand('dcflush', 'FushState')
end

function DataToColor:StartSetup()
    if not SETUP_SEQUENCE then
        SETUP_SEQUENCE = true
    else
        SETUP_SEQUENCE = false
    end
end

function DataToColor:Print(...)
	(_G.DEFAULT_CHAT_FRAME):AddMessage(strjoin('', '|cff00b3ff', 'DataToColor:|r ', ...)) -- I put DEFAULT_CHAT_FRAME as a fail safe.
end

function DataToColor:error(msg)
    DataToColor:log("|cff0000ff" .. msg .. "|r")
    DataToColor:log(msg)
    DataToColor:log(debugstack())
    error(msg)
end


-- This function runs when addon is initialized/player logs in
-- Decides length of white box
function DataToColor:OnInitialize()
    DataToColor:SetupRequirements()
    DataToColor:CreateFrames(NUMBER_OF_FRAMES)
    DataToColor:RegisterSlashCommands()

    DataToColor:InitStorage()

    -- handle error events
    UIErrorsFrame:UnregisterEvent("UI_ERROR_MESSAGE")

    DataToColor:RegisterEvents()
    DataToColor:UpdateTimer()

    local version = GetAddOnMetadata('DataToColor', 'Version')
    DataToColor:Print("Welcome. Using "..version)
end

function DataToColor:SetupRequirements()
    SetCVar("autoInteract", 1);
    SetCVar("autoLootDefault", 1)
    -- /run SetCVar("cameraSmoothStyle", 2) --always
	SetCVar('Contrast',50,'[]')
	SetCVar('Brightness',50,'[]')
	SetCVar('Gamma',1,'[]')
end

function DataToColor:Update()
    DataToColor.globalTime = DataToColor.globalTime + 1
    if DataToColor.globalTime > (256 * 256 * 256 - 1) then
        DataToColor.globalTime = 0
    end
    --DataToColor:Print(DataToColor.globalTime)
end

local UpdateFuncCache={};
function DataToColor:UpdateTimer()
    DataToColor:Update()

    local func = UpdateFuncCache[self]
    if not func then
        func = function() DataToColor:UpdateTimer(); end;
        UpdateFuncCache[self] = func;
    end
    C_Timer.After(DataToColor.timeUpdateSec, func);
end


function DataToColor:FushState()
    DataToColor.targetChanged = true
    DataToColor.inventoryChanged = true
    DataToColor.equipmentChanged = true
    DataToColor.updateActionBarCost = true

    DataToColor:Print('Flush State')
end

function DataToColor:ConsumeChanges()
    if DataToColor.targetChanged then
        DataToColor.targetChanged = false
    end
end


local valueCache = {}
-- Function to mass generate all of the initial frames for the pixel reader
function DataToColor:CreateFrames(n)
    -- Note: Use single frame and update color on game update call
    local function UpdateFrameColor(f)
        -- Apply color to backdrop
        function MakePixelSquareArr(col, slot)
            DataToColor.frames[slot + 1]:SetBackdropColor(col[1], col[2], col[3], 1)
        end

        -- DataToColor:integerToColor
        function MakePixelSquareArrI(value, slot)
            if valueCache[slot + 1].last ~= value then
                valueCache[slot + 1].last = value
                MakePixelSquareArr(DataToColor:integerToColor(value), slot)
            end
        end

        -- DataToColor:fixedDecimalToColor
        function MakePixelSquareArrF(value, slot)
            if valueCache[slot + 1].last ~= value then
                valueCache[slot + 1].last = value
                MakePixelSquareArr(DataToColor:fixedDecimalToColor(value), slot)
            end
        end

        if not SETUP_SEQUENCE then
            MakePixelSquareArrI(0, 0)
            -- The final data square, reserved for additional metadata.
            MakePixelSquareArrI(2000001, NUMBER_OF_FRAMES - 1)

            -- Position related variables --
            local x, y = DataToColor:GetCurrentPlayerPosition()
            if x == nil or y == nil then
                x = 0
                y = 0
            end

            MakePixelSquareArrF(x * 10, 1) --1 The x-coordinate
            MakePixelSquareArrF(y * 10, 2) --2 The y-coordinate

            MakePixelSquareArrF(DataToColor:GetPlayerFacing(), 3) --3 The direction the player is facing in radians
            MakePixelSquareArrI(DataToColor:GetZoneName(0), 4) -- Get name of first 3 characters of zone
            MakePixelSquareArrI(DataToColor:GetZoneName(3), 5) -- Get name of last 3 characters of zone

            x, y = DataToColor:CorpsePosition()

            MakePixelSquareArrF(x * 10, 6) -- Returns the x coordinates of corpse
            MakePixelSquareArrF(y * 10, 7) -- Return y coordinates of corpse

            -- Boolean variables --
            MakePixelSquareArrI(DataToColor:Base2Converter(), 8)

            -- Start combat/NPC related variables --
            MakePixelSquareArrI(DataToColor:getHealthMax(DataToColor.C.unitPlayer), 10) --8 Represents maximum amount of health
            MakePixelSquareArrI(DataToColor:getHealthCurrent(DataToColor.C.unitPlayer), 11) --9 Represents current amount of health
            MakePixelSquareArrI(DataToColor:getManaMax(DataToColor.C.unitPlayer), 12) --10 Represents maximum amount of mana
            MakePixelSquareArrI(DataToColor:getManaCurrent(DataToColor.C.unitPlayer), 13) --11 Represents current amount of mana
            MakePixelSquareArrI(DataToColor:getPlayerLevel(), 14) --12 Represents character level
            MakePixelSquareArrI(DataToColor:getRange(), 15) -- 15 Represents if target is within 0-5 5-15 15-20, 20-30, 30-35, or greater than 35 yards

            if DataToColor.targetChanged then
                MakePixelSquareArrI(DataToColor:GetTargetName(0), 16) -- Characters 1-3 of target's name
                MakePixelSquareArrI(DataToColor:GetTargetName(3), 17) -- Characters 4-6 of target's name

                MakePixelSquareArrI(DataToColor:getHealthMax(DataToColor.C.unitTarget), 18) -- Return the maximum amount of health a target can have
            end

            MakePixelSquareArrI(DataToColor:getHealthCurrent(DataToColor.C.unitTarget), 19) -- Returns the current amount of health the target currently has

            -- Begin Items section --
            -- there are 5 item slots: main backpack and 4 pouches
            -- Indexes one slot from each bag each frame. SlotN (1-16) and bag (0-4) calculated here:
            if DataToColor:Modulo(globalCounter, ITEM_ITERATION_FRAME_CHANGE_RATE) == 0 then
                if DataToColor.inventoryChanged then
                    itemNum = itemNum + 1
                    bagNum = bagNum + 1
                end

                if DataToColor.equipmentChanged then
                    equipNum = equipNum + 1
                end

                if itemNum >= 21 then
                    itemNum = 1

                    if bagNum >= 5 then
                        DataToColor.inventoryChanged = false
                    end
                end

                if bagNum >= 5 then
                    bagNum = 0
                end

                -- Worn inventory start.
                -- Starts at beginning once we have looked at all desired slots.
                if equipNum > 24 then
                    equipNum = 1
                    DataToColor.equipmentChanged = false
                end

                -- Reseting global counter to prevent integer overflow
                if globalCounter > 10000 then
                    globalCounter = 1000
                end
            end

            if DataToColor.updateActionBarCost then
                if DataToColor:Modulo(globalCounter, ACTION_BAR_ITERATION_FRAME_CHANGE_RATE) == 0 then
                    actionNum = actionNum + 1
                    if actionNum >= 84 then
                        actionNum = 1
                        DataToColor.updateActionBarCost = false
                    end
                end
            end
            -- Controls rate at which item frames change.
            globalCounter = globalCounter + 1

            if DataToColor.inventoryChanged then
                -- Bag contents - Uses data pixel positions 20-29
                for bagNo = 0, 4 do
                    -- Returns item ID and quantity
                    MakePixelSquareArrI(DataToColor:itemName(bagNo, itemNum), 20 + bagNo * 2) -- 20,22,24,26,28
                    -- Return item slot number
                    MakePixelSquareArrI(bagNo * 20 + itemNum, 21 + bagNo * 2) -- 21,23,25,27,29
                    MakePixelSquareArrI(DataToColor:itemInfo(bagNo, itemNum), 60 + bagNo) -- 60,61,62,63,64
                end
            end

            if DataToColor.equipmentChanged then
                local equipName = DataToColor:equipName(equipNum)
                -- Equipment ID
                MakePixelSquareArrI(equipName, 30)
                -- Equipment slot
                MakePixelSquareArrI(equipNum, 31)
            end

            -- Amount of money in coppers
            MakePixelSquareArrI(DataToColor:Modulo(DataToColor:getMoneyTotal(), 1000000), 32) -- 13 Represents amount of money held (in copper)
            MakePixelSquareArrI(floor(DataToColor:getMoneyTotal() / 1000000), 33) -- 14 Represents amount of money held (in gold)  

            -- Start main action page (page 1)
            MakePixelSquareArrI(DataToColor:isActionUseable(1, 24), 34)
            MakePixelSquareArrI(DataToColor:isActionUseable(25, 48), 35)
            MakePixelSquareArrI(DataToColor:isActionUseable(49, 72), 36)
            MakePixelSquareArrI(DataToColor:isActionUseable(73, 96), 42)

            if DataToColor.inventoryChanged then
                local freeSlots, bagType = GetContainerNumFreeSlots(bagNum)
                if bagType == nil then
                    bagType = 0
                end
                MakePixelSquareArrI(bagType * 1000000 + bagNum * 100000 + freeSlots * 1000 + DataToColor:bagSlots(bagNum), 37) -- BagType + Index + FreeSpace + BagSlots
            end

            MakePixelSquareArrI(DataToColor:getHealthMax(DataToColor.C.unitPet), 38)
            MakePixelSquareArrI(DataToColor:getHealthCurrent(DataToColor.C.unitPet), 39)
            -- 40

            -- Profession levels:
            -- tracks our skinning level
            --MakePixelSquareArr(DataToColor:integerToColor(DataToColor:GetProfessionLevel("Skinning")), 41) -- Skinning profession level
            -- tracks our fishing level
            --MakePixelSquareArr(DataToColor:integerToColor(DataToColor:GetProfessionLevel("Fishing")), 42) -- Fishing profession level
            MakePixelSquareArrI(DataToColor:getAuraMaskForClass(UnitBuff, DataToColor.C.unitPlayer, DataToColor.S.playerBuffs), 41);
            -- 42 used by keys
            
            MakePixelSquareArrI(DataToColor:getTargetLevel(), 43)

            MakePixelSquareArrI(DataToColor:actionbarCost(actionNum), 44)
            --MakePixelSquareArrI(DataToColor:GetGossipIcons(), 45) -- Returns which gossip icons are on display in dialogue box

            MakePixelSquareArrI(DataToColor.S.PlayerClass, 46) -- Returns player class as an integer
            MakePixelSquareArrI(DataToColor:isUnskinnable(), 47) -- Returns 1 if creature is unskinnable
            MakePixelSquareArrI(DataToColor:shapeshiftForm(), 48) -- Shapeshift id https://wowwiki.fandom.com/wiki/API_GetShapeshiftForm
            MakePixelSquareArrI(DataToColor:areSpellsInRange(), 49) -- Are spells in range

            MakePixelSquareArrI(DataToColor:getUnitXP(DataToColor.C.unitPlayer), 50) -- Player Xp
            MakePixelSquareArrI(DataToColor:getUnitXPMax(DataToColor.C.unitPlayer), 51) -- Player Level Xp
            MakePixelSquareArrI(DataToColor.uiErrorMessage, 52) -- Last UI Error message
            DataToColor.uiErrorMessage=0;

            MakePixelSquareArrI(DataToColor:CastingInfoSpellId(), 53) -- Spell being cast
            MakePixelSquareArrI(DataToColor:ComboPoints(), 54) -- Combo points for rogue / druid
            MakePixelSquareArrI(DataToColor:getAuraMaskForClass(UnitDebuff, DataToColor.C.unitTarget, DataToColor.S.targetDebuffs), 55); -- target debuffs

            if DataToColor.targetChanged then
                MakePixelSquareArrI(DataToColor:targetNpcId(), 56) -- target id
                MakePixelSquareArrI(DataToColor:getGuid(DataToColor.C.unitTarget),57) -- target reasonably uniqueId
            end

            MakePixelSquareArrI(DataToColor:GetBestMap(),58) -- MapId

            MakePixelSquareArrI(DataToColor:IsTargetOfTargetPlayerAsNumber(),59) -- IsTargetOfTargetPlayerAsNumber
            -- 60-64 = Bag item info
            MakePixelSquareArrI(DataToColor.lastCombatCreature,65) -- Combat message creature
            MakePixelSquareArrI(DataToColor.lastCombatDamageDealerCreature,66) -- Combat message last damage dealer creature
            MakePixelSquareArrI(DataToColor.lastCombatCreatureDied,67) -- Last Killed Unit

            MakePixelSquareArrI(DataToColor:getGuid(DataToColor.C.unitPet),68) -- pet guid
            MakePixelSquareArrI(DataToColor:getGuid(DataToColor.C.unitPetTarget),69) -- pet target

            -- Timers
            MakePixelSquareArrI(DataToColor.globalTime, 70)
            MakePixelSquareArrI(DataToColor.lastLoot, 71)

            DataToColor:ConsumeChanges()

            DataToColor:HandlePlayerInteractionEvents()

            DataToColor:Update()
        end

        if SETUP_SEQUENCE then
            -- Emits meta data in data square index 0 concerning our estimated cell size, number of rows, and the numbers of frames
            MakePixelSquareArrI(CELL_SPACING * 10000000 + CELL_SIZE * 100000 + 1000 * FRAME_ROWS + NUMBER_OF_FRAMES, 0)
            -- Assign pixel squares a value equivalent to their respective indices.
            for i = 1, NUMBER_OF_FRAMES - 1 do
                MakePixelSquareArrI(i, i)
            end
        end
    end
    -- Function used to generate a single frame
    local function setFramePixelBackdrop(f)
        f:SetBackdrop({
            bgFile = "Interface\\AddOns\\DataToColor\\white.tga",
            insets = {top = 0, left = 0, bottom = 0, right = 0},
        })
    end
    
    local function genFrame(name, x, y)
        local f = CreateFrame("Frame", name, UIParent, BackdropTemplateMixin and "BackdropTemplate") or CreateFrame("Frame", name, UIParent)
        f:SetPoint("TOPLEFT", x * (CELL_SIZE + CELL_SPACING), -y * (CELL_SIZE + CELL_SPACING))
        f:SetHeight(CELL_SIZE)
        f:SetWidth(CELL_SIZE) -- Change this to make white box wider
        setFramePixelBackdrop(f)
        f:SetFrameStrata("DIALOG")
        f:SetBackdropColor(0, 0, 0, 1)
        return f
    end
    
    n = n or 0
    
    local frame = 1 -- try 1
    local frames = {}
    
    -- background frame
    local backgroundframe = genFrame("frame_0", 0, 0)
    backgroundframe:SetHeight(FRAME_ROWS * (CELL_SIZE + CELL_SPACING))
    backgroundframe:SetWidth(floor(n / FRAME_ROWS) * (CELL_SIZE + CELL_SPACING))
    backgroundframe:SetFrameStrata("HIGH")
    backgroundframe:SetBackdropColor(0, 0, 0, 1)
    
    -- Note: Use for loop based on input to generate "n" number of frames
    for frame = 0, n - 1 do
        local y = DataToColor:Modulo(frame, FRAME_ROWS) -- those are grid coordinates (1,2,3,4 by  1,2,3,4 etc), not pixel coordinates
        local x = floor(frame / FRAME_ROWS)
        -- Put frame information in to an object/array
        frames[frame + 1] = genFrame("frame_"..tostring(frame), x, y)
        valueCache[frame + 1] = { last = -1 }
    end
    
    -- Assign DataToColor.frames to frame list generated above
    DataToColor.frames = frames
    DataToColor.frames[1]:SetScript("OnUpdate", function() UpdateFrameColor(f) end)
end

function DataToColor:delete(items)
    for b=0,4 do for s=1,GetContainerNumSlots(b) 
        do local n=GetContainerItemLink(b,s) 
            if n then
                for i = 1, table.getn(items), 1 do
                    if strfind(n,items[i]) then
                        DataToColor:Print("Delete: " .. items[i]);
                        PickupContainerItem(b,s);
                        DeleteCursorItem();
                    end
                end
            end
        end
    end
end

function DataToColor:sell(items)

    local target = GetUnitName(DataToColor.C.unitTarget)
    if target ~= nil then
        local item= GetMerchantItemLink(1);

        if  item ~= nil then
            DataToColor:Print("Selling items...");

            DataToColor:OnMerchantShow();

            TotalPrice = 0

            for b=0,4 do for s=1,GetContainerNumSlots(b) 
                do local CurrentItemLink=GetContainerItemLink(b,s) 
                    if CurrentItemLink then
                        for i = 1, table.getn(items), 1 do
                            if strfind(CurrentItemLink,items[i]) then
                                _, _, itemRarity, _, _, _, _, _, _, _, itemSellPrice = GetItemInfo(CurrentItemLink);
                                if (itemRarity<2) then
                                    _, itemCount = GetContainerItemInfo(b, s);
                                    TotalPrice = TotalPrice + (itemSellPrice * itemCount);
                                    DataToColor:Print("Selling: "..itemCount.." "..CurrentItemLink.." for "..GetCoinTextureString(itemSellPrice * itemCount));
                                    UseContainerItem(b,s);
                                else
                                    DataToColor:Print("Item is not gray or common, not selling it: " .. items[i]);
                                end
                            end
                        end
                    end
                end
            end

            if TotalPrice ~= 0 then
                DataToColor:Print("Total Price for all items: " .. GetCoinTextureString(TotalPrice))
            else
                DataToColor:Print("No grey items were sold.")
            end

        else
            DataToColor:Print("Merchant is not open to sell to, please approach and open.");
        end
    else
        DataToColor:Print("Merchant is not targetted.");
    end
end


-- A variable which can trigger a process exit on the node side with this macro:
-- /script EXIT_PROCESS_STATfort = 1
function DataToColor:ProcessExitStatus()
    -- Check if a process exit has been requested
    if EXIT_PROCESS_STATUS == 1 then
        -- If a process exit has been requested, resets global frame tracker to zero in order to give node time to read frames
        if globalCounter > 200 then
            DataToColor:log('Manual exit request processing...')
            globalCounter = 0
        end
    end
    -- Number of frames until EXIT_PROCESS_STATUS returns to false so that node process can begin again
    if globalCounter > 100 and EXIT_PROCESS_STATUS ~= 0 then
        EXIT_PROCESS_STATUS = 0
    end
    return EXIT_PROCESS_STATUS
end

-- List of possible subzones to which a player's hearthstone may be bound
local HearthZoneList = {"CENARION HOLD", "VALLEY OF TRIALS", "THE CROSSROADS", "RAZOR HILL", "DUROTAR", "ORGRIMMAR", "CAMP TAURAJO", "FREEWIND POST", "GADGETZAN", "SHADOWPREY VILLAGE", "THUNDER BLUFF", "UNDERCITY", "CAMP MOJACHE", "COLDRIDGE VALLEY", "DUN MOROGH", "THUNDERBREW DISTILLERY", "IRONFORGE", "STOUTLAGER INN", "STORMWIND CITY", "SOUTHSHORE", "LAKESHIRE", "STONETALON PEAK", "GOLDSHIRE", "SENTINEL HILL", "DEEPWATER TAVERN", "THERAMORE ISLE", "DOLANAAR", "ASTRANAAR", "NIJEL'S POINT", "CRAFTSMEN'S TERRACE", "AUBERDINE", "FEATHERMOON STRONGHOLD", "BOOTY BAY", "WILDHAMMER KEEP", "DARKSHIRE", "EVERLOOK", "RATCHET", "LIGHT'S HOPE CHAPEL"}

-- Returns sub zone ID based on index of subzone in constant variable
function DataToColor:hearthZoneID()
    local index = {}
    local hearthzone = string.upper(GetBindLocation())
    for k, v in pairs(HearthZoneList) do
        index[v] = k
    end
    if index[hearthzone] ~= nil then
        return index[hearthzone]
    else DataToColor:log(hearthzone .. "is not registered. Please add it to the table in D2C.")
    end
end