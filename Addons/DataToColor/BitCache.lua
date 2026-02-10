--[[
    BitCache.lua - Event-Driven Caching for Bits Functions

    This module caches the values used by Bits1/2/3 functions and updates them
    only when relevant events fire, reducing API calls from ~56 per frame to ~5.

    Values that cannot be event-driven (IsFalling, IsSwimming, etc.) are polled
    at a reduced rate using a throttled update.
]]

local Load = select(2, ...)
local DataToColor = unpack(Load)

-- Cache WoW API functions locally for performance
local UnitAffectingCombat = UnitAffectingCombat
local UnitIsDead = UnitIsDead
local UnitIsDeadOrGhost = UnitIsDeadOrGhost
local UnitExists = UnitExists
local UnitIsVisible = UnitIsVisible
local UnitIsPlayer = UnitIsPlayer
local UnitPlayerControlled = UnitPlayerControlled
local UnitCharacterPoints = UnitCharacterPoints
local UnitOnTaxi = UnitOnTaxi
local UnitReaction = UnitReaction
local UnitIsFriend = UnitIsFriend
local GetWeaponEnchantInfo = GetWeaponEnchantInfo
local IsMounted = IsMounted
local IsSwimming = IsSwimming
local IsFalling = IsFalling
local IsFlying = IsFlying
local IsIndoors = IsIndoors
local IsStealthed = IsStealthed
local IsAutoRepeatSpell = IsAutoRepeatSpell
local IsCurrentSpell = IsCurrentSpell
local GetMirrorTimerInfo = GetMirrorTimerInfo
local HasPetUI = HasPetUI
local GetPetHappiness = GetPetHappiness
local GetPetActionInfo = GetPetActionInfo
local UnitIsTrivial = UnitIsTrivial
local CharacterFrame = CharacterFrame
local SpellBookFrame = SpellBookFrame
local FriendsFrame = FriendsFrame

--------------------------------------------------------------------------------
-- Bit Cache Storage
--------------------------------------------------------------------------------

-- Pre-allocated cache tables to avoid runtime allocation
local bits1Cache = {
    targetInCombat = false,         -- bit 0
    targetIsDead = false,           -- bit 1
    playerIsDeadOrGhost = false,    -- bit 2
    hasUnspentTalents = false,      -- bit 3
    mouseoverExists = false,        -- bit 4
    targetIsHostile = false,        -- bit 5
    petIsAlive = false,             -- bit 6
    mainHandEnchant = false,        -- bit 7
    offHandEnchant = false,         -- bit 8
    equipmentBroken = false,        -- bit 9 (uses existing method)
    onTaxi = false,                 -- bit 10
    isSwimming = false,             -- bit 11 (polled)
    petIsHappy = false,             -- bit 12
    hasAmmo = false,                -- bit 13
    playerInCombat = false,         -- bit 14
    targetTargetsPlayer = false,    -- bit 15
    autoShotActive = false,         -- bit 16
    targetExists = false,           -- bit 17
    isMounted = false,              -- bit 18
    shootActive = false,            -- bit 19
    attackActive = false,           -- bit 20
    targetIsPlayer = false,         -- bit 21
    targetIsTapDenied = false,      -- bit 22
    isFalling = false,              -- bit 23 (polled)
}

local bits2Cache = {
    isBreathHeld = false,           -- bit 0
    -- corpseInRange uses existing DataToColor.corpseInRange (bit 1)
    isIndoors = false,              -- bit 2 (polled)
    focusExists = false,            -- bit 3
    focusInCombat = false,          -- bit 4
    focusTargetExists = false,      -- bit 5
    focusTargetInCombat = false,    -- bit 6
    focusTargetIsHostile = false,   -- bit 7
    mouseoverIsDead = false,        -- bit 8
    petTargetIsDead = false,        -- bit 9
    isStealthed = false,            -- bit 10
    targetIsTrivial = false,        -- bit 11
    mouseoverIsTrivial = false,     -- bit 12
    mouseoverIsTapDenied = false,   -- bit 13
    mouseoverIsHostile = false,     -- bit 14
    mouseoverIsPlayer = false,      -- bit 15
    mouseoverTargetsPlayer = false, -- bit 16
    mouseoverPlayerControlled = false, -- bit 17
    targetPlayerControlled = false, -- bit 18
    -- autoFollow uses existing DataToColor.autoFollow (bit 19)
    gameMenuShown = false,          -- bit 20 (polled)
    isFlying = false,               -- bit 21 (polled)
    isMoving = false,               -- bit 22 (polled)
    petIsDefensive = false,         -- bit 23
}

