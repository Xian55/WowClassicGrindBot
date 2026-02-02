local Load = select(2, ...)
local DataToColor = unpack(Load)

local InCombatLockdown = InCombatLockdown

local SetBinding = SetBinding
local SaveBindings = SaveBindings
local GetCurrentBindingSet = GetCurrentBindingSet
local GetBindingKey = GetBindingKey


-- Compact key IDs for encoding (1-90 range to fit in 7 bits with modifiers)
-- Format: (mod1 << 22) | (mod2 << 20) | (index << 14) | (key1Id << 7) | key2Id
-- Modifier values: 0=none, 1=Shift, 2=Ctrl, 3=Alt
local WoWKeyToId = {
    -- Letters A-Z: 1-26
    ["A"] = 1, ["B"] = 2, ["C"] = 3, ["D"] = 4, ["E"] = 5,
    ["F"] = 6, ["G"] = 7, ["H"] = 8, ["I"] = 9, ["J"] = 10,
    ["K"] = 11, ["L"] = 12, ["M"] = 13, ["N"] = 14, ["O"] = 15,
    ["P"] = 16, ["Q"] = 17, ["R"] = 18, ["S"] = 19, ["T"] = 20,
    ["U"] = 21, ["V"] = 22, ["W"] = 23, ["X"] = 24, ["Y"] = 25, ["Z"] = 26,
    -- Numbers 0-9: 27-36
    ["0"] = 27, ["1"] = 28, ["2"] = 29, ["3"] = 30, ["4"] = 31,
    ["5"] = 32, ["6"] = 33, ["7"] = 34, ["8"] = 35, ["9"] = 36,
    -- Numpad 0-9: 37-46
    ["NUMPAD0"] = 37, ["NUMPAD1"] = 38, ["NUMPAD2"] = 39, ["NUMPAD3"] = 40,
    ["NUMPAD4"] = 41, ["NUMPAD5"] = 42, ["NUMPAD6"] = 43, ["NUMPAD7"] = 44,
    ["NUMPAD8"] = 45, ["NUMPAD9"] = 46,
    -- Numpad operators: 47-51
    ["NUMPADMULTIPLY"] = 47, ["NUMPADPLUS"] = 48, ["NUMPADMINUS"] = 49,
    ["NUMPADDECIMAL"] = 50, ["NUMPADDIVIDE"] = 51,
    -- Function keys F1-F12: 52-63
    ["F1"] = 52, ["F2"] = 53, ["F3"] = 54, ["F4"] = 55,
    ["F5"] = 56, ["F6"] = 57, ["F7"] = 58, ["F8"] = 59,
    ["F9"] = 60, ["F10"] = 61, ["F11"] = 62, ["F12"] = 63,
    -- Special keys: 64-80
    ["SPACE"] = 64, ["TAB"] = 65, ["ENTER"] = 66, ["ESCAPE"] = 67,
    ["BACKSPACE"] = 68, ["DELETE"] = 69, ["INSERT"] = 70,
    ["HOME"] = 71, ["END"] = 72, ["PAGEUP"] = 73, ["PAGEDOWN"] = 74,
    ["UP"] = 75, ["DOWN"] = 76, ["LEFT"] = 77, ["RIGHT"] = 78,
    -- Punctuation: 79-89
    ["-"] = 79, ["="] = 80, [","] = 81, ["."] = 82,
    [";"] = 83, ["/"] = 84, ["`"] = 85,
    ["["] = 86, ["\\"] = 87, ["]"] = 88, ["'"] = 89,
}

-- Parses a key string like "SHIFT-F" and returns the base key and modifier value
-- Returns: keyString (base key), modifierValue (0=none, 1=Shift, 2=Ctrl, 3=Alt)
local function ParseKeyString(keyString)
    if not keyString then return nil, 0 end

    if string.find(keyString, "^SHIFT%-") then
        return string.sub(keyString, 7), 1  -- MODIFIER_SHIFT
    elseif string.find(keyString, "^CTRL%-") then
        return string.sub(keyString, 6), 2  -- MODIFIER_CTRL
    elseif string.find(keyString, "^ALT%-") then
        return string.sub(keyString, 5), 3  -- MODIFIER_ALT
    end
    return keyString, 0
end

-- BindingIDs we care about, with their index for encoding
-- Index is used in 24-bit encoding: index * 65536 + key1Id * 256 + key2Id
local BindingIndex = {
    -- Movement (1-8)
    ["JUMP"] = 1,
    ["MOVEFORWARD"] = 2,
    ["MOVEBACKWARD"] = 3,
    ["STRAFELEFT"] = 4,
    ["STRAFERIGHT"] = 5,
    ["TURNLEFT"] = 6,
    ["TURNRIGHT"] = 7,
    ["SITORSTAND"] = 8,

    -- Targeting (9-14)
    ["TARGETNEARESTENEMY"] = 9,
    ["TARGETLASTTARGET"] = 10,
    ["ASSISTTARGET"] = 11,
    ["TARGETPET"] = 12,
    ["TARGETFOCUS"] = 13,          -- TBC+: target focus unit (also sets focus to target)

    -- Combat (15-17)
    ["STARTATTACK"] = 15,
    ["STOPATTACK"] = 16,           -- WoW built-in (limited, prefer CUSTOM_STOPATTACK)
    ["PETATTACK"] = 17,

    -- Interaction (18-20)
    ["INTERACTTARGET"] = 18,
    ["INTERACTMOUSEOVER"] = 19,
    ["FOLLOWTARGET"] = 20,

    -- Main Action Bar slots 1-12 (21-32)
    ["ACTIONBUTTON1"] = 21,
    ["ACTIONBUTTON2"] = 22,
    ["ACTIONBUTTON3"] = 23,
    ["ACTIONBUTTON4"] = 24,
    ["ACTIONBUTTON5"] = 25,
    ["ACTIONBUTTON6"] = 26,
    ["ACTIONBUTTON7"] = 27,
    ["ACTIONBUTTON8"] = 28,
    ["ACTIONBUTTON9"] = 29,
    ["ACTIONBUTTON10"] = 30,
    ["ACTIONBUTTON11"] = 31,
    ["ACTIONBUTTON12"] = 32,

    -- Bottom Right Action Bar slots 49-60 (33-44)
    ["MULTIACTIONBAR2BUTTON1"] = 33,
    ["MULTIACTIONBAR2BUTTON2"] = 34,
    ["MULTIACTIONBAR2BUTTON3"] = 35,
    ["MULTIACTIONBAR2BUTTON4"] = 36,
    ["MULTIACTIONBAR2BUTTON5"] = 37,
    ["MULTIACTIONBAR2BUTTON6"] = 38,
    ["MULTIACTIONBAR2BUTTON7"] = 39,
    ["MULTIACTIONBAR2BUTTON8"] = 40,
    ["MULTIACTIONBAR2BUTTON9"] = 41,
    ["MULTIACTIONBAR2BUTTON10"] = 42,
    ["MULTIACTIONBAR2BUTTON11"] = 43,
    ["MULTIACTIONBAR2BUTTON12"] = 44,

    -- Bottom Left Action Bar slots 61-72 (45-56)
    ["MULTIACTIONBAR1BUTTON1"] = 45,
    ["MULTIACTIONBAR1BUTTON2"] = 46,
    ["MULTIACTIONBAR1BUTTON3"] = 47,
    ["MULTIACTIONBAR1BUTTON4"] = 48,
    ["MULTIACTIONBAR1BUTTON5"] = 49,
    ["MULTIACTIONBAR1BUTTON6"] = 50,
    ["MULTIACTIONBAR1BUTTON7"] = 51,
    ["MULTIACTIONBAR1BUTTON8"] = 52,
    ["MULTIACTIONBAR1BUTTON9"] = 53,
    ["MULTIACTIONBAR1BUTTON10"] = 54,
    ["MULTIACTIONBAR1BUTTON11"] = 55,
    ["MULTIACTIONBAR1BUTTON12"] = 56,

    -- Custom actions (secure buttons) (57-61)
    ["CUSTOM_STOPATTACK"] = 57,
    ["CUSTOM_CLEARTARGET"] = 58,
    ["CUSTOM_CONFIG"] = 59,
    ["CUSTOM_FLUSH"] = 61,

    -- Vanilla-specific (60)
    ["TARGETPARTYMEMBER1"] = 60,   -- Vanilla: no focus system, use this instead
}

-- Queue for sending binding data via pixels
DataToColor.bindingQueue = DataToColor.TimedQueue:new(5, nil)

-- Cache of last known bindings (bindingId -> encoded value)
local bindingCache = {}

-- Custom secure button definitions (bindingId -> click command)
-- Uses BindPad addon's secure button with wildcard attributes
-- Note: BindPad addon must be installed for custom actions to work
-- Format: "CLICK BindPadMacro:actionName" triggers *macrotext-actionName
local CustomBindings = {
    ["CUSTOM_STOPATTACK"] = "CLICK BindPadMacro:stopattack",
    ["CUSTOM_CLEARTARGET"] = "CLICK BindPadMacro:cleartarget",
    ["CUSTOM_CONFIG"] = "CLICK BindPadMacro:config",
    ["CUSTOM_FLUSH"] = "CLICK BindPadMacro:flush",
}

-- Encoding format (24 bits) with modifier support:
-- Bits 22-23: key1 modifier (2 bits: 0=none, 1=Shift, 2=Ctrl, 3=Alt)
-- Bits 20-21: key2 modifier (2 bits: 0=none, 1=Shift, 2=Ctrl, 3=Alt)
-- Bits 14-19: index (6 bits, max 63)
-- Bits 7-13:  key1Id (7 bits, max 127)
-- Bits 0-6:   key2Id (7 bits, max 127)
-- Formula: (mod1 << 22) | (mod2 << 20) | (index << 14) | (key1Id << 7) | key2Id
-- Max value: 16,777,215 (exactly 24 bits)

-- Bit shift helpers using multiplication (Lua 5.1 compatible)
local function lshift(value, bits)
    return value * (2 ^ bits)
end

-- Encodes a single binding and returns the encoded value (or 0 if unbound)
local function EncodeBinding(bindingId)
    local index = BindingIndex[bindingId]
    if not index then return 0 end

    local key1Raw, key2Raw

    -- Check if this is a custom binding (secure button)
    local clickCommand = CustomBindings[bindingId]
    if clickCommand then
        -- For custom bindings, query by the click command
        key1Raw, key2Raw = GetBindingKey(clickCommand)
    else
        -- Standard WoW binding
        key1Raw, key2Raw = GetBindingKey(bindingId)
    end

    -- Parse modifiers from key strings (e.g., "SHIFT-F" -> "F", 1)
    local key1Base, mod1 = ParseKeyString(key1Raw)
    local key2Base, mod2 = ParseKeyString(key2Raw)

    local key1Id = key1Base and WoWKeyToId[key1Base] or 0
    local key2Id = key2Base and WoWKeyToId[key2Base] or 0

    if key1Id == 0 and key2Id == 0 then
        return 0 -- No keys bound
    end

    -- New encoding: (mod1 << 22) | (mod2 << 20) | (index << 14) | (key1Id << 7) | key2Id
    return lshift(mod1, 22) + lshift(mod2, 20) + lshift(index, 14) + lshift(key1Id, 7) + key2Id
end

-- Populates the binding queue with current in-game bindings (initial load)
function DataToColor:InitBindingQueue()
    for bindingId, index in pairs(BindingIndex) do
        local encoded = EncodeBinding(bindingId)
        bindingCache[bindingId] = encoded

        if encoded > 0 then
            DataToColor.bindingQueue:push(encoded)
        end
    end
end

-- Checks for binding changes and pushes only changed bindings to the queue
function DataToColor:CheckBindingChanges()
    for bindingId, index in pairs(BindingIndex) do
        local encoded = EncodeBinding(bindingId)
        local cached = bindingCache[bindingId] or 0

        if encoded ~= cached then
            bindingCache[bindingId] = encoded
            -- Push the new value (even if 0, to indicate unbind)
            -- For unbind, we encode with keys=0 so C# knows it changed
            if encoded > 0 then
                DataToColor.bindingQueue:push(encoded)
            else
                -- Push index with 0 keys to indicate unbind
                DataToColor.bindingQueue:push(index * 65536)
            end
            --DataToColor:Print(string.format("Binding changed: %s", bindingId))
        end
    end
end

-- Returns encoded binding value for a single bindingId (for immediate query)
function DataToColor:GetBindingEncoded(bindingId)
    return EncodeBinding(bindingId)
end

-- Checks if a key is already bound to a command
local function IsAlreadyBound(key, command)
  -- Check if this command is bound to this key
  local key1, key2 = GetBindingKey(command)
  if key1 == key or key2 == key then
    return true
  end
  return false
end

-- Binds a key to a command, returns true if binding was changed
local function Bind(key, command)
  -- Skip if already bound correctly
  if IsAlreadyBound(key, command) then
    return false
  end

  -- Unbind whatever was on this key first
  SetBinding(key)  -- unbind (nil command)
  local ok = SetBinding(key, command)
  if not ok then
    DataToColor:Print(string.format("failed: %s -> %s", key, tostring(command)))
    return false
  end
  return true
end

-- Tries to bind a key (for bindings that may not exist in all clients)
-- Returns true if binding was changed
local function TryBind(key, command)
  -- Skip if already bound correctly
  if IsAlreadyBound(key, command) then
    return false
  end

  SetBinding(key)
  local ok = SetBinding(key, command)
  if not ok then
    -- Not an error - just not available in this client
    return false
  end
  return true
end

function DataToColor:SetDefaultBindings()
  if InCombatLockdown and InCombatLockdown() then
    DataToColor:Print("Can't apply bindings in combat.")
    return
  end

  DataToColor:Print("Applying default action bar bindings...")

  local wasChanged = false

  -- Bottom Right bar (slots 49-60) => NUMPAD1..NUMPAD9,NUMPAD0
  -- Numpad key names are NUMPAD0-9 + NUMPADMULTIPLY/SUBTRACT/etc.
  local numKeys = {"NUMPAD1","NUMPAD2","NUMPAD3","NUMPAD4","NUMPAD5","NUMPAD6","NUMPAD7","NUMPAD8","NUMPAD9","NUMPAD0"}
  for i = 1, 10 do
    wasChanged = Bind(numKeys[i], "MULTIACTIONBAR2BUTTON"..i) or wasChanged
  end

  -- Bottom Left bar (your slots 61-72) => F1..F12
  for i = 1, 12 do
    wasChanged = Bind("F"..i, "MULTIACTIONBAR1BUTTON"..i) or wasChanged
  end

  -- Only save if something actually changed
  if wasChanged then
    -- Save to whichever binding set the user currently has selected (account=1, character=2)
    local bindingSet = GetCurrentBindingSet()
    SaveBindings(bindingSet)
    local bindingType = bindingSet == 1 and "account-wide" or "character-specific"
    DataToColor:Print("Key bindings changed and saved to " .. bindingType .. ".")
  end

  -- Refresh binding cache so KeyBindingsReader picks up any changes
  DataToColor:CheckBindingChanges()
end

-- Sets only essential bindings (targeting, interaction, pet) without touching action bars
-- Used by auto-setup to avoid overwriting player's action bar keybinds
function DataToColor:SetEssentialBindings()
  if InCombatLockdown and InCombatLockdown() then
    DataToColor:Print("Can't apply bindings in combat.")
    return
  end

  DataToColor:Print("Applying essential key bindings...")

  local wasChanged = false

  -- ===== Targeting / interaction =====
  wasChanged = TryBind("TAB", "TARGETNEARESTENEMY") or wasChanged
  wasChanged = TryBind("G",   "TARGETLASTTARGET") or wasChanged
  wasChanged = TryBind("F",   "ASSISTTARGET") or wasChanged

  -- Interact keys (not present in every era / rules differ by client)
  wasChanged = TryBind("ALT-HOME", "INTERACTTARGET") or wasChanged
  wasChanged = TryBind("ALT-END", "INTERACTMOUSEOVER") or wasChanged

  -- Combat: Start attack (bypasses soft target interaction)
  wasChanged = TryBind("ALT-NUMPADPLUS", "STARTATTACK") or wasChanged

  -- Pet keys (only meaningful for pet classes)
  wasChanged = TryBind("NUMPADMULTIPLY",  "TARGETPET") or wasChanged
  wasChanged = TryBind("NUMPADMINUS",  "PETATTACK") or wasChanged

  -- Focus / party targeting (client-dependent)
  if DataToColor.IsVanilla() then
    wasChanged = TryBind("ALT-PAGEUP", "TARGETPARTYMEMBER1") or wasChanged
  else
    wasChanged = TryBind("ALT-PAGEUP", "TARGETFOCUS") or wasChanged
  end

  -- Follow target
  wasChanged = TryBind("ALT-PAGEDOWN", "FOLLOWTARGET") or wasChanged

  -- Only save if something actually changed
  if wasChanged then
    local bindingSet = GetCurrentBindingSet()
    SaveBindings(bindingSet)
    local bindingType = bindingSet == 1 and "account-wide" or "character-specific"
    DataToColor:Print("Essential bindings changed and saved to " .. bindingType .. ".")
  end

  -- Refresh binding cache so KeyBindingsReader picks up any changes
  DataToColor:CheckBindingChanges()
end

-- ========================
-- Utility actions using BindPad-style approach:
-- - Single button defined in XML (BindPadMacro)
-- - Wildcard attributes: *type* and *macrotext-actionName
-- - Bindings use format: CLICK BindPadMacro:actionName
local UtilityActions = {
  {
    actionName = "stopattack",
    key = "ALT-DELETE",
    macrotext = "/stopattack\n/stopcasting",
  },
  {
    actionName = "cleartarget",
    key = "ALT-INSERT",
    macrotext = "/cleartarget",
  },
  {
    actionName = "config",
    key = "SHIFT-PAGEUP",
    macrotext = "/dc",
  },
  {
    actionName = "flush",
    key = "SHIFT-PAGEDOWN",
    macrotext = "/dcflush",
  },
}

-- Sets up the BindPadMacro button with wildcard attributes
-- Requires BindPad addon to be installed (provides the working secure button)
local function SetupMacroButton()
  local btn = BindPadMacro
  if not btn then
    DataToColor:Print("ERROR: BindPadMacro not found! Install BindPad addon.")
    return false
  end

  -- Set wildcard type for all button clicks
  btn:SetAttribute("*type*", "macro")

  -- Set wildcard macrotext for each action
  for _, action in ipairs(UtilityActions) do
    btn:SetAttribute("*macrotext-" .. action.actionName, action.macrotext)
  end

  return true
end

function DataToColor:CreateSecureButtons()
  if InCombatLockdown and InCombatLockdown() then
    DataToColor:Print("Can't create/bind actions in combat.")
    return
  end

  -- Setup the macro button with wildcard attributes
  if not SetupMacroButton() then
    return
  end

  local wasChanged = false

  for _, action in ipairs(UtilityActions) do
    -- Binding format: CLICK BindPadMacro:actionName
    local clickCommand = "CLICK BindPadMacro:" .. action.actionName
    if not IsAlreadyBound(action.key, clickCommand) then
      -- Unbind the key first, then bind to our button with action suffix
      SetBinding(action.key)
      local ok = SetBinding(action.key, clickCommand)
      if ok then
        wasChanged = true
        DataToColor:Print(string.format("  Bound: %s -> %s", action.key, action.actionName))
      end
    end
  end

  if wasChanged then
    -- Save to whichever binding set the user currently has selected (account=1, character=2)
    local bindingSet = GetCurrentBindingSet()
    SaveBindings(bindingSet)
    local bindingType = bindingSet == 1 and "account-wide" or "character-specific"
    DataToColor:Print("Custom actions changed and saved to " .. bindingType .. ".")
  end

  -- Refresh binding cache so KeyBindingsReader picks up any changes
  DataToColor:CheckBindingChanges()
end

-- ========================
-- Checks if essential bindings are missing
-- Returns true if any critical bindings need to be set up
local function NeedsBindingSetup()
  -- Check if custom actions are bound (using new format)
  for _, action in ipairs(UtilityActions) do
    local clickCommand = "CLICK BindPadMacro:" .. action.actionName
    local key1, key2 = GetBindingKey(clickCommand)
    if not key1 and not key2 then
      return true -- At least one custom action is not bound
    end
  end

  -- Check essential targeting/interaction bindings
  local essentialBindings = {
    "TARGETNEARESTENEMY",
    "TARGETLASTTARGET",
    "ASSISTTARGET",
    "TARGETPET",
    "PETATTACK",
    "INTERACTTARGET",
    "INTERACTMOUSEOVER",
    "FOLLOWTARGET",
  }

  -- Add focus/party binding based on client version
  if DataToColor.IsVanilla() then
    table.insert(essentialBindings, "TARGETPARTYMEMBER1")
  else
    table.insert(essentialBindings, "TARGETFOCUS")
  end

  for _, bindingId in ipairs(essentialBindings) do
    local key1, key2 = GetBindingKey(bindingId)
    if not key1 and not key2 then
      return true -- Essential binding is missing
    end
  end

  return false
end

-- Auto-setup bindings if needed (called deferred after login)
function DataToColor:AutoSetupBindingsIfNeeded()
  -- Skip if in combat - user can run /dcactions manually later
  if InCombatLockdown and InCombatLockdown() then
    return
  end

  if NeedsBindingSetup() then
    DataToColor:Print("Essential bindings missing, setting up defaults...")
    DataToColor:SetEssentialBindings()
    DataToColor:CreateSecureButtons()
  else
    -- Just ensure the macro button is set up (without re-binding)
    SetupMacroButton()
    -- Initialize binding queue
    DataToColor:InitBindingQueue()
  end
end

-- Schedule auto-setup after a short delay (called from PLAYER_ENTERING_WORLD)
function DataToColor:ScheduleAutoSetup()
  C_Timer.After(1, function()
    DataToColor:AutoSetupBindingsIfNeeded()
  end)
end