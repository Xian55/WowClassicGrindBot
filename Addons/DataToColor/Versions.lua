local Load = select(2, ...)
local DataToColor = unpack(Load)

local GetBuildInfo = GetBuildInfo

local UnitIsUnit = UnitIsUnit
local UnitLevel = UnitLevel

local UnitChannelInfo = UnitChannelInfo
local UnitCastingInfo = UnitCastingInfo
local GetSpellInfo = GetSpellInfo

local WOW_PROJECT_ID = WOW_PROJECT_ID or -1 -- -1 = Legacy client (old retail)
local WOW_PROJECT_CLASSIC = WOW_PROJECT_CLASSIC
local WOW_PROJECT_BURNING_CRUSADE_CLASSIC = WOW_PROJECT_BURNING_CRUSADE_CLASSIC
local WOW_PROJECT_WRATH_CLASSIC = WOW_PROJECT_WRATH_CLASSIC
local WOW_PROJECT_CATACLYSM_CLASSIC = WOW_PROJECT_CATACLYSM_CLASSIC
local WOW_PROJECT_MAINLINE = WOW_PROJECT_MAINLINE

local buildVersion = select(4, GetBuildInfo())
local isVanilla = buildVersion < 20000

-- Is this a Legacy client (old retail, e.g., Cataclysm 4.3.4)?
function DataToColor.IsLegacy()
  return WOW_PROJECT_ID == -1
end

function DataToColor.IsVanilla()
  return isVanilla
end

-- Is this a Classic-era client (any of the Classic versions)?
function DataToColor.IsClassicEra()
  return WOW_PROJECT_ID ~= -1
end

function DataToColor.IsClassic()
  return WOW_PROJECT_ID == WOW_PROJECT_CLASSIC
end

function DataToColor.IsClassic_BCC()
  return WOW_PROJECT_ID == WOW_PROJECT_BURNING_CRUSADE_CLASSIC
end

function DataToColor.IsClassic_Wrath()
  return WOW_PROJECT_ID == WOW_PROJECT_WRATH_CLASSIC
end

function DataToColor.IsClassic_Cata()
  return WOW_PROJECT_ID == WOW_PROJECT_CATACLYSM_CLASSIC
end

function DataToColor.IsRetail()
  return WOW_PROJECT_ID == WOW_PROJECT_MAINLINE
end

function DataToColor.IsClassicPreCata()
  return DataToColor.IsClassic() or DataToColor.IsClassic_BCC() or DataToColor.IsClassic_Wrath()
end

local LibClassicCasterino
if DataToColor.IsClassic() then
  LibClassicCasterino = _G.LibStub("LibClassicCasterino")
  LibClassicCasterino.callbacks:OnUsed()
end

local Som140 = DataToColor.IsClassic() and buildVersion == 11400 or buildVersion == 11401 or buildVersion == 11402
local TBC253 = DataToColor.IsClassic_BCC() and buildVersion >= 20503
local TBC252 = DataToColor.IsClassic_BCC() and buildVersion >= 20502
local Wrath340 = DataToColor.IsClassic_Wrath() and buildVersion >= 30400
local Cata440 = DataToColor.IsClassic_Cata() and buildVersion >= 40400

--------------------------------------------------------------------------------
-- CLIENT VERSION ASSIGNMENT
--------------------------------------------------------------------------------

if DataToColor.IsLegacy() then
  DataToColor.ClientVersion = 90 + math.floor(buildVersion / 10000)
else
  DataToColor.ClientVersion = WOW_PROJECT_ID
end

--------------------------------------------------------------------------------
-- API COMPATIBILITY WRAPPERS
--------------------------------------------------------------------------------

