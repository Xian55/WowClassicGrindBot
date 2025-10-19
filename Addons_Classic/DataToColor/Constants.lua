local Load = select(2, ...)
local DataToColor = unpack(Load)

local UnitName = UnitName
local UnitGUID = UnitGUID
local UnitClass = UnitClass
local UnitRace = UnitRace

local WOW_PROJECT_ID = WOW_PROJECT_ID
local WOW_PROJECT_CLASSIC = WOW_PROJECT_CLASSIC

DataToColor.C.MAX_ACTIONBAR_SLOT = 120 -- up to moonkin form

DataToColor.C.unitPlayer = "player"
DataToColor.C.unitTarget = "target"
DataToColor.C.unitParty = "party"
DataToColor.C.unitRaid = "raid"
DataToColor.C.unitPet = "pet"

DataToColor.C.unitPartyNames = {}
DataToColor.C.unitPartyPetNames = {}

if WOW_PROJECT_ID == WOW_PROJECT_CLASSIC then
    DataToColor.C.unitFocus = "party1"
    DataToColor.C.unitFocusTarget = "party1target"
else
    DataToColor.C.unitFocus = "focus"
    DataToColor.C.unitFocusTarget = "focustarget"
end

DataToColor.C.unitPetTarget = "pettarget"
DataToColor.C.unitTargetTarget = "targettarget"
DataToColor.C.unitNormal = "normal"
DataToColor.C.unitmouseover = "mouseover"
DataToColor.C.unitmouseovertarget = "mouseovertarget"
DataToColor.C.unitSoftInteract = "softinteract"

DataToColor.C.SpellQueueWindow = "SpellQueueWindow"

-- Character's name
DataToColor.C.CHARACTER_NAME = UnitName(DataToColor.C.unitPlayer)
DataToColor.C.CHARACTER_GUID = UnitGUID(DataToColor.C.unitPlayer)
_, DataToColor.C.CHARACTER_CLASS, DataToColor.C.CHARACTER_CLASS_ID = UnitClass(DataToColor.C.unitPlayer)
_, _, DataToColor.C.CHARACTER_RACE_ID = UnitRace(DataToColor.C.unitPlayer)

-- Spells
DataToColor.C.Spell.AutoShotId = 75
DataToColor.C.Spell.ShootId = 5019
DataToColor.C.Spell.AttackId = 6603

-- Item / Inventory
DataToColor.C.ItemPattern = "(m:%d+)"

-- Loot
DataToColor.C.Loot.Corpse = 0
DataToColor.C.Loot.Ready = 1
DataToColor.C.Loot.Closed = 2

-- Gossips

-- https://www.townlong-yak.com/framexml/live/Helix/ArtTextureID.lua
-- [132060]="Interface/GossipFrame/VendorGossipIcon"
DataToColor.C.GossipIcon = {
    [132050] = 0,   --banker
    [132051] = 1,   --battlemaster
    [132052] = 2,   --binder
    [132053] = 3,   --gossip
    [132054] = 4,   --healer
    [132055] = 5,   --petition
    [132056] = 6,   --tabard
    [132057] = 7,   --taxi
    [132058] = 8,   --trainer
    [132059] = 9,   --unlearn
    [132060] = 10,  --vendor
}

DataToColor.C.Gossip = {
    ["banker"] = 0,
    ["battlemaster"] = 1,
    ["binder"] = 2,
    ["gossip"] = 3,
    ["healer"] = 4,
    ["petition"] = 5,
    ["tabard"] = 6,
    ["taxi"] = 7,
    ["trainer"] = 8,
    ["unlearn"] = 9,
    ["vendor"] = 10,
}

-- Gossips
DataToColor.C.GuidType = {
    ["None"] = 0,
    ["Creature"] = 1,
    ["Pet"] = 2,
    ["GameObject"] = 3,
    ["Vehicle"] = 4,
}

DataToColor.C.unitClassification = {
    ["normal"] = 1,
    ["trivial"] = 2,
    ["minus"] = 4,
    ["rare"] = 8,
    ["elite"] = 16,
    ["rareelite"] = 32,
    ["worldboss"] = 64
}

-- Mirror timer labels
DataToColor.C.MIRRORTIMER.BREATH = "BREATH"

DataToColor.C.ActionType.Spell = "spell"
DataToColor.C.ActionType.Macro = "macro"

DataToColor.C.PET_MODE_DEFENSIVE = "PET_MODE_DEFENSIVE"

DataToColor.C.CVarSoftTargetInteract = "SoftTargetInteract"