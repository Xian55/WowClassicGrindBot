local Load = select(2, ...)
local DataToColor = unpack(Load)

DataToColor.S.spellInRangeTarget = {}
DataToColor.S.spellInRangeUnit = {}
DataToColor.S.interactInRangeUnit = {}

DataToColor.S.playerBuffs = {}
DataToColor.S.targetDebuffs = {}

DataToColor.S.playerAuraMap = {}

DataToColor.S.playerSpellBookName = {}
DataToColor.S.playerSpellBookId = {}
DataToColor.S.playerSpellBookIdHighest = {}
DataToColor.S.playerSpellBookIconToId = {}

function DataToColor:InitStorage()
    CreateSpellInRangeTarget()
    CreateSpellInRangeUnit()

    CreateInteractInRangeList()

    CreatePlayerBuffList()
    CreateTargetDebuffList()
    CreatePlayerAuraMap()
end

-- Using spell IconId over SpellId
-- Since Cataclysm Spell Ranks have been removed
-- In special cases uses SpellId such as Shoot/Wand or Auto Shot
function CreateSpellInRangeTarget()
    if DataToColor.C.CHARACTER_CLASS == "ROGUE" then
        DataToColor.S.spellInRangeTarget = {
            136189, -- 1752, -- "Sinister Strike"
            132324, -- 2764, -- "Throw"
            132222, -- 3018, -- "Shoot" for classic -> 7918, -- "Shoot Gun"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.spellInRangeTarget = {
            136006, -- 5176, -- "Wrath"
            132114, -- 5211, -- "Bash"
            132152, -- 1079, -- "Rip"
            132136, -- 6807, -- "Maul"
            136041, -- 5185, -- "Healing Touch"
            136078, -- 1126, -- "Mark of the Wild"
            136085, -- 8936, -- "Regrowth"
            136081, -- 774, -- "Rejuvenation",
            136104, -- 467, -- "Thorns"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.spellInRangeTarget = {
            132337, -- 100, -- "Charge"
            132155, -- 772, -- "Rend"
            132222, -- 3018, -- "Shoot" for classic -> 7918, -- "Shoot Gun"
            132324, -- 2764, -- "Throw"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "PRIEST" then
        DataToColor.S.spellInRangeTarget = {
            136207, -- 589, -- "Shadow Word: Pain"
            5019, -- "Shoot" -- special case changes with equipped weapon
            136208, -- 15407, -- "Mind Flay"
            136224, -- 8092, -- "Mind Blast"
            135924, -- 585, -- "Smite"
            135898, -- 14752, -- "Divine Spirit"
            135987, -- 1243, -- "Power World: Fortitude"
            135940, -- 17, -- Power Word: Shield
            135929, -- 2050, -- "Lesser Heal"
            135944, -- 33076, -- "Prayer of Mending"
            135953, -- 139, -- "Renew"
            136121, -- 976, -- "Shadow Protection"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
        DataToColor.S.spellInRangeTarget = {
            135959, -- 20271, -- "Judgement" / "Judgement of Light"
            135903, -- 879, -- "Exorcism"
            135907, -- 19750, -- "Flash Heal"
            135920, -- 635, -- "Holy Light"
            135906, -- 19740, -- "Blessing of Might"
            135908, -- 25782, -- "Greater Blessing of Might"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.spellInRangeTarget = {
            135812, -- 133, -- "Fireball"
            5019, -- "Shoot" -- special case changes with equipped weapon
            135808, -- 11366, -- "Pyroblast"
            135846, -- 116, -- "Frostbolt"
            135807, -- 2136, -- "Fire Blast"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.spellInRangeTarget = {
            132223, -- 2973, -- "Raptor Strike"
            75, -- "Auto Shot" special case icon updates based on weapon so use SpellId
            132204, -- 1978, -- "Serpent Sting"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.spellInRangeTarget = {
            136197, -- 686, -- "Shadow Bolt",
            5019, -- "Shoot" -- special case changes with equipped weapon
        }
    elseif DataToColor.C.CHARACTER_CLASS == "SHAMAN" then
        DataToColor.S.spellInRangeTarget = {
            136048, -- 403, -- "Lightning Bolt",
            136026, -- 8042, -- "Earth Shock"
            136052, -- 331, -- "Healing Wave"
            136043, -- 8004, -- "Lesser Healing Wave"
            136148, -- 131, -- "Water Breathing"
            136042, -- 1064, -- "Chain Heal"
            136089, -- 974, -- "Earth Shield"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "DEATHKNIGHT" then
        DataToColor.S.spellInRangeTarget = {
            237526, -- 49903, -- "Icy Touch"
            136145, -- 49893, -- "Death Coil"
            237532, -- 49576, -- "Death Grip"
            136088, -- 56222, -- "Dark Command",
            136119, -- 46584 -- "Raise Dead"
        }
    end
end

function CreateSpellInRangeUnit()
    if DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.spellInRangeUnit = {
            { 132165, DataToColor.C.unitPet } -- 6991 "Feed pet"
        }
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.spellInRangeUnit = {
            { 136168, DataToColor.C.unitPet }, -- 755 "Health Funnel"
        }
    end
end

function CreateInteractInRangeList()
    DataToColor.S.interactInRangeUnit = {}
    DataToColor.S.interactInRangeUnit[1] = { DataToColor.C.unitFocusTarget, 1 }
    DataToColor.S.interactInRangeUnit[2] = { DataToColor.C.unitFocusTarget, 2 }
    DataToColor.S.interactInRangeUnit[3] = { DataToColor.C.unitFocusTarget, 3 }

    DataToColor.S.interactInRangeUnit[4] = { DataToColor.C.unitFocus, 1 }
    DataToColor.S.interactInRangeUnit[5] = { DataToColor.C.unitFocus, 2 }
    DataToColor.S.interactInRangeUnit[6] = { DataToColor.C.unitFocus, 3 }

    DataToColor.S.interactInRangeUnit[7] = { DataToColor.C.unitPet, 1 }
    DataToColor.S.interactInRangeUnit[8] = { DataToColor.C.unitPet, 2 }
    DataToColor.S.interactInRangeUnit[9] = { DataToColor.C.unitPet, 3 }

    DataToColor.S.interactInRangeUnit[10] = { DataToColor.C.unitTarget, 1 }
    DataToColor.S.interactInRangeUnit[11] = { DataToColor.C.unitTarget, 2 }
    DataToColor.S.interactInRangeUnit[12] = { DataToColor.C.unitTarget, 3 }
end

function CreatePlayerBuffList()
    DataToColor.S.playerBuffs = {}
    DataToColor.S.playerBuffs[0] = { "Food", [134062] = 1, [134032] = 1, [133906] = 1, [133984] = 1 }
    DataToColor.S.playerBuffs[1] = { "Drink", [132794] = 1, [132800] = 1, [132805] = 1, [132802] = 1 }
    DataToColor.S.playerBuffs[2] = { "Well Fed", [136000] = 1 }
    DataToColor.S.playerBuffs[3] = { "Mana Regeneration", [2] = 1 } -- potion?
    DataToColor.S.playerBuffs[4] = { "Clearcasting", [136170] = 1 } -- Druid / Mage / Shaman

    if DataToColor.C.CHARACTER_CLASS == "PRIEST" then
        DataToColor.S.playerBuffs[10] = { "Fortitude", [135987] = 1, [135941] = 1 }
        DataToColor.S.playerBuffs[11] = { "Inner Fire", [135926] = 1 }
        DataToColor.S.playerBuffs[12] = { "Renew", [135953] = 1 }
        DataToColor.S.playerBuffs[13] = { "Shield", [135940] = 1 }
        DataToColor.S.playerBuffs[14] = { "Spirit", [1358982] = 1, [135946] = 1 }
        DataToColor.S.playerBuffs[15] = { "Inner Focus", [135863] = 1 }
        DataToColor.S.playerBuffs[16] = { "Abolish Disease", [136066] = 1 }
        DataToColor.S.playerBuffs[17] = { "Power Infusion", [135939] = 1 }
        DataToColor.S.playerBuffs[18] = { "Prayer of Shadow Protection", [135945] = 1 }
        DataToColor.S.playerBuffs[19] = { "Shadow Protection", [136121] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.playerBuffs[10] = { "Mark of the Wild", [136078] = 1 }
        DataToColor.S.playerBuffs[11] = { "Thorns", [136104] = 1 }
        DataToColor.S.playerBuffs[12] = { "Fury", [132242] = 1 }
        DataToColor.S.playerBuffs[13] = { "Prowl", [132089] = 1 }
        DataToColor.S.playerBuffs[14] = { "Rejuvenation", [136081] = 1 }
        DataToColor.S.playerBuffs[15] = { "Regrowth", [136085] = 1 }
        DataToColor.S.playerBuffs[16] = { "Omen of Clarity", [136017] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
        DataToColor.S.playerBuffs[5] = { "Seal of Righteousness", [132325] = 1 }
        DataToColor.S.playerBuffs[6] = { "Seal of the Crusader", [135924] = 1 }
        DataToColor.S.playerBuffs[7] = { "Seal of Command", [132347] = 1 }
        DataToColor.S.playerBuffs[8] = { "Seal of Wisdom", [135960] = 1 }
        DataToColor.S.playerBuffs[9] = { "Seal of Light", [135917] = 1 }
        DataToColor.S.playerBuffs[10] = { "Seal of Blood", [135961] = 1 }
        DataToColor.S.playerBuffs[11] = { "Seal of Vengeance", [135969] = 1 }
        DataToColor.S.playerBuffs[12] = { "Blessing of Might", [135906] = 1, [135908] = 1 }
        DataToColor.S.playerBuffs[13] = { "Blessing of Protection", [135964] = 1 }
        DataToColor.S.playerBuffs[14] = { "Blessing of Wisdom", [135970] = 1, [135912] = 1 }
        DataToColor.S.playerBuffs[15] = { "Blessing of Kings", [135995] = 1, [135993] = 1 }
        DataToColor.S.playerBuffs[16] = { "Blessing of Salvation", [135967] = 1, [135910] = 1 }
        DataToColor.S.playerBuffs[17] = { "Blessing of Sanctuary", [136051] = 1, [135911] = 1 }
        DataToColor.S.playerBuffs[18] = { "Blessing of Light", [135943] = 1, [135909] = 1 }
        DataToColor.S.playerBuffs[19] = { "Righteous Fury", [135962] = 1 }
        DataToColor.S.playerBuffs[20] = { "Divine Protection", [135954] = 1 }
        DataToColor.S.playerBuffs[21] = { "Avenging Wrath", [135875] = 1 }
        DataToColor.S.playerBuffs[22] = { "Holy Shield", [135880] = 1 }
        DataToColor.S.playerBuffs[23] = { "Divine Shield", [135896] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.playerBuffs[10] = { "Armor", [135843] = 1, [135991] = 1, [132221] = 1 }
        DataToColor.S.playerBuffs[11] = { "Arcane Intellect", [135932] = 1 }
        DataToColor.S.playerBuffs[12] = { "Ice Barrier", [135988] = 1 }
        DataToColor.S.playerBuffs[13] = { "Ward", [135806] = 1, [135850] = 1 }
        DataToColor.S.playerBuffs[14] = { "Fire Power", [135817] = 1 } -- not sure what is this
        DataToColor.S.playerBuffs[15] = { "Mana Shield", [136153] = 1 }
        DataToColor.S.playerBuffs[16] = { "Presence of Mind", [136031] = 1 }
        DataToColor.S.playerBuffs[17] = { "Arcane Power", [136048] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "ROGUE" then
        DataToColor.S.playerBuffs[10] = { "Slice and Dice", [132306] = 1 }
        DataToColor.S.playerBuffs[11] = { "Stealth", [132320] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.playerBuffs[10] = { "Battle Shout", [132333] = 1 }
        DataToColor.S.playerBuffs[11] = { "Bloodrage", [132277] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.playerBuffs[10] = { "Demon", [136185] = 1 }
        DataToColor.S.playerBuffs[11] = { "Soul Link", [136160] = 1 }
        DataToColor.S.playerBuffs[12] = { "Soulstone Resurrection", [136210] = 1 }
        DataToColor.S.playerBuffs[13] = { "Shadow Trance", [136223] = 1 }
        DataToColor.S.playerBuffs[14] = { "Fel Armor", [136156] = 1 }
        DataToColor.S.playerBuffs[15] = { "Fel Domination", [136082] = 1 }
        DataToColor.S.playerBuffs[16] = { "Demonic Sacrifice", [136184] = 1 }
        DataToColor.S.playerBuffs[17] = { "Sacrifice", [136190] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "SHAMAN" then
        DataToColor.S.playerBuffs[10] = { "Lightning Shield", [136051] = 1 }
        DataToColor.S.playerBuffs[11] = { "Water Shield", [132315] = 1 }
        DataToColor.S.playerBuffs[12] = { "Focused", [136027] = 1 } -- Shamanistic Focus
        DataToColor.S.playerBuffs[13] = { "Stoneskin", [136098] = 1 }
        DataToColor.S.playerBuffs[14] = { "Elemental Mastery", [136115] = 1 }
        DataToColor.S.playerBuffs[15] = { "Stormstrike", [135963] = 1 }
        DataToColor.S.playerBuffs[16] = { "Nature's Swiftness", [136076] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.playerBuffs[10] = { "Aspect of the Cheetah", [132242] = 1 }
        DataToColor.S.playerBuffs[11] = { "Aspect of the Pack", [132267] = 1 }
        DataToColor.S.playerBuffs[12] = { "Aspect of the Hawk", [136076] = 1 }
        DataToColor.S.playerBuffs[13] = { "Aspect of the Monkey", [132159] = 1 }
        DataToColor.S.playerBuffs[14] = { "Aspect of the Viper", [132160] = 1 }
        DataToColor.S.playerBuffs[15] = { "Rapid Fire", [132208] = 1 }
        DataToColor.S.playerBuffs[16] = { "Quick Shots", [132347] = 1 }
        DataToColor.S.playerBuffs[17] = { "Trueshot Aura", [132329] = 1 }
        DataToColor.S.playerBuffs[18] = { "Aspect of the Dragonhawk", [132188] = 1 }
        DataToColor.S.playerBuffs[19] = { "Lock and Load", [236185] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "DEATHKNIGHT" then
        DataToColor.S.playerBuffs[10] = { "Blood Tap", [237515] = 1 }
        DataToColor.S.playerBuffs[11] = { "Horn of Winter", [134228] = 1 }
        DataToColor.S.playerBuffs[12] = { "Icebound Fortitude", [237525] = 1 }
        DataToColor.S.playerBuffs[13] = { "Path of Frost", [237528] = 1 }
        DataToColor.S.playerBuffs[14] = { "Anti-Magic Shell", [136120] = 1 }
        DataToColor.S.playerBuffs[15] = { "Army of the Dead", [237511] = 1 }
        DataToColor.S.playerBuffs[16] = { "Vampiric Blood", [136168] = 1 }
        DataToColor.S.playerBuffs[17] = { "Dancing Rune Weapon", [135277] = 1 }
        DataToColor.S.playerBuffs[18] = { "Unbreakable Armor", [132388] = 1 }
        DataToColor.S.playerBuffs[19] = { "Bone Shield", [132728] = 1 }
        DataToColor.S.playerBuffs[20] = { "Summon Gargoyle", [132182] = 1 }
        DataToColor.S.playerBuffs[21] = { "Freezing Fog", [135840] = 1 }
    end
end

function CreateTargetDebuffList()
    DataToColor.S.targetDebuffs = {}
    if DataToColor.C.CHARACTER_CLASS == "PRIEST" then
        DataToColor.S.targetDebuffs[0] = { "Pain", [136207] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Holy Fire", [135972] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Vampiric Embrace", [136230] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Silence", [136164] = 1 }
        DataToColor.S.targetDebuffs[4] = { "Shackle Undead", [136091] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "DRUID" then
        DataToColor.S.targetDebuffs[0] = { "Roar", [132121] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Faerie Fire", [136033] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Rip", [132152] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Moonfire", [136096] = 1 }
        DataToColor.S.targetDebuffs[4] = { "Entangling Roots", [136100] = 1 }
        DataToColor.S.targetDebuffs[5] = { "Rake", [132122] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "PALADIN" then
        DataToColor.S.targetDebuffs[0] = { "Judgement of the Crusader", [135924] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Hammer of Justice", [135963] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Judgement of Wisdom", [135960] = 1, [236255] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Judgement of Light", [135959] = 1 }
        DataToColor.S.targetDebuffs[4] = { "Judgement of Justice", [236258] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "MAGE" then
        DataToColor.S.targetDebuffs[0] = { "Frostbite", [135842] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Slow", [136091] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "ROGUE" then
    elseif DataToColor.C.CHARACTER_CLASS == "WARRIOR" then
        DataToColor.S.targetDebuffs[0] = { "Rend", [132155] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Thunder Clap", [136105] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Hamstring", [132316] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Charge Stun", [135860] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "WARLOCK" then
        DataToColor.S.targetDebuffs[0] = { "Curse of", [136139] = 1, [136122] = 1, [136162] = 1, [136225] = 1,
            [136130] = 1,
            [136140] = 1, [136138] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Corruption", [136118] = 1, [136193] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Immolate", [135817] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Siphon Life", [136188] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "HUNTER" then
        DataToColor.S.targetDebuffs[0] = { "Serpent Sting", [132204] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Hunter's Mark", [132212] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Viper Sting", [132157] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Explosive Shot", [236178] = 1 }
        DataToColor.S.targetDebuffs[4] = { "Black Arrow", [136181] = 1 }
    elseif DataToColor.C.CHARACTER_CLASS == "DEATHKNIGHT" then
        DataToColor.S.targetDebuffs[0] = { "Blood Plague", [237514] = 1 }
        DataToColor.S.targetDebuffs[1] = { "Frost Fever", [237522] = 1 }
        DataToColor.S.targetDebuffs[2] = { "Strangulate", [136214] = 1 }
        DataToColor.S.targetDebuffs[3] = { "Chains of Ice", [135834] = 1 }
    end
end

function CreatePlayerAuraMap()

    -- spell id -> GetShapeshiftForm

    -- Druid
    DataToColor.S.playerAuraMap[5487] = 1 -- bear
    DataToColor.S.playerAuraMap[9634] = 1 -- dire bear

    DataToColor.S.playerAuraMap[1066] = 2 -- aqua
    DataToColor.S.playerAuraMap[768] = 3 -- cat
    DataToColor.S.playerAuraMap[783] = 4 -- travel
    DataToColor.S.playerAuraMap[24858] = 5 -- moonkin

    DataToColor.S.playerAuraMap[40120] = 6 -- fly
    DataToColor.S.playerAuraMap[33943] = 6 -- fly

    -- Paladin
    DataToColor.S.playerAuraMap[465] = 1 -- devo
    DataToColor.S.playerAuraMap[10290] = 1
    DataToColor.S.playerAuraMap[643] = 1
    DataToColor.S.playerAuraMap[10291] = 1
    DataToColor.S.playerAuraMap[1032] = 1
    DataToColor.S.playerAuraMap[10292] = 1
    DataToColor.S.playerAuraMap[10293] = 1
    DataToColor.S.playerAuraMap[27149] = 1

    DataToColor.S.playerAuraMap[7294] = 2 -- retri
    DataToColor.S.playerAuraMap[10298] = 2
    DataToColor.S.playerAuraMap[10299] = 2
    DataToColor.S.playerAuraMap[10300] = 2
    DataToColor.S.playerAuraMap[10301] = 2
    DataToColor.S.playerAuraMap[27150] = 2

    DataToColor.S.playerAuraMap[19746] = 3 -- concent

    DataToColor.S.playerAuraMap[19876] = 4 -- shadow
    DataToColor.S.playerAuraMap[19895] = 4
    DataToColor.S.playerAuraMap[19896] = 4
    DataToColor.S.playerAuraMap[27151] = 4

    DataToColor.S.playerAuraMap[19888] = 5 -- frost
    DataToColor.S.playerAuraMap[19897] = 5
    DataToColor.S.playerAuraMap[19898] = 5
    DataToColor.S.playerAuraMap[27152] = 5

    DataToColor.S.playerAuraMap[19891] = 6 -- fire
    DataToColor.S.playerAuraMap[19899] = 6
    DataToColor.S.playerAuraMap[19900] = 6
    DataToColor.S.playerAuraMap[27153] = 6

    DataToColor.S.playerAuraMap[20218] = 7 -- sanct

    DataToColor.S.playerAuraMap[32223] = 8 -- crusader

    -- Death Knight
end