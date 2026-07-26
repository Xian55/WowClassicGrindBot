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

    -- BindPad references these two and they were missing, so PlaySound received nil:
    -- BindPad.lua:607 (GS_TITLE_OPTION_OK), :963 and :1491 (IG_ABILITY_ICON_DROP).
    -- Keep this list in step with `grep -ohE "SOUNDKIT\.[A-Z_]+" Addons/BindPad/*.lua`.
    GS_TITLE_OPTION_OK        = "gsTitleOptionOK",
    IG_ABILITY_ICON_DROP      = "igAbilityIconDrop",
}