--[[
    AuraCache.lua - Event-Driven Aura Caching

    This module caches UnitBuff/UnitDebuff results and only refreshes when
    UNIT_AURA fires, dramatically reducing per-frame allocations.

    Before: ~300 UnitBuff/UnitDebuff calls per frame (each returns strings)
    After: 0 calls per frame (only on UNIT_AURA events)
]]

local Load = select(2, ...)
local DataToColor = unpack(Load)

-- Cache WoW API locally
local UnitBuff = UnitBuff
local UnitDebuff = UnitDebuff
local UnitExists = UnitExists
local UnitGUID = UnitGUID
local GetTime = GetTime
local next = next
local wipe = wipe

--------------------------------------------------------------------------------
-- Cache Storage
--------------------------------------------------------------------------------

-- Pre-allocated cache tables per unit
-- Structure: cache[unitId].buffs[index] = { name, texture, count, duration, expirationTime }
-- Structure: cache[unitId].debuffs[index] = { name, texture, count, duration, expirationTime }
local unitCache = {}

-- Units we track
local trackedUnits = {
    "player",
    "target",
    "focus",
    "pet",
    "mouseover",
    "softenemy",
    "softfriend",
    "softinteract",
}

-- Initialize cache structure for each unit
for _, unit in ipairs(trackedUnits) do
    unitCache[unit] = {
        buffs = {},
        debuffs = {},
        buffCount = 0,
        debuffCount = 0,
        guid = nil,
        lastUpdate = 0,
    }
end

-- Reusable aura entry tables (object pool to avoid allocation)
local auraPool = {}
local poolSize = 0
local MAX_POOL_SIZE = 200

local function AcquireAuraEntry()
    if poolSize > 0 then
        local entry = auraPool[poolSize]
        auraPool[poolSize] = nil
        poolSize = poolSize - 1
        return entry
    end
    return {}
end

local function ReleaseAuraEntry(entry)
    if poolSize < MAX_POOL_SIZE then
        -- Clear entry for reuse
        entry.name = nil
        entry.texture = nil
        entry.count = nil
        entry.duration = nil
        entry.expirationTime = nil
        poolSize = poolSize + 1
        auraPool[poolSize] = entry
    end
end

--------------------------------------------------------------------------------
-- Cache Update Functions
--------------------------------------------------------------------------------

-- Refresh buff cache for a unit
local function RefreshBuffCache(unitId)
    local cache = unitCache[unitId]
    if not cache then return end

    -- Release old entries back to pool
    for i = 1, cache.buffCount do
        if cache.buffs[i] then
            ReleaseAuraEntry(cache.buffs[i])
            cache.buffs[i] = nil
        end
    end

    cache.buffCount = 0

    if not UnitExists(unitId) then
        cache.guid = nil
        return
    end

    cache.guid = UnitGUID(unitId)
    cache.lastUpdate = GetTime()

    -- Scan buffs (max 40)
    for i = 1, 40 do
        local name, texture, count, debuffType, duration, expirationTime = UnitBuff(unitId, i)
        if not name then
            break
        end

        local entry = AcquireAuraEntry()
        entry.name = name
        entry.texture = texture
        entry.count = count or 0
        entry.duration = duration or 0
        entry.expirationTime = expirationTime or 0

        cache.buffCount = cache.buffCount + 1
        cache.buffs[cache.buffCount] = entry
    end
end

-- Refresh debuff cache for a unit
local function RefreshDebuffCache(unitId)
    local cache = unitCache[unitId]
    if not cache then return end

    -- Release old entries back to pool
    for i = 1, cache.debuffCount do
        if cache.debuffs[i] then
            ReleaseAuraEntry(cache.debuffs[i])
            cache.debuffs[i] = nil
        end
    end

    cache.debuffCount = 0

    if not UnitExists(unitId) then
        return
    end

    -- Scan debuffs (max 40)
    for i = 1, 40 do
        local name, texture, count, debuffType, duration, expirationTime = UnitDebuff(unitId, i)
        if not name then
            break
        end

        local entry = AcquireAuraEntry()
        entry.name = name
        entry.texture = texture
        entry.count = count or 0
        entry.duration = duration or 0
        entry.expirationTime = expirationTime or 0

        cache.debuffCount = cache.debuffCount + 1
        cache.debuffs[cache.debuffCount] = entry
    end
end

-- Refresh all auras for a unit
local function RefreshUnitAuras(unitId)
    RefreshBuffCache(unitId)
    RefreshDebuffCache(unitId)
end

-- Clear cache for a unit (when unit no longer exists)
local function ClearUnitCache(unitId)
    local cache = unitCache[unitId]
    if not cache then return end

    for i = 1, cache.buffCount do
        if cache.buffs[i] then
            ReleaseAuraEntry(cache.buffs[i])
            cache.buffs[i] = nil
        end
    end

    for i = 1, cache.debuffCount do
        if cache.debuffs[i] then
            ReleaseAuraEntry(cache.debuffs[i])
            cache.debuffs[i] = nil
        end
    end

    cache.buffCount = 0
    cache.debuffCount = 0
    cache.guid = nil
end

--------------------------------------------------------------------------------
-- Public API for Query.lua
--------------------------------------------------------------------------------

