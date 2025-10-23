-- Backport SOUNDKIT for pre-WoD clients (Cataclysm 4.3.4)
if SOUNDKIT then return end

-- Cataclysm-safe SOUNDKIT backport
SOUNDKIT = {
    IG_ABILITY_OPEN           = "igAbilityOpen",
    IG_ABILITY_CLOSE          = "igAbilityClose",
    IG_MAINMENU_OPEN          = "igMainMenuOpen",
    IG_MAINMENU_CLOSE         = "igMainMenuClose",
    IG_CHARACTER_INFO_OPEN    = "igCharacterInfoOpen",
    IG_CHARACTER_INFO_CLOSE   = "igCharacterInfoClose",
    IG_SPELLBOOK_OPEN         = "igSpellBookOpen",
    IG_SPELLBOOK_CLOSE        = "igSpellBookClose",
    IG_BACKPACK_OPEN          = "igBackPackOpen",
    IG_BACKPACK_CLOSE         = "igBackPackClose",
    -- add any other ones BindPad uses here
}