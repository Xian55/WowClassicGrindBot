local Load = select(2, ...)
local DataToColor = unpack(Load)

local GetBuildInfo = GetBuildInfo

local UnitIsUnit = UnitIsUnit
local UnitLevel = UnitLevel

local UnitChannelInfo = UnitChannelInfo
local UnitCastingInfo = UnitCastingInfo

local WOW_PROJECT_ID = WOW_PROJECT_ID or -1 -- -1 = Legacy client (old retail)
local WOW_PROJECT_CLASSIC = WOW_PROJECT_CLASSIC
local WOW_PROJECT_BURNING_CRUSADE_CLASSIC = WOW_PROJECT_BURNING_CRUSADE_CLASSIC
local WOW_PROJECT_WRATH_CLASSIC = WOW_PROJECT_WRATH_CLASSIC
local WOW_PROJECT_CATACLYSM_CLASSIC = WOW_PROJECT_CATACLYSM_CLASSIC
local WOW_PROJECT_MAINLINE = WOW_PROJECT_MAINLINE

-- Is this a Legacy client (old retail, e.g., Cataclysm 4.3.4)?
function DataToColor.IsLegacy()
  return WOW_PROJECT_ID == -1
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
end

local buildVersion = select(4, GetBuildInfo())
local Som140 = DataToColor.IsClassic() and buildVersion == 11400
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

-- UnitCastingInfo wrapper for different client versions
if DataToColor.IsLegacy() or DataToColor.IsRetail() or TBC253 or DataToColor.IsClassic_Wrath() or DataToColor.IsClassic_Cata() then
  -- Legacy clients and newer Classic-era clients have the full API
  DataToColor.UnitCastingInfo = UnitCastingInfo
elseif Som140 or TBC252 then
  DataToColor.UnitCastingInfo = function(unit)
    local name, text, texture, startTimeMS, endTimeMS, isTradeSkill, castID, spellId = UnitCastingInfo(unit)
    return name, text, texture, startTimeMS, endTimeMS, isTradeSkill, castID, nil, spellId
  end
elseif DataToColor.IsClassic_BCC() or DataToColor.IsClassic() then
  DataToColor.UnitCastingInfo = function(unit)
    local name, text, texture, startTimeMS, endTimeMS, isTradeSkill, castID, interrupt, spellId = UnitCastingInfo(unit)
    return name, text, texture, startTimeMS, endTimeMS, isTradeSkill, castID, nil, spellId
  end
else
  DataToColor.UnitCastingInfo = function(unit)
    if UnitIsUnit(unit, DataToColor.C.unitPlayer) then
      return UnitCastingInfo(DataToColor.C.unitPlayer)
    else
      return LibClassicCasterino:UnitCastingInfo(unit)
    end
  end
end

-- UnitChannelInfo wrapper for different client versions
if DataToColor.IsLegacy() or DataToColor.IsRetail() or TBC253 or DataToColor.IsClassic_Wrath() or DataToColor.IsClassic_Cata() then
  -- Legacy clients and newer Classic-era clients have the full API
  DataToColor.UnitChannelInfo = UnitChannelInfo
elseif Som140 or TBC252 then
  DataToColor.UnitChannelInfo = function(unit)
    local name, text, texture, startTimeMS, endTimeMS, isTradeSkill, spellId = UnitChannelInfo(unit)
    return name, text, texture, startTimeMS, endTimeMS, isTradeSkill, nil, spellId
  end
elseif DataToColor.IsClassic_BCC() or DataToColor.IsClassic() then
  DataToColor.UnitChannelInfo = function(unit)
    local name, text, texture, startTimeMS, endTimeMS, isTradeSkill, interrupt, spellId = UnitChannelInfo(unit)
    return name, text, texture, startTimeMS, endTimeMS, isTradeSkill, nil, spellId
  end