-- Get cached buff data (replacement for UnitBuff)
-- Returns: name, texture, count, debuffType, duration, expirationTime
function DataToColor:GetCachedBuff(unitId, index)
    local cache = unitCache[unitId]
    if not cache or index > cache.buffCount then
        return nil
    end

    local entry = cache.buffs[index]
    if not entry then
        return nil
    end

    return entry.name, entry.texture, entry.count, nil, entry.duration, entry.expirationTime
end

-- Get cached debuff data (replacement for UnitDebuff)
function DataToColor:GetCachedDebuff(unitId, index)
    local cache = unitCache[unitId]
    if not cache or index > cache.debuffCount then
        return nil
    end

    local entry = cache.debuffs[index]
    if not entry then
        return nil
    end

    return entry.name, entry.texture, entry.count, nil, entry.duration, entry.expirationTime
end

-- Get cached buff count
function DataToColor:GetCachedBuffCount(unitId)
    local cache = unitCache[unitId]
    return cache and cache.buffCount or 0
end

-- Get cached debuff count
function DataToColor:GetCachedDebuffCount(unitId)
    local cache = unitCache[unitId]
    return cache and cache.debuffCount or 0
end

-- Wrapper functions that match UnitBuff/UnitDebuff signature for compatibility
function DataToColor.CachedUnitBuff(unitId, index)
    return DataToColor:GetCachedBuff(unitId, index)
end

function DataToColor.CachedUnitDebuff(unitId, index)
    return DataToColor:GetCachedDebuff(unitId, index)
end

--------------------------------------------------------------------------------
-- Event Handlers
--------------------------------------------------------------------------------

local function OnUnitAura(_, unitId)
    -- Map unit tokens to our tracked units
    local mappedUnit = unitId

    -- Handle party/raid unit mapping if needed
    if unitCache[mappedUnit] then
        RefreshUnitAuras(mappedUnit)
    end
end

local function OnTargetChanged()
    RefreshUnitAuras("target")
end

local function OnFocusChanged()
    RefreshUnitAuras("focus")
end

local function OnMouseoverChanged()
    RefreshUnitAuras("mouseover")
end

local function OnPetChanged()
    RefreshUnitAuras("pet")
end

local function OnSoftTargetChanged()
    RefreshUnitAuras("softenemy")
    RefreshUnitAuras("softfriend")
    RefreshUnitAuras("softinteract")
end

--------------------------------------------------------------------------------
-- Initialization
--------------------------------------------------------------------------------

local cacheInitialized = false

function DataToColor:RegisterAuraCacheEvents()
    if cacheInitialized then
        -- Just refresh all caches
        for _, unit in ipairs(trackedUnits) do
            RefreshUnitAuras(unit)
        end
        return
    end
    cacheInitialized = true

    -- Register UNIT_AURA for all units
    DataToColor:RegisterEvent("UNIT_AURA", OnUnitAura)

    -- Register unit change events
    -- NOTE: These events are registered in EventHandlers.lua to avoid AceEvent overwrites:
    -- - PLAYER_TARGET_CHANGED -> OnPlayerTargetChanged -> AuraCache.refresh("target")
    -- - PLAYER_FOCUS_CHANGED -> OnFocusChanged_BitCache -> AuraCache.refresh("focus")
    -- - UPDATE_MOUSEOVER_UNIT -> OnMouseoverChanged_BitCache -> AuraCache.refresh("mouseover")
    -- - UNIT_PET -> OnPetChanged -> AuraCache.refresh("pet")
    -- - PLAYER_SOFT_INTERACT_CHANGED -> OnPlayerSoftInteractChanged -> AuraCache.refresh("softinteract")

    -- Soft target events (only the ones not handled elsewhere)
    -- Use safe registration since these events don't exist in all WoW versions
    DataToColor:SafeRegisterEvent("PLAYER_SOFT_ENEMY_CHANGED", OnSoftTargetChanged)
    DataToColor:SafeRegisterEvent("PLAYER_SOFT_FRIEND_CHANGED", OnSoftTargetChanged)
    -- NOTE: PLAYER_SOFT_INTERACT_CHANGED is handled by EventHandlers.lua

    -- Initial cache population
    for _, unit in ipairs(trackedUnits) do
        RefreshUnitAuras(unit)
    end

    --DataToColor:Print("AuraCache initialized - event-driven aura caching enabled")
end

-- Force refresh all caches (useful after loading screens)
function DataToColor:RefreshAllAuraCaches()
    for _, unit in ipairs(trackedUnits) do
        RefreshUnitAuras(unit)
    end
end

--------------------------------------------------------------------------------
-- Debug/Stats API
--------------------------------------------------------------------------------

DataToColor.AuraCache = {
    isInitialized = function() return cacheInitialized end,
    getPoolSize = function() return poolSize end,
    getStats = function()
        local stats = {}
        for _, unit in ipairs(trackedUnits) do
            local cache = unitCache[unit]
            if cache then
                stats[unit] = {
                    buffs = cache.buffCount,
                    debuffs = cache.debuffCount,
                    lastUpdate = cache.lastUpdate,
                }
            end
        end
        return stats
    end,
    refresh = function(unit)
        if unit then
            RefreshUnitAuras(unit)
        else
            for _, u in ipairs(trackedUnits) do
                RefreshUnitAuras(u)
            end
        end
    end,
}
