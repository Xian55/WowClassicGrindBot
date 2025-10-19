--[[
Name: LibRangeCheck-2.0
Revision: $Revision: 98 $
Author(s): mitch0
Website: http://www.wowace.com/projects/librangecheck-2-0/
Description: A range checking library based on interact distances and spell ranges
Dependencies: LibStub
License: Public Domain
]]

--- LibRangeCheck-2.0 provides an easy way to check for ranges and get suitable range checking functions for specific ranges.\\
-- The checkers use spell and item range checks, or interact based checks for special units where those two cannot be used.\\
-- The lib handles the refreshing of checker lists in case talents / spells / glyphs change and in some special cases when equipment changes (for example some of the mage pvp gloves change the range of the Fire Blast spell), and also handles the caching of items used for item-based range checks.\\
-- A callback is provided for those interested in checker changes.
-- @usage
-- local rc = LibStub("LibRangeCheck-2.0")
-- 
-- rc.RegisterCallback(self, rc.CHECKERS_CHANGED, function() print("need to refresh my stored checkers") end)
-- 
-- local minRange, maxRange = rc:GetRange('target')
-- if not minRange then
--     print("cannot get range estimate for target")
-- elseif not maxRange then
--     print("target is over " .. minRange .. " yards")
-- else
--     print("target is between " .. minRange .. " and " .. maxRange .. " yards")
-- end
-- 
-- local meleeChecker = rc:GetFriendMaxChecker(rc.MeleeRange) -- 5 yds
-- for i = 1, 4 do
--     -- TODO: check if unit is valid, etc
--     if meleeChecker("party" .. i) then
--         print("Party member " .. i .. " is in Melee range")
--     end
-- end
--
-- local safeDistanceChecker = rc:GetHarmMinChecker(30)
-- -- negate the result of the checker!
-- local isSafelyAway = not safeDistanceChecker('target')
--
-- @class file
-- @name LibRangeCheck-2.0
local MAJOR_VERSION = "LibRangeCheck-2.0"
local MINOR_VERSION = tonumber(("$Revision: 98 $"):match("%d+")) + 100000

local lib, oldminor = LibStub:NewLibrary(MAJOR_VERSION, MINOR_VERSION)
if not lib then
    return
end

-- << STATIC CONFIG

local UpdateDelay = .5
local ItemRequestTimeout = 10.0

-- interact distance based checks. ranges are based on my own measurements (thanks for all the folks who helped me with this)
local DefaultInteractList = {
    [3] = 8,
    [2] = 9,
    [4] = 28,
}

-- interact list overrides for races
local InteractLists = {
    ["Tauren"] = {
        [3] = 6,
        [2] = 7,
        [4] = 25,
    },
    ["Scourge"] = {
        [3] = 7,
        [2] = 8,
        [4] = 27,
    },
}

local MeleeRange = 5

-- list of friendly spells that have different ranges
local FriendSpells = {}
-- list of harmful spells that have different ranges 
local HarmSpells = {}

