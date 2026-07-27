local Load = select(2, ...)
local DataToColor = unpack(Load)

local UnitName = UnitName
local UnitGUID = UnitGUID
local UnitClass = UnitClass
local UnitRace = UnitRace
local UnitFactionGroup = UnitFactionGroup

DataToColor.C.MAX_ACTIONBAR_SLOT = 120 -- up to moonkin form

DataToColor.C.unitPlayer = "player"
DataToColor.C.unitTarget = "target"
DataToColor.C.unitParty = "party"
DataToColor.C.unitRaid = "raid"
DataToColor.C.unitPet = "pet"

DataToColor.C.unitPartyNames = {}
DataToColor.C.unitPartyPetNames = {}

-- Vanilla has no focus unit. IsClassic() and not a raw `WOW_PROJECT_ID ==
-- WOW_PROJECT_CLASSIC`: on a Legacy client (4.3.4, 5.4.8) both globals are nil,
-- so the raw compare is nil == nil and would strip focus support there.
if DataToColor.IsClassic() then
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

DataToColor.C.CHARACTER_CLASS_MAP = {
    ["None"] = 0,
    ["Warrior"] = 1,
    ["Paladin"] = 2,
    ["Hunter"] = 3,
    ["Rogue"] = 4,
    ["Priest"] = 5,
    ["DeathKnight"] = 6,
    ["Shaman"] = 7,
    ["Mage"] = 8,
    ["Warlock"] = 9,
    ["Monk"] = 10,
    ["Druid"] = 11,
    ["DemonHunter"] = 12
}

DataToColor.C.CHARACTER_RACE_MAP = {
    ["None"] = 0,
    ["Human"] = 1,
    ["Orc"] = 2,
    ["Dwarf"] = 3,
    ["NightElf"] = 4,
    ["Undead"] = 5,
    ["Tauren"] = 6,
    ["Gnome"] = 7,
    ["Troll"] = 8,
    ["Goblin"] = 9,
    ["BloodElf"] = 10,
    ["Draenei"] = 11,
    ["Worgen"] = 22,
    ["Gilnean"] = 23,
    ["Pandaren"] = 24
}

-- MoP gives a Pandaren a different race id per faction. The bot models a single
-- Pandaren race plus the faction from FACTION_MAP, so these collapse onto 24.
DataToColor.C.PANDAREN_ALLIANCE_RACE_ID = 25
DataToColor.C.PANDAREN_HORDE_RACE_ID = 26

DataToColor.C.FACTION_MAP = {
    ["Alliance"] = 0,
    ["Horde"] = 1,
    ["Neutral"] = 2
}

-- Character info — wrapped so it can be re-detected from OnEnteringWorld.
-- On cold-start (addon loaded before PLAYER_ENTERING_WORLD) UnitClass("player")
-- returns nil and any class-conditional table built from these constants ends
-- up empty (e.g. S.spellInRangeTarget is empty -> Pull/Combat range always false).
function DataToColor:DetectPlayerCharacter()
    DataToColor.C.CHARACTER_NAME = UnitName(DataToColor.C.unitPlayer)
    DataToColor.C.CHARACTER_GUID = UnitGUID(DataToColor.C.unitPlayer)
    DataToColor.C.CHARACTER_CLASS_LOWER, DataToColor.C.CHARACTER_CLASS, DataToColor.C.CHARACTER_CLASS_ID = UnitClass(DataToColor.C.unitPlayer)
    DataToColor.C.CHARACTER_RACE, _, DataToColor.C.CHARACTER_RACE_ID = UnitRace(DataToColor.C.unitPlayer)

    if DataToColor.C.CHARACTER_RACE_ID == nil then
        DataToColor.C.CHARACTER_RACE_ID = DataToColor.C.CHARACTER_RACE_MAP[DataToColor.C.CHARACTER_RACE]
    end

    if DataToColor.C.CHARACTER_CLASS_ID == nil then
        DataToColor.C.CHARACTER_CLASS_ID = DataToColor.C.CHARACTER_CLASS_MAP[DataToColor.C.CHARACTER_CLASS_LOWER]
    end

    if DataToColor.C.CHARACTER_RACE_ID == DataToColor.C.PANDAREN_ALLIANCE_RACE_ID
        or DataToColor.C.CHARACTER_RACE_ID == DataToColor.C.PANDAREN_HORDE_RACE_ID then
        DataToColor.C.CHARACTER_RACE_ID = DataToColor.C.CHARACTER_RACE_MAP.Pandaren
    end

    -- A Pandaren has no faction until the Wandering Isle is finished, and
    -- UnitFactionGroup answers nil for that state on 5.4.8 rather than the
    -- "Neutral" the modern API documents. Anything unrecognised is Neutral too:
    -- guessing a side would make the bot walk up to hostile vendors.
    DataToColor.C.CHARACTER_FACTION = UnitFactionGroup(DataToColor.C.unitPlayer) or "Neutral"
    DataToColor.C.CHARACTER_FACTION_ID =
        DataToColor.C.FACTION_MAP[DataToColor.C.CHARACTER_FACTION] or DataToColor.C.FACTION_MAP.Neutral

    -- Cell 46 payload, decoded by Core/Addon/PlayerReader.cs:
    --   FACTION_ID * 1000000 + RACE_ID * 10000 + CLASS_ID * 100 + ClientVersion
    -- Highest possible value is 2241295, a pixel carries 0..16777215.
    -- Race/class stay nil-safe: on a cold start the C# side reports "failed to
    -- read UnitClass and UnitRace" instead of this erroring out mid-frame.
    DataToColor.C.RACE_CLASS_VERSION_CELL =
        DataToColor.C.CHARACTER_FACTION_ID * 1000000
        + (DataToColor.C.CHARACTER_RACE_ID or 0) * 10000
        + (DataToColor.C.CHARACTER_CLASS_ID or 0) * 100
        + DataToColor.ClientVersion
end

DataToColor:DetectPlayerCharacter()

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

-- Mail state constants (used by Mail.lua and C# MailReader)
-- Note: Opened/Closed states are handled by the MailFrameShown bit, not gossip
DataToColor.C.Mail = {
    Sending = 9999988,
    SendSuccess = 9999987,
    SendFailed = 9999986,
    Finished = 9999985,
    ItemAttached = 9999984,
}
