----------------------------------------------------------------------------
--  DataToColor - display player position as color
----------------------------------------------------------------------------

DataToColor = {}
DataToColor = LibStub("AceAddon-3.0"):NewAddon("AceConsole-3.0", "AceEvent-3.0", "AceTimer-3.0", "AceComm-3.0", "AceSerializer-3.0")
Range = {}
Range = LibStub("LibRangeCheck-2.0")

DATA_CONFIG = {
    ACCEPT_PARTY_REQUESTS = false, -- O
    DECLINE_PARTY_REQUESTS = false, -- O
    RIGHT = true,
    DUEL = false,
    GOSSIP = true,
    REZ = true,
    HIDE_SHAPESHIFT_BAR = true,
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
local ASSIGN_MACROS = true

-- Trigger between emitting game data and frame location data
SETUP_SEQUENCE = false
-- Exit process trigger
EXIT_PROCESS_STATUS = 0
-- Assigns various macros if user changes variable to true
ASSIGN_MACROS_INITIALIZE = false
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
local globalCounter = 0
-- Global table of all items player has
local items = {}
local itemsPlaceholderComparison = {}
local enchantedItemsList = {}
-- How often item frames change
local ITEM_ITERATION_FRAME_CHANGE_RATE = 6
-- How often the actionbar frames change
local ACTION_BAR_ITERATION_FRAME_CHANGE_RATE = 5

local MAX_POWER_TYPE = 1000000
local MAX_ACTION_IDX = 1000

-- Action bar configuration for which spells are tracked
local MAIN_MIN = 1
local MAIN_MAX = 12
local BOTTOM_LEFT_MIN = 61
local BOTTOM_LEFT_MAX = 72

DataToColor.frames = nil
DataToColor.r = 0

-- Note: Coordinates where player is standing (max: 10, min: -10)
-- Note: Player direction is in radians (360 degrees = 2π radians)
-- Note: Player health/mana is taken out of 100% (0 - 1)

-- Character's name
local CHARACTER_NAME = UnitName("player")
local CHARACTER_GUID = UnitGUID("player")
local uiErrorMessage=0;
local lastCombatDamageDealerCreature=0;
local lastCombatCreature=0;
local lastCombatCreatureDied=0;

-- List of possible subzones to which a player's hearthstone may be bound
local HearthZoneList = {"CENARION HOLD", "VALLEY OF TRIALS", "THE CROSSROADS", "RAZOR HILL", "DUROTAR", "ORGRIMMAR", "CAMP TAURAJO", "FREEWIND POST", "GADGETZAN", "SHADOWPREY VILLAGE", "THUNDER BLUFF", "UNDERCITY", "CAMP MOJACHE", "COLDRIDGE VALLEY", "DUN MOROGH", "THUNDERBREW DISTILLERY", "IRONFORGE", "STOUTLAGER INN", "STORMWIND CITY", "SOUTHSHORE", "LAKESHIRE", "STONETALON PEAK", "GOLDSHIRE", "SENTINEL HILL", "DEEPWATER TAVERN", "THERAMORE ISLE", "DOLANAAR", "ASTRANAAR", "NIJEL'S POINT", "CRAFTSMEN'S TERRACE", "AUBERDINE", "FEATHERMOON STRONGHOLD", "BOOTY BAY", "WILDHAMMER KEEP", "DARKSHIRE", "EVERLOOK", "RATCHET", "LIGHT'S HOPE CHAPEL"}
local EnchantmentStrings = {}

function DataToColor:slashCommands()
    SLASH_DC1 = "/dc";
    SlashCmdList["DC"] = StartSetup;
end

UIErrorsFrame:UnregisterEvent("UI_ERROR_MESSAGE")


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

-- handle error events
local function OnUIErrorMessage(self, event, messageType, message)
    local errorName, soundKitID, voiceID = GetGameMessageInfo(messageType)

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
                uiErrorMessage = i;
                foundMessage=true;
                UIErrorsFrame:AddMessage(message, 0, 1, 0) -- show as green messasge
            end
        end
    end

    if not foundMessage then
        --DataToColor:log(message .. ":" .. errorName);
        UIErrorsFrame:AddMessage(message, 0, 0, 1) -- show as blue message (unknown message)
    end
  end

local function OnCombatEvent(self, event)
    local timestamp, eventType, _, sourceGUID, sourceName, _, _, destGUID, destName, _, _, spellId, spellName, spellSchool = CombatLogGetCurrentEventInfo();
    if eventType=="SPELL_PERIODIC_DAMAGE" then
        lastCombatCreature=0;
    elseif string.find(sourceGUID, "Creature") then
        lastCombatCreature = DataToColor:getGuidFromUUID(sourceGUID);
        lastCombatDamageDealerCreature = lastCombatCreature;
        --print(sourceGUID.." "..lastCombatCreature);
    else
        lastCombatCreature=0;
        --print("Other "..eventType);
    end

    if eventType=="UNIT_DIED" then
        if string.find(destGUID, "Creature") then
            lastCombatCreatureDied = DataToColor:getGuidFromUUID(destGUID);
            --print("v_killing blow " .. destGUID .. " " .. lastCombatCreatureDied .. " " .. destName)
        else
            --print("i_killing blow " .. destGUID .. " " .. destName)
        end
    end

    --print(CombatLogGetCurrentEventInfo());
end  

--event handler
local eventHandler = CreateFrame("Frame");
eventHandler:SetScript("OnEvent", OnUIErrorMessage);
eventHandler:RegisterEvent("UI_ERROR_MESSAGE")

local f = CreateFrame("Frame")
f:RegisterEvent("COMBAT_LOG_EVENT_UNFILTERED")
f:SetScript("OnEvent", OnCombatEvent)

function DataToColor:OnMerchantShow(self, event, messageType, message)
    
    TotalPrice = 0
    for myBags = 0,4 do
        for bagSlots = 1, GetContainerNumSlots(myBags) do
            CurrentItemLink = GetContainerItemLink(myBags, bagSlots)
                if CurrentItemLink then
                    _, _, itemRarity, _, _, _, _, _, _, _, itemSellPrice = GetItemInfo(CurrentItemLink)
                    _, itemCount = GetContainerItemInfo(myBags, bagSlots)
                    if itemRarity == 0 and itemSellPrice ~= 0 then
                        TotalPrice = TotalPrice + (itemSellPrice * itemCount);
                        DataToColor:log("Selling: "..itemCount.." "..CurrentItemLink.." for "..GetCoinTextureString(itemSellPrice * itemCount));
                        UseContainerItem(myBags, bagSlots)
                    end
                end
        end
    end
    if TotalPrice ~= 0 then
        print("Total Price for all items: " .. GetCoinTextureString(TotalPrice))
    else
        print("No grey items were sold.")
    end
end

DataToColor:RegisterEvent('MERCHANT_SHOW','OnMerchantShow');



-- Function to quickly log info to wow console
function DataToColor:log(msg)
    DEFAULT_CHAT_FRAME:AddMessage(msg) -- alias for convenience
end

function StartSetup()
    if not SETUP_SEQUENCE then
        SETUP_SEQUENCE = true
    else
        SETUP_SEQUENCE = false
    end
end
function DataToColor:error(msg)
    self:log("|cff0000ff" .. msg .. "|r")
    self:log(msg)
    self:log(debugstack())
    error(msg)
end
-- Automatic Modulo function for Lua 5 and earlier
function Modulo(val, by)
    return val - math.floor(val / by) * by
end

-- Check if two tables are identical
function ValuesAreEqual(t1, t2, ignore_mt)
    local ty1 = type(t1)
    local ty2 = type(t2)
    if ty1 ~= ty2 then return false end
    -- non-table types can be directly compared
    if ty1 ~= 'table' and ty2 ~= 'table' then return t1 == t2 end
    -- as well as tables which have the metamethod __eq
    local mt = getmetatable(t1)
    if not ignore_mt and mt and mt.__eq then return t1 == t2 end
    for k1, v1 in pairs(t1) do
        local v2 = t2[k1]
        if v2 == nil or not ValuesAreEqual(v1, v2) then return false end
    end
    for k2, v2 in pairs(t2) do
        local v1 = t1[k2]
        if v1 == nil or not ValuesAreEqual(v1, v2) then return false end
    end
    return true
end

-- Discover player's direction in radians (360 degrees = 2π radians)
function DataToColor:GetPlayerFacing()
    local p = Minimap
    local m = ({p:GetChildren()})[9]
    local facing = GetPlayerFacing()

    if facing ~= nil then
        return facing
    else
        return 0
    end
end

-- This function runs when addon is initialized/player logs in
-- Decides length of white box
function DataToColor:OnInitialize()
    self:SetupRequirements()
    self:CreateFrames(NUMBER_OF_FRAMES)
    self:slashCommands();

    self:log("We're in")

    LoggingChat(1);
end

function DataToColor:SetupRequirements()
    SetCVar("autoInteract", 1);
    SetCVar("autoLootDefault", 1)
    -- /run SetCVar("cameraSmoothStyle", 2) --always
	SetCVar('Contrast',50,'[]')
	SetCVar('Brightness',50,'[]')
	SetCVar('Gamma',1,'[]')
end

-- This function is able to pass numbers in range 0 to 16777215
function integerToColor(i)
    -- r,g,b are integers in range 0-255
    -- then we turn them into 0-1 range
    return {bit.band(bit.rshift(i,16),255) / 255, bit.band(bit.rshift(i,8),255) / 255, bit.band(i,255) / 255};
end

-- This function is able to pass numbers in range 0 to 9.99999 (6 digits)
-- converting them to a 6-digit integer.
function fixedDecimalToColor(f)
    if f > 9.99999 then
        -- error("Number too big to be passed as a fixed-point decimal")
        return {0}
    elseif f < 0 then
        return {0}
    end
    -- "%f" denotes formatting a string as floating point decimal
    -- The number (.5 in this case) is used to denote the number of decimal places
    local f6 = tonumber(string.format("%.5f", 1))
    -- Makes number an integer so it can be encoded
    local i = math.floor(f * 100000)
    return integerToColor(i)
end

-- Pass in a string to get the upper case ASCII values. Converts any special character with ASCII values below 100
function DataToColor:StringToASCIIHex(str)
    -- Converts string to upper case so only 2 digit ASCII values
    -- All lowercase letters have a decimal ASCII value >100, so we only uppercase numbers which are a mere 2 digits long.
    str = string.sub(string.upper(str), 0, 6)
    -- Sets string to an empty string
    local ASCII = ''
    -- Loops through all of string passed to it and converts to upper case ASCII values
    for i = 1, string.len(str) do
        -- Assigns the specific value to a character to then assign to the ASCII string/number
        local c = string.sub(str, i, i)
        -- Concatenation of old string and new character
        ASCII = ASCII .. string.byte(c)
    end
    return tonumber(ASCII)
end

-- Function to mass generate all of the initial frames for the pixel reader
function DataToColor:CreateFrames(n)
    -- Note: Use single frame and update color on game update call
    local function UpdateFrameColor(f)
        -- set the frame color to random values
        xCoordi, yCoordi = self:GetCurrentPlayerPosition()
        if xCoordi == nil or yCoordi == nil then
            xCoordi = 0
            yCoordi = 0
        end
        -- Makes a 5px by 5px square. Might be 6x5 or 6x5.
        -- This is APPROXIMATE MATH. startingFrame is the x start, startingFramey is the "y" start (both starts are in regard to pixel position on the main frame)
        function MakePixelSquareArr(col, slot)
            --if type(slot) ~= "number" or slot < 0 or slot >= NUMBER_OF_FRAMES then
            --    self:error("Invalid slot value")
            --end
            
            --if type(col) ~= "table" then
            --    self:error("Invalid color value (" .. tostring(col) .. ")")
            --end
            
            self.frames[slot + 1]:SetBackdropColor(col[1], col[2], col[3], 1)
        end
        -- Number of loops is based on the number of generated frames declared at beginning of script
        
        if not SETUP_SEQUENCE then
            MakePixelSquareArr(integerToColor(0), 0)
            -- The final data square, reserved for additional metadata.
            MakePixelSquareArr(integerToColor(2000001), NUMBER_OF_FRAMES - 1)
            -- Position related variables --
            MakePixelSquareArr(fixedDecimalToColor(xCoordi), 1) --1 The x-coordinate
            MakePixelSquareArr(fixedDecimalToColor(yCoordi), 2) --2 The y-coordinate
            MakePixelSquareArr(fixedDecimalToColor(DataToColor:GetPlayerFacing()), 3) --3 The direction the player is facing in radians
            MakePixelSquareArr(integerToColor(self:GetZoneName(0)), 4) -- Get name of first 3 characters of zone
            MakePixelSquareArr(integerToColor(self:GetZoneName(3)), 5) -- Get name of last 3 characters of zone
            MakePixelSquareArr(fixedDecimalToColor(self:CorpsePosition("x") * 10), 6) -- Returns the x coordinates of corpse
            MakePixelSquareArr(fixedDecimalToColor(self:CorpsePosition("y") * 10), 7) -- Return y coordinates of corpse
            -- Boolean variables --
            MakePixelSquareArr(integerToColor(self:Base2Converter()), 8)
            -- Start combat/NPC related variables --
            MakePixelSquareArr(integerToColor(self:getHealthMax("player")), 10) --8 Represents maximum amount of health
            MakePixelSquareArr(integerToColor(self:getHealthCurrent("player")), 11) --9 Represents current amount of health
            MakePixelSquareArr(integerToColor(self:getManaMax("player")), 12) --10 Represents maximum amount of mana
            MakePixelSquareArr(integerToColor(self:getManaCurrent("player")), 13) --11 Represents current amount of mana
            MakePixelSquareArr(integerToColor(self:getPlayerLevel()), 14) --12 Represents character level
            MakePixelSquareArr(integerToColor(self:isInRange()), 15) -- 15 Represents if target is within 20, 30, 35, or greater than 35 yards
            MakePixelSquareArr(integerToColor(self:GetTargetName(0)), 16) -- Characters 1-3 of target's name
            MakePixelSquareArr(integerToColor(self:GetTargetName(3)), 17) -- Characters 4-6 of target's name
            MakePixelSquareArr(integerToColor(self:getHealthMax("target")), 18) -- Return the maximum amount of health a target can have
            MakePixelSquareArr(integerToColor(self:getHealthCurrent("target")), 19) -- Returns the current amount of health the target currently has
            -- Begin Items section --
            -- there are 5 item slots: main backpack and 4 pouches
            -- Indexes one slot from each bag each frame. SlotN (1-16) and bag (0-4) calculated here:
            if Modulo(globalCounter, ITEM_ITERATION_FRAME_CHANGE_RATE) == 0 then
                itemNum = itemNum + 1
                equipNum = equipNum + 1
                -- Reseting global counter to prevent integer overflow
                if globalCounter > 10000 then
                    globalCounter = 1000
                end
            end
            if Modulo(globalCounter, ACTION_BAR_ITERATION_FRAME_CHANGE_RATE) == 0 then
                actionNum = actionNum + 1
            end
            -- Controls rate at which item frames change.
            globalCounter = globalCounter + 1

            if itemNum >= 21 then
                itemNum = 1
            end

            if actionNum >= 84 then
                actionNum = 1
            end

            -- Bag contents - Uses data pixel positions 20-29
            for bagNo = 0, 4 do
                -- Returns item ID and quantity
                MakePixelSquareArr(integerToColor(self:itemName(bagNo, itemNum)), 20 + bagNo * 2) -- 20,22,24,26,28
                -- Return item slot number
                MakePixelSquareArr(integerToColor(bagNo * 20 + itemNum), 21 + bagNo * 2) -- 21,23,25,27,29
                MakePixelSquareArr(integerToColor(self:itemInfo(bagNo, itemNum)), 60 + bagNo ) -- 60,61,62,63,64
            end
            -- Worn inventory start.
            -- Starts at beginning once we have looked at all desired slots.
            if equipNum - 19 == 0 then
                equipNum = 1
            end

            local equipName = self:equipName(equipNum)
            -- Equipment ID
            MakePixelSquareArr(integerToColor(equipName), 30)
            -- Equipment slot
            MakePixelSquareArr(integerToColor(equipNum), 31)
            
            -- Amount of money in coppers
            MakePixelSquareArr(integerToColor(Modulo(self:getMoneyTotal(), 1000000)), 32) -- 13 Represents amount of money held (in copper)
            MakePixelSquareArr(integerToColor(floor(self:getMoneyTotal() / 1000000)), 33) -- 14 Represents amount of money held (in gold)
           
            -- Start main action page (page 1)
            MakePixelSquareArr(integerToColor(self:isActionUseable(1,24)), 34) 
            MakePixelSquareArr(integerToColor(self:isActionUseable(25,48)), 35) 
            MakePixelSquareArr(integerToColor(self:isActionUseable(49,72)), 36) 
            MakePixelSquareArr(integerToColor(self:isActionUseable(73,96)), 42) 


            -- Number of slots each bag contains, not including our default backpack
            MakePixelSquareArr(integerToColor(self:bagSlots(1)), 37) -- Bag slot 1
            MakePixelSquareArr(integerToColor(self:bagSlots(2)), 38) -- Bag slot 2
            MakePixelSquareArr(integerToColor(self:bagSlots(3)), 39) -- Bag slot 3
            MakePixelSquareArr(integerToColor(self:bagSlots(4)), 40) -- Bag slot 4
            -- Profession levels:
            -- tracks our skinning level
            --MakePixelSquareArr(integerToColor(self:GetProfessionLevel("Skinning")), 41) -- Skinning profession level
            -- tracks our fishing level
            --MakePixelSquareArr(integerToColor(self:GetProfessionLevel("Fishing")), 42) -- Fishing profession level
            MakePixelSquareArr(integerToColor(self:getBuffsForClass()), 41);
            -- 42 used by keys
            
            MakePixelSquareArr(integerToColor(self:getTargetLevel()), 43)

            MakePixelSquareArr(integerToColor(DataToColor:actionbarCost(actionNum)), 44)
            --MakePixelSquareArr(integerToColor(self:GetGossipIcons()), 45) -- Returns which gossip icons are on display in dialogue box

            MakePixelSquareArr(integerToColor(self:PlayerClass()), 46) -- Returns player class as an integer
            MakePixelSquareArr(integerToColor(self:isUnskinnable()), 47) -- Returns 1 if creature is unskinnable
            MakePixelSquareArr(integerToColor(self:shapeshiftForm()), 48) -- Shapeshift id https://wowwiki.fandom.com/wiki/API_GetShapeshiftForm
            MakePixelSquareArr(integerToColor(self:areSpellsInRange()), 49) -- Are spells in range

            MakePixelSquareArr(integerToColor(self:getUnitXP("player")), 50) -- Player Xp
            MakePixelSquareArr(integerToColor(self:getUnitXPMax("player")), 51) -- Player Level Xp
            MakePixelSquareArr(integerToColor(uiErrorMessage), 52) -- Last UI Error message
            uiErrorMessage=0;

            MakePixelSquareArr(integerToColor(DataToColor:CastingInfoSpellId()), 53) -- Spell being cast
            MakePixelSquareArr(integerToColor(DataToColor:ComboPoints()), 54) -- Combo points for rogue / druid
            MakePixelSquareArr(integerToColor(DataToColor:getDebuffsForTarget()), 55) -- target debuffs

            MakePixelSquareArr(integerToColor(DataToColor:targetNpcId()), 56) -- target id
            MakePixelSquareArr(integerToColor(DataToColor:getGuid("target")),57) -- target reasonably uniqueId
            MakePixelSquareArr(integerToColor(DataToColor:GetBestMap()),58) -- MapId

            MakePixelSquareArr(integerToColor(DataToColor:IsTargetOfTargetPlayerAsNumber()),59) -- IsTargetOfTargetPlayerAsNumber
            -- 60-64 = Bag item info
            MakePixelSquareArr(integerToColor(lastCombatCreature),65) -- Combat message creature
            MakePixelSquareArr(integerToColor(lastCombatDamageDealerCreature),66) -- Combat message last damage dealer creature
            MakePixelSquareArr(integerToColor(lastCombatCreatureDied),67) -- Last Killed Unit

            MakePixelSquareArr(integerToColor(DataToColor:getGuid("pet")),68) -- pet guid
            MakePixelSquareArr(integerToColor(DataToColor:getGuid("pettarget")),69) -- pet target

            self:HandleEvents()
        end
        if SETUP_SEQUENCE then
            -- Emits meta data in data square index 0 concerning our estimated cell size, number of rows, and the numbers of frames
            MakePixelSquareArr(integerToColor(CELL_SPACING * 10000000 + CELL_SIZE * 100000 + 1000 * FRAME_ROWS + NUMBER_OF_FRAMES), 0)
            -- Assign pixel squares a value equivalent to their respective indices.
            for i = 1, NUMBER_OF_FRAMES - 1 do
                MakePixelSquareArr(integerToColor(i), i)
            end
        end
        -- Note: Use this area to set color for individual pixel frames
        -- Cont: For example self.frames[0] = playerXCoordinate while self.frames[1] refers to playerXCoordinate
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
    
    --local windowCheckFrame = CreateFrame("Frame", "frame_windowcheck", UIParent)
    --windowCheckFrame:SetPoint("TOPLEFT", 120, -200)
    --windowCheckFrame:SetHeight(5)
    --windowCheckFrame:SetWidth(5)
    --windowCheckFrame:SetFrameStrata("LOW")
    --setFramePixelBackdrop(windowCheckFrame)
    --windowCheckFrame:SetBackdropColor(0.5, 0.1, 0.8, 1)
    
    -- creating a new frame to check for open BOE and BOP windows
    --local bindingCheckFrame = CreateFrame("Frame", "frame_bindingcheck", UIParent)
    -- 90 and 200 are the x and y offsets from the default "CENTER" position
    --bindingCheckFrame:SetPoint("CENTER", 90, 200)
    --bindingCheckFrame:SetHeight(5)
    --bindingCheckFrame:SetWidth(5)
    --bindingCheckFrame:SetFrameStrata("LOW")
    --setFramePixelBackdrop(bindingCheckFrame)
    --bindingCheckFrame:SetBackdropColor(0.5, 0.1, 0.8, 1)
    
    -- Note: Use for loop based on input to generate "n" number of frames
    for frame = 0, n - 1 do
        local y = Modulo(frame, FRAME_ROWS) -- those are grid coordinates (1,2,3,4 by  1,2,3,4 etc), not pixel coordinates
        local x = floor(frame / FRAME_ROWS)
        -- Put frame information in to an object/array
        frames[frame + 1] = genFrame("frame_"..tostring(frame), x, y)
    end
    
    -- Assign self.frames to frame list generated above
    self.frames = frames
    self.frames[1]:SetScript("OnUpdate", function() UpdateFrameColor(f) end)
end

-- Use Astrolabe function to get current player position
function DataToColor:GetCurrentPlayerPosition()
    local map = C_Map.GetBestMapForUnit("player")
    if map ~= nil then
        local position = C_Map.GetPlayerMapPosition(map, "player")
        -- Resets map to correct zone ... removed in 8.0.1, needs to be tested to see if zone auto update
        -- SetMapToCurrentZone()
        return position:GetXY()
    else
        return;
    end
end

-- Base 2 converter for up to 24 boolean values to a single pixel square.
function DataToColor:Base2Converter()
    -- 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384
    return self:MakeIndexBase2(self:targetCombatStatus(), 0) + 
    self:MakeIndexBase2(self:GetEnemyStatus(), 1) + 
    self:MakeIndexBase2(self:deadOrAlive(), 2) +
    self:MakeIndexBase2(self:checkTalentPoints(), 3) + 
    self:MakeIndexBase2(self:inInMeleeRange(), 4) + 
    self:MakeIndexBase2(0, 5) +
    self:MakeIndexBase2(self:IsPetVisible(), 6) + 
    self:MakeIndexBase2(self:mainhandEnchantActive(), 7) + 
    self:MakeIndexBase2(self:offhandEnchantActive(), 8) +
    self:MakeIndexBase2(self:GetInventoryBroken(), 9) + 
    self:MakeIndexBase2(self:IsPlayerFlying(), 10) + 
    self:MakeIndexBase2(self:IsPlayerSwimming(), 11) +
    self:MakeIndexBase2(0, 12) + 
    self:MakeIndexBase2(0, 13) + 
    self:MakeIndexBase2(self:playerCombatStatus(), 14) +
    self:MakeIndexBase2(self:IsTargetOfTargetPlayer(), 15) + 
    self:MakeIndexBase2(0, 16) + 
    self:MakeIndexBase2(self:ProcessExitStatus(), 17)+
    self:MakeIndexBase2(self:IsPlayerMounted(), 18)+
    self:MakeIndexBase2(self:IsAutoRepeatSpellOn("Shoot"), 19)+
    self:MakeIndexBase2(self:IsCurrentSpell(6603), 20)+ -- AutoAttack enabled
    self:MakeIndexBase2(self:targetIsNormal(), 21)+
    self:MakeIndexBase2(self:IsTagged(), 22);
end



function DataToColor:getBuffsForClass()
    local class, CC = UnitClass("player");

    class=self:MakeIndexBase2(self:GetBuffs("Food"), 0) +
    self:MakeIndexBase2(self:GetBuffs("Drink"), 1) +
    self:MakeIndexBase2(self:GetBuffs("Well Fed"), 2) +
    self:MakeIndexBase2(self:GetBuffs("Mana Regeneration"), 3);

    if CC == "PRIEST" then 
        class=class+self:MakeIndexBase2(self:GetBuffs("Fortitude"),10) +
	    self:MakeIndexBase2(self:GetBuffs("Inner Fire"), 11)+
	    self:MakeIndexBase2(self:GetBuffs("Renew"), 12)+
        self:MakeIndexBase2(self:GetBuffs("Shield"), 13)+
        self:MakeIndexBase2(self:GetBuffs("Spirit"), 14);
    elseif CC == "DRUID" then
        class=class+self:MakeIndexBase2(self:GetBuffs("Mark of the Wild"), 10) +
        self:MakeIndexBase2(self:GetBuffs("Thorns"), 11)+
        self:MakeIndexBase2(self:GetBuffs("Fury"), 12);
    elseif CC == "PALADIN" then
        class=class+self:MakeIndexBase2(self:GetBuffs("Aura"), 10) +
        self:MakeIndexBase2(self:GetBuffs("Blessing"), 11)+       
        self:MakeIndexBase2(self:GetBuffs("Seal"), 12); 
    elseif CC == "MAGE" then
        class=class+self:MakeIndexBase2(self:GetBuffs("Armor"), 10)+
        self:MakeIndexBase2(self:GetBuffs("Arcane Intellect"), 11)+       
        self:MakeIndexBase2(self:GetBuffs("Ice Barrier"), 12)+
        self:MakeIndexBase2(self:GetBuffs("Ward"), 13)+
        self:MakeIndexBase2(self:GetBuffs("Fire Power"), 14);
    elseif CC == "ROGUE" then        
        class=class+self:MakeIndexBase2(self:GetBuffs("Slice and Dice"), 10);
    elseif CC == "WARRIOR" then        
        class=class+self:MakeIndexBase2(self:GetBuffs("Battle Shout"), 10);        
    elseif CC == "WARLOCK" then        
        class=class+self:MakeIndexBase2(self:GetBuffs("Demon"), 10) + -- Demon Skin or Demon Armor
        self:MakeIndexBase2(self:GetBuffs("Soul Link"), 11) +
        self:MakeIndexBase2(self:GetBuffs("Soulstone Resurrection"), 12) +
        self:MakeIndexBase2(self:GetBuffs("Shadow Trance"), 13);
    elseif CC == "SHAMAN" then
        class=class+self:MakeIndexBase2(self:GetBuffs("Lightning Shield"), 10);
    end
    return class;
end

function DataToColor:delete(items)
    for b=0,4 do for s=1,GetContainerNumSlots(b) 
        do local n=GetContainerItemLink(b,s) 
            if n then
                for i = 1, table.getn(items), 1 do
                    if strfind(n,items[i]) then
                        DataToColor:log("Delete: " .. items[i]);
                        PickupContainerItem(b,s);
                        DeleteCursorItem();
                    end
                end
            end
        end
    end
end

function DataToColor:sell(items)

    local target = GetUnitName("target")
    if target ~= nil then
        local item= GetMerchantItemLink(1);

        if  item ~= nil then
            DataToColor:log("Selling items...");

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
                                    DataToColor:log("Selling: "..itemCount.." "..CurrentItemLink.." for "..GetCoinTextureString(itemSellPrice * itemCount));
                                    UseContainerItem(b,s);
                                else
                                    DataToColor:log("Item is not gray or common, not selling it: " .. items[i]);
                                end
                            end
                        end
                    end
                end
            end

            if TotalPrice ~= 0 then
                print("Total Price for all items: " .. GetCoinTextureString(TotalPrice))
            else
                print("No grey items were sold.")
            end

        else
            DataToColor:log("Merchant is not open to sell to, please approach and open.");
        end
    else
        DataToColor:log("Merchant is not targetted.");
    end
end


function DataToColor:getDebuffsForTarget()

    local class, CC = UnitClass("player");
    class=0;

    if CC == "PRIEST" then 
        class=class+self:MakeIndexBase2(self:GetDebuffs("Pain"), 0);
    elseif CC == "DRUID" then
        class=class+self:MakeIndexBase2(self:GetDebuffs("Roar"), 0) +
        self:MakeIndexBase2(self:GetDebuffs("Faerie Fire"), 1) +
        self:MakeIndexBase2(self:GetDebuffs("Rip"), 2);
    elseif CC == "PALADIN" then
        class=0;
    elseif CC == "MAGE" then
        class=class+self:MakeIndexBase2(self:GetDebuffs("Frostbite"), 0);
    elseif CC == "ROGUE" then        
        class=0;
    elseif CC == "WARRIOR" then        
        class=class+self:MakeIndexBase2(self:GetDebuffs("Rend"), 0);
    elseif CC == "WARLOCK" then        
        class=self:MakeIndexBase2(self:GetDebuffs("Curse of"), 0) + -- Curse of Agony or Curse of Recklessness or Curse of Weakness or Curse of Elements or Curse of Shadow 
        self:MakeIndexBase2(self:GetDebuffs("Corruption"), 1) +
        self:MakeIndexBase2(self:GetDebuffs("Immolate"), 2) +
        self:MakeIndexBase2(self:GetDebuffs("Siphon Life"), 3);
    end

    return class;
end

-- Returns bitmask values.
-- MakeIndexBase2(true, 4) --> returns 16
-- MakeIndexBase2(false, 9) --> returns 0
function DataToColor:MakeIndexBase2(bool, power)
    if bool ~= nil and bool > 0 then
        return math.pow(2, power)
    else return 0
    end
end

-- Grabs current target's name (friend or foe)
function DataToColor:GetTargetName(partition)
    -- Uses wow function to get target string
    local target = GetUnitName("target")
    if target ~= nil then
        target = DataToColor:StringToASCIIHex(target)
        if partition < 3 then
            return tonumber(string.sub(target, 0, 6))
        else if target > 999999 then
                return tonumber(string.sub(target, 7, 12))
            end
        end
        return 0
    end
    return 0
end

function DataToColor:CastingInfoSpellId()
    local name, text, texture, startTime, endTime, isTradeSkill, castID, notInterruptible, spellID = CastingInfo();
    if spellID ~= nil then
        return spellID
    end
    if texture ~= nil then -- temp fix for tbc
        return texture
    end
     _, _, _, _, _, _, _, spellID = ChannelInfo();
     if spellID ~= nil then
        return spellID
    end
    return 0
end

function DataToColor:getUnitXP(unit)
    local value = UnitXP(unit);
    return value
end

function DataToColor:getUnitXPMax(unit)
    local value = UnitXPMax(unit);
    return value
end

-- Finds maximum amount of health player can have
function DataToColor:getHealthMax(unit)
    local health = UnitHealthMax(unit)
    return health
end
-- Finds axact amount of health player current has
function DataToColor:getHealthCurrent(unit)
    local health = UnitHealth(unit)
    return health
end

-- Finds maximum amount of mana a character can store
function DataToColor:getManaMax(unit)
    local manaMax = UnitPowerMax(unit)
    return manaMax
end

-- Finds exact amount of mana player is storing
function DataToColor:getManaCurrent(unit)
    local mana = UnitPower(unit)
    return mana
end

-- Finds player current level
function DataToColor:getPlayerLevel()
    return UnitLevel("player")
end

function DataToColor:getTargetLevel()
    return UnitLevel("target")
end

-- Finds the total amount of money.
function DataToColor:getMoneyTotal()
    return GetMoney()
end

-- Finds if target is attackable with the fireball which is the longest distance spell.
-- Fireball or a spell with equivalent range must be in slot 2 for this to work
function DataToColor:isInRange()
    -- Assigns arbitrary value (50) to note the target is not within range
    local range = 50
    if IsActionInRange(2) then range = 35 end -- Checks Fireball Range, slot 2
    if IsActionInRange(3) and self:getPlayerLevel() < 25 then range = 30 end -- Checks Frostbolt Range, slot 3
    if IsActionInRange(10) then range = 30 end -- Checks Counterspell Range, slot 11. Useful for when after arctic reach is applied
    if IsActionInRange(4) then range = 20 end -- Checks Fire Blast Range, slot 4
    return range
end

function DataToColor:inInMeleeRange()
    -- local rc = LibStub("LibRangeCheck-2.0")
    -- local minRange, maxRange = rc:GetRange('target')
    -- local minRangeIfVisible, maxRangeIfVisible = rc:GetRange('target', true)
        local target = GetUnitName("target")
        if target ~= nil then
            local min, max = Range:GetRange("target")
            if min == 0 and max == 5 then
                return 1
            end
        end
        return 0
    end

function DataToColor:targetNpcId()
    local unitType, _, _, _, _, npcID, guid = strsplit('-', UnitGUID("target") or ''); 
    if npcID ~= nil then
        return tonumber(npcID);
    end
    return 0;
end

function DataToColor:getGuid(src)
    local unitType, _, _, _, _, npcID, spawnUID = strsplit('-', UnitGUID(src) or ''); 
    if npcID ~= nil then
        return self:uniqueGuid(npcID, spawnUID);
    end
    return 0;
end

function DataToColor:getGuidFromUUID(uuid)
    local unitType, _, _, _, _, npcID, spawnUID = strsplit('-', uuid or ''); 
    return self:uniqueGuid(npcID, spawnUID);
end

function DataToColor:uniqueGuid(npcId, spawn)
    local spawnEpochOffset = bit.band(tonumber(string.sub(spawn, 5), 16), 0x7fffff)
    local spawnIndex = bit.band(tonumber(string.sub(spawn, 1, 5), 16), 0xffff8)

    local dd = date("*t", spawnEpochOffset)
    local num = 
    self:sum24(
        dd.day +
        dd.hour +
        dd.min +
        dd.sec +
        npcId +
        spawnIndex
    );
    return tonumber(num, 16);
end

function DataToColor:sum24(num)
    return num % 0x1000000
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
            --print(button:GetName(), actionType, (GetSpellLink(id)), actionName, type, cost, id)
            return MAX_POWER_TYPE * type + MAX_ACTION_IDX * slot + cost
        end
    end
    return MAX_ACTION_IDX * slot
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
    else _, _, item = string.find(GetContainerItemLink(bag, slot), "(m:%d+)")
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
    local itemCount;
    _, itemCount, _, _, _, _, _ = GetContainerItemInfo(bag, slot);
    local value=0;
    if itemCount ~= nil and itemCount > 0 then 
        local isSoulBound = C_Item.IsBound(ItemLocation:CreateFromBagAndSlot(bag,slot));
        if isSoulBound == true then value=1 end
    else
        value=2;
    end
    return value;
end

-- Returns item id from specific index in global items table
function DataToColor:returnItemFromIndex(index)
    return items[index]
end

function DataToColor:enchantedItems()
    if ValuesAreEqual(items, itemsPlaceholderComparison) then
    end
end

function DataToColor:mainhandEnchantActive() 
    local hasMainHandEnchant = GetWeaponEnchantInfo()
    if hasMainHandEnchant then
        return 1
    end
    return 0
end

function DataToColor:offhandEnchantActive() 
    local _, _, _, _, hasOffHandEnchant = GetWeaponEnchantInfo()
    if hasOffHandEnchant then
        return 1
    end
    return 0
end

function DataToColor:equipName(slot)
    local equip
    if GetInventoryItemLink("player", slot) == nil then
        equip = 0
    else _, _, equip = string.find(GetInventoryItemLink("player", slot), "(m:%d+)")
        equip = string.gsub(equip, 'm:', '')
    end
    if equip == nil then equip = 0
    end
    return tonumber(equip)
end
-- -- Function to tell if a spell is on cooldown and if the specified slot has a spell assigned to it
-- -- Slot ID information can be found on WoW Wiki. Slots we are using: 1-12 (main action bar), Bottom Right Action Bar maybe(49-60), and  Bottom Left (61-72)

function DataToColor:areSpellsInRange()

    local spellList, CC = UnitClass("player");
    if CC == "ROGUE" then
         spellList = {
            "Sinister Strike", --1
            "Throw", --2
            "Shoot Gun" --4
        };
    elseif CC == "DRUID" then
        spellList = {
            "Wrath", --1
            "Bash", --2
            "Rip" --3
        };
    elseif CC == "WARRIOR" then
        spellList = {
            "Charge", --1
            "Rend", --2
            "Shoot Gun", --4
        };        
    elseif CC == "PRIEST" then
        spellList = {
            "Shadow Word: Pain", --1
            "Mind Blast", --2
            "Mind Flay", --4
            "Shoot", --8
        };
    elseif CC == "PALADIN" then
        spellList = {
            "Judgement" --1
        };
    elseif CC == "MAGE" then
        spellList = {
            "Fireball", --1
            "Shoot",
            "Pyroblast",
            "Frostbolt",
            "Fire Blast"
        };        
    elseif CC == "HUNTER" then
        spellList = {
            "Raptor Strike", --1
            "Auto Shot", --2
        };        
    elseif CC == "WARLOCK" then
        spellList = {
            "Shadow Bolt",
            "Shoot"
        };
    elseif CC == "SHAMAN" then
        spellList = {
            "Lightning Bolt",
            "Earth Shock"
        }
    else
        spellList = {};
    end


    local inRange = 0
    for i = 1, table.getn(spellList ), 1 do
        local isInRange = IsSpellInRange(spellList[i], "target");
        if isInRange==1 then
            inRange = inRange + (2 ^ (i - 1))
        end
    end
    return inRange;
end

function DataToColor:isActionUseable(min,max)
    local isUsableBits = 0
    -- Loops through main action bar slots 1-12
    for i = min, max do
        local status, b, available = GetActionCooldown(i)
        local isUsable, notEnough = IsUsableAction(i)
        if isUsable == true and notEnough==false and status == 0 and available == 1  then
            isUsableBits = isUsableBits + (2 ^ (i - min))
        end
    end
    return isUsableBits
end

-- Function to tell how many bag slots we have in each bag
function DataToColor:bagSlots(bag)
    bagSlots = GetContainerNumSlots(bag)
    return bagSlots
end

-- Finds passed in string to return profession level
function DataToColor:GetProfessionLevel(skill)
    local numskills = GetNumSkillLines();
    for c = 1, numskills do
        local skillname, _, _, skillrank = GetSkillLineInfo(c);
        if(skillname == skill) then
            return tonumber(skillrank);
        end
    end
    return 0;
end

-- Checks target to see if  target has a specified debuff
function DataToColor:GetDebuffs(debuff)
    for i = 1, 5 do local db = UnitDebuff("target", i);
        if db ~= nil then
            if string.find(db, debuff) then
                return 1
            end
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
    local map= C_Map.GetBestMapForUnit("player");
    if map ~= nil then
        return map
    else
        return 0
    end
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
function DataToColor:CorpsePosition(coord)
    -- Assigns death coordinates
    local cX
    local cY
    if UnitIsGhost("player") then
        local map = C_Map.GetBestMapForUnit("player")
        if C_DeathInfo.GetCorpseMapPosition(map) ~= nil then
            cX, cY = C_DeathInfo.GetCorpseMapPosition(map):GetXY()
        end
    end
    if coord == "x" then
        if cX ~= nil then
            return cX
        else
            return 0
        end
        
    end
    if coord == "y" then
        if cY ~= nil then
            return cY
        else
            return 0
        end
    end
end

--returns class of player
function DataToColor:PlayerClass()
    -- UnitClass returns class and the class in uppercase e.g. "Mage" and "MAGE"
    local class, CC = UnitClass("player")
    if CC == "MAGE" then
        class = 128
    elseif CC == "ROGUE" then
        class = 64
    elseif CC == "WARRIOR" then
        class = 32
    elseif CC == "PALADIN" then
        class = 16
    elseif CC == "HUNTER" then
        class = 8  
    elseif CC == "PRIEST" then
        class = 4      
    elseif CC == "SHAMAN" then
        class = 2    
    elseif CC == "WARLOCK" then
        class = 1    
    elseif CC == "DRUID" then
        class = 256
    else
        class = 0
    end
    return class
end

function DataToColor:ComboPoints()
    local points = GetComboPoints("player","target");
    -- if target is in combat, return 0 for bitmask
    if points ~= nil then
        return points
        -- if target is not in combat, return 1 for bitmask
    else 
        return 0
    end
end

-----------------------------------------------------------------
-- Boolean functions --------------------------------------------
-- Only put functions here that are part of a boolean sequence --
-- Sew BELOW for examples ---------------------------------------
-----------------------------------------------------------------

-- Finds if player or target is in combat
function DataToColor:targetCombatStatus()
    local combatStatus = UnitAffectingCombat("target")
    -- if target is in combat, return 0 for bitmask
    if combatStatus then
        return 1
        -- if target is not in combat, return 1 for bitmask
    else return 0
    end
end

-- Checks if target is dead. Returns 1 if target is dead, nil otherwise (converts to 0)
function DataToColor:GetEnemyStatus()
    local targStatus = UnitIsDead("target")
    if targStatus then
        return 1
    else
        return 0
    end
end

function DataToColor:targetIsNormal()
    local classification = UnitClassification("target");
    if classification=="normal" then
        if (UnitIsPlayer("target")) then 
            return 0 
        end

        if UnitName("pet") ==  UnitName("target") then
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
    local deathStatus = UnitIsDeadOrGhost("player")
    if deathStatus then
        return 1
    else
        return 0
    end
end

-- Checks the number of talent points we have available to spend
function DataToColor:checkTalentPoints()
    if UnitCharacterPoints("player") > 0 then
        return 1
    else return 0
    end
end

function DataToColor:shapeshiftForm()
    local form = GetShapeshiftForm(true)
    if form == nil then
        form =0
    end;
    return form;
end

function DataToColor:playerCombatStatus()
    local combatStatus = UnitAffectingCombat("player")
    -- if player is not in combat, convert nil to 0
    if combatStatus then
        return 1
    else
        return 0
    end
end

-- Iterates through index of buffs to see if we have the buff is passed in
function DataToColor:GetBuffs(buff)
    for i = 1, 10 do
        local b = UnitBuff("player", i);
        if b ~= nil then
            if string.find(b, buff) then
                return 1
            end
        end
    end
    return 0
end

-- Returns the slot in which we have a fully degraded item
function DataToColor:GetInventoryBroken()
    for i = 1, 16 do
        local isBroken = GetInventoryItemBroken("player", i)
        if isBroken == true then
            return 1
        end
    end
    return 0
end
-- Checks if we are on a taxi
function DataToColor:IsPlayerFlying()
    local taxiStatus = UnitOnTaxi("player")
    if taxiStatus then
        return 1
    end
    -- Returns 0 if not on a wind rider beast
    return 0
end

function DataToColor:IsPlayerSwimming()
    if IsSwimming() then
        return 1
    end
    return 0
end

function DataToColor:IsPlayerMounted()
    local mounted = IsMounted()
    if mounted then
        return 1
    end
    -- Returns 0 if not on a wind rider beast
    return 0
end

-- Returns true is player has less than 10 food in action slot 66
function DataToColor:needFood()
    if GetActionCount(6) < 10 then
        return 1
    else return 0
    end
end

-- Returns true if the player has less than 10 water in action slot 7
function DataToColor:needWater()
    if GetActionCount(7) < 10 then
        return 1
    else return 0
    end
end

-- Returns if we have a mana gem (Agate, Ruby, etc.) in slot 67
function DataToColor:needManaGem()
    if GetActionCount(67) < 1 then
        return 1
    else return 0
    end
end

function DataToColor:IsTargetOfTargetPlayerAsNumber()
    if not(UnitName("targettarget")) then return 2 end; -- target has no target
    if CHARACTER_NAME == UnitName("target") then return 0 end; -- targeting self
    if UnitName("pet") == UnitName("targettarget") then return 4 end; -- targetting my pet
    if CHARACTER_NAME == UnitName("targettarget") then return 1 end; -- targetting me
    if UnitName("pet") == UnitName("target") and UnitName("targettarget") ~= nil then return 5 end;
    return 3;
end

-- Returns true if target of our target is us
function DataToColor:IsTargetOfTargetPlayer()
    local x = self:IsTargetOfTargetPlayerAsNumber();
    if x==1 or x==4 then return 1 else return 0 end;
end

function DataToColor:IsTagged()
    if UnitIsTapDenied("target") then return 1 else return 0 end;
end

function DataToColor:IsAutoRepeatActionOn(actionSlot)
    if IsAutoRepeatAction(actionSlot)  then
        return 1
    else return 0
    end
end

function DataToColor:IsAutoRepeatSpellOn(spell)
    if IsAutoRepeatSpell(spell)  then
        return 1
    else return 0
    end
end

function DataToColor:IsCurrentSpell(spell)
    if IsCurrentSpell(spell) then
        return 1
    else return 0
    end
end

function DataToColor:IsCurrentActionOn(actionSlot)
    if IsCurrentAction(actionSlot)  then
        return 1
    else return 0
    end
end

function DataToColor:IsPetVisible()
    if UnitIsVisible("pet") and not UnitIsDead("pet")  then
        return 1
    else return 0
    end
end

-- Returns 0 if target is unskinnable or if we have no target.
function DataToColor:isUnskinnable()
    local creatureType = UnitCreatureType("target")
    -- Demons COULD be included in this list, but there are some skinnable demon dogs.
    if creatureType == "Humanoid" or creatureType == "Elemental" or creatureType == "Mechanical" or creatureType == "Totem" then
        return 1
    else if creatureType ~= nil then
            return 0
        end
    end
    return 1
end

-- A variable which can trigger a process exit on the node side with this macro:
-- /script EXIT_PROCESS_STATfort = 1
function DataToColor:ProcessExitStatus()
    -- Check if a process exit has been requested
    if EXIT_PROCESS_STATUS == 1 then
        -- If a process exit has been requested, resets global frame tracker to zero in order to give node time to read frames
        if globalCounter > 200 then
            self:log('Manual exit request processing...')
            globalCounter = 0
        end
    end
    -- Number of frames until EXIT_PROCESS_STATUS returns to false so that node process can begin again
    if globalCounter > 100 and EXIT_PROCESS_STATUS ~= 0 then
        EXIT_PROCESS_STATUS = 0
    end
    return EXIT_PROCESS_STATUS
end

-- Returns sub zone ID based on index of subzone in constant variable
function DataToColor:hearthZoneID()
    local index = {}
    local hearthzone = string.upper(GetBindLocation())
    for k, v in pairs(HearthZoneList) do
        index[v] = k
    end
    if index[hearthzone] ~= nil then
        return index[hearthzone]
    else self:log(hearthzone .. "is not registered. Please add it to the table in D2C.")
    end
end

-----------------------------------------------------------------------------
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-- Begin Event Section -- -- Begin Event Section -- -- Begin Event Section --
-----------------------------------------------------------------------------
function DataToColor:HandleEvents()
    -- Handles group accept/decline
    if DATA_CONFIG.ACCEPT_PARTY_REQUESTS or DATA_CONFIG.DECLINE_PARTY_REQUESTS then
        self:HandlePartyInvite()
    end
    -- Handles item repairs when talking to item repair NPC
    if DATA_CONFIG.AUTO_REPAIR_ITEMS then
        self:RepairItems()
    end
    -- Handles learning talents, only works after level 10
    if DATA_CONFIG.AUTO_LEARN_TALENTS then
        self:LearnTalents()
    end
    -- Handles train new spells and talents
    if DATA_CONFIG.AUTO_TRAIN_SPELLS then
        self:CheckTrainer()  
    end
    -- Resurrect player
    if DATA_CONFIG.AUTO_RESURRECT then
        self:ResurrectPlayer()
    end

    self:IncrementIterator();
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
    if UnitCharacterPoints("player") > 0 then
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

local iterator = 0

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
    iterator = iterator + 1
end 

-- Used purely for training spells and professions
function DataToColor:CheckTrainer()
    iterator = iterator + 1
    if Modulo(iterator, 30) == 1 then
        -- First checks that the trainer gossip window is open
        -- DEFAULT_CHAT_FRAME:AddMessage(GetTrainerServdiceInfo(1))
        if GetTrainerServiceInfo(1) ~= nil and DATA_CONFIG .AUTO_TRAIN_SPELLS then
            -- LPCONFIG.AUTO_TRAIN_SPELLS = false
            local allAvailableOptions = GetNumTrainerServices()
            local money = GetMoney()
            local level = UnitLevel("player")
            
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
    if Modulo(iterator, 150) == 1 then
        if UnitIsDeadOrGhost("player") then
            
            -- Accept Release Spirit immediately after dying
            if not UnitIsGhost("player") and UnitIsGhost("player") ~= nil then
                RepopMe()
            end
            if UnitIsGhost("player") then
                local map = C_Map.GetBestMapForUnit("player")
                if C_DeathInfo.GetCorpseMapPosition(map) ~= nil then
                    local cX, cY = C_DeathInfo.GetCorpseMapPosition(map):GetXY()
                    local x, y = self:GetCurrentPlayerPosition()
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