FriendSpells["DRUID"] = {
    5185, -- ["Healing Touch"], -- 40
    467, -- ["Thorns"], -- 30 (Nature's Reach: 33, 36)
    1126, -- ["Mark of the Wild"], -- 30
}
HarmSpells["DRUID"] = {
    16979, -- ["Feral Charge"], -- 8-25
    5176, -- ["Wrath"], -- 30 (Nature's Reach: 33, 36)
    33786, -- ["Cyclone"], -- 20 (Nature's Reach: 22, 24; Gale Winds: +10/20%)
    6795, -- ["Growl"], -- 20
    5211, -- ["Bash"], -- 5
}

FriendSpells["HUNTER"] = {}
HarmSpells["HUNTER"] = {
    1130, -- ["Hunter's Mark"] -- 100
    53351, -- ["Kill Shot"] -- 5-45 (Hawk Eye: 47, 49, 51)
    75, -- ["Auto Shot"], -- 5-35 (Hawk Eye: 37, 39, 41)
    2764, -- ["Throw"], -- 30
    19503, -- ["Scatter Shot"], -- 15 (Hawk Eye: 17, 19, 21; Glyph of Scatter Shot: +3)
    2974, -- ["Wing Clip"], -- 5
}

FriendSpells["MAGE"] = {
    475, -- ["Remove Curse"], -- 40 (Magic Attunement: 43, 46)
    1459, -- ["Arcane Intellect"], -- 30 (Magic Attunement: 33, 36)
}
HarmSpells["MAGE"] = {
    44614, -- ["Frostfire Bolt"], -- 40
    133, -- ["Fireball"], -- 35 (Flame Throwing: 38, 41)
    116, -- ["Frostbolt"], -- 30 (Arctic Reach: 33, 36)
    30455, -- ["Ice Lance"], -- 30 (Arctic Reach: 33, 36, Glyph of Ice Lance: +5)
    5143, -- ["Arcane Missiles"], -- 30 (Magic Attunement: 33, 36; Glyph of Arcane Missiles: +5)
    30451, -- ["Arcane Blast"], -- 30 (Magic Attunement: 33, 36)
    2948, -- ["Scorch"], -- 30 (Flame Throwing: 33, 36)
    5019, -- ["Shoot"], -- 30
    2136, -- ["Fire Blast"], -- 20 (Flame Throwing: 23, 26; Gladiator Gloves: +5)
}

FriendSpells["PALADIN"] = {
    635, -- ["Holy Light"], -- 40
    19740, -- ["Blessing of Might"], -- 30
    20473, -- ["Holy Shock"], -- 20
}
HarmSpells["PALADIN"] = {
    24275, -- ["Hammer of Wrath"],  -- 30 (Glyph of Hammer of Wrath: +5)
    20473, -- ["Holy Shock"], -- 20
    20271, -- ["Judgement"], -- 10
    35395, -- ["Crusader Strike"], -- 5
} 

FriendSpells["PRIEST"] = {
    2050, -- ["Lesser Heal"], -- 40
    1243, -- ["Power Word: Fortitude"], -- 30
}
HarmSpells["PRIEST"] = {
    585, -- ["Smite"], -- 30 (Holy Reach: 33, 36)
    589, -- ["Shadow Word: Pain"], -- 30 (Shadow Reach: 33, 36)
    5019, -- ["Shoot"], -- 30
    15407, -- ["Mind Flay"], -- 20 (Shadow Reach: 22, 24, Glyph of Mind Flay: +10)
}

FriendSpells["ROGUE"] = {}
HarmSpells["ROGUE"] = {
    2764, -- ["Throw"], -- 30
    26679, -- ["Deadly Throw"], -- 30 (Glyph of Deadly Throw: +5)
    2094, -- ["Blind"], -- 10 (Dirty Tricks: 12, 15)
    2098, -- ["Eviscerate"], -- 5
}

FriendSpells["SHAMAN"] = {
    331, -- ["Healing Wave"], -- 40
    526, -- ["Cure Poison"], -- 30
}
HarmSpells["SHAMAN"] = {
    403, -- ["Lightning Bolt"], -- 30 (Storm Reach: 33, 36)
    370, -- ["Purge"], -- 30
    8050, -- ["Flame Shock"], -- 20 (Elemental Reach: 27, 35; Gladiator Gloves: +5)
--    8042, -- ["Earth Shock"], -- 20 (Storm, Earth and Fire: 21-25; Gladiator Gloves: +5)
    8056, -- ["Frost Shock"], -- 20 (Gladiator Gloves: +5)
}

FriendSpells["WARRIOR"] = {}
HarmSpells["WARRIOR"] = {
    100, -- ["Charge"], -- 8-25 (Glyph of Charge: +5)
    3018, -- ["Shoot"], -- 30
    2764, -- ["Throw"], -- 30
    355, -- ["Taunt"], -- 30
    5246, -- ["Intimidating Shout"], -- 8
    772, -- ["Rend"], -- 5
}

FriendSpells["WARLOCK"] = {
    5697, -- ["Unending Breath"], -- 30 (demo)
}
HarmSpells["WARLOCK"] = {
    5019, -- ["Shoot"], -- 30
    348, -- ["Immolate"], -- 30 (Destructive Reach: 33, 36)
    172, -- ["Corruption"], -- 30 (Grim Reach: 33, 36)
    18223, -- ["Curse of Exhaustion"], -- 30 (Grim Reach: 33, 36, Glyph of Curse of Exhaustion: +5)
    5782, -- ["Fear"], -- 20 (Grim Reach: 22, 24)
    17877, -- ["Shadowburn"], -- 20 (Destructive Reach: 22, 24)
}

FriendSpells["DEATHKNIGHT"] = {
}
HarmSpells["DEATHKNIGHT"] = {
    47541, -- ["Death Coil"], -- 30
    47476, -- ["Strangulate"], -- 30 (Glyph of Strangulate: +20)
    45477, -- ["Icy Touch"], -- 20 (Icy Reach: 25, 30)
    56222, -- ["Dark Command"], -- 20
    50842, -- ["Pestilence"], -- 5
    45902, -- ["Blood Strike"], -- 5, but requires weapon, use Pestilence if possible, so keep it after Pestilence in this list
}

-- Items [Special thanks to Maldivia for the nice list]

local FriendItems  = {
    [5] = {
        37727, -- Ruby Acorn
    },
    [8] = {
        34368, -- Attuned Crystal Cores
        33278, -- Burning Torch
    },
    [10] = {
        32321, -- Sparrowhawk Net
    },
    [15] = {
        1251, -- Linen Bandage
        2581, -- Heavy Linen Bandage
        3530, -- Wool Bandage
        3531, -- Heavy Wool Bandage
        6450, -- Silk Bandage
        6451, -- Heavy Silk Bandage
        8544, -- Mageweave Bandage
        8545, -- Heavy Mageweave Bandage
        14529, -- Runecloth Bandage
        14530, -- Heavy Runecloth Bandage
        21990, -- Netherweave Bandage
        21991, -- Heavy Netherweave Bandage
        34721, -- Frostweave Bandage
        34722, -- Heavy Frostweave Bandage
--        38643, -- Thick Frostweave Bandage
--        38640, -- Dense Frostweave Bandage
    },
    [20] = {
        21519, -- Mistletoe
    },
    [25] = {
        31463, -- Zezzak's Shard
    },
    [30] = {
        1180, -- Scroll of Stamina
        1478, -- Scroll of Protection II
        3012, -- Scroll of Agility
        1712, -- Scroll of Spirit II
        2290, -- Scroll of Intellect II
        1711, -- Scroll of Stamina II
        34191, -- Handful of Snowflakes
    },
    [35] = {
        18904, -- Zorbin's Ultra-Shrinker
    },
    [40] = {
        34471, -- Vial of the Sunwell
    },
    [45] = {
        32698, -- Wrangling Rope
    },
    [60] = {
        32825, -- Soul Cannon
        37887, -- Seeds of Nature's Wrath
    },
    [80] = {
        35278, -- Reinforced Net
    },
}

local HarmItems = {
    [5] = {
        37727, -- Ruby Acorn
    },
    [8] = {
        34368, -- Attuned Crystal Cores
        33278, -- Burning Torch
    },
    [10] = {
        32321, -- Sparrowhawk Net
    },
    [15] = {
        33069, -- Sturdy Rope
    },
    [20] = {
        10645, -- Gnomish Death Ray
    },
    [25] = {
        24268, -- Netherweave Net
        41509, -- Frostweave Net
        31463, -- Zezzak's Shard
    },
    [30] = {
        835, -- Large Rope Net
        7734, -- Six Demon Bag
        34191, -- Handful of Snowflakes
    },
    [35] = {
        24269, -- Heavy Netherweave Net
        18904, -- Zorbin's Ultra-Shrinker
    },
    [40] = {
        28767, -- The Decapitator
    },
    [45] = {
        32698, -- Wrangling Rope
    },
    [60] = {
        32825, -- Soul Cannon
        37887, -- Seeds of Nature's Wrath
    },
    [80] = {
        35278, -- Reinforced Net
    },
}

-- This could've been done by checking player race as well and creating tables for those, but it's easier like this
for k, v in pairs(FriendSpells) do
    tinsert(v, 28880) -- ["Gift of the Naaru"]
end
for k, v in pairs(HarmSpells) do
    tinsert(v, 28734) -- ["Mana Tap"]
end

-- >> END OF STATIC CONFIG

-- cache

local setmetatable = setmetatable
local tonumber = tonumber
local pairs = pairs
local tostring = tostring
local print = print
local next = next
local type = type
local wipe = wipe
local tinsert = tinsert
local tremove = tremove
local BOOKTYPE_SPELL = BOOKTYPE_SPELL
local GetSpellInfo = GetSpellInfo
local GetSpellName = GetSpellName
local GetItemInfo = GetItemInfo
local UnitCanAttack = UnitCanAttack
local UnitCanAssist = UnitCanAssist
local UnitExists = UnitExists
local UnitIsDeadOrGhost = UnitIsDeadOrGhost
local CheckInteractDistance = CheckInteractDistance
local IsSpellInRange = IsSpellInRange
local IsItemInRange = IsItemInRange
local UnitClass = UnitClass
local UnitRace = UnitRace
local GetInventoryItemLink = GetInventoryItemLink
local GetTime = GetTime
local HandSlotId = GetInventorySlotInfo("HandsSlot")
local TT = ItemRefTooltip

-- temporary stuff

local itemRequestTimeoutAt
local foundNewItems
local cacheAllItems
local friendItemRequests
local harmItemRequests
local lastUpdate = 0

-- minRangeCheck is a function to check if spells with minimum range are really out of range, or fail due to range < minRange. See :init() for its setup
local minRangeCheck = function(unit) return CheckInteractDistance(unit, 2) end

local checkers_Spell = setmetatable({}, {
    __index = function(t, spellIdx)
        local func = function(unit)
            if IsSpellInRange(spellIdx, BOOKTYPE_SPELL, unit) == 1 then
                 return true
            end
        end
        t[spellIdx] = func
        return func
    end
})
local checkers_SpellWithMin = setmetatable({}, {
    __index = function(t, spellIdx)
        local func = function(unit)
            if IsSpellInRange(spellIdx, BOOKTYPE_SPELL, unit) == 1 then
                return true
            elseif minRangeCheck(unit) then
                return true, true
            end
        end
        t[spellIdx] = func
        return func
    end
})
local checkers_Item = setmetatable({}, {
    __index = function(t, item)
        local func = function(unit)
            if IsItemInRange(item, unit) == 1 then
                 return true
            end
        end
        t[item] = func
        return func
    end
})
local checkers_Interact = setmetatable({}, {
    __index = function(t, index)
        local func = function(unit)
            if CheckInteractDistance(unit, index) then
                 return true
            end
        end
        t[index] = func
        return func
    end
})

-- helper functions

local function copyTable(src, dst)
    if type(dst) ~= "table" then dst = {} end
    if type(src) == "table" then
        for k, v in pairs(src) do
            if type(v) == "table" then
                v = copyTable(v, dst[k])
            end
            dst[k] = v
        end
    end
    return dst
end


local function initItemRequests(cacheAll)
    friendItemRequests = copyTable(FriendItems)
    harmItemRequests = copyTable(HarmItems)
    cacheAllItems = cacheAll
    foundNewItems = nil
end

local function requestItemInfo(itemId)
    if not itemId then return end
    TT:SetHyperlink(string.format("item:%d", itemId))
end

-- return the spellIndex of the given spell by scanning the spellbook
local function findSpellIdx(spellName)
    local i = 1
    while true do
        local spell, rank = GetSpellName(i, BOOKTYPE_SPELL)
        if not spell then return nil end
        if spell == spellName then return i end
        i = i + 1
    end
    return nil
end

-- minRange should be nil if there's no minRange, not 0
local function addChecker(t, range, minRange, checker)
    local rc = { ["range"] = range, ["minRange"] = minRange, ["checker"] = checker }
    for i = 1, #t do
        local v = t[i]
        if rc.range == v.range then return end
        if rc.range > v.range then
            tinsert(t, i, rc)
            return
        end
    end
    tinsert(t, rc)
end

local function createCheckerList(spellList, itemList, interactList)
    local res = {}
    if spellList then
        for i = 1, #spellList do
            local sid = spellList[i]
            local name, _, _, _, _, _, _, minRange, range = GetSpellInfo(sid)
            local spellIdx = findSpellIdx(name)
            if spellIdx and range then
                minRange = math.floor(minRange + 0.5)
                range = math.floor(range + 0.5)
                -- print("### spell: " .. tostring(name) .. ", " .. tostring(minRange) .. " - " ..  tostring(range))
                if minRange == 0 then -- getRange() expects minRange to be nil in this case
                    minRange = nil
                end
                if range == 0 then
                    range = MeleeRange
                end
                if minRange then
                    addChecker(res, range, minRange, checkers_SpellWithMin[spellIdx])
                else
                    addChecker(res, range, minRange, checkers_Spell[spellIdx])
                end
            end
        end
    end
    
    if itemList then
        for range, items in pairs(itemList) do
            for i = 1, #items do
                local item = items[i]
                if GetItemInfo(item) then
                    addChecker(res, range, nil, checkers_Item[item])
                    break
                end
            end
        end
    end
    
    if interactList and not next(res) then
        for index, range in pairs(interactList) do
            addChecker(res, range, nil,  checkers_Interact[index])
        end
    end

    return res
end

-- returns minRange, maxRange  or nil
local function getRange(unit, checkerList)
    local min, max = 0, nil
    for i = 1, #checkerList do
        local rc = checkerList[i]
        if not max or max > rc.range then
            if rc.minRange then
                local inRange, inMinRange = rc.checker(unit)
                if inMinRange then
                    max = rc.minRange
                elseif inRange then
                    min, max = rc.minRange, rc.range
                elseif min > rc.range then
                    return min, max
                else
                    return rc.range, max
                end
            elseif rc.checker(unit) then
                max = rc.range
            elseif min > rc.range then
                return min, max
            else
                return rc.range, max
            end
        end
    end
    return min, max
end

local function updateCheckers(origList, newList)
    if #origList ~= #newList then
        wipe(origList)
        copyTable(newList, origList)
        return true
    end
    for i = 1, #origList do
        if origList[i].range ~= newList[i].range or origList[i].checker ~= newList[i].checker then
            wipe(origList)
            copyTable(newList, origList)
            return true
        end
    end
end

local function rcIterator(checkerList)
    local curr = #checkerList
    return function()
        local rc = checkerList[curr]
        if not rc then
             return nil
        end
        curr = curr - 1
        return rc.range, rc.checker
    end
end

local function getMinChecker(checkerList, range)
    local checker, checkerRange
    for i = 1, #checkerList do
        local rc = checkerList[i]
        if rc.range < range then
            return checker, checkerRange
        end
        checker, checkerRange = rc.checker, rc.range
    end
    return checker, checkerRange
end

local function getMaxChecker(checkerList, range)
    for i = 1, #checkerList do
        local rc = checkerList[i]
        if rc.range <= range then
            return rc.checker, rc.range
        end
    end
end

local function getChecker(checkerList, range)
    for i = 1, #checkerList do
        local rc = checkerList[i]
        if rc.range == range then
            return rc.checker
        end
    end
end

local function null()
end

local function createSmartChecker(friendChecker, harmChecker, miscChecker)
    miscChecker = miscChecker or null
    friendChecker = friendChecker or miscChecker
    harmChecker = harmChecker or miscChecker
    return function(unit)
        if not UnitExists(unit) then
            return nil
        end
        if UnitIsDeadOrGhost(unit) then
            return miscChecker(unit)
        end
        if UnitCanAttack("player", unit) then
            return harmChecker(unit)
        elseif UnitCanAssist("player", unit) then
            return friendChecker(unit)
        else
            return miscChecker(unit)
        end
    end
end


-- OK, here comes the actual lib

-- pre-initialize the checkerLists here so that we can return some meaningful result even if
-- someone manages to call us before we're properly initialized. miscRC should be independent of
-- race/class/talents, so it's safe to initialize it here
-- friendRC and harmRC will be properly initialized later when we have all the necessary data for them
lib.checkerCache_Spell = lib.checkerCache_Spell or {}
lib.checkerCache_Item = lib.checkerCache_Item or {}
lib.miscRC = createCheckerList(nil, nil, DefaultInteractList)
lib.friendRC = createCheckerList(nil, nil, DefaultInteractList)
lib.harmRC = createCheckerList(nil, nil, DefaultInteractList)

lib.failedItemRequests = {}

-- << Public API

 

--- The callback name that is fired when checkers are changed.
-- @field
lib.CHECKERS_CHANGED = "CHECKERS_CHANGED"
-- "export" it, maybe someone will need it for formatting
--- Constant for Melee range (5yd).
-- @field
lib.MeleeRange = MeleeRange

function lib:findSpellIndex(spell)
    if type(spell) == 'number' then
        spell = GetSpellInfo(spell)
    end
    if not spell then return nil end
    return findSpellIdx(spell)
end

-- returns the range estimate as a string
-- deprecated, use :getRange(unit) instead and build your own strings
-- (checkVisible is not used any more, kept for compatibility only)
function lib:getRangeAsString(unit, checkVisible, showOutOfRange)
    local minRange, maxRange = self:getRange(unit)
    if not minRange then return nil end
    if not maxRange then
        return showOutOfRange and minRange .. " +" or nil
    end
    return minRange .. " - " .. maxRange
end

-- initialize RangeCheck if not yet initialized or if "forced"
function lib:init(forced)
    if self.initialized and (not forced) then return end
    self.initialized = true
    local _, playerClass = UnitClass("player")
    local _, playerRace = UnitRace("player")

    minRangeCheck = nil
    -- first try to find a nice item we can use for minRangeCheck
    if HarmItems[15] then
        local items = HarmItems[15]
        for i = 1, #items do
            local item = items[i]
            if GetItemInfo(item) then
                minRangeCheck = function(unit)
                    return (IsItemInRange(item, unit) == 1)
                end
                break
            end
        end
    end
    if not minRangeCheck then
        -- ok, then try to find some class specific spell
        if playerClass == "WARRIOR" then
            -- for warriors, use Intimidating Shout if available
            local name = GetSpellInfo(5246) -- ["Intimidating Shout"]
            local spellIdx = findSpellIdx(name)
            if spellIdx then
                minRangeCheck = function(unit)
                    return (IsSpellInRange(spellIdx, BOOKTYPE_SPELL, unit) == 1)
                end
            end
        elseif playerClass == "ROGUE" then
            -- for rogues, use Blind if available
            local name = GetSpellInfo(2094) -- ["Blind"]
            local spellIdx = findSpellIdx(name)
            if spellIdx then
                minRangeCheck = function(unit)
                    return (IsSpellInRange(spellIdx, BOOKTYPE_SPELL, unit) == 1)
                end
            end
        end
    end
    if not minRangeCheck then
        -- fall back to interact distance checks
        if playerClass == "HUNTER" or playerRace == "Tauren" then
            -- for hunters, use interact4 as it's safer
            -- for Taurens interact4 is actually closer than 25yd and interact2 is closer than 8yd, so we can't use that
            minRangeCheck = checkers_Interact[4]
        else
            minRangeCheck = checkers_Interact[2]
        end
    end

    local interactList = InteractLists[playerRace] or DefaultInteractList
    self.handSlotItem = GetInventoryItemLink("player", HandSlotId)
    local changed = false
    if updateCheckers(self.friendRC, createCheckerList(FriendSpells[playerClass], FriendItems, interactList)) then
        changed = true
    end
    if updateCheckers(self.harmRC, createCheckerList(HarmSpells[playerClass], HarmItems, interactList)) then
        changed = true
    end
    if updateCheckers(self.miscRC, createCheckerList(nil, nil, interactList)) then
        changed = true
    end
    if changed and self.callbacks then
        self.callbacks:Fire(self.CHECKERS_CHANGED)
    end
end

--- Return an iterator for checkers usable on friendly units as (**range**, **checker**) pairs.
function lib:GetFriendCheckers()
    return rcIterator(self.friendRC)
end

--- Return an iterator for checkers usable on enemy units as (**range**, **checker**) pairs.
function lib:GetHarmCheckers()
    return rcIterator(self.harmRC)
end

--- Return an iterator for checkers usable on miscellaneous units as (**range**, **checker**) pairs.  These units are neither enemy nor friendly, such as people in sanctuaries or corpses.
function lib:GetMiscCheckers()
    return rcIterator(self.miscRC)
end

--- Return a checker suitable for out-of-range checking on friendly units, that is, a checker whose range is equal or larger than the requested range.
-- @param range the range to check for.
-- @return **checker**, **range** pair or **nil** if no suitable checker is available. **range** is the actual range the returned **checker** checks for.
function lib:GetFriendMinChecker(range)
    return getMinChecker(self.friendRC, range)
end

--- Return a checker suitable for out-of-range checking on enemy units, that is, a checker whose range is equal or larger than the requested range.
-- @param range the range to check for.
-- @return **checker**, **range** pair or **nil** if no suitable checker is available. **range** is the actual range the returned **checker** checks for.
function lib:GetHarmMinChecker(range)
    return getMinChecker(self.harmRC, range)
end

--- Return a checker suitable for out-of-range checking on miscellaneous units, that is, a checker whose range is equal or larger than the requested range.
-- @param range the range to check for.
-- @return **checker**, **range** pair or **nil** if no suitable checker is available. **range** is the actual range the returned **checker** checks for.
function lib:GetMiscMinChecker(range)
    return getMinChecker(self.miscRC, range)
end

--- Return a checker suitable for in-range checking on friendly units, that is, a checker whose range is equal or smaller than the requested range.
-- @param range the range to check for.
-- @return **checker**, **range** pair or **nil** if no suitable checker is available. **range** is the actual range the returned **checker** checks for.
function lib:GetFriendMaxChecker(range)
    return getMaxChecker(self.friendRC, range)
end

--- Return a checker suitable for in-range checking on enemy units, that is, a checker whose range is equal or smaller than the requested range.
-- @param range the range to check for.
-- @return **checker**, **range** pair or **nil** if no suitable checker is available. **range** is the actual range the returned **checker** checks for.
function lib:GetHarmMaxChecker(range)
    return getMaxChecker(self.harmRC, range)
end

--- Return a checker suitable for in-range checking on miscellaneous units, that is, a checker whose range is equal or smaller than the requested range.
-- @param range the range to check for.
-- @return **checker**, **range** pair or **nil** if no suitable checker is available. **range** is the actual range the returned **checker** checks for.
function lib:GetMiscMaxChecker(range)
    return getMaxChecker(self.miscRC, range)
end

--- Return a checker for the given range for friendly units.
-- @param range the range to check for.
-- @return **checker** function or **nil** if no suitable checker is available.
function lib:GetFriendChecker(range)
    return getChecker(self.friendRC, range)
end

--- Return a checker for the given range for enemy units.
-- @param range the range to check for.
-- @return **checker** function or **nil** if no suitable checker is available.
function lib:GetHarmChecker(range)
    return getChecker(self.harmRC, range)
end

--- Return a checker for the given range for miscellaneous units.
-- @param range the range to check for.
-- @return **checker** function or **nil** if no suitable checker is available.
function lib:GetMiscChecker(range)
    return getChecker(self.miscRC, range)
end

--- Return a checker suitable for out-of-range checking that checks the unit type and calls the appropriate checker (friend/harm/misc).
-- @param range the range to check for.
-- @return **checker** function.
function lib:GetSmartMinChecker(range)
    return createSmartChecker(
        getMinChecker(self.friendRC, range),
        getMinChecker(self.harmRC, range),
        getMinChecker(self.miscRC, range))
end

--- Return a checker suitable for in-of-range checking that checks the unit type and calls the appropriate checker (friend/harm/misc).
-- @param range the range to check for.
-- @return **checker** function.
function lib:GetSmartMaxChecker(range)
    return createSmartChecker(
        getMaxChecker(self.friendRC, range),
        getMaxChecker(self.harmRC, range),
        getMaxChecker(self.miscRC, range))
end

--- Return a checker for the given range that checks the unit type and calls the appropriate checker (friend/harm/misc).
-- @param range the range to check for.
-- @param fallback optional fallback function that gets called as fallback(unit) if a checker is not available for the given type (friend/harm/misc) at the requested range. The default fallback function return nil.
-- @return **checker** function.
function lib:GetSmartChecker(range, fallback)
    return createSmartChecker(
        getChecker(self.friendRC, range) or fallback,
        getChecker(self.harmRC, range) or fallback,
        getChecker(self.miscRC, range) or fallback)
end

--- Get a range estimate as **minRange**, **maxRange**.
-- @param unit the target unit to check range to.
-- @return **minRange**, **maxRange** pair if a range estimate could be determined, **nil** otherwise. **maxRange** is **nil** if **unit** is further away than the highest possible range we can check.
-- Includes checks for unit validity and friendly/enemy status.
-- @usage
-- local rc = LibStub("LibRangeCheck-2.0")
-- local minRange, maxRange = rc:GetRange('target')
function lib:GetRange(unit)
    if not UnitExists(unit) then
        return nil
    end
    if UnitIsDeadOrGhost(unit) then
        return getRange(unit, self.miscRC)
    end
    if UnitCanAttack("player", unit) then
        return getRange(unit, self.harmRC)
    elseif UnitCanAssist("player", unit) then
        return getRange(unit, self.friendRC)
    else
        return getRange(unit, self.miscRC)
    end
end

-- keep this for compatibility
lib.getRange = lib.GetRange

-- >> Public API

function lib:OnEvent(event, ...)
    if type(self[event]) == 'function' then
        self[event](self, event, ...)
    end
end

function lib:LEARNED_SPELL_IN_TAB()
    self:scheduleInit()
end

function lib:CHARACTER_POINTS_CHANGED()
    self:scheduleInit()
end

function lib:PLAYER_TALENT_UPDATE()
    self:scheduleInit()
end

function lib:GLYPH_ADDED()
    self:scheduleInit()
end

function lib:GLYPH_REMOVED()
    self:scheduleInit()
end

function lib:GLYPH_UPDATED()
    self:scheduleInit()
end

function lib:UNIT_INVENTORY_CHANGED(event, unit)
    if self.initialized and unit == "player" and self.handSlotItem ~= GetInventoryItemLink("player", HandSlotId) then
        self:scheduleInit()
    end
end

function lib:processItemRequests(itemRequests)
    while true do
        local range, items = next(itemRequests)
        if not range then return end
        while true do
            local i, item = next(items)
            if not i then
                itemRequests[range] = nil
                break
            elseif self.failedItemRequests[item] then
                tremove(items, i)
            elseif GetItemInfo(item) then
                if itemRequestTimeoutAt then
                    foundNewItems = true
                    itemRequestTimeoutAt = nil
                end
                if not cacheAllItems then
                    itemRequests[range] = nil
                    break
                end
                tremove(items, i)   
            elseif not itemRequestTimeoutAt then
                requestItemInfo(item)
                itemRequestTimeoutAt = GetTime() + ItemRequestTimeout
                return true
            elseif GetTime() > itemRequestTimeoutAt then
                if cacheAllItems then
                    print(MAJOR_VERSION .. ": timeout for item: " .. tostring(item))
                end
                self.failedItemRequests[item] = true
                itemRequestTimeoutAt = nil
                tremove(items, i)
            else
                return true -- still waiting for server response
            end
        end
    end
end

function lib:initialOnUpdate()
    self:init()
    if friendItemRequests then
        if self:processItemRequests(friendItemRequests) then return end
        friendItemRequests = nil
    end
    if harmItemRequests then
        if self:processItemRequests(harmItemRequests) then return end
        harmItemRequests = nil
    end
    if foundNewItems then
        self:init(true)
        foundNewItems = nil
    end
    if cacheAllItems then
        print(MAJOR_VERSION .. ": finished cache")
        cacheAllItems = nil
    end
    self.frame:Hide()
end

function lib:scheduleInit()
    self.initialized = nil
    lastUpdate = 0
    self.frame:Show()
end

 

-- << load-time initialization 

function lib:activate()
    if not self.frame then
        local frame = CreateFrame("Frame")
        self.frame = frame
        frame:RegisterEvent("LEARNED_SPELL_IN_TAB")
        frame:RegisterEvent("CHARACTER_POINTS_CHANGED")
        frame:RegisterEvent("PLAYER_TALENT_UPDATE")
        frame:RegisterEvent("GLYPH_ADDED")
        frame:RegisterEvent("GLYPH_REMOVED")
        frame:RegisterEvent("GLYPH_UPDATED")
        local _, playerClass = UnitClass("player")
        if playerClass == "MAGE" or playerClass == "SHAMAN" then
            -- Mage and Shaman gladiator gloves modify spell ranges
            frame:RegisterEvent("UNIT_INVENTORY_CHANGED")
        end
    end
    initItemRequests()
    self.frame:SetScript("OnEvent", function(frame, ...) self:OnEvent(...) end)
    self.frame:SetScript("OnUpdate", function(frame, elapsed)
        lastUpdate = lastUpdate + elapsed
        if lastUpdate < UpdateDelay then
            return
        end
        lastUpdate = 0
        self:initialOnUpdate()
    end)
    self:scheduleInit()
end

--- BEGIN CallbackHandler stuff

do
    local lib = lib -- to keep a ref even though later we nil lib
    --- Register a callback to get called when checkers are updated
    -- @class function
    -- @name lib.RegisterCallback
    -- @usage
    -- rc.RegisterCallback(self, rc.CHECKERS_CHANGED, "myCallback")
    -- -- or
    -- rc.RegisterCallback(self, "CHECKERS_CHANGED", someCallbackFunction)
    -- @see CallbackHandler-1.0 documentation for more details
    lib.RegisterCallback = lib.RegisterCallback or function(...)
        local CBH = LibStub("CallbackHandler-1.0")
        lib.RegisterCallback = nil -- extra safety, we shouldn't get this far if CBH is not found, but better an error later than an infinite recursion now
        lib.callbacks = CBH:New(lib)
        -- ok, CBH hopefully injected or new shiny RegisterCallback
        return lib.RegisterCallback(...)
    end
end

--- END CallbackHandler stuff

lib:activate()
lib = nil