local bits3Cache = {
    softInteractExists = false,     -- bit 0
    softInteractIsDead = false,     -- bit 1
    softInteractIsDeadOrGhost = false, -- bit 2
    softInteractIsPlayer = false,   -- bit 3
    softInteractIsTapDenied = false, -- bit 4
    softInteractInCombat = false,   -- bit 5
    softInteractIsHostile = false,  -- bit 6
    -- channeling uses existing DataToColor.channeling (bit 7)
    lootFrameShown = false,         -- bit 8
    chatInputActive = false,        -- bit 9 (polled)
    softTargetEnabled = false,      -- bit 10
    mailFrameShown = false,         -- bit 11
    anyBagOpen = false,             -- bit 12 (event-driven: BAG_OPEN/BAG_CLOSED)
    characterFrameOpen = false,     -- bit 13 (hooked)
    spellBookFrameOpen = false,     -- bit 14 (hooked)
    friendsFrameOpen = false,       -- bit 15 (hooked)
}

-- Track if cache has been initialized
local cacheInitialized = false

-- Debug flag to disable caching (for baseline memory testing)
local BITCACHE_ENABLED = true

--------------------------------------------------------------------------------
-- Helper Functions
--------------------------------------------------------------------------------

local function IsUnitHostile(unit, target)
    return UnitExists(target) and
           (UnitReaction(unit, target) or 0) <= 4 and
           not UnitIsFriend(unit, target)
end

local function IsUnitsTargetIsPlayerOrPet(unit, unitTarget)
    local x = DataToColor:UnitsTargetAsNumber(unit, unitTarget)
    return x == 1 or x == 4
end

--------------------------------------------------------------------------------
-- Cache Update Functions (called by events)
--------------------------------------------------------------------------------

-- Update target-related bits
local function UpdateTargetCache()
    local target = DataToColor.C.unitTarget
    local targetExists = UnitExists(target)

    bits1Cache.targetExists = targetExists

    if targetExists then
        bits1Cache.targetInCombat = UnitAffectingCombat(target) or false
        bits1Cache.targetIsDead = UnitIsDead(target) or false
        bits1Cache.targetIsHostile = IsUnitHostile(DataToColor.C.unitPlayer, target)
        bits1Cache.targetIsPlayer = UnitIsPlayer(target) or false
        bits1Cache.targetIsTapDenied = DataToColor:UnitIsTapDenied(target) or false
        bits2Cache.targetIsTrivial = UnitIsTrivial(target) or false
        bits2Cache.targetPlayerControlled = UnitPlayerControlled(target) or false
    else
        bits1Cache.targetInCombat = false
        bits1Cache.targetIsDead = false
        bits1Cache.targetIsHostile = false
        bits1Cache.targetIsPlayer = false
        bits1Cache.targetIsTapDenied = false
        bits2Cache.targetIsTrivial = false
        bits2Cache.targetPlayerControlled = false
    end
end

-- Update target's target
local function UpdateTargetTargetCache()
    if bits1Cache.targetExists then
        bits1Cache.targetTargetsPlayer = IsUnitsTargetIsPlayerOrPet(
            DataToColor.C.unitTarget,
            DataToColor.C.unitTargetTarget
        )
    else
        bits1Cache.targetTargetsPlayer = false
    end
end

-- Update mouseover-related bits
local function UpdateMouseoverCache()
    local mouseover = DataToColor.C.unitmouseover
    local exists = UnitExists(mouseover)

    bits1Cache.mouseoverExists = exists

    if exists then
        bits2Cache.mouseoverIsDead = UnitIsDead(mouseover) or false
        bits2Cache.mouseoverIsTrivial = UnitIsTrivial(mouseover) or false
        bits2Cache.mouseoverIsTapDenied = DataToColor:UnitIsTapDenied(mouseover) or false
        bits2Cache.mouseoverIsHostile = IsUnitHostile(DataToColor.C.unitPlayer, mouseover)
        bits2Cache.mouseoverIsPlayer = UnitIsPlayer(mouseover) or false
        bits2Cache.mouseoverTargetsPlayer = IsUnitsTargetIsPlayerOrPet(
            mouseover,
            DataToColor.C.unitmouseovertarget
        )
        bits2Cache.mouseoverPlayerControlled = UnitPlayerControlled(mouseover) or false
    else
        bits2Cache.mouseoverIsDead = false
        bits2Cache.mouseoverIsTrivial = false
        bits2Cache.mouseoverIsTapDenied = false
        bits2Cache.mouseoverIsHostile = false
        bits2Cache.mouseoverIsPlayer = false
        bits2Cache.mouseoverTargetsPlayer = false
        bits2Cache.mouseoverPlayerControlled = false
    end