else
  DataToColor.UnitChannelInfo = function(unit)
    if UnitIsUnit(unit, DataToColor.C.unitPlayer) then
      return UnitChannelInfo(DataToColor.C.unitPlayer)
    else
      return LibClassicCasterino:UnitChannelInfo(unit)
    end
  end
end

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
local function SafeUnitIsTapDenied(unit)
  local ok, result = pcall(UnitIsTapDenied, unit)
  if ok then
    return result
  else
    return UnitIsTapDenied_Fallback(unit)
  end
end

DataToColor.UnitIsTapDenied = SafeUnitIsTapDenied

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
-- MAP API COMPATIBILITY
-- Legacy clients and older Classic-era versions don't have C_Map
--------------------------------------------------------------------------------

-- C_Map compatibility
if C_Map and C_Map.GetBestMapForUnit then
  DataToColor.GetBestMapForUnit = C_Map.GetBestMapForUnit
else
  local map = {}
  map.GetBestMapForUnit = function(unit)
    SetMapToCurrentZone()
    return GetPlayerMapPosition(unit or "player")
  end

  map.GetPlayerMapPosition = function(map, unit)
    local x, y = GetPlayerMapPosition(unit or "player")
    local obj = {}

    obj.GetXY = function()
      return x, y
    end

    return obj
  end

  C_Map = map
  DataToColor.GetBestMapForUnit = C_Map.GetBestMapForUnit
  DataToColor.GetPlayerMapPosition = C_Map.GetPlayerMapPosition
end

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

DataToColor.OnGossipShow = function(event)
  if Som140 or TBC252 then
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
  -- LEGACY CATACLYSM 4.3.4 IMPLEMENTATIONS
  -- ========================================

  -- Extract NPC ID from GUID
  -- Legacy Cataclysm format: Creature-0-X-Y-Z-NpcId-UniqueSpawnId
  function DataToColor:NpcId(unit)
    local guid = UnitGUID(unit) or ""

    -- Legacy format (hex)
    -- pattern: 0xF13000C5000034D7 → extract 00C5
    local npc_hex = guid:match("^0xF[0-9A-F]+00(%x%x%x%x)")
    if npc_hex then
        return tonumber(npc_hex, 16)
    end

    return 0
  end

  -- Get unique GUID from unit
  -- Legacy: Uses direct extraction of the last hex segment
  function DataToColor:getGuidFromUnit(unit)
    if not UnitExists(unit) then
      return 0
    end

    local guid = UnitGUID(unit)
    if not guid then return 0 end

    -- Legacy format: Extract last segment (unique spawn ID) directly
    local spawn_hex = guid:match("0xF[0-9A-F]+%x%x%x%x(%x%x%x%x%x%x)")
    if spawn_hex then
        return tonumber(spawn_hex, 16)
    end

    return 0
  end

  -- Get unique GUID from UUID
  -- Legacy: Direct extraction without hash calculation
  function DataToColor:getGuidFromUUID(uuid)
    if not uuid then
      return 0
    end

    local _, lastPart = uuid:match("^(.+)-([^-]+)$")
    if lastPart then
      return tonumber(lastPart, 16) % 0x1000000
    end
    return 0
  end

  -- Extract NPC ID from UUID
  -- Legacy: Same extraction pattern as modern
  function DataToColor:getNpcIdFromUUID(uuid)
    if not uuid then
      return 0
    end

    local npc_hex = uuid:match("^0xF[0-9A-F]+00(%x%x%x%x)")
    if npc_hex then return tonumber(npc_hex, 16) end

    return 0
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

-- Unique GUID calculation (Modern Classic only)
-- This is called from modern client implementations only
function DataToColor:uniqueGuid(npcId, spawn)
  if not spawn then
    return 0
  end

  local spawnEpochOffset = band(tonumber(sub(spawn, 5), 16), 0x7fffff)
  local spawnIndex = band(tonumber(sub(spawn, 1, 5), 16), 0xffff8)

  return (spawnEpochOffset + spawnIndex + npcId) % 0x1000000
end