------------------------------------------------------------
-- UnitCastingInfo (no allocations, cross-version safe)
------------------------------------------------------------
if DataToColor.IsLegacy() then
    local S = DataToColor.S
    -- Fast path: built-in function exists
    function DataToColor.UnitCastingInfo(unit)
        local n1, n2, n3, n4, n5, n6, n7, n8, n9 = UnitCastingInfo(unit)
        -- Legacy (e.g., 4.3.4) may not return spellId (n9)
        if not n9 and n4 and S and S.playerSpellBookIconToId then
          n4 = DataToColor:NormalizeTexture(n4)
          n9 = S.playerSpellBookIconToId[n4] or 0
        end
        return n1, n2, n3, n4, n5, n6, n7, n8, n9
    end
else
  function DataToColor.UnitCastingInfo(unit)
    local n1, n2, n3, n4, n5, n6, n7, n8, n9
    if LibClassicCasterino then
      n1, n2, n3, n4, n5, n6, n7, n8, n9 = LibClassicCasterino:UnitCastingInfo(unit)
    else
      n1, n2, n3, n4, n5, n6, n7, n8, n9 = UnitCastingInfo(unit)
    end

    if not n9 then
      return n1, n2, n3, n4, n5, n6, n7, nil, n8
    end
    return n1, n2, n3, n4, n5, n6, n7, n8, n9
  end
end


------------------------------------------------------------
-- UnitChannelInfo (no allocations, cross-version safe)
------------------------------------------------------------

local S = DataToColor.S
if DataToColor.IsLegacy() then
    function DataToColor.UnitChannelInfo(unit)
        local n1, n2, n3, n4, n5, n6, n7, n8 = UnitChannelInfo(unit)
        -- Legacy (e.g., 4.3.4) may not return spellId (n8)
        if not n8 and n4 and S and S.playerSpellBookIconToId then
          n4 = DataToColor:NormalizeTexture(n4)
          n8 = S.playerSpellBookIconToId[n4] or 0
        end

        return n1, n2, n3, n4, n5, n6, n7, n8
    end
else
  function DataToColor.UnitChannelInfo(unit)
    local n1, n2, n3, n4, n5, n6, n7, n8
    if LibClassicCasterino then
      n1, n2, n3, n4, n5, n6, n7, n8 =  LibClassicCasterino:UnitChannelInfo(unit)
    else
      n1, n2, n3, n4, n5, n6, n7, n8 = UnitChannelInfo(unit)
    end

    if not n8 then
      return n1, n2, n3, n4, n5, n6, nil, n7
    end
    return n1, n2, n3, n4, n5, n6, n7, n8
  end
end

------------------------------------------------------------
-- GetSpellInfo return layout
-- Long (vanilla through 5.4.8, nine values):
--   name, rank, icon, cost, isFunnel, powerType, castTime, minRange, maxRange
-- Short (4.x Classic re-releases onwards, six or seven):
--   name, rank, icon, castTime, minRange, maxRange[, spellID]
-- Probed, not derived from the build: 5.4.8 keeps the long shape even though it
-- postdates the 4.0 that supposedly dropped it, and reading castTime at index 4
-- there returns the cost instead - a 2000ms Steady Shot reported as 35.
-- Attack (6603) exists on every client this addon supports.
------------------------------------------------------------
local SPELL_INFO_PROBE_SPELL_ID = 6603
local LONG_SPELL_INFO_RETURNS = 9

local hasLongSpellInfo =
    select('#', GetSpellInfo(SPELL_INFO_PROBE_SPELL_ID)) >= LONG_SPELL_INFO_RETURNS

if hasLongSpellInfo then
    function DataToColor.GetSpellCastTime(spellID)
        if not spellID then return 0 end
        return (select(7, GetSpellInfo(spellID))) or 0
    end
else
    function DataToColor.GetSpellCastTime(spellID)
        if not spellID then return 0 end
        return (select(4, GetSpellInfo(spellID))) or 0
    end
end

------------------------------------------------------------
-- GetSpellPowerCost (6.0+)
-- Query.lua:populateActionbarCost walks the result with ipairs and reads
-- .cost/.type, so every fallback must return an ARRAY of those entries -
-- a flat { cost = , powerType = } table iterates zero times and every slot
-- silently reports the zero-cost mana default.
------------------------------------------------------------

if GetSpellPowerCost then
    DataToColor.GetSpellPowerCost = GetSpellPowerCost
elseif hasLongSpellInfo then
    -- Reused across calls: this runs per action bar slot on every
    -- ACTIONBAR_SLOT_CHANGED and the caller only reads it inside the loop.
    local entry = { cost = 0, type = 0 }
    local result = { entry }

    DataToColor.GetSpellPowerCost = function(spellID)
        -- name, rank, icon, cost, isFunnel, powerType, castTime, ...
        local cost, _, powerType = select(4, GetSpellInfo(spellID))
        if not cost then return nil end
        entry.cost = cost
        entry.type = powerType or 0
        return result
    end
else
    -- 4.3.4 / 5.4.8: no API carries the cost, the spell tooltip is the only
    -- place it appears ("30 Energy" on the first right-hand line). Flat costs
    -- resolve; "x% of base mana" does not match a power name and is left to
    -- the zero-cost default, since base mana is not queryable either.
    --
    -- Power names come from the client's own global strings, so this works on
    -- any locale. The numbers are what Core/AddonComponent/PowerType.cs
    -- expects BEFORE offsetEnumPowerType is added.
    local POWER_TYPE_BY_GLOBAL_STRING = {
        MANA = 0,
        RAGE = 1,
        FOCUS = 2,
        ENERGY = 3,
        RUNES = 5,
        RUNIC_POWER = 6,
        SOUL_SHARDS = 7,
        HOLY_POWER = 9,
        CHI = 12,
        COMBO_POINTS = 14,
    }

    -- Longest name first: a short name must never win over a longer one that
    -- also matches the same line.
    local powerNames = {}
    for globalString, powerType in pairs(POWER_TYPE_BY_GLOBAL_STRING) do
        local localized = _G[globalString]
        if type(localized) == "string" and localized ~= "" then
            powerNames[#powerNames + 1] = { name = localized, type = powerType }
        end
    end
    table.sort(powerNames, function(a, b) return #a.name > #b.name end)

    local SCANNER_NAME = "DataToColorCostScanner"
    local scanner = CreateFrame("GameTooltip", SCANNER_NAME, nil, "GameTooltipTemplate")
    scanner:SetOwner(UIParent, "ANCHOR_NONE")

    -- The cost sits on the first line; a few more are scanned because some
    -- spells push it down. Description text starts well below that, which is
    -- what keeps a stray "mana" in flavour text out of the match.
    -- Looked up per call, not captured: GameTooltipTemplate only ships line 1,
    -- the rest are created the first time a tooltip is that tall.
    local costLineNames = {
        SCANNER_NAME .. "TextRight1",
        SCANNER_NAME .. "TextLeft2",
        SCANNER_NAME .. "TextRight2",
    }

    -- Memoized per spell: a full bar sweep is up to 120 tooltip builds, the same
    -- spell often sits on several bars, and SPELLS_CHANGED arrives in bursts.
    -- false = scanned, no flat cost (percentage or costless spell).
    local costCache = {}

    local function ScanSpellPowerCost(spellID)
        scanner:SetOwner(UIParent, "ANCHOR_NONE")
        scanner:ClearLines()

        if scanner.SetSpellByID then
            scanner:SetSpellByID(spellID)
        else
            scanner:SetHyperlink("spell:" .. spellID)
        end

        for i = 1, #costLineNames do
            local line = _G[costLineNames[i]]
            local text = line and line:GetText()
            -- A percentage line ("4% of base mana") is not a flat cost, and
            -- base mana is not queryable, so leave it to the zero default.
            if text and not text:find("%%") then
                for j = 1, #powerNames do
                    local power = powerNames[j]
                    if text:find(power.name, 1, true) then
                        -- Digits only: locales group thousands differently
                        -- ("1,200" / "1 200" / "1.200").
                        local digits = text:match("%d[%d%s,%.]*")
                        local cost = digits and tonumber((digits:gsub("[^%d]", "")))
                        if cost then
                            return { { cost = cost, type = power.type } }
                        end
                    end
                end
            end
        end

        return false
    end

    DataToColor.GetSpellPowerCost = function(spellID)
        local cached = costCache[spellID]
        if cached == nil then
            cached = ScanSpellPowerCost(spellID)
            costCache[spellID] = cached
        end
        return cached or nil
    end

    -- Talents, glyphs and specialisation swaps all change costs and all raise
    -- SPELLS_CHANGED, which is where OnSpellsChanged calls this.
    DataToColor.InvalidateSpellPowerCostCache = function()
        wipe(costCache)
    end
end

-- No cache to drop when the client answers costs directly.
DataToColor.InvalidateSpellPowerCostCache =
    DataToColor.InvalidateSpellPowerCostCache or function() end

-- define your safe version under a different name
local function UnitIsTapDenied_Fallback(unit)
  if not UnitExists(unit) then
    return false
  end
  if UnitIsUnit(unit, "pet") then
    return false
  end
  if UnitIsTapped(unit) and not UnitIsTappedByPlayer(unit) then
    return true
  end
  return false
end

-- if Blizzard’s version exists and works, use it; otherwise use the fallback
local UnitIsTapDeniedExists = type(UnitIsTapDenied) == "function"
local function SafeUnitIsTapDenied(self, unit)
  if UnitIsTapDeniedExists then
    return UnitIsTapDenied(unit)
  else
    return UnitIsTapDenied_Fallback(unit)
  end
end

DataToColor.UnitIsTapDenied = SafeUnitIsTapDenied

--------------------------------------------------------
-- NormalizeTexture: convert path or numeric into a fileID
--------------------------------------------------------
function DataToColor:NormalizeTexture(texture)
    if not texture then return nil end

    -- modern numeric ID
    if type(texture) == "number" then
        return texture
    end

    return DataToColor.LegacyTextureToFileID[texture] or -1
end


function DataToColor:GetAuraInfo(func, unit, index)
    -- one call only; positions differ by era:
    -- modern: name(1), icon(2), count(3), dispel(4), duration(5), expiration(6), source(7), ...
    -- legacy: name(1), rank(2), icon(3),  count(4), dispel(5), duration(6),   expiration(7), source(8)
    local a1, a2, a3, a4, a5, a6, a7 = func(unit, index)
    if not a1 then return nil end

    -- decide which slot is the texture:
    --  - modern: a2 is a file path like "Interface\\Icons\\..."
    --  - legacy: a2 is rank ("" or "Rank X"), a3 is the texture path
    local texture = a2
    if not texture
       or texture == ""
       or type(texture) ~= "string"
       or (not texture:find("\\") and not texture:find("/"))  -- not a path-looking string
    then
        texture = a3
    end

    -- ✅ normalize texture cross-version (ALWAYS do it here)
    texture = DataToColor:NormalizeTexture(texture)

    -- duration/expiration are at a6/a7 in both eras
    local duration       = tonumber(a6) or 0
    local expirationTime = tonumber(a7) or 0

    return a1, texture, duration, expirationTime
end

-- Cached version of GetAuraInfo that reads from AuraCache instead of calling WoW API
-- This avoids string allocations from UnitBuff/UnitDebuff every frame
function DataToColor:GetCachedAuraInfo(isBuff, unit, index)
    local name, texture, count, _, duration, expirationTime
    if isBuff then
        name, texture, count, _, duration, expirationTime = DataToColor:GetCachedBuff(unit, index)
    else
        name, texture, count, _, duration, expirationTime = DataToColor:GetCachedDebuff(unit, index)
    end

    if not name then return nil end

    -- Normalize texture (same as GetAuraInfo)
    texture = DataToColor:NormalizeTexture(texture)

    return name, texture, duration or 0, expirationTime or 0
end


--------------------------------------------------------------------------------
-- CONTAINER API COMPATIBILITY (Bag changes from 10.0)
-- Legacy clients use old API, newer clients may use C_Container
--------------------------------------------------------------------------------

DataToColor.GetContainerNumSlots = GetContainerNumSlots or C_Container.GetContainerNumSlots
DataToColor.GetContainerItemInfo = GetContainerItemInfo or
    function(bagID, slot)
      local o = C_Container.GetContainerItemInfo(bagID, slot)
      if o == nil then return nil end
      return o.iconFileID, o.stackCount, o.isLocked, o.quality, o.isReadable, o.hasLoot, o.hyperlink, o.isFiltered,
          o.hasNoValue, o.itemID, o.isBound
    end

DataToColor.GetContainerNumFreeSlots = GetContainerNumFreeSlots or C_Container.GetContainerNumFreeSlots
DataToColor.GetContainerItemLink = GetContainerItemLink or C_Container.GetContainerItemLink
DataToColor.PickupContainerItem = PickupContainerItem or C_Container.PickupContainerItem
DataToColor.UseContainerItem = UseContainerItem or C_Container.UseContainerItem
DataToColor.ContainerIDToInventoryID = ContainerIDToInventoryID or C_Container.ContainerIDToInventoryID

DataToColor.GetGossipOptions = GetGossipOptions or C_GossipInfo.GetOptions

--------------------------------------------------------------------------------
--------------------------------------------------------------------------------
-- TALENT POINTS API COMPATIBILITY
-- Legacy Cataclysm 4.3.4 lacks UnitCharacterPoints
-- Polyfill uses GetUnspentTalentPoints which exists in that client
--------------------------------------------------------------------------------

if not UnitCharacterPoints then
    -- Which replacement exists depends on the client, so feature-detect rather than
    -- branch on build number:
    --   Cata 4.3.4 and earlier  GetUnspentTalentPoints(isInspect, isPet)
    --   MoP 5.x                 GetNumUnspentTalents() - 5.0 deleted the point-based
    --                           trees for six tiers of one pick each, and removed pet
    --                           talents outright
    -- Captured once here because DataToColor:Bits1 calls this every frame: on a 5.4.8
    -- client the old body raised "attempt to call global 'GetUnspentTalentPoints'"
    -- thousands of times, which then drowned Blizzard_DebugTools itself.
    local GetUnspentTalentPoints = GetUnspentTalentPoints
    local GetNumUnspentTalents = GetNumUnspentTalents

    UnitCharacterPoints = function(unit)
        if not UnitExists(unit) then
            return 0
        end

        if UnitIsUnit(unit, "pet") then
            -- No pet talents after 5.0, so nothing is ever unspent.
            return GetUnspentTalentPoints and GetUnspentTalentPoints(false, true) or 0
        elseif UnitIsUnit(unit, "player") then
            if GetUnspentTalentPoints then
                return GetUnspentTalentPoints(false)
            elseif GetNumUnspentTalents then
                return GetNumUnspentTalents()
            end
        end

        return 0
    end
end

--------------------------------------------------------------------------------
-- FRIEND LIST API COMPATIBILITY
-- Legacy/older clients use GetNumFriends/GetFriendInfo
-- Newer clients use C_FriendList namespace
--------------------------------------------------------------------------------

DataToColor.GetNumFriends = GetNumFriends or C_FriendList.GetNumFriends

-- GetFriendInfo returns: name, level, class, area, connected, status, notes (old API)
-- C_FriendList.GetFriendInfoByIndex returns a table with: name, level, className, area, connected, etc.
DataToColor.GetFriendInfo = GetFriendInfo or
    function(index)
        local info = C_FriendList.GetFriendInfoByIndex(index)
        if not info then return nil end
        return info.name, info.level, info.className, info.area, info.connected, info.status, info.notes
    end

--------------------------------------------------------------------------------
-- MAP API COMPATIBILITY
-- Legacy clients and older Classic-era versions don't have C_Map
--------------------------------------------------------------------------------

-- C_Map compatibility
if not C_Map or not C_Map.GetBestMapForUnit then

--------------------------------------------------------
-- Case-insensitive, slash-tolerant lookup for legacy table
--------------------------------------------------------
  setmetatable(DataToColor.LegacyTextureToFileID, {
      __index = function(t, k)
          if type(k) ~= "string" then
              return nil
          end
          local key = k:lower()
          return rawget(t, key)
      end
  })

    C_Map = C_Map or {}

    function C_Map.GetBestMapForUnit(unit)
        unit = unit or "player"
        SetMapToCurrentZone()
        local id = GetCurrentMapAreaID and GetCurrentMapAreaID() or 0
        return DataToColor.WorldMapAreaIDToUiMapID[id]
    end

    function C_Map.GetPlayerMapPosition(mapID, unit)
        local x, y = GetPlayerMapPosition(unit or "player")
        local pos = {}
        function pos:GetXY() return x, y end
        return pos
    end
end

DataToColor.GetBestMapForUnit    = C_Map.GetBestMapForUnit
DataToColor.GetPlayerMapPosition = C_Map.GetPlayerMapPosition

-- GetCVar compatibility wrapper
local originalGetCVar = GetCVar
DataToColor.SafeGetCVar = function(cvar, default)
  local success, value = pcall(originalGetCVar, cvar)
  if success and value then
    return value
  end
  return default or "0"
end

-- SetCVar compatibility wrapper
-- Safely sets a CVar value, silently failing if the CVar doesn't exist
local originalSetCVar = SetCVar
DataToColor.SafeSetCVar = function(cvar, value, eventType)
  local success = pcall(originalSetCVar, cvar, value, eventType)
  return success
end

DataToColor.UnitLevelSafe = function(unit, playerLevel)
  local level = UnitLevel(unit)

  if not level then
    return 0
  end

  if level == -1 then
    return playerLevel + 10
  end

  return level
end

local IS_LEGACY_GOSSIP = type(_G.GetNumGossipOptions) == "function" and type(DataToColor.GetGossipOptions) == "function"

DataToColor.OnGossipShow = function(event)
  if IS_LEGACY_GOSSIP then
    local options = { DataToColor:GetGossipOptions() }
    local count = #options / 2
    if count == 0 then
      return
    end

    DataToColor.gossipQueue:push(DataToColor.GOSSIP_START)
    -- returns variable string - format of one entry
    -- [1] localized name
    -- [2] gossip_type
    for k, v in pairs(options) do
      if k % 2 == 0 then
        DataToColor.gossipQueue:push(10000 * count + 100 * (k / 2) + DataToColor.C.Gossip[v])
      end
    end
  else
    local options = DataToColor:GetGossipOptions()
    if not options then
      return
    end

    table.sort(options, function(a, b)
      return (a.orderIndex or 0) < (b.orderIndex or 0)
    end)

    DataToColor.gossipQueue:push(DataToColor.GOSSIP_START)

    local count = #options
    for i, v in pairs(options) do
      local hash = 10000 * count + 100 * i + DataToColor.C.GossipIcon[v.icon]
      --DataToColor:Print(i .. " " .. v.icon .. " " .. DataToColor.C.GossipIcon[v.icon] .. " " .. v.name .. " " .. hash)
      DataToColor.gossipQueue:push(hash)
    end
  end

  DataToColor.gossipQueue:push(DataToColor.GOSSIP_END)
end

--------------------------------------------------------------------------------
-- GUID HANDLING FUNCTIONS - Version-specific implementations
-- Legacy 4.3.4: Uses simpler Cataclysm-era GUID format
-- Modern Classic: Uses newer GUID format with uniqueGuid hash calculation
--------------------------------------------------------------------------------

-- Compatibility layer for older WoW versions

if not IsInGroup then
    function IsInGroup()
        return (GetNumPartyMembers() > 0) or (GetNumRaidMembers() > 0)
    end
end

if not bit then
	bit = {
		band = function(a, b)
			a = tonumber(a) or 0
			b = tonumber(b) or 0
			local result = 0
			local p = 1
			while a > 0 and b > 0 do
				result = result + (p * ((a % 2) * (b % 2)))
				a = math.floor(a / 2)
				b = math.floor(b / 2)
				p = p * 2
			end
			return result
		end,
		rshift = function(a, bits)
			a = tonumber(a) or 0
			return math.floor(a / (2 ^ bits))
		end
	}
end

local bit = bit
local band = bit.band
local sub = string.sub
local strsplit = strsplit

if DataToColor.IsLegacy() then
  -- ========================================
  -- LEGACY 4.3.4 / 5.4.8 IMPLEMENTATIONS
  -- ========================================

  function DataToColor:GetActionTexture(slot)
    if not slot then return nil end
    return DataToColor:NormalizeTexture(GetActionTexture(slot))
  end

  ------------------------------------------------------------
  -- Legacy hex GUID layout
  --   0x | 3 nibbles high type | 5 nibbles entry | 8 nibbles spawn counter
  --   0xF130C2CF0000355D -> F13 creature, entry 0C2CF = 49871
  --   0xF130EB1729000001 -> F13 creature, entry 0EB17 = 60183
  -- The entry is 20 bits wide, not the 16 that the familiar "0xF130" prefix
  -- suggests: 5.4.8 ships ~2800 creatures above 0xFFFF (highest is 80674) and
  -- those spill into the 4th nibble, turning the prefix into 0xF131 and up.
  -- High types seen on units: F13 creature, F14 pet, F15 vehicle.
  ------------------------------------------------------------
  local LEGACY_ENTRY_PATTERN = "^0x[fF]1%x(%x%x%x%x%x)"
  local LEGACY_SPAWN_LENGTH = 8

  local function LegacyEntryId(guid)
    local hex = guid and guid:match(LEGACY_ENTRY_PATTERN)
    return hex and tonumber(hex, 16) or 0
  end

  -- Player GUIDs carry no entry and do not match the pattern above, so they
  -- fall out as 0 the way the modern branch's "^Player" check does.
  local function LegacySpawnId(guid)
    local hex = guid and guid:match("^0x(%x+)$")
    return hex and hex:sub(-LEGACY_SPAWN_LENGTH) or nil
  end

  -- Extract NPC ID from GUID
  function DataToColor:NpcId(unit)
    return LegacyEntryId(UnitGUID(unit))
  end

  -- Get unique GUID from unit
  -- Legacy: Uses uniqueGuid with NPC ID for bit-packed encoding
  function DataToColor:getGuidFromUnit(unit)
    if not UnitExists(unit) then
      return 0
    end

    local guid = UnitGUID(unit)
    if not guid then return 0 end

    return DataToColor:uniqueGuid(LegacyEntryId(guid), LegacySpawnId(guid))
  end

  -- /dump DataToColor:getGuidFromUUID("0xF130C2CF0000355D")
  -- Get unique GUID from UUID
  -- Legacy: Direct extraction without hash calculation
  function DataToColor:getGuidFromUUID(uuid)
    if not uuid then
      return 0
    end

    return DataToColor:uniqueGuid(LegacyEntryId(uuid), LegacySpawnId(uuid))
  end

  -- Extract NPC ID from UUID
  function DataToColor:getNpcIdFromUUID(uuid)
    return LegacyEntryId(uuid)
  end

  -- Get unit type from UUID
  -- Same for all versions - extract first segment
  function DataToColor:getTypeFromUUID(uuid)
    if not uuid then
      return 0
    end

    --local type = uuid:match("^(.-)-")
    --return DataToColor.C.GuidType[type] or 0

    -- Legacy hex GUID: first byte identifies type
    local high = uuid:sub(3,4)
    local firstByte = tonumber(high, 16)
    local typeID = bit.rshift(firstByte, 4)
    -- Map typeID → your GuidType table if you maintain one
    return typeID

  end

else
  -- ========================================
  -- MODERN CLASSIC IMPLEMENTATIONS
  -- ========================================

  function DataToColor:GetActionTexture(slot)
    return GetActionTexture(slot)
  end

  -- Extract NPC ID from GUID
  -- Modern format: Uses standard extraction
  function DataToColor:NpcId(unit)
    local guid = UnitGUID(unit) or ""
    local id = guid:match("-(%d+)-[^-]+$")

    if id and not guid:find("^Player") then
      return tonumber(id, 10)
    end
    return 0
  end

  -- Get unique GUID from unit
  -- Modern: Uses uniqueGuid calculation with spawn data
  function DataToColor:getGuidFromUnit(unit)
    if not UnitExists(unit) then
      return 0
    end

    -- Modern Classic: Uses uniqueGuid calculation
    -- Player-4731-02AAD4FF
    -- Creature-0-4488-530-222-19350-000005C0D70
    -- Pet-0-4448-530-222-22123-15004E200E
    return DataToColor:uniqueGuid(select(-2, strsplit('-', UnitGUID(unit))))
  end

  -- Get unique GUID from UUID
  -- Modern: Uses uniqueGuid calculation
  function DataToColor:getGuidFromUUID(uuid)
    if not uuid then
      return 0
    end
    return DataToColor:uniqueGuid(select(-2, strsplit('-', uuid)))
  end

  -- Extract NPC ID from UUID
  -- Modern: Standard extraction
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

  -- Get unit type from UUID
  -- Same for all versions - extract first segment
  function DataToColor:getTypeFromUUID(uuid)
    if not uuid then
      return 0
    end

    local type = uuid:match("^(.-)-")
    return DataToColor.C.GuidType[type] or 0
  end

end

-- Unique GUID calculation - bit-packed encoding
-- High 18 bits: NPC ID (max 262,143), Low 6 bits: spawn uniqueness hash (64 values)
-- This allows C# to extract the NPC ID via: npcId = guid >> 6
function DataToColor:uniqueGuid(npcId, spawn)
  npcId = tonumber(npcId, 10) or tonumber(npcId, 16) or 0
  if not spawn then
    return 0
  end

  -- Extract spawn uniqueness from spawn string
  local spawnEpochOffset = band(tonumber(sub(spawn, 5), 16) or 0, 0x7fffff)
  local spawnIndex = band(tonumber(sub(spawn, 1, 5), 16) or 0, 0xffff8)
  local spawnHash = band(spawnEpochOffset + spawnIndex, 0x3F)  -- 6 bits (0-63)

  -- Pack: NPC ID (18 bits) | spawn hash (6 bits)
  -- bit.lshift(npcId, 6) puts NPC ID in high bits
  -- bit.bor combines with spawn hash in low bits
  return bit.bor(bit.lshift(band(npcId, 0x3FFFF), 6), spawnHash)
end


---

if DataToColor:IsLegacy() then
  function DataToColor:PlayerIsMoving()
      return GetUnitSpeed(DataToColor.C.unitPlayer) > 0
  end
else
  function DataToColor:PlayerIsMoving()
    return DataToColor.moving
  end
end

--------------------------------------------------------------------------------
-- SAFE EVENT REGISTRATION
-- Pre-validates event existence before AceEvent registration to avoid errors
--------------------------------------------------------------------------------

local eventTestFrame = CreateFrame("Frame")
local validatedEvents = {}

-- Check if an event exists in this WoW version
-- Uses raw frame registration which returns silently for unknown events
function DataToColor.IsEventSupported(eventName)
    if validatedEvents[eventName] ~= nil then
        return validatedEvents[eventName]
    end

    -- Try to register on raw frame - this doesn't error for unknown events
    local success = pcall(function()
        eventTestFrame:RegisterEvent(eventName)
    end)

    if success then
        -- Check if it was actually registered (some versions silently fail)
        local isRegistered = eventTestFrame:IsEventRegistered(eventName)
        eventTestFrame:UnregisterEvent(eventName)
        validatedEvents[eventName] = isRegistered
        return isRegistered
    end

    validatedEvents[eventName] = false
    return false
end

-- Safe wrapper for AceEvent registration
-- Only registers if the event exists in this WoW version
function DataToColor:SafeRegisterEvent(eventName, handler)
    if DataToColor.IsEventSupported(eventName) then
        DataToColor:RegisterEvent(eventName, handler)
        return true
    end
    return false
end