end

-- Update focus-related bits
local function UpdateFocusCache()
    local focus = DataToColor.C.unitFocus
    local focusTarget = DataToColor.C.unitFocusTarget

    bits2Cache.focusExists = UnitExists(focus) or false

    if bits2Cache.focusExists then
        bits2Cache.focusInCombat = UnitAffectingCombat(focus) or false
        bits2Cache.focusTargetExists = UnitExists(focusTarget) or false

        if bits2Cache.focusTargetExists then
            bits2Cache.focusTargetInCombat = UnitAffectingCombat(focusTarget) or false
            bits2Cache.focusTargetIsHostile = IsUnitHostile(DataToColor.C.unitPlayer, focusTarget)
        else
            bits2Cache.focusTargetInCombat = false
            bits2Cache.focusTargetIsHostile = false
        end
    else
        bits2Cache.focusInCombat = false
        bits2Cache.focusTargetExists = false
        bits2Cache.focusTargetInCombat = false
        bits2Cache.focusTargetIsHostile = false
    end
end

-- Update pet-related bits
local function UpdatePetCache()
    local pet = DataToColor.C.unitPet
    local petVisible = UnitIsVisible(pet)
    local petDead = UnitIsDead(pet)

    bits1Cache.petIsAlive = petVisible and not petDead

    -- Pet happiness (Classic only)
    if DataToColor:IsClassicPreCata() then
        bits1Cache.petIsHappy = GetPetHappiness() == 3
    else
        bits1Cache.petIsHappy = true
    end

    -- Pet target
    bits2Cache.petTargetIsDead = UnitIsDead(DataToColor.C.unitPetTarget) or false

    -- Pet defensive mode
    if HasPetUI() then
        bits2Cache.petIsDefensive = false
        for i = 1, 10 do
            local name, _, _, isActive = GetPetActionInfo(i)
            if isActive and name == DataToColor.C.PET_MODE_DEFENSIVE then
                bits2Cache.petIsDefensive = true
                break
            end
        end
    else
        bits2Cache.petIsDefensive = false
    end
end

-- Update player combat state
local function UpdatePlayerCombatCache()
    bits1Cache.playerInCombat = UnitAffectingCombat(DataToColor.C.unitPlayer) or false
end

-- Update player death state
local function UpdatePlayerDeathCache()
    bits1Cache.playerIsDeadOrGhost = UnitIsDeadOrGhost(DataToColor.C.unitPlayer) or false
end

-- Update mount state
local function UpdateMountCache()
    bits1Cache.isMounted = IsMounted() or false
end

-- Update taxi state
local function UpdateTaxiCache()
    bits1Cache.onTaxi = UnitOnTaxi(DataToColor.C.unitPlayer) or false
end

-- Update stealth state
local function UpdateStealthCache()
    bits2Cache.isStealthed = IsStealthed() or false
end

-- Update weapon enchants
local function UpdateWeaponEnchantCache()
    local mainHand, _, _, _, offHand = GetWeaponEnchantInfo()
    bits1Cache.mainHandEnchant = mainHand or false
    bits1Cache.offHandEnchant = offHand or false
end

-- Update equipment durability
local function UpdateEquipmentCache()
    bits1Cache.equipmentBroken = DataToColor:GetInventoryBroken() > 0

    -- Ammo (Classic only)
    if DataToColor:IsClassicPreCata() then
        bits1Cache.hasAmmo = DataToColor:HasAmmo()
    else
        bits1Cache.hasAmmo = true
    end
end

-- Update talent points
local function UpdateTalentCache()
    bits1Cache.hasUnspentTalents = UnitCharacterPoints(DataToColor.C.unitPlayer) > 0
end

