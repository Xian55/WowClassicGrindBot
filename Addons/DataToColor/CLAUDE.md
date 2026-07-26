# DataToColor WoW Addon (Lua 5.1)

World of Warcraft uses **Lua 5.1** (all versions including Classic). The addon encodes game state as pixel colors for external reading.

## Addon Version Tracking

We track each addon change per Pull Request. PR titles start with "Addon: [x.y.z] - TITLE".

When a change happens in `*.lua` files, bump the patch version (if not already bumped) in
**all four** TOCs - they are separate files and drift silently if one is missed:
- `Addons/DataToColor/DataToColor.toc` (legacy: 4.3.4 Cataclysm **and 5.4.8 MoP**)
- `Addons/DataToColor/DataToColor_Classic.toc`
- `Addons/DataToColor/DataToColor_TBC.toc`
- `Addons/DataToColor/DataToColor_Wrath.toc`

## Legacy clients (4.3.4 Cata, 5.4.8 MoP)

Both load `DataToColor.toc` - old clients only read `<foldername>.toc`, so there is no
per-flavour TOC for them and one file serves both. `WOW_PROJECT_ID` is nil there, which
is what `DataToColor.IsLegacy()` detects.

**`C_Timer` does not exist before 6.0.** It is supplied by the separate
`Addons/cTimerBackport` addon, declared in `## OptionalDeps` so WoW loads it first.
Do not inline it here: `Mail.lua` and `SellJunk.lua` capture `local C_Timer = C_Timer`
at file scope, so the polyfill must be in place before they load, and OptionalDeps is
what guarantees that.

**Prefer feature detection over version branching.** 5.4.8 is not "Cata plus a bit" -
MoP 5.0 deleted the point-based talent trees, so `GetUnspentTalentPoints`,
`GetNumTalentTabs` and `GetNumTalents` are all absent while `GetTalentInfo` survives
with a different signature. A polyfill written for 4.3.4 can therefore fail on 5.4.8;
guard on the specific function, not on the build number. Watch for this in anything
touching talents, pets (pet talents were removed too) or specialisations.

## Event-Driven Change Tracking

Use event-driven change tracking in the addon instead of polling state each time.
Lua events should be handled in `EventHandlers.lua`.

## File Structure

```
Addons/DataToColor/
├── init.lua                     - Addon initialization, AceAddon setup
├── DataToColor.lua              - Main frame update loop (performance critical)
├── Constants.lua                - Static data tables
├── Query.lua                    - Game state queries
├── BitCache.lua                 - Cache for Query.lua, avoids excessive WoW API calls
├── AuraCache.lua                - Aura/buff/debuff caching
├── Storage.lua                  - Data storage structures
├── EventHandlers.lua            - WoW event handling
├── Collections.lua              - Data structure implementations
├── ActionBarTextures.lua        - Action bar texture tracking
├── ActionBarMacros.lua          - Macro detection
├── Diagnostics.lua              - Diagnostic utilities
├── Mail.lua                     - Mail system interaction
├── SellJunk.lua                 - Junk selling automation
├── SetupDefaultBindings.lua     - Default key binding setup
├── Versions.lua                 - Version info
├── LegacyTextureToFileID.lua    - Legacy texture ID mapping
├── WorldMapAreaIDToUiMapID.lua  - Map area ID conversion
└── libs/                        - Ace3 libraries (external, don't modify)
```
