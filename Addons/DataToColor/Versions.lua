local Load = select(2, ...)
local DataToColor = unpack(Load)

local GetBuildInfo = GetBuildInfo

local UnitIsUnit = UnitIsUnit
local UnitLevel = UnitLevel

local UnitChannelInfo = UnitChannelInfo
local UnitCastingInfo = UnitCastingInfo

local WOW_PROJECT_ID = WOW_PROJECT_ID or -1  -- -1 = Legacy client (old retail)
local WOW_PROJECT_CLASSIC = WOW_PROJECT_CLASSIC
local WOW_PROJECT_BURNING_CRUSADE_CLASSIC = WOW_PROJECT_BURNING_CRUSADE_CLASSIC
local WOW_PROJECT_WRATH_CLASSIC = WOW_PROJECT_WRATH_CLASSIC
local WOW_PROJECT_CATACLYSM_CLASSIC = WOW_PROJECT_CATACLYSM_CLASSIC
local WOW_PROJECT_MAINLINE = WOW_PROJECT_MAINLINE

local LE_EXPANSION_LEVEL_CURRENT = LE_EXPANSION_LEVEL_CURRENT
local LE_EXPANSION_NORTHREND = LE_EXPANSION_NORTHREND
local LE_EXPANSION_BURNING_CRUSADE = LE_EXPANSION_BURNING_CRUSADE
local LE_EXPANSION_WRATH_OF_THE_LICH_KING = LE_EXPANSION_WRATH_OF_THE_LICH_KING

-- Is this a Legacy client (old retail, e.g., Cataclysm 4.3.4)?
function DataToColor.IsLegacy()
  return WOW_PROJECT_ID == -1
end

-- Is this Legacy Cataclysm 4.3.4?
function DataToColor.IsLegacyCataclysm()
  if not DataToColor.IsLegacy() then return false end
  local buildVersion = select(4, GetBuildInfo())
  return buildVersion >= 40000 and buildVersion < 50000
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
-- Legacy clients: Version 6
-- Classic-era clients: Versions 1-5
--------------------------------------------------------------------------------

if DataToColor.IsLegacy() then
  -- Legacy (old retail) clients - e.g., Cataclysm 4.3.4
  DataToColor.ClientVersion = 6
elseif WOW_PROJECT_ID == WOW_PROJECT_MAINLINE then
  -- Retail (modern)
  DataToColor.ClientVersion = 1
elseif WOW_PROJECT_ID == WOW_PROJECT_CATACLYSM_CLASSIC then
  -- Cataclysm Classic
  DataToColor.ClientVersion = 5
elseif WOW_PROJECT_ID == WOW_PROJECT_WRATH_CLASSIC then
  -- Wrath Classic
  DataToColor.ClientVersion = 4
elseif WOW_PROJECT_ID == WOW_PROJECT_BURNING_CRUSADE_CLASSIC then
  if LE_EXPANSION_LEVEL_CURRENT == LE_EXPANSION_NORTHREND or
      LE_EXPANSION_LEVEL_CURRENT == LE_EXPANSION_WRATH_OF_THE_LICH_KING then
    DataToColor.ClientVersion = 4
  elseif LE_EXPANSION_LEVEL_CURRENT == LE_EXPANSION_BURNING_CRUSADE then
    DataToColor.ClientVersion = 3
  end
elseif WOW_PROJECT_ID == WOW_PROJECT_CLASSIC then
  -- Classic Era
  DataToColor.ClientVersion = 2
else
  -- Unknown client, default to Legacy
  DataToColor.ClientVersion = 6
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

--------------------------------------------------------------------------------
-- CONTAINER API COMPATIBILITY (Bag changes from 10.0)
-- Legacy clients use old API, newer clients may use C_Container
--------------------------------------------------------------------------------

DataToColor.GetContainerNumSlots = GetContainerNumSlots or C_Container.GetContainerNumSlots
DataToColor.GetContainerItemInfo = GetContainerItemInfo or
    function(bagID, slot)
      local o = C_Container.GetContainerItemInfo(bagID, slot)
      if o == nil then return nil end
      return o.iconFileID, o.stackCount, o.isLocked, o.quality, o.isReadable, o.hasLoot, o.hyperlink, o.isFiltered, o.hasNoValue, o.itemID, o.isBound
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