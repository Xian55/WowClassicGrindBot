local Load = select(2, ...)
local DataToColor = unpack(Load)

local HasAction = HasAction
local GetActionInfo = GetActionInfo
local GetMacroInfo = GetMacroInfo
local strlower = string.lower
local byte = string.byte

-- Reuse slot mappings from ActionBarTextures.lua (exposed globally)
local SlotToIndex = DataToColor.SlotToIndex
local IndexToSlot = DataToColor.IndexToSlot

-- Queue for sending macro name hash changes via pixels
DataToColor.actionBarMacroQueue = DataToColor.TimedQueue:new(5, nil)

-- Cache of last known macro name hashes (slot -> nameHash)
local macroHashCache = {}

-- Encoding format (24 bits max = 16,777,215):
-- index * 200000 + (nameHash % 200000)
-- index: 1-84 (max encoded: 84 * 200000 + 199999 = 16,999,999)
-- nameHash: DJB2 hash truncated to fit (modulo preserves distribution)

local MACRO_MULTIPLIER = 200000

-- DJB2 hash function (24-bit, matching C# implementation)
-- Converts to lowercase before hashing for case-insensitive matching
local function DJB2Hash24(str)
    if not str or str == "" then
        return 0
    end

    local hash = 5381
    local lower = strlower(str)

    for i = 1, #lower do
        local c = byte(lower, i)
        -- hash = hash * 33 + c, but we need to keep it within bounds
        hash = ((hash * 33) + c) % 16777216
    end

    return hash
end

-- Get macro name hash for a slot (0 if not a macro or no macro)
local function GetMacroNameHash(slot)
    if not HasAction(slot) then
        return 0
    end

    local actionType, id = GetActionInfo(slot)
    if actionType ~= "macro" then
        return 0
    end

    local name = GetMacroInfo(id)
    if not name or name == "" then
        return 0
    end

    return DJB2Hash24(name)
end

local function EncodeMacro(slot)
    local index = SlotToIndex[slot]
    if not index then return 0 end

    local nameHash = GetMacroNameHash(slot)

    return index * MACRO_MULTIPLIER + (nameHash % MACRO_MULTIPLIER)
end

-- Populates the macro queue with current action bar macros (initial load)
function DataToColor:InitActionBarMacroQueue()
    for index, slot in pairs(IndexToSlot) do
        local encoded = EncodeMacro(slot)
        local nameHash = encoded % MACRO_MULTIPLIER
        macroHashCache[slot] = nameHash

        if nameHash > 0 then
            DataToColor.actionBarMacroQueue:push(encoded)
        end
    end
end

-- Checks a specific slot for macro change and pushes if changed
function DataToColor:CheckActionBarMacroChange(slot)
    local index = SlotToIndex[slot]
    if not index then return end -- Not a slot we track

    local encoded = EncodeMacro(slot)
    local nameHash = encoded % MACRO_MULTIPLIER
    local cached = macroHashCache[slot] or 0

    if nameHash ~= cached then
        macroHashCache[slot] = nameHash
        -- Push the change (even if 0 to indicate macro cleared)
        DataToColor.actionBarMacroQueue:push(encoded)
        --DataToColor:Print(string.format("ActionBar slot %d macro hash changed: %d -> %d", slot, cached, nameHash))
    end
end

-- Check all tracked slots for macro changes (can be called periodically or on events)
function DataToColor:CheckAllActionBarMacroChanges()
    for index, slot in pairs(IndexToSlot) do
        DataToColor:CheckActionBarMacroChange(slot)
    end
end

-- Debug: Print all current action bar macros
function DataToColor:PrintActionBarMacros()
    DataToColor:Print("Action Bar Macros:")
    for index, slot in pairs(IndexToSlot) do
        if HasAction(slot) then
            local actionType, id = GetActionInfo(slot)
            if actionType == "macro" then
                local name = GetMacroInfo(id)
                local hash = DJB2Hash24(name)
                DataToColor:Print(string.format("  Slot %d (idx %d): '%s' hash=%d", slot, index, name or "?", hash))
            end
        end
    end
end

-- Expose hash function for testing
DataToColor.DJB2Hash24 = DJB2Hash24
