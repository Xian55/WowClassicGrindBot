# DataToColor WoW Addon (Lua 5.1)

World of Warcraft uses **Lua 5.1** (all versions including Classic). The addon encodes game state as pixel colors for external reading.

## Addon Version Tracking

We track each addon change per Pull Request. PR titles start with "Addon: [x.y.z] - TITLE".

When a change happens in `*.lua` files, bump the patch version (if not already bumped) in:
- `Addons/DataToColor/DataToColor_TBC.toc`
- `Addons/DataToColor/DataToColor.toc`
- `Addons/DataToColor/DataToColor_Classic.toc`

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