-- Update auto-attack/spell states
local function UpdateSpellStateCache()
    bits1Cache.autoShotActive = IsAutoRepeatSpell(DataToColor.C.Spell.AutoShotId) or false
    bits1Cache.shootActive = IsAutoRepeatSpell(DataToColor.C.Spell.ShootId) or false
    bits1Cache.attackActive = IsCurrentSpell(DataToColor.C.Spell.AttackId) or false
end

-- Update breath/mirror timer
local function UpdateMirrorTimerCache()
    local timerType, _, _, scale = GetMirrorTimerInfo(2)
    bits2Cache.isBreathHeld = timerType == DataToColor.C.MIRRORTIMER.BREATH and scale < 0
end

-- Update soft interact target
local function UpdateSoftInteractCache()
    local softInteract = DataToColor.C.unitSoftInteract
    local exists = UnitExists(softInteract)

    bits3Cache.softInteractExists = exists

    if exists then
        bits3Cache.softInteractIsDead = UnitIsDead(softInteract) or false
        bits3Cache.softInteractIsDeadOrGhost = UnitIsDeadOrGhost(softInteract) or false
        bits3Cache.softInteractIsPlayer = UnitIsPlayer(softInteract) or false
        bits3Cache.softInteractIsTapDenied = DataToColor:UnitIsTapDenied(softInteract) or false
        bits3Cache.softInteractInCombat = UnitAffectingCombat(softInteract) or false
        bits3Cache.softInteractIsHostile = IsUnitHostile(DataToColor.C.unitPlayer, softInteract)
    else
        bits3Cache.softInteractIsDead = false
        bits3Cache.softInteractIsDeadOrGhost = false
        bits3Cache.softInteractIsPlayer = false
        bits3Cache.softInteractIsTapDenied = false
        bits3Cache.softInteractInCombat = false
        bits3Cache.softInteractIsHostile = false
    end

    bits3Cache.softTargetEnabled = DataToColor:SoftTargetInteractEnabled()
end

-- Update loot frame state
local function UpdateLootFrameCache()
    bits3Cache.lootFrameShown = LootFrame:IsShown() or false
end

-- Update mail frame state
local function UpdateMailFrameCache()
    bits3Cache.mailFrameShown = MailFrame:IsShown() or false
end

--------------------------------------------------------------------------------
-- Polled Values (called every frame)
-- These values cannot be reliably event-driven but are few enough
-- that polling every frame is still a major improvement (56 → 7 API calls)
--------------------------------------------------------------------------------

local function UpdatePolledValues()
    -- Movement/position states - must be real-time for accurate bot behavior
    bits1Cache.isSwimming = IsSwimming() or false
    bits1Cache.isFalling = IsFalling() or false
    bits2Cache.isIndoors = IsIndoors() or false
    bits2Cache.isFlying = IsFlying() or false
    bits2Cache.isMoving = DataToColor:PlayerIsMoving() or false

    -- UI states
    bits2Cache.gameMenuShown = GameMenuFrame:IsShown() or false
    bits3Cache.chatInputActive = DataToColor:IsChatInputActive() or false
    UpdateMailFrameCache()

    -- Spell states - must be polled because bot checks these immediately after
    -- sending key presses, faster than START/STOP_AUTOREPEAT_SPELL events fire
    UpdateSpellStateCache()
end

--------------------------------------------------------------------------------
-- Full Cache Initialization
--------------------------------------------------------------------------------

local function InitializeCache()
    -- Update all caches
    UpdateTargetCache()
    UpdateTargetTargetCache()
    UpdateMouseoverCache()
    UpdateFocusCache()
    UpdatePetCache()
    UpdatePlayerCombatCache()
    UpdatePlayerDeathCache()
    UpdateMountCache()
    UpdateTaxiCache()
    UpdateStealthCache()
    UpdateWeaponEnchantCache()
    UpdateEquipmentCache()
    UpdateTalentCache()
    UpdateMirrorTimerCache()
    UpdateSoftInteractCache()
    UpdateLootFrameCache()
    UpdateMailFrameCache()

    bits3Cache.anyBagOpen = DataToColor:AnyBagOpen()
    bits3Cache.characterFrameOpen = DataToColor:CharacterFrameOpen()
    bits3Cache.spellBookFrameOpen = DataToColor:SpellBookFrameOpen()
    bits3Cache.friendsFrameOpen = DataToColor:FriendsFrameOpen()

    -- Initialize polled values (includes UpdateSpellStateCache)
    UpdatePolledValues()

    cacheInitialized = true
end

--------------------------------------------------------------------------------
-- Cached Bits Functions
--------------------------------------------------------------------------------

function DataToColor:Bits1Cached()
    if not BITCACHE_ENABLED or not cacheInitialized then
        return DataToColor:Bits1()  -- Fallback to original
    end

    -- Update polled values
    UpdatePolledValues()

    return
        (bits1Cache.targetInCombat and 1 or 0) +
        (bits1Cache.targetIsDead and 2 or 0) ^ 1 +
        (bits1Cache.playerIsDeadOrGhost and 2 or 0) ^ 2 +
        (bits1Cache.hasUnspentTalents and 2 or 0) ^ 3 +
        (bits1Cache.mouseoverExists and 2 or 0) ^ 4 +
        (bits1Cache.targetIsHostile and 2 or 0) ^ 5 +
        (bits1Cache.petIsAlive and 2 or 0) ^ 6 +
        (bits1Cache.mainHandEnchant and 2 or 0) ^ 7 +
        (bits1Cache.offHandEnchant and 2 or 0) ^ 8 +
        (DataToColor:GetInventoryBroken() ^ 9) +  -- Keep original for bit 9 (returns 0 or 2)
        (bits1Cache.onTaxi and 2 or 0) ^ 10 +
        (bits1Cache.isSwimming and 2 or 0) ^ 11 +
        (bits1Cache.petIsHappy and 2 or 0) ^ 12 +
        (bits1Cache.hasAmmo and 2 or 0) ^ 13 +
        (bits1Cache.playerInCombat and 2 or 0) ^ 14 +
        (bits1Cache.targetTargetsPlayer and 2 or 0) ^ 15 +
        (bits1Cache.autoShotActive and 2 or 0) ^ 16 +
        (bits1Cache.targetExists and 2 or 0) ^ 17 +
        (bits1Cache.isMounted and 2 or 0) ^ 18 +
        (bits1Cache.shootActive and 2 or 0) ^ 19 +
        (bits1Cache.attackActive and 2 or 0) ^ 20 +
        (bits1Cache.targetIsPlayer and 2 or 0) ^ 21 +
        (bits1Cache.targetIsTapDenied and 2 or 0) ^ 22 +
        (bits1Cache.isFalling and 2 or 0) ^ 23
end

function DataToColor:Bits2Cached()
    if not BITCACHE_ENABLED or not cacheInitialized then
        return DataToColor:Bits2()  -- Fallback to original
    end

    return
        (bits2Cache.isBreathHeld and 1 or 0) +
        (DataToColor.corpseInRange ^ 1) +  -- Keep original
        (bits2Cache.isIndoors and 2 or 0) ^ 2 +
        (bits2Cache.focusExists and 2 or 0) ^ 3 +
        (bits2Cache.focusInCombat and 2 or 0) ^ 4 +
        (bits2Cache.focusTargetExists and 2 or 0) ^ 5 +
        (bits2Cache.focusTargetInCombat and 2 or 0) ^ 6 +
        (bits2Cache.focusTargetIsHostile and 2 or 0) ^ 7 +
        (bits2Cache.mouseoverIsDead and 2 or 0) ^ 8 +
        (bits2Cache.petTargetIsDead and 2 or 0) ^ 9 +
        (bits2Cache.isStealthed and 2 or 0) ^ 10 +
        (bits2Cache.targetIsTrivial and 2 or 0) ^ 11 +
        (bits2Cache.mouseoverIsTrivial and 2 or 0) ^ 12 +
        (bits2Cache.mouseoverIsTapDenied and 2 or 0) ^ 13 +
        (bits2Cache.mouseoverIsHostile and 2 or 0) ^ 14 +
        (bits2Cache.mouseoverIsPlayer and 2 or 0) ^ 15 +
        (bits2Cache.mouseoverTargetsPlayer and 2 or 0) ^ 16 +
        (bits2Cache.mouseoverPlayerControlled and 2 or 0) ^ 17 +
        (bits2Cache.targetPlayerControlled and 2 or 0) ^ 18 +
        (self.autoFollow and 2 or 0) ^ 19 +  -- Keep original
        (bits2Cache.gameMenuShown and 2 or 0) ^ 20 +
        (bits2Cache.isFlying and 2 or 0) ^ 21 +
        (bits2Cache.isMoving and 2 or 0) ^ 22 +
        (bits2Cache.petIsDefensive and 2 or 0) ^ 23
end

function DataToColor:Bits3Cached()
    if not BITCACHE_ENABLED or not cacheInitialized then
        return DataToColor:Bits3()  -- Fallback to original
    end

    return
        (bits3Cache.softInteractExists and 1 or 0) +
        (bits3Cache.softInteractIsDead and 2 or 0) ^ 1 +
        (bits3Cache.softInteractIsDeadOrGhost and 2 or 0) ^ 2 +
        (bits3Cache.softInteractIsPlayer and 2 or 0) ^ 3 +
        (bits3Cache.softInteractIsTapDenied and 2 or 0) ^ 4 +
        (bits3Cache.softInteractInCombat and 2 or 0) ^ 5 +
        (bits3Cache.softInteractIsHostile and 2 or 0) ^ 6 +
        (self.channeling and 2 or 0) ^ 7 +  -- Keep original
        (bits3Cache.lootFrameShown and 2 or 0) ^ 8 +
        (bits3Cache.chatInputActive and 2 or 0) ^ 9 +
        (bits3Cache.softTargetEnabled and 2 or 0) ^ 10 +
        (bits3Cache.mailFrameShown and 2 or 0) ^ 11 +
        (bits3Cache.anyBagOpen and 2 or 0) ^ 12 +
        (bits3Cache.characterFrameOpen and 2 or 0) ^ 13 +
        (bits3Cache.spellBookFrameOpen and 2 or 0) ^ 14 +
        (bits3Cache.friendsFrameOpen and 2 or 0) ^ 15
end

--------------------------------------------------------------------------------
-- Initialization
-- NOTE: All events are registered in EventHandlers.lua to avoid AceEvent overwrites
-- EventHandlers.lua calls the exported update functions (updateTarget, updateFocus, etc.)
--------------------------------------------------------------------------------

local cacheInitializedOnce = false

local function HookFrameVisibility()
    if CharacterFrame then
        CharacterFrame:HookScript("OnShow", function()
            bits3Cache.characterFrameOpen = true
        end)
        CharacterFrame:HookScript("OnHide", function()
            bits3Cache.characterFrameOpen = false
        end)
    end

    if SpellBookFrame then
        SpellBookFrame:HookScript("OnShow", function()
            bits3Cache.spellBookFrameOpen = true
        end)
        SpellBookFrame:HookScript("OnHide", function()
            bits3Cache.spellBookFrameOpen = false
        end)
    end

    if FriendsFrame then
        FriendsFrame:HookScript("OnShow", function()
            bits3Cache.friendsFrameOpen = true
        end)
        FriendsFrame:HookScript("OnHide", function()
            bits3Cache.friendsFrameOpen = false
        end)
    end
end

function DataToColor:RegisterBitCacheEvents()
    -- Initialize cache (events are registered in EventHandlers.lua)
    InitializeCache()

    if not cacheInitializedOnce then
        cacheInitializedOnce = true
        HookFrameVisibility()
    end
end

--------------------------------------------------------------------------------
-- Public API for testing/debugging
--------------------------------------------------------------------------------

DataToColor.BitCache = {
    bits1 = bits1Cache,
    bits2 = bits2Cache,
    bits3 = bits3Cache,
    isInitialized = function() return cacheInitialized end,
    isEnabled = function() return BITCACHE_ENABLED end,
    setEnabled = function(enabled)
        BITCACHE_ENABLED = enabled
        DataToColor:Print("BitCache " .. (enabled and "ENABLED" or "DISABLED"))
    end,
    toggle = function()
        BITCACHE_ENABLED = not BITCACHE_ENABLED
        DataToColor:Print("BitCache " .. (BITCACHE_ENABLED and "ENABLED" or "DISABLED"))
    end,
    reinitialize = InitializeCache,
    updateTarget = UpdateTargetCache,
    updateTargetTarget = UpdateTargetTargetCache,
    updateMouseover = UpdateMouseoverCache,
    updateFocus = UpdateFocusCache,
    updatePet = UpdatePetCache,
    updateSoftInteract = UpdateSoftInteractCache,
    updateWeaponEnchant = UpdateWeaponEnchantCache,
    updateEquipment = UpdateEquipmentCache,
    updateSpellState = UpdateSpellStateCache,
    updateMirrorTimer = UpdateMirrorTimerCache,
}
