local Load = select(2, ...)
local DataToColor = unpack(Load)

local HasAction = HasAction
local GetActionTexture = GetActionTexture

-- Action bar slots we care about:
-- Main Action Bar: slots 1-12
-- Bottom Right (MULTIACTIONBAR2): slots 49-60
-- Bottom Left (MULTIACTIONBAR1): slots 61-72
-- Stance Bar 1 (Battle/Cat/Stealth/Shadow): slots 73-84
-- Stance Bar 2 (Defensive/CatProwl): slots 85-96
-- Stance Bar 3 (Berserker/Bear): slots 97-108
-- Stance Bar 4 (Moonkin): slots 109-120

-- Map actual slot to index (1-84 for compact encoding)
-- Exposed on DataToColor for reuse by ActionBarMacros.lua
local SlotToIndex = {}
local IndexToSlot = {}
DataToColor.SlotToIndex = SlotToIndex
DataToColor.IndexToSlot = IndexToSlot

-- Main bar: slots 1-12 -> indices 1-12
for i = 1, 12 do
    SlotToIndex[i] = i
    IndexToSlot[i] = i
end

-- Bottom Right bar: slots 49-60 -> indices 13-24
for i = 49, 60 do
    SlotToIndex[i] = i - 49 + 13
    IndexToSlot[i - 49 + 13] = i
end

-- Bottom Left bar: slots 61-72 -> indices 25-36
for i = 61, 72 do
    SlotToIndex[i] = i - 61 + 25
    IndexToSlot[i - 61 + 25] = i
end

-- Stance bar 1: slots 73-84 -> indices 37-48
for i = 73, 84 do
    SlotToIndex[i] = i - 73 + 37
    IndexToSlot[i - 73 + 37] = i
end

-- Stance bar 2: slots 85-96 -> indices 49-60
for i = 85, 96 do
    SlotToIndex[i] = i - 85 + 49
    IndexToSlot[i - 85 + 49] = i
end

-- Stance bar 3: slots 97-108 -> indices 61-72
for i = 97, 108 do
    SlotToIndex[i] = i - 97 + 61
    IndexToSlot[i - 97 + 61] = i
end

-- Stance bar 4: slots 109-120 -> indices 73-84
for i = 109, 120 do
    SlotToIndex[i] = i - 109 + 73
    IndexToSlot[i - 109 + 73] = i
end

-- Queue for sending texture changes via pixels
DataToColor.actionBarTextureQueue = DataToColor.TimedQueue:new(5, nil)

-- Cache of last known textures (slot -> textureId)
local textureCache = {}

-- Encoding format (24 bits max = 16,777,215):
-- index * 190000 + (textureId % 190000)
-- index: 1-84 (max encoded: 84 * 190000 + 189999 = 16,149,999)
-- textureId: truncated for uniqueness (modulo preserves lower digits)

local TEXTURE_MULTIPLIER = 190000

local function EncodeTexture(slot)
    local index = SlotToIndex[slot]
    if not index then return 0 end

    local textureId = 0
    if HasAction(slot) then
        textureId = DataToColor:GetActionTexture(slot) or 0
    end

    return index * TEXTURE_MULTIPLIER + (textureId % TEXTURE_MULTIPLIER)
end

-- Populates the texture queue with current action bar textures (initial load)
function DataToColor:InitActionBarTextureQueue()
    for index, slot in pairs(IndexToSlot) do
        local encoded = EncodeTexture(slot)
        local textureId = encoded % TEXTURE_MULTIPLIER
        textureCache[slot] = textureId

        if textureId > 0 then
            DataToColor.actionBarTextureQueue:push(encoded)
        end
    end
end

-- Checks a specific slot for texture change and pushes if changed
function DataToColor:CheckActionBarTextureChange(slot)
    local index = SlotToIndex[slot]
    if not index then return end -- Not a slot we track

    local encoded = EncodeTexture(slot)
    local textureId = encoded % TEXTURE_MULTIPLIER
    local cached = textureCache[slot] or 0

    if textureId ~= cached then
        textureCache[slot] = textureId
        -- Push the change (even if 0 to indicate slot cleared)
        DataToColor.actionBarTextureQueue:push(encoded)
        --DataToColor:Print(string.format("ActionBar slot %d texture changed: %d -> %d", slot, cached, textureId))
    end
end

-- Check all tracked slots for changes (can be called periodically or on events)
function DataToColor:CheckAllActionBarTextureChanges()
    for index, slot in pairs(IndexToSlot) do
        DataToColor:CheckActionBarTextureChange(slot)
    end
end

-- Debug: Print all current action bar textures
function DataToColor:PrintActionBarTextures()
    DataToColor:Print("Action Bar Textures:")
    for index, slot in pairs(IndexToSlot) do
        local textureId = 0
        if HasAction(slot) then
            textureId = DataToColor:GetActionTexture(slot) or 0
        end
        if textureId > 0 then
            DataToColor:Print(string.format("  Slot %d (idx %d): %d", slot, index, textureId))
        end
    end
end
