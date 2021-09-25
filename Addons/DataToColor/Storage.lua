local Load = select(2, ...)
local DataToColor = unpack(Load)

DataToColor.S.PlayerClass = 0
DataToColor.S.spellInRangeList = {}

DataToColor.S.playerBuffs = {}
DataToColor.S.targetDebuffs = {}

function DataToColor:InitStorage()
    CreatePlayerClass()
    CreateSpellInRangeList()

    CreatePlayerBuffList()
    CreateTargetDebuffList()
end

function CreatePlayerClass()
    -- UnitClass returns class and the class in uppercase e.g. "Mage" and "MAGE"
    if DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.PlayerClass = 128
    elseif DataToColor.C.CHARACTER_CLASS == "ROGUE" then
        DataToColor.S.PlayerClass = 64
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.PlayerClass = 32
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
        DataToColor.S.PlayerClass = 16
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.PlayerClass = 8
    elseif DataToColor.C.CHARACTER_CLASS == "PRIEST" then
        DataToColor.S.PlayerClass = 4
    elseif DataToColor.C.CHARACTER_CLASS == "SHAMAN" then
        DataToColor.S.PlayerClass = 2
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.PlayerClass = 1    
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.PlayerClass = 256
    else
        DataToColor.S.PlayerClass = 0
    end
end

function CreateSpellInRangeList()
    if DataToColor.C.CHARACTER_CLASS == "ROGUE" then
        DataToColor.S.spellInRangeList = {
            "Sinister Strike", --1
            "Throw", --2
            "Shoot Gun" --4
        }
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.spellInRangeList = {
            "Wrath", --1
            "Bash", --2
            "Rip" --3
        }
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.spellInRangeList = {
            "Charge", --1
            "Rend", --2
            "Shoot Gun", --4
        }       
    elseif DataToColor.C.CHARACTER_CLASS == "PRIEST" then
        DataToColor.S.spellInRangeList = {
            "Shadow Word: Pain", --1
            "Mind Blast", --2
            "Mind Flay", --4
            "Shoot", --8
        }
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
        DataToColor.S.spellInRangeList = {
            "Judgement" --1
        }
    elseif DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.spellInRangeList = {
            "Fireball", --1
            "Shoot",
            "Pyroblast",
            "Frostbolt",
            "Fire Blast"
        }     
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.spellInRangeList = {
            "Raptor Strike", --1
            "Auto Shot", --2
            "Serpent Sting" --3
        }      
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.spellInRangeList = {
            "Shadow Bolt",
            "Shoot"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "SHAMAN" then
        DataToColor.S.spellInRangeList = {
            "Lightning Bolt",
            "Earth Shock"
        }
    end
end

function CreatePlayerBuffList()
    DataToColor.S.playerBuffs = {}
    DataToColor.S.playerBuffs[0] = "Food"
    DataToColor.S.playerBuffs[1] = "Drink"
    DataToColor.S.playerBuffs[2] = "Well Fed"
    DataToColor.S.playerBuffs[3] = "Mana Regeneration"

    if DataToColor.C.CHARACTER_CLASS == "PRIEST" then
        DataToColor.S.playerBuffs[10] = "Fortitude"
        DataToColor.S.playerBuffs[11] = "Inner Fire"
        DataToColor.S.playerBuffs[12] = "Renew"
        DataToColor.S.playerBuffs[13] = "Shield"
        DataToColor.S.playerBuffs[14] = "Spirit"
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.playerBuffs[10] = "Mark of the Wild"
        DataToColor.S.playerBuffs[11] = "Thorns"
        DataToColor.S.playerBuffs[12] = "Fury"
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
        DataToColor.S.playerBuffs[10] = "Aura"
        DataToColor.S.playerBuffs[11] = "Blessing"
        DataToColor.S.playerBuffs[12] = "Seal"
    elseif DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.playerBuffs[10] = "Armor"
        DataToColor.S.playerBuffs[11] = "Arcane Intellect"
        DataToColor.S.playerBuffs[12] = "Ice Barrier"
        DataToColor.S.playerBuffs[13] = "Ward"
        DataToColor.S.playerBuffs[14] = "Fire Power"
        DataToColor.S.playerBuffs[15] = "Mana Shield"
    elseif DataToColor.C.CHARACTER_CLASS == "ROGUE" then
        DataToColor.S.playerBuffs[10] = "Slice and Dice"
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.playerBuffs[10] = "Battle Shout"
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.playerBuffs[10] = "Demon"
        DataToColor.S.playerBuffs[11] = "Soul Link"
        DataToColor.S.playerBuffs[12] = "Soulstone Resurrection"
        DataToColor.S.playerBuffs[13] = "Shadow Trance"
    elseif DataToColor.C.CHARACTER_CLASS == "SHAMAN" then
        DataToColor.S.playerBuffs[10] = "Lightning Shield"
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.playerBuffs[10] = "Aspect of"
        DataToColor.S.playerBuffs[11] = "Rapid Fire"
        DataToColor.S.playerBuffs[12] = "Quick Shots"
    end
end

function CreateTargetDebuffList()
    DataToColor.S.targetDebuffs = {}
    if DataToColor.C.CHARACTER_CLASS == "PRIEST" then 
        DataToColor.S.targetDebuffs[0] = "Pain"
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.targetDebuffs[0] = "Roar"
        DataToColor.S.targetDebuffs[1] = "Faerie Fire"
        DataToColor.S.targetDebuffs[2] = "Rip"
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
    elseif DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.targetDebuffs[0] = "Frostbite"
    elseif DataToColor.C.CHARACTER_CLASS == "ROGUE" then
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.targetDebuffs[0] = "Rend"
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.targetDebuffs[0] = "Curse of"
        DataToColor.S.targetDebuffs[1] = "Corruption"
        DataToColor.S.targetDebuffs[2] = "Immolate"
        DataToColor.S.targetDebuffs[3] = "Siphon Life"
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.targetDebuffs[0] = "Serpect Sting"
    end
end