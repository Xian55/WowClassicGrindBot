--[[

BindPad Addon for World of Warcraft

Author: Tageshi

--]]
-- luacheck: globals BindPadFrame BindPadFrame_Toggle BindPad_SlashCmd BindPadFrame_OutputText BINDPAD_TEXT_USAGE BindPadSlot_OnReceiveDrag BindPadSlot_UpdateState
-- luacheck: globals BindPadKey BindPadMacro BindPadDialogFrame BindPadMacroFrameText BindPadBindFrameAction BindPadBindFrameKey BindPadMacroPopupFrame

local _, addon = ...

local GetContainerItemID = GetContainerItemID or C_Container.GetContainerItemID

local function concat(arg1, arg2)
    if arg1 and arg2 then
        return arg1..arg2;
    end
end

local SaveBindings = SaveBindings or AttemptToSaveBindings
local NUM_MACRO_ICONS_SHOWN = 20;
local NUM_ICONS_PER_ROW = 5;
local NUM_ICON_ROWS = 4;
local MACRO_ICON_ROW_HEIGHT = 36;
local MACRO_ICON_FILENAMES = {};

-- Register BindPad frame to be controlled together with
-- other panels in standard UI.
UIPanelWindows["BindPadFrame"] = { area = "left", pushable = 8, whileDead = 1 };
UIPanelWindows["BindPadMacroFrame"] = { area = "left", pushable = 9, whileDead = 1 };

local BINDPAD_MAXSLOTS_DEFAULT = 42;
local BINDPAD_MAXPROFILETAB = 1;
local BINDPAD_GENERAL_TAB = 1;
local BINDPAD_SPECIFIC_1ST_TAB = 2;
local BINDPAD_SAVEFILE_VERSION = 1.3;
local BINDPAD_PROFILE_VERSION252 = 252;

local TYPE_ITEM = "ITEM";
local TYPE_SPELL = "SPELL";
local TYPE_MACRO = "MACRO";
local TYPE_BPMACRO = "CLICK";

local BindPadPetAction = {
    [PET_ACTION_MOVE_TO] = SLASH_PET_MOVE_TO1,
    [PET_ACTION_ATTACK] = SLASH_PET_ATTACK1,
    [PET_ACTION_FOLLOW] = SLASH_PET_FOLLOW1,
    [PET_ACTION_WAIT] = SLASH_PET_STAY1,
    [PET_MODE_AGGRESSIVE] = SLASH_PET_AGGRESSIVE1,
    [PET_MODE_DEFENSIVE] = SLASH_PET_DEFENSIVE1,
    [PET_MODE_PASSIVE] = SLASH_PET_PASSIVE1,
    [PET_MODE_ASSIST] = SLASH_PET_ASSIST1,
};

-- Initialize the saved variable for BindPad.
BindPadVars = {
    tab = BINDPAD_GENERAL_TAB,
    version = BINDPAD_SAVEFILE_VERSION,
    GeneralKeyBindings = {};
};

-- Initialize BindPad core object.
BindPadCore = {
    drag = {};
    dragswap = {};
    specInfoCache = {};
    currentkeybindings = {};
    eventProc = {};
};

local BindPadCore = BindPadCore;

function BindPadFrame_Toggle()
    if BindPadFrame:IsVisible() then
        HideUIPanel(BindPadFrame);
    else
        ShowUIPanel(BindPadFrame);
    end
end

function BindPad_SlashCmd(msg)
    local cmd, arg = msg:match("^(%S*)%s*(.-)$")

    if cmd == nil or cmd == "" then
        BindPadFrame_Toggle();
    elseif cmd == "list" then
        BindPadCore.DoList(arg);
    elseif cmd == "delete" then
        BindPadCore.DoDelete(arg);
    elseif cmd == "copyfrom" then
        BindPadCore.DoCopyFrom(arg);
    else
        BindPadFrame_OutputText(BINDPAD_TEXT_USAGE);
    end
end

function BindPadFrame_OnLoad(self)
    PanelTemplates_SetNumTabs(BindPadFrame, 4);

    SlashCmdList["BINDPAD"] = BindPad_SlashCmd;
    SLASH_BINDPAD1 = "/bindpad";
    SLASH_BINDPAD2 = "/bp";

    self:RegisterEvent("UPDATE_BINDINGS");
    self:RegisterEvent("ACTIONBAR_SLOT_CHANGED");
    self:RegisterEvent("UPDATE_BONUS_ACTIONBAR");
    self:RegisterEvent("ACTIONBAR_PAGE_CHANGED");
    self:RegisterEvent("UPDATE_SHAPESHIFT_FORM");

    self:RegisterEvent("CVAR_UPDATE");

    self:RegisterEvent("PLAYER_ENTERING_WORLD");

    GetMacroIcons(MACRO_ICON_FILENAMES);
end

function BindPadFrame_OnMouseDown(self, button)
    if button == "RightButton" then
        BindPadCore.ClearCursor();
    end
end

function BindPadFrame_OnEnter(self)
    BindPadCore.UpdateCursor();
end

function BindPadFrame_OnEvent(self, event, ...)
    local arg1, arg2 = ...;
    if event == "UPDATE_BINDINGS" then
        -- BindPad will always save keybindings when something changed
        -- because current spec can be changed while BindPad addon is disabled.
        -- If we don't save now, we can lose the new keybind when logout/relogin as other spec.
        BindPadCore.DoSaveAllKeys(); -- correct?

        BindPadCore.UpdateAllHotkeys();

    elseif event == "ACTIONBAR_SLOT_CHANGED"
        or event == "UPDATE_BONUS_ACTIONBAR"
        or event == "UPDATE_VEHICLE_ACTIONBAR"
        or event == "UPDATE_OVERRIDE_ACTIONBAR"
        or event == "ACTIONBAR_PAGE_CHANGED"
        or event == "UPDATE_SHAPESHIFT_FORM"
        or event == "UPDATE_POSSESS_BAR" then
        BindPadCore.UpdateAllHotkeys();
    elseif event == "PLAYER_ENTERING_WORLD" then
        BindPadCore.InitBindPadOnce(event);
    elseif event == "PLAYER_TALENT_UPDATE" then
        BindPadCore.PlayerTalentUpdate();
    elseif event == "CVAR_UPDATE" then
        BindPadCore.CVAR_UPDATE(arg1, arg2);
    elseif event == "ADDON_LOADED" and arg1 == addon then
        BindPadFrame_OutputText(event..":"..arg1);
    end
end

function BindPadFrame_OutputText(text)
    ChatFrame1:AddMessage("[BindPad] "..text, 1.0, 1.0, 0.0);
end

function BindPadFrame_OnShow()
    if not BindPadVars.tab then
        BindPadVars.tab = 1;
    end

    if GetCurrentBindingSet() == 1 then
        -- Don't show Character Specific Slots tab at first.
        BindPadVars.tab = 1;
    end

    BindPadFrameTitleText:SetText(BINDPAD_TITLE);
    PanelTemplates_SetTab(BindPadFrame, BindPadVars.tab);

    -- Update character button
    BindPadFrameCharacterButton:SetChecked(GetCurrentBindingSet() == 2);

    -- Update Option buttons
    BindPadFrameSaveAllKeysButton:SetChecked(BindPadVars.saveAllKeysFlag);

    BindPadVars.showHotkey = (BindPadVars.showHotkey or BindPadVars.showKeyInTooltipFlag);
    BindPadVars.showKeyInTooltipFlag = nil;
    BindPadFrameShowHotkeyButton:SetChecked(BindPadVars.showHotkey);

    local tabInfo = BindPadCore.GetTabInfo(BindPadVars.tab);
    BindPadCore.CreateBindPadSlot(tabInfo.numSlot);
    for i = 1, tabInfo.numSlot do
        local button = _G["BindPadSlot"..i];
        BindPadSlot_UpdateState(button);
    end
end

function BindPadFrame_OnHide(self)
    BindPadCore.HideSubFrames();
end

function BindPadFrameTab_OnClick(self)
    local id = self:GetID();
    local function f()
        if GetCurrentBindingSet() == 1 then
            local answer = BindPadCore.ShowDialog(BINDPAD_TEXT_CONFIRM_CHANGE_BINDING_PROFILE);
            if answer then
                LoadBindings(2);
                BindPadCore.SaveBindings(2);
            else
                BindPadVars.tab = 1;
                return;
            end
        end
        BindPadVars.tab = id;
        BindPadFrame_OnShow();
    end

    -- Handles callback with coroutine.
    return coroutine.wrap(f)();
end

function BindPadFrameTab_OnEnter(self)
    local id = self:GetID();
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT");
    if id == 1 then
        GameTooltip:SetText(BINDPAD_TOOLTIP_TAB1, nil, nil, nil, nil, 1);
        GameTooltip:AddLine(BINDPAD_TOOLTIP_GENERAL_TAB_EXPLAIN, 1.0, 0.8, 0.8);
    else
        GameTooltip:SetText(format(_G["BINDPAD_TOOLTIP_TAB"..id], UnitName("player")), nil, nil, nil, nil, 1);
        GameTooltip:AddLine(BINDPAD_TOOLTIP_SPECIFIC_TAB_EXPLAIN, 0.8, 1.0, 0.8);
    end
    GameTooltip:Show();
end

function BindPadBindFrame_Update()
    BindPadCore.CancelDialogs();

    BindPadBindFrameAction:SetText(BindPadCore.selectedSlot.action);

    local key = GetBindingKey(BindPadCore.selectedSlot.action);
    if key then
        BindPadBindFrameKey:SetText(BINDPAD_TEXT_KEY..BindPadCore.GetBindingText(key, "KEY_"));
    else
        BindPadBindFrameKey:SetText(BINDPAD_TEXT_KEY..BINDPAD_TEXT_NOTBOUND);
    end

    if (BindPadVars.tab or 1) == 1 then
        BindPadBindFrameForAllCharacterButton:SetChecked(BindPadCore.selectedSlot.isForAllCharacters);
        BindPadBindFrameForAllCharacterButton:Show();
    else
        BindPadBindFrameForAllCharacterButton:Hide();
    end
end

function BindPadBindFrame_OnKeyDown(self, keyOrButton)
    if keyOrButton == "ESCAPE" then
        BindPadBindFrame:Hide()
        return
    end

    if GetBindingFromClick(keyOrButton) == "SCREENSHOT" then
        RunBinding("SCREENSHOT");
        return;
    end

    local keyPressed = keyOrButton;

    if keyPressed == "UNKNOWN" then
        return;
    end

    -- Convert the mouse button names
    if keyPressed == "LeftButton" then
        keyPressed = "BUTTON1";
    elseif keyPressed == "RightButton" then
        keyPressed = "BUTTON2";
    elseif keyPressed == "MiddleButton" then
        keyPressed = "BUTTON3";
    elseif keyPressed == "Button4" then
        keyPressed = "BUTTON4"
    elseif keyOrButton == "Button5" then
        keyPressed = "BUTTON5"
    elseif keyPressed == "Button6" then
        keyPressed = "BUTTON6"
    elseif keyOrButton == "Button7" then
        keyPressed = "BUTTON7"
    elseif keyPressed == "Button8" then
        keyPressed = "BUTTON8"
    elseif keyOrButton == "Button9" then
        keyPressed = "BUTTON9"
    elseif keyPressed == "Button10" then
        keyPressed = "BUTTON10"
    elseif keyOrButton == "Button11" then
        keyPressed = "BUTTON11"
    elseif keyPressed == "Button12" then
        keyPressed = "BUTTON12"
    elseif keyOrButton == "Button13" then
        keyPressed = "BUTTON13"
    elseif keyPressed == "Button14" then
        keyPressed = "BUTTON14"
    elseif keyOrButton == "Button15" then
        keyPressed = "BUTTON15"
    elseif keyPressed == "Button16" then
        keyPressed = "BUTTON16"
    elseif keyOrButton == "Button17" then
        keyPressed = "BUTTON17"
    elseif keyPressed == "Button18" then
        keyPressed = "BUTTON18"
    elseif keyOrButton == "Button19" then
        keyPressed = "BUTTON19"
    elseif keyPressed == "Button20" then
        keyPressed = "BUTTON20"
    elseif keyOrButton == "Button21" then
        keyPressed = "BUTTON21"
    elseif keyPressed == "Button22" then
        keyPressed = "BUTTON22"
    elseif keyOrButton == "Button23" then
        keyPressed = "BUTTON23"
    elseif keyPressed == "Button24" then
        keyPressed = "BUTTON24"
    elseif keyOrButton == "Button25" then
        keyPressed = "BUTTON25"
    elseif keyPressed == "Button26" then
        keyPressed = "BUTTON26"
    elseif keyOrButton == "Button27" then
        keyPressed = "BUTTON27"
    elseif keyPressed == "Button28" then
        keyPressed = "BUTTON28"
    elseif keyOrButton == "Button29" then
        keyPressed = "BUTTON29"
    elseif keyPressed == "Button30" then
        keyPressed = "BUTTON30"
    elseif keyOrButton == "Button31" then
        keyPressed = "BUTTON31"
    end

    if keyPressed == "LSHIFT" or
        keyPressed == "RSHIFT" or
        keyPressed == "LCTRL" or
        keyPressed == "RCTRL" or
        keyPressed == "LALT" or
        keyPressed == "RALT" then
        return;
    end

    if IsShiftKeyDown() then
        keyPressed = "SHIFT-"..keyPressed
    end

    if IsControlKeyDown() then
        keyPressed = "CTRL-"..keyPressed
    end

    if IsAltKeyDown() then
        keyPressed = "ALT-"..keyPressed
    end

    if keyPressed == "BUTTON1" or keyPressed == "BUTTON2" then
        return;
    end

    if not keyPressed then
        return;
    end

    local function f()
        local answer;
        local padSlot = BindPadCore.selectedSlot;
        local oldAction = GetBindingAction(keyPressed)

        if oldAction ~= "" and oldAction ~= padSlot.action then
            local keyText = BindPadCore.GetBindingText(keyPressed, "KEY_");
            local text = format(BINDPAD_TEXT_CONFIRM_BINDING, keyText, oldAction, keyText, padSlot.action);
            answer = BindPadCore.ShowDialog(text);
        else
            answer = true;
        end

        if answer then
            BindPadCore.BindKey(padSlot, keyPressed);
        end
        BindPadBindFrame_Update();
    end
    -- Handles callback with coroutine.
    return coroutine.wrap(f)();
end

function BindPadBindFrame_Unbind()
    BindPadCore.UnbindSlot(BindPadCore.selectedSlot);
    BindPadBindFrame_Update();
end

function BindPadBindFrame_OnHide(self)
    -- Close the confirmation dialog frame if it is still open.
    BindPadCore.CancelDialogs();
end

function BindPadSlot_OnUpdateBindings(self)
    if BindPadCore.character then
        BindPadSlot_UpdateState(self);
    end
end

function BindPadSlot_OnClick(self, button, down)
    if button == "RightButton" then
        if BindPadCore.CursorHasIcon() then
            BindPadCore.ClearCursor();
        else
            BindPadMacroFrame_Open(self);
        end

        return;
    end

    if BindPadCore.CursorHasIcon() then
        -- If cursor has icon to drop, drop it.
        BindPadSlot_OnReceiveDrag(self);
    elseif IsShiftKeyDown() then
        -- Shift+click to start drag.
        BindPadSlot_OnDragStart(self);
    else
        -- Otherwise open dialog window to set keybinding.
        if BindPadCore.GetSlotInfo(self:GetID()) then
            BindPadCore.HideSubFrames();
            BindPadCore.selectedSlot = BindPadCore.GetSlotInfo(self:GetID());
            BindPadCore.selectedSlotButton = self;
            BindPadBindFrame_Update();
            BindPadBindFrame:Show();
        end
    end
end

function BindPadSlot_OnDragStart(self)
    if not BindPadCore.CanPickupSlot(self) then
        return;
    end

    BindPadCore.PickupSlot(self, self:GetID(), true);
    BindPadSlot_UpdateState(self);
end

function BindPadSlot_OnReceiveDrag(self)
    if self == BindPadCore.selectedSlotButton then
        BindPadCore.HideSubFrames();
    end

    if not BindPadCore.CanPickupSlot(self) then
        return;
    end

    local type, detail, subdetail, spellid = GetCursorInfo();
    if type then
        if type == "petaction" then
            detail = BindPadCore.PickupSpellBookItem_slot;
            subdetail = BindPadCore.PickupSpellBookItem_bookType;
        end
        ClearCursor();
        ResetCursor();
        BindPadCore.PickupSlot(self, self:GetID());
        BindPadCore.PlaceIntoSlot(self:GetID(), type, detail, subdetail, spellid);

        BindPadSlot_UpdateState(self);
        BindPadSlot_OnEnter(self);
    elseif TYPE_BPMACRO == BindPadCore.drag.type then
        local drag = BindPadCore.drag;
        ClearCursor();
        ResetCursor();
        BindPadCore.PickupSlot(self, self:GetID());
        BindPadCore.PlaceVirtualIconIntoSlot(self:GetID(), drag);

        BindPadSlot_UpdateState(self);
        BindPadSlot_OnEnter(self);
    end
end

function BindPadSlot_OnEnter(self)
    BindPadCore.UpdateCursor();

    local padSlot = BindPadCore.GetSlotInfo(self:GetID());

    if not padSlot then
        return;
    end

    if BindPadCore.CheckCorruptedSlot(padSlot) then
        return;
    end

    GameTooltip:SetOwner(self, "ANCHOR_LEFT");

    if TYPE_ITEM == padSlot.type then
        GameTooltip:SetHyperlink(padSlot.linktext);
    elseif TYPE_SPELL == padSlot.type then
        if padSlot.spellid then
            GameTooltip:SetSpellByID(padSlot.spellid);
        else
            local spellBookId = BindPadCore.FindSpellBookIdByName(padSlot.name, padSlot.rank, padSlot.bookType);
            if spellBookId then
                GameTooltip:SetSpellBookItem(spellBookId, padSlot.bookType)
            else
                GameTooltip:SetText(BINDPAD_TOOLTIP_UNKNOWN_SPELL..padSlot.name, 1.0, 1.0, 1.0);
            end
            if padSlot.rank then
                GameTooltip:AddLine(padSlot.rank, 1.0, 0.7, 0.7);
            end
        end
    elseif TYPE_MACRO == padSlot.type then
        GameTooltip:SetText(BINDPAD_TOOLTIP_MACRO..padSlot.name, 1.0, 1.0, 1.0);
    elseif TYPE_BPMACRO == padSlot.type then
        GameTooltip:SetText(format(BINDPAD_TOOLTIP_BINDPADMACRO, padSlot.name), 1.0, 1.0, 1.0);
    end

    -- Spell keybind is already shown if "Show Keys in Tooltip" option is ON.
    if not (BindPadVars.showHotkey and TYPE_SPELL == padSlot.type) then
        local key = GetBindingKey(padSlot.action);
        if key then
            GameTooltip:AddLine(BINDPAD_TOOLTIP_KEYBINDING..BindPadCore.GetBindingText(key, "KEY_"), 0.8, 0.8, 1.0);
        end
    end

    if not BindPadCore.CursorHasIcon() then
        if TYPE_BPMACRO == padSlot.type then
            GameTooltip:AddLine(BINDPAD_TOOLTIP_CLICK_USAGE1, 0.8, 1.0, 0.8);
        else
            GameTooltip:AddLine(BINDPAD_TOOLTIP_CLICK_USAGE2, 0.8, 1.0, 0.8);
        end
    end

    GameTooltip:Show();
end

function BindPadSlot_UpdateState(self)
    local padSlot = BindPadCore.GetSlotInfo(self:GetID());

    if padSlot and padSlot.type and padSlot.action then
        self.icon:SetTexture(padSlot.texture);
        self.icon:Show();
        self.addbutton:Hide();

        self.name:SetText(padSlot.name or "");

        local key = GetBindingKey(padSlot.action);
        if key then
            self.hotkey:SetText(BindPadCore.GetBindingText(key, "KEY_", 1));
        else
            self.hotkey:SetText("");
        end

        if TYPE_BPMACRO == padSlot.type then
            self.border:SetVertexColor(0, 1.0, 0, 0.35);
            self.border:Show();
        else
            self.border:Hide();
        end
    else
        self.icon:Hide();
        self.addbutton:Show();
        self.name:SetText("");
        self.hotkey:SetText("");
        self.border:Hide();
    end
end

local BindPadMacroPopup_oldPadSlot = {};
function BindPadMacroPopupFrame_Open(self)
    if InCombatLockdown() then
        BindPadFrame_OutputText(BINDPAD_TEXT_ERR_BINDPADMACRO_INCOMBAT);
        return;
    end
    local padSlot = BindPadCore.GetSlotInfo(self:GetID(), true);
    local newFlag = false;
    BindPadCore.CheckCorruptedSlot(padSlot);

    BindPadMacroPopup_oldPadSlot.action = padSlot.action;
    BindPadMacroPopup_oldPadSlot.id = padSlot.id;
    BindPadMacroPopup_oldPadSlot.macrotext = padSlot.macrotext;
    BindPadMacroPopup_oldPadSlot.name = padSlot.name;
    BindPadMacroPopup_oldPadSlot.texture = padSlot.texture;
    BindPadMacroPopup_oldPadSlot.type = padSlot.type;

    if not padSlot.type then
        newFlag = true;

        padSlot.type = TYPE_BPMACRO;
        padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, "1");
        padSlot.texture = BindPadCore.GetMacroIconInfo(1);
        padSlot.macrotext = "";
        padSlot.action = BindPadCore.CreateBindPadMacroAction(padSlot);
        BindPadCore.UpdateMacroText(padSlot); -- Fix
        BindPadSlot_UpdateState(self)
    end

    if TYPE_BPMACRO == padSlot.type then
        BindPadCore.selectedSlot = padSlot;
        BindPadCore.selectedSlotButton = self;
        BindPadMacroPopupEditBox:SetText(padSlot.name);
        BindPadMacroPopupFrame.selectedIconTexture = padSlot.texture;
        BindPadMacroPopupFrame.selectedIcon = nil;
        BindPadCore.HideSubFrames();
        BindPadMacroPopupFrame:Show();
        if newFlag then
            BindPadMacroPopupEditBox:HighlightText();
        end
    end
end

function BindPadMacroAddButton_OnClick(self)
    if BindPadCore.CursorHasIcon() then
        BindPadSlot_OnReceiveDrag(self);
    else
        BindPadCore.HideSubFrames();
        PlaySound(SOUNDKIT.GS_TITLE_OPTION_OK)
        BindPadMacroPopupFrame_Open(self);
    end
end

function BindPadMacroPopupFrame_OnShow(self)
    BindPadMacroPopupEditBox:SetFocus();
    BindPadMacroPopupFrame_Update(self);
    BindPadMacroPopupOkayButton_Update(self);
end

function BindPadMacroPopupFrame_OnHide(self)
    if not BindPadFrame:IsVisible() then
        ShowUIPanel(BindPadFrame);
    end
end

function BindPadMacroPopupFrame_Update(self)
    local numMacroIcons = #MACRO_ICON_FILENAMES;
    local macroPopupIcon, macroPopupButton;
    local macroPopupOffset = FauxScrollFrame_GetOffset(BindPadMacroPopupScrollFrame) or 0;
    local index;

    -- Icon list
    local texture;
    for i=1, NUM_MACRO_ICONS_SHOWN do
        macroPopupIcon = _G["BindPadMacroPopupButton"..i.."Icon"];
        macroPopupButton = _G["BindPadMacroPopupButton"..i];
        index = (macroPopupOffset * NUM_ICONS_PER_ROW) + i;
        texture = BindPadCore.GetMacroIconInfo(index);
        if index <= numMacroIcons and texture then
            macroPopupIcon:SetTexture(texture);
            macroPopupButton:Show();
        else
            macroPopupIcon:SetTexture("");
            macroPopupButton:Hide();
        end
        if BindPadMacroPopupFrame.selectedIcon and index == BindPadMacroPopupFrame.selectedIcon then
            macroPopupButton:SetChecked(1);
        elseif BindPadMacroPopupFrame.selectedIconTexture ==  texture then
            macroPopupButton:SetChecked(1);
        else
            macroPopupButton:SetChecked(nil);
        end
    end

    -- Scrollbar stuff
    FauxScrollFrame_Update(BindPadMacroPopupScrollFrame, ceil(numMacroIcons / NUM_ICONS_PER_ROW) , NUM_ICON_ROWS, MACRO_ICON_ROW_HEIGHT );
end

function BindPadMacroPopupFrame_OnScroll(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, MACRO_ICON_ROW_HEIGHT, BindPadMacroPopupFrame_Update);
end

function BindPadMacroPopupEditBox_OnTextChanged(self)
    if InCombatLockdown() then
        BindPadFrame_OutputText(BINDPAD_TEXT_ERR_BINDPADMACRO_INCOMBAT);
        BindPadCore.HidePopup();
        return;
    end

    local padSlot = BindPadCore.selectedSlot;
    BindPadCore.DeleteBindPadMacroID(padSlot);
    padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, self:GetText());
    if self:GetText() ~= padSlot.name then
        BindPadFrame_OutputText(BINDPAD_TEXT_ERR_UNIQUENAME);
        self:SetText(padSlot.name);
    end
    BindPadCore.UpdateMacroText(padSlot);
    BindPadSlot_UpdateState(BindPadCore.selectedSlotButton)
end

function BindPadMacroPopupFrame_CancelEdit()
    local padSlot = BindPadCore.GetSlotInfo(BindPadCore.selectedSlotButton:GetID());
    if padSlot == nil then
        return;
    end
    BindPadCore.HidePopup();

    if InCombatLockdown() then
        BindPadFrame_OutputText(BINDPAD_TEXT_ERR_BINDPADMACRO_INCOMBAT);
        return;
    end

    padSlot.action = BindPadMacroPopup_oldPadSlot.action;
    padSlot.id = BindPadMacroPopup_oldPadSlot.id;
    padSlot.macrotext = BindPadMacroPopup_oldPadSlot.macrotext;

    BindPadCore.DeleteBindPadMacroID(padSlot);
    padSlot.name = BindPadMacroPopup_oldPadSlot.name;
    BindPadCore.UpdateMacroText(padSlot);

    padSlot.texture = BindPadMacroPopup_oldPadSlot.texture;
    padSlot.type = BindPadMacroPopup_oldPadSlot.type;

    BindPadMacroPopupFrame.selectedIcon = nil;
    BindPadSlot_UpdateState(BindPadCore.selectedSlotButton)
end

function BindPadMacroPopupOkayButton_Update(self)
    if (strlen(BindPadMacroPopupEditBox:GetText()) > 0) then
        BindPadMacroPopupOkayButton:Enable();
    else
        BindPadMacroPopupOkayButton:Disable();
    end
end

function BindPadMacroPopupButton_OnClick(self)
    BindPadMacroPopupFrame.selectedIcon = self:GetID() + (FauxScrollFrame_GetOffset(BindPadMacroPopupScrollFrame) * NUM_ICONS_PER_ROW);
    -- Clear out selected texture
    BindPadMacroPopupFrame.selectedIconTexture = nil;

    BindPadCore.selectedSlot.texture = BindPadCore.GetMacroIconInfo(BindPadMacroPopupFrame.selectedIcon);
    BindPadSlot_UpdateState(BindPadCore.selectedSlotButton);

    BindPadMacroPopupOkayButton_Update(self);
    BindPadMacroPopupFrame_Update(self);
end

function BindPadMacroPopupOkayButton_OnClick()
    BindPadCore.HidePopup();
    BindPadSlot_UpdateState(BindPadCore.selectedSlotButton);
    BindPadMacroFrame_Open(BindPadCore.selectedSlotButton);
end

function BindPadMacroFrame_Open(self)
    BindPadCore.HideSubFrames();

    local id = self:GetID();
    local padSlot = BindPadCore.GetSlotInfo(id);
    if padSlot == nil then
        return;
    end
    BindPadCore.selectedSlot = padSlot;
    BindPadCore.selectedSlotButton = self;

    if TYPE_ITEM == padSlot.type
        or TYPE_SPELL == padSlot.type
        or TYPE_MACRO == padSlot.type then

        local function f()
            local answer = BindPadCore.ShowDialog(format(BINDPAD_TEXT_CONFIRM_CONVERT, padSlot.type, padSlot.name));
            if answer then
                BindPadCore.ConvertToBindPadMacro();
            end
        end
        -- Handles callback with coroutine.
        return coroutine.wrap(f)();
    end

    BindPadMacroFrameSlotName:SetText(padSlot.name);
    BindPadMacroFrameSlotButtonIcon:SetTexture(padSlot.texture);
    BindPadMacroFrameText:SetText(padSlot.macrotext);
    BindPadMacroFrameText:SetMaxBytes(1024);

    if not InCombatLockdown() then
        BindPadMacroFrameTestButton:SetAttribute("macrotext", padSlot.macrotext);
    end

    BindPadCore.HidePopup();
    ShowUIPanel(BindPadMacroFrame);
end

function BindPadMacroFrameEditButton_OnClick(self)
    BindPadMacroPopupFrame_Open(BindPadCore.selectedSlotButton);
end

function BindPadMacroDeleteButton_OnClick(self)
    BindPadCore.HideSubFrames();

    local padSlot = BindPadCore.GetSlotInfo(BindPadCore.selectedSlotButton:GetID());
    if padSlot == nil then
        return;
    end

    BindPadCore.DeleteBindPadMacroID(padSlot);

    table.wipe(padSlot);

    BindPadSlot_UpdateState(BindPadCore.selectedSlotButton);
end

function BindPadMacroFrame_OnShow(self)
    BindPadMacroFrameText:SetFocus();
end

function BindPadMacroFrame_OnHide(self)
    if BindPadCore.selectedSlot.macrotext ~= BindPadMacroFrameText:GetText() then
        if InCombatLockdown() then
            BindPadFrame_OutputText(BINDPAD_TEXT_ERR_BINDPADMACRO_INCOMBAT);
            BindPadMacroFrameText:SetText(BindPadCore.selectedSlot.macrotext);
        else
            BindPadCore.selectedSlot.macrotext = BindPadMacroFrameText:GetText();
            BindPadCore.UpdateMacroText(BindPadCore.selectedSlot);
        end
    end

    if not BindPadFrame:IsVisible() then
        ShowUIPanel(BindPadFrame);
    end
end

--
-- BindPadCore:  A set of core functions
--

function BindPadCore.GetEquipmentSetTexture(setName)
    -- Replacement for buggy GetEquipmentSetInfoByName().
    local name, textureName;
    for idx = 1, GetNumEquipmentSets() do
        name, textureName = GetEquipmentSetInfo(idx);
        if name == setName then
            return textureName;
        end
    end
    return nil;
end

function BindPadCore.PlaceIntoSlot(id, type, detail, subdetail, spellid)
    local padSlot = BindPadCore.GetSlotInfo(id, true);

    if type == "item" then
        padSlot.type = TYPE_ITEM;
        padSlot.linktext = subdetail;
        local name,_,_,_,_,_,_,_,_,texture = GetItemInfo(padSlot.linktext);
        padSlot.name = name;
        padSlot.texture = texture;

    elseif type == "macro" then
        padSlot.type = TYPE_MACRO;
        local name, texture = GetMacroInfo(detail);
        padSlot.name = name;
        padSlot.texture = texture;

    elseif type == "spell" then
        padSlot.type = TYPE_SPELL;
        local spellName, spellRank, texture = GetSpellInfo(spellid);
        padSlot.bookType = subdetail;
        padSlot.name = spellName;
        padSlot.rank = nil;
        padSlot.spellid = spellid;
        padSlot.texture = texture;

    elseif type == "petaction" then
        local spellName, spellRank = GetSpellBookItemName(detail, subdetail);
        local texture = GetSpellBookItemTexture(detail, subdetail);
        if BindPadPetAction[spellName] then
            padSlot.type = TYPE_BPMACRO;
            padSlot.bookType = nil;
            padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, BindPadPetAction[spellName]);
            padSlot.rank = nil;
            padSlot.texture = texture;
            padSlot.macrotext = BindPadPetAction[spellName];
        else
            padSlot.type = TYPE_SPELL;
            padSlot.bookType = subdetail;
            padSlot.name = spellName;
            padSlot.rank = nil;
            padSlot.texture = texture;
            padSlot.macrotext = nil;
        end

    elseif type == "merchant" then
        padSlot.type = TYPE_ITEM;
        padSlot.linktext = GetMerchantItemLink(detail);
        local name,_,_,_,_,_,_,_,_,texture = GetItemInfo(padSlot.linktext);
        padSlot.name = name;
        padSlot.texture = texture;

    elseif type == "companion" then
        padSlot.type = TYPE_BPMACRO;
        local creatureID, creatureName, creatureSpellID, texture = GetCompanionInfo(subdetail, detail);
        local spellName = GetSpellInfo(creatureSpellID);
        padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, spellName);
        padSlot.texture = texture;
        padSlot.macrotext = SLASH_CAST1.." "..spellName;

    elseif type == "mount" then
        padSlot.type = TYPE_BPMACRO;
        if subdetail == 0 then
            local SUMMON_RANDOM_FAVORITE_MOUNT_SPELL = 150544;
            local spellName, spellSubname, spellIcon = GetSpellInfo(SUMMON_RANDOM_FAVORITE_MOUNT_SPELL);
            padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, spellName);
            padSlot.texture = spellIcon;

            -- This fails when player is in a Druid's shapeshift form;
            --  padSlot.macrotext = SLASH_SCRIPT1.." C_MountJournal.Summon(0)";

            -- This fails if Blizzard_PetBattleUI is not loaded in memory;
            --  padSlot.macrotext = SLASH_CLICK1.." MountJournalSummonRandomFavoriteButton";

            -- This will accidently cancel Priest's Shadowform.
            -- padSlot.macrotext = "/cancelform\n"..SLASH_SCRIPT1.." C_MountJournal.Summon(0)";

            -- A very hacky workaround to all of the above.
            padSlot.macrotext = "/cancelform [worn:Leather]\n"..SLASH_SCRIPT1.." C_MountJournal.SummonByID(0)";

        else
            local creatureName, spellID, icon, active = C_MountJournal.GetMountInfoByID(detail);
            padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, creatureName);
            padSlot.texture = icon;
            padSlot.macrotext = SLASH_CAST1.." "..creatureName;
        end

    elseif type == "battlepet" then
        padSlot.type = TYPE_BPMACRO;
        local speciesID, customName, level, xp, maxXp, displayID, isFavorite, petName, petIcon, petType, creatureID = C_PetJournal.GetPetInfoByPetID(detail);
        padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, customName or petName);
        padSlot.texture = petIcon;
        padSlot.macrotext = SLASH_SUMMON_BATTLE_PET1.." "..(customName or petName);

    elseif type == "equipmentset" then
        padSlot.type = TYPE_BPMACRO;
        local textureName = BindPadCore.GetEquipmentSetTexture(detail);
        padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, detail);
        padSlot.texture = textureName;
        padSlot.macrotext = SLASH_EQUIP_SET1.." "..detail;

    else
        BindPadFrame_OutputText(format(BINDPAD_TEXT_CANNOT_PLACE, type));
        return;
    end

    padSlot.action = BindPadCore.CreateBindPadMacroAction(padSlot);
    if (BindPadVars.tab or 1) == 1 then
        local key = GetBindingKey(padSlot.action);
        if key then
            if BindPadVars.GeneralKeyBindings[key] == padSlot.action then
                padSlot.isForAllCharacters = true;
            end
        end
    end
    BindPadCore.UpdateMacroText(padSlot);
end

function BindPadCore.PlaceVirtualIconIntoSlot(id, drag)
    if TYPE_BPMACRO ~= drag.type then
        return;
    end

    local padSlot = BindPadCore.GetSlotInfo(id, true);

    padSlot.type = drag.type;
    padSlot.id = drag.id;
    padSlot.macrotext = drag.macrotext;
    padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, drag.name);
    padSlot.texture = drag.texture;
    padSlot.action = BindPadCore.CreateBindPadMacroAction(padSlot);
    if (BindPadVars.tab or 1) == 1 then
        padSlot.isForAllCharacters = drag.isForAllCharacters;
    else
        padSlot.isForAllCharacters = nil;
    end
    BindPadCore.UpdateMacroText(padSlot);

    table.wipe(drag);
    PlaySound(SOUNDKIT.IG_ABILITY_ICON_DROP)
end

function BindPadCore.CheckCorruptedSlot(padSlot)
    if padSlot.type == TYPE_ITEM and
        padSlot.linktext and
        padSlot.name and
        padSlot.texture and
        padSlot.action then
        return false;
    end
    if padSlot.type == TYPE_MACRO and
        padSlot.name and
        padSlot.texture and
        padSlot.action then
        return false;
    end
    if padSlot.type == TYPE_SPELL and
        padSlot.bookType and
        padSlot.name and
        padSlot.texture and
        padSlot.action then
        return false;
    end
    if padSlot.type == TYPE_BPMACRO and
        padSlot.name and
        padSlot.texture and
        padSlot.macrotext and
        padSlot.action then
        return false;
    end

    table.wipe(padSlot);
    return true;
end

function BindPadCore.GetCurrentProfileNum()
    if not BindPadCore.profileNum then
        BindPadCore.profileNum = 1;
    end

    return BindPadCore.profileNum;
end

function BindPadCore.GetProfileForSpec(specIndex)
    local character = BindPadCore.character;
    if not character then
        return nil;
    end
    if not BindPadVars[character].profileForTalentGroup[specIndex] then
        BindPadVars[character].profileForTalentGroup[specIndex] = specIndex;
    end

    return BindPadVars[character].profileForTalentGroup[specIndex];
end

function BindPadCore.GetProfileData()
    local character = BindPadCore.character;
    if not character then
        return nil;
    end
    local profileNum = BindPadCore.GetCurrentProfileNum();
    local profile = BindPadVars[character][profileNum];

    return profile;
end

function BindPadCore.SwitchProfile(newProfileNum, force)
    local oldProfileNum = BindPadCore.GetCurrentProfileNum();
    if not force and newProfileNum == oldProfileNum then
        return;
    end

    if InCombatLockdown() then
        return;
    end

    -- Close any optional frames.
    BindPadCore.HideSubFrames();

    local character = BindPadCore.character;
    if not character then
        return;
    end

    BindPadCore.profileNum = newProfileNum;

    --local specIndex = GetSpecialization();
    --BindPadVars[character].profileForTalentGroup[specIndex] = newProfileNum;
    BindPadVars[character].profileForTalentGroup[1] = newProfileNum;

    -- Create new profile if not available
    if not BindPadVars[character][newProfileNum] then
        BindPadVars[character][newProfileNum] = {};

        -- This call to DoSaveAllKeys is nesessary
        -- Putting current keybindings data into a new profile tab table.
        BindPadCore.DoSaveAllKeys();
        BindPadFrame_OutputText(BINDPAD_TEXT_CREATE_PROFILETAB);
    end

    -- Restore all Blizzard's Key Bindings for this spec if possible.
    BindPadCore.DoRestoreAllKeys();
end

function BindPadCore.CanPickupSlot(self)
    if not InCombatLockdown() then
        return true;
    end
    local padSlot = BindPadCore.GetSlotInfo(self:GetID());
    if padSlot == nil then
        return false;
    end
    if TYPE_SPELL == padSlot.type then
        BindPadFrame_OutputText(BINDPAD_TEXT_ERR_SPELL_INCOMBAT);
        return false;
    end
    if TYPE_MACRO == padSlot.type then
        BindPadFrame_OutputText(BINDPAD_TEXT_ERR_MACRO_INCOMBAT);
        return false;
    end
    return true;
end

function BindPadCore.PickupSlot(self, id, isOnDragStart)
    local padSlot = BindPadCore.GetSlotInfo(id);
    if padSlot == nil then
        return;
    end

    if self == BindPadCore.selectedSlotButton then
        BindPadCore.HideSubFrames();
    end

    if TYPE_ITEM == padSlot.type then
        PickupItem(padSlot.linktext);
    elseif TYPE_SPELL == padSlot.type then
        if padSlot.spellid then
            PickupSpell(padSlot.spellid);
        else
            local spellBookId = BindPadCore.FindSpellBookIdByName(padSlot.name, padSlot.rank, padSlot.bookType);
            if spellBookId then
                PickupSpellBookItem(spellBookId, padSlot.bookType);
            end
        end
    elseif TYPE_MACRO == padSlot.type then
        PickupMacro(padSlot.name);
    elseif TYPE_BPMACRO == padSlot.type then
        local drag = BindPadCore.dragswap;
        BindPadCore.dragswap = BindPadCore.drag;
        BindPadCore.drag = drag;

        drag.action = padSlot.action;
        drag.id = padSlot.id;
        drag.macrotext = padSlot.macrotext;
        drag.name = padSlot.name;
        drag.texture = padSlot.texture;
        drag.type = padSlot.type;
        drag.isForAllCharacters = padSlot.isForAllCharacters;

        BindPadCore.UpdateCursor();
        PlaySound(SOUNDKIT.IG_ABILITY_OPEN)
    end
    if (not ( isOnDragStart and IsModifierKeyDown() )) then
        -- Disable BindPadMacro (It will be re-enabled when placed on a slot.)
        BindPadCore.DeleteBindPadMacroID(padSlot);
        -- Empty the original slot
        table.wipe(padSlot);
    end
end

function BindPadCore.CarryOverKeybinding(key, action)
    local character = BindPadCore.character;
    local idx;
    for profileNum = 1, 5 do
        local profile = BindPadVars[character][profileNum];
        if profile ~= nil then
            if (profile.version or 0) >= BINDPAD_PROFILE_VERSION252 then
                profile.AllKeyBindings[key] = action;
            end
        end
    end
end

function BindPadCore.InnerSetBinding(key, action)
    BindPadCore.currentkeybindings[key] = action;
    SetBinding(key, action);
end

function BindPadCore.ManuallySetBinding(key, action)
    BindPadCore.InnerSetBinding(key, action);

    -- Set common binding for all Profiles if it's general tab.
    if (BindPadVars.tab or 1) == 1 then
        BindPadCore.CarryOverKeybinding(key, action);

        if BindPadCore.selectedSlot.isForAllCharacters then
            BindPadVars.GeneralKeyBindings[key] = action;
        else
            BindPadVars.GeneralKeyBindings[key] = nil;
        end
    end
end

function BindPadCore.BindKey(padSlot, keyPressed)
    if not InCombatLockdown() then
        BindPadCore.UnbindSlot(padSlot);
        BindPadCore.ManuallySetBinding(keyPressed, padSlot.action);
        BindPadCore.SaveBindings(GetCurrentBindingSet());
    else
        BindPadFrame_OutputText(BINDPAD_TEXT_CANNOT_BIND);
    end
end

function BindPadCore.UnbindSlot(padSlot)
    if not InCombatLockdown() then
        repeat
            local key = GetBindingKey(padSlot.action);
            if key then
                BindPadCore.ManuallySetBinding(key);
            end
        until key == nil
        BindPadCore.SaveBindings(GetCurrentBindingSet());
    end
end

function BindPadCore.GetSpellNum(bookType)
    local spellNum;
    if bookType == BOOKTYPE_PET then
        spellNum = HasPetSpells() or 0;
    else
        local i = 1;
        while (true) do
            local name, texture, offset, numSpells, isGuild, offSpecID = GetSpellTabInfo(i);
            if not name then
                break;
            end
            spellNum = offset + numSpells;
            i = i + 1;
        end
    end
    return spellNum;
end

function BindPadCore.FindSpellBookIdByName(srchName, srchRank, bookType)
    for i = 1, BindPadCore.GetSpellNum(bookType), 1 do
        local spellName, spellRank = GetSpellBookItemName(i, bookType);
        if spellName == srchName then
            return i;
        end
    end
end

function BindPadCore.GetBindingText(name, prefix, returnAbbr)
    local modKeys = GetBindingText(name);

    if returnAbbr then
        modKeys = gsub(modKeys, "CTRL", "c");
        modKeys = gsub(modKeys, "SHIFT", "s");
        modKeys = gsub(modKeys, "ALT", "a");
        modKeys = gsub(modKeys, "STRG", "st");
        modKeys = gsub(modKeys, "(%l)-(%l)-", "%1%2-");
        modKeys = gsub(modKeys, "-?Num Pad ", "#");
    end

    return modKeys;
end

function BindPadFrame_ChangeBindingProfile()
    if GetCurrentBindingSet() == 1 then
        LoadBindings(2);
        BindPadCore.SaveBindings(2);
        BindPadFrameCharacterButton:SetChecked(true);
    else
        local function f()
            local answer1 = BindPadCore.ShowDialog(CONFIRM_DELETING_CHARACTER_SPECIFIC_BINDINGS);
            if not answer1 then
                BindPadFrameCharacterButton:SetChecked(GetCurrentBindingSet() == 2);
                return;
            end

            local answer2 = BindPadCore.ShowDialog(BINDPAD_TEXT_ARE_YOU_SURE);
            if not answer2 then
                BindPadFrameCharacterButton:SetChecked(GetCurrentBindingSet() == 2);
                return;
            end

            LoadBindings(1);
            BindPadCore.SaveBindings(1);
            BindPadVars.tab = 1;
            BindPadFrame_OnShow();
        end

        -- Handles callback with coroutine.
        return coroutine.wrap(f)();
    end
end

function BindPadCore.ChatEdit_InsertLinkHook(text)
    if not text then
        return;
    end
    local activeWindow = ChatEdit_GetActiveWindow();
    if activeWindow then
        return;
    end
    if BrowseName and BrowseName:IsVisible() then
        return;
    end
    if MacroFrameText and MacroFrameText:IsVisible() then
        return;
    end

    if BindPadMacroFrameText and BindPadMacroFrameText:IsVisible() then
        local _, _, kind, spellid = string.find(text, "^|c%x+|H(%a+):(%d+)[|:]");

        if kind == "item" then
            text = GetItemInfo(text);
        elseif kind == "spell" and spellid then
            local name, rank = GetSpellInfo(spellid);
            text = name;
        end
        if BindPadMacroFrameText:GetText() == "" then
            if kind == "item" then
                if GetItemSpell(text) then
                    BindPadMacroFrameText:Insert(SLASH_USE1.." "..text);
                else
                    BindPadMacroFrameText:Insert(SLASH_EQUIP1.." "..text);
                end
            elseif kind == "spell" then
                BindPadMacroFrameText:Insert(SLASH_CAST1.." "..text);
            else
                BindPadMacroFrameText:Insert(text);
            end
        else
            BindPadMacroFrameText:Insert(text);
        end
    end
end
hooksecurefunc("ChatEdit_InsertLink", BindPadCore.ChatEdit_InsertLinkHook);

function BindPadCore.PickupSpellBookItemHook(slot, bookType)
    BindPadCore.PickupSpellBookItem_slot = slot;
    BindPadCore.PickupSpellBookItem_bookType = bookType;
end
hooksecurefunc("PickupSpellBookItem", BindPadCore.PickupSpellBookItemHook);

function BindPadCore.InitBindPadOnce(event)
    if not BindPadCore.initialized then
        BindPadCore.initialized = true;

        -- GetCurrentBindingSet() may not be ready yet.
        -- But sometimes we are already in combat at login.
        -- So do it now! or we won't have a chance to do it until combat finished.
        BindPadCore.InitProfile();
        BindPadCore.InitHotKeyList();
        BindPadCore.UpdateAllHotkeys();
    end
end

function BindPadCore.InitProfile()
    BindPadCore.character = "PROFILE_"..GetRealmName().."_"..UnitName("player")
    local character = BindPadCore.character

    if not BindPadVars[character] then
        local profileNum = BindPadCore.GetCurrentProfileNum()
        BindPadVars[character] = {}
        BindPadVars[character][profileNum] = {}
    end

    if not BindPadVars[character].profileForTalentGroup then
        BindPadVars[character].profileForTalentGroup = {}
    end

    --local newActiveTalentGroup = GetSpecialization()
    --local profileNum = BindPadCore.GetProfileForSpec(newActiveTalentGroup)
    local profileNum = 1

    -- Make sure profileNum tab is set for current talent group.
    BindPadCore.SwitchProfile(profileNum, true)

    -- Initialize activeTalentGroup variable
    BindPadCore.activeTalentGroup = newActiveTalentGroup

    BindPadMacro:SetAttribute("*type*", "macro")
    BindPadKey:SetAttribute("*checkselfcast*", true)
    BindPadKey:SetAttribute("*checkfocuscast*", true)

    BindPadCore.SetTriggerOnKeydown()

    -- HACK: Making sure BindPadMacroFrame has UIPanelLayout defined.
    -- If we don't do this at the init, ShowUIPanel() may fail in combat.
    GetUIPanelWidth(BindPadMacroFrame)
    -- Set current version number
    BindPadVars.version = BINDPAD_SAVEFILE_VERSION
end

function BindPadCore.UpdateMacroText(padSlot)
    if padSlot == nil then
        return;
    end

    BindPadCore.CheckCorruptedSlot(padSlot);
    if TYPE_ITEM == padSlot.type then
        BindPadKey:SetAttribute("*type-ITEM "..padSlot.name, "item");
        BindPadKey:SetAttribute("*item-ITEM "..padSlot.name, padSlot.name);
    elseif TYPE_SPELL == padSlot.type then
        local spellName = padSlot.name;
        BindPadKey:SetAttribute("*type-SPELL "..spellName, "spell");
        BindPadKey:SetAttribute("*spell-SPELL "..spellName, spellName);
    elseif TYPE_MACRO == padSlot.type then
        BindPadKey:SetAttribute("*type-MACRO "..padSlot.name, "macro");
        BindPadKey:SetAttribute("*macro-MACRO "..padSlot.name, padSlot.name);
    elseif TYPE_BPMACRO == padSlot.type then
        BindPadMacro:SetAttribute("*macrotext-"..padSlot.name, padSlot.macrotext);
    else
        return;
    end

    -- !!!!! It's NOT old file conversion.
    -- Update string of padSlot.action
    -- And then update a keybinding for the padSlot.action.
    local newAction = BindPadCore.CreateBindPadMacroAction(padSlot);
    if padSlot.action ~= newAction then
       local key = GetBindingKey(padSlot.action);
       if key then
           BindPadCore.InnerSetBinding(key, newAction);
           BindPadCore.SaveBindings(GetCurrentBindingSet());
       end
       padSlot.action = newAction;
    end
end

function BindPadCore.NewBindPadMacroName(padSlot, name)
    local successFlag;
    repeat
        successFlag = true;
        for curSlot in BindPadCore.AllSlotInfoIter() do
            if (TYPE_BPMACRO == curSlot.type and padSlot ~= curSlot
                and curSlot.name ~= nil
                and strlower(name) == strlower(curSlot.name)) then
                local first, last, num = strfind(name, "(%d+)$");
                if not num then
                    name = name.."_2";
                else
                    name = strsub(name, 0, first - 1)..(num+1);
                end
                successFlag = false;
                break;
            end
        end
    until successFlag;

    return name;
end

function BindPadCore.DeleteBindPadMacroID(padSlot)
    BindPadMacro:SetAttribute("*macrotext-"..padSlot.name, nil);
end

function BindPadCore.UpdateCursor()
    local drag = BindPadCore.drag;
    if GetCursorInfo() then
        BindPadCore.ClearCursor();
    end
    if TYPE_BPMACRO == drag.type then
        if type(drag.texture) == "number" then
            -- SetCursor() doesn't accept numbers.
            SetCursor("Interface\\ICONS\\INV_Misc_QuestionMark");
        else
            SetCursor(drag.texture);
        end
    end
end

function BindPadCore.CreateBindPadMacroAction(padSlot)
    if padSlot.name == nil then
        return nil;
    end
    if TYPE_ITEM == padSlot.type then
        return "CLICK BindPadKey:ITEM "..padSlot.name;
    elseif TYPE_SPELL == padSlot.type then
        return "CLICK BindPadKey:SPELL "..padSlot.name;
    elseif TYPE_MACRO == padSlot.type then
        return "CLICK BindPadKey:MACRO "..padSlot.name;
    elseif TYPE_BPMACRO == padSlot.type then
        return "CLICK BindPadMacro:"..padSlot.name;
    end
    return nil;
end

function BindPadCore.ConvertToBindPadMacro()
    local padSlot = BindPadCore.selectedSlot;

    if TYPE_ITEM == padSlot.type then
        padSlot.type = TYPE_BPMACRO;
        padSlot.linktext = nil;
        padSlot.macrotext = SLASH_USE1.." [mod:SELFCAST,@player][mod:FOCUSCAST,@focus][] "..padSlot.name;
    elseif TYPE_SPELL == padSlot.type then
        padSlot.macrotext = SLASH_CAST1.." [mod:SELFCAST,@player][mod:FOCUSCAST,@focus][] "..padSlot.name;
        padSlot.type = TYPE_BPMACRO;
        padSlot.bookType = nil;
        padSlot.rank = nil;
        padSlot.spellid = nil;
    elseif TYPE_MACRO == padSlot.type then
        local name, texture, macrotext = GetMacroInfo(padSlot.name);
        padSlot.type = TYPE_BPMACRO;
        padSlot.macrotext = macrotext or "";
    else
        return;
    end

    padSlot.name = BindPadCore.NewBindPadMacroName(padSlot, padSlot.name);
    padSlot.action = BindPadCore.CreateBindPadMacroAction(padSlot);
    BindPadCore.UpdateMacroText(padSlot);

    BindPadSlot_UpdateState(BindPadCore.selectedSlotButton);
    BindPadMacroFrame_Open(BindPadCore.selectedSlotButton);
end

function BindPadCore.CursorHasIcon()
    return GetCursorInfo() or BindPadCore.drag.type
end

function BindPadCore.ClearCursor()
    local drag = BindPadCore.drag;
    if TYPE_BPMACRO == drag.type then
        ResetCursor();
        PlaySound(SOUNDKIT.IG_ABILITY_ICON_DROP)
    end
    drag.type = nil;
end

function BindPadCore.PlayerTalentUpdate()
    -- Reset cache for morphing spells
    BindPadCore.morphingSpellCache = nil;

    local newActiveSpec = GetSpecialization();
    local profileNum = BindPadCore.GetProfileForSpec(newActiveSpec)

    BindPadCore.SwitchProfile(profileNum);
    if BindPadFrame:IsShown() then
        BindPadFrame_OnShow();
    end

    BindPadCore.activeTalentGroup = newActiveSpec;
end

function BindPadCore.CVAR_UPDATE(arg1, arg2)
    if arg1 == "ACTION_BUTTON_USE_KEY_DOWN" then
        BindPadCore.SetTriggerOnKeydown();
    end
end

function BindPadCore.GetSpecTexture(specIndex)
    return "Interface\\Icons\\Ability_Marksmanship";
end

function BindPadCore.SetTriggerOnKeydown()
    if GetCVarBool("ActionButtonUseKeyDown") then
        -- Triggered on pressing a key instead of releasing.
        BindPadMacro:RegisterForClicks("AnyDown");
        BindPadKey:RegisterForClicks("AnyDown");
    else
        -- Triggered on releasing a key.
        BindPadMacro:RegisterForClicks("AnyUp");
        BindPadKey:RegisterForClicks("AnyUp");
    end
end

function BindPadCore.DoList(arg)
    for k,v in pairs(BindPadVars) do
        local name = string.match(k, "^PROFILE_(.*)");
        if name then
            print(name);
        end
    end
end

function BindPadCore.DoDelete(arg)
    local name = "PROFILE_"..arg;
    if name == BindPadCore.character then
        BindPadFrame_OutputText(BINDPAD_TEXT_DO_DELETE_ERR_CURRENT);
    else
        if BindPadVars[name] then
            BindPadVars[name] = nil;
            BindPadFrame_OutputText(string.format(BINDPAD_TEXT_DO_DELETE, arg));
        else
            BindPadFrame_OutputText(string.format(BINDPAD_TEXT_DO_ERR_NOT_FOUND, arg));
        end
    end
end

function BindPadCore.DoCopyFrom(arg)
    local name = "PROFILE_"..arg;
    if name == BindPadCore.character then
        BindPadFrame_OutputText(BINDPAD_TEXT_DO_COPY_ERR_CURRENT);
    else
        if BindPadVars[name] then
            local backupname = BindPadCore.character.."_backup";
            if BindPadVars[backupname] == nil then
                BindPadVars[backupname] = BindPadVars[BindPadCore.character];
            end
            BindPadVars[BindPadCore.character] = BindPadCore.DuplicateTable(BindPadVars[name]);
            BindPadCore.InitProfile();

            if BindPadFrame:IsShown() then
                BindPadFrame_OnShow();
            end
            BindPadFrame_OutputText(string.format(BINDPAD_TEXT_DO_COPY, arg));
        else
            BindPadFrame_OutputText(string.format(BINDPAD_TEXT_DO_ERR_NOT_FOUND, arg));
        end
    end
end

function BindPadCore.DuplicateTable(table)
    local newtable = {};
    for k,v in pairs(table) do
        if type(v) == "table" then
            newtable[k] = BindPadCore.DuplicateTable(v);
        else
            newtable[k] = v;
        end
    end
    return newtable;
end

function BindPadCore.GetMacroIconInfo(index)
    if not index then
        return;
    end
    if BindPadCore.HiddenSlot == nil then
        BindPadCore.HiddenSlot = CreateFrame("CheckButton", "BindPadHiddenSlot", BindPadFrame, "BindPadSlotTemplate", 100);
    end

    local texture = MACRO_ICON_FILENAMES[index];
    if texture then
        if type(texture) == "number" then --should stop using texture paths completely
            -- Trying to convert number to the file path string.
            -- Sometimes works. Sometimes doesn't work. As of patch 7.0
            BindPadCore.HiddenSlot.icon:SetTexture(texture);
            return BindPadCore.HiddenSlot.icon:GetTexture();
        else
            return "INTERFACE\\ICONS\\"..texture;
        end
    else
        return nil;
    end
end

function BindPadFrame_SaveAllKeysToggle(self)
    BindPadVars.saveAllKeysFlag = (not not self:GetChecked());
    BindPadCore.DoSaveAllKeys();
end

function BindPadFrame_ShowHotkeyToggle(self)
    BindPadVars.showHotkey = (not not self:GetChecked());
    BindPadCore.UpdateAllHotkeys();
end

function BindPadCore.DoSaveAllKeys()
    if BindPadCore.ChangingKeyBindings then
        return;
    end
    if not BindPadCore.character then
        return;
    end
    local profile = BindPadCore.GetProfileData();

    if profile.AllKeyBindings == nil then
        profile.AllKeyBindings = {};
    else
        table.wipe(profile.AllKeyBindings);
    end
    if BindPadVars.GeneralKeyBindings == nil then
        BindPadVars.GeneralKeyBindings = {};
    else
        table.wipe(BindPadVars.GeneralKeyBindings);
    end

    for i=1,GetNumBindings() do
        local command, category, key1, key2 = GetBinding(i);
        if key1 then
            profile.AllKeyBindings[key1] = command;
            if key2 then
                profile.AllKeyBindings[key2] = command;
            end
        end
    end
    for padSlot in BindPadCore.AllSlotInfoIter() do
        local key = GetBindingKey(padSlot.action);
        if key then
            profile.AllKeyBindings[key] = padSlot.action;
            if padSlot.isForAllCharacters then
                BindPadVars.GeneralKeyBindings[key] = padSlot.action;
            end
        end
    end
end

function BindPadCore.DoRestoreAllKeys()
    local profile = BindPadCore.GetProfileData();
    if profile.AllKeyBindings == nil then
        -- Initialize keyBindings table if none available.
        BindPadCore.DoSaveAllKeys();
    end

    if BindPadVars.GeneralKeyBindings == nil then
        BindPadVars.GeneralKeyBindings = {};
    end

    local count = 0;
    for k,v in pairs(profile.AllKeyBindings) do
        count = count + 1;
    end

    if count < 10 then
        BindPadFrame_OutputText("DEBUG: Something wrong. profile.AllKeyBindings is most likely broken.");
        return;
    end

    BindPadCore.ChangingKeyBindings = true;

    -- Override GeneralKeyBindings over all profiles.
    for k,v in pairs(BindPadVars.GeneralKeyBindings) do
        BindPadCore.CarryOverKeybinding(k, v);
    end

    -- Unbind Blizzard's key bindings only when "Save All Keys" option is ON.
    if BindPadVars.saveAllKeysFlag then
        for i=1,GetNumBindings() do
            local command, category, key1, key2 = GetBinding(i);
            -- Ensure to be unbinded if not binded.
            if key1 and profile.AllKeyBindings[key1] == nil then
                BindPadCore.InnerSetBinding(key1, nil);
            end
            -- Ensure to be unbinded if not binded.
            if key2 and profile.AllKeyBindings[key2] == nil then
                BindPadCore.InnerSetBinding(key2, nil);
            end
        end
    end

    --   for padSlot in BindPadCore.AllSlotInfoIter() do
    --      if padSlot.action then
    --         -- Ensure to be unbinded if not binded.
    --         local key = GetBindingKey(padSlot.action);
    --         if key then
    --	    if profile.AllKeyBindings[key] == nil then
    --	       BindPadCore.InnerSetBinding(key, nil);
    --	    end
    --         end
    --      end
    --   end
    local to_be_removed = {};
    for k,v in pairs(BindPadCore.currentkeybindings) do
        -- Ensure to be unbinded if not binded.
        if profile.AllKeyBindings[k] == nil then
            if strfind(v, "^CLICK BindPad") then
                table.insert(to_be_removed, k);
            end
        end
    end

    for i=1,#to_be_removed do
        BindPadCore.InnerSetBinding(to_be_removed[i], nil);
    end

    for k,v in pairs(profile.AllKeyBindings) do
        if BindPadVars.saveAllKeysFlag
            or strfind(v, "^CLICK BindPad") then

            local key1, key2 = GetBindingKey(v);
            if key1 ~= k and key2 ~= k then
                BindPadCore.InnerSetBinding(k, v);
            else
                BindPadCore.currentkeybindings[k] = v;
            end
        end
    end

    BindPadCore.ChangingKeyBindings = false;

    -- Don't do it twice.
    local ticker = BindPadCore.ticker_SaveBindings;
    if ticker ~= nil then
        ticker:Cancel();
    end

    -- 2.7.13: It really need to be saved.  So do it later with 0.5 sec delay.
    BindPadCore.ticker_SaveBindings =
        C_Timer.NewTicker(0.5,
            function()
                BindPadCore.ticker_SaveBindings = nil;
                local function run()
                    BindPadCore.SaveBindings(GetCurrentBindingSet());
                end
                if InCombatLockdown() then
                    BindPadCore.WaitForEvent("PLAYER_REGEN_ENABLED", run);
                else
                    run();
                end
            end, 1);

    for padSlot in BindPadCore.AllSlotInfoIter() do
        -- Prepare macro text for every BindPad Macro for this profile.
        BindPadCore.UpdateMacroText(padSlot);
    end
end

function BindPadCore.InsertBindingTooltip(action)
    if not BindPadVars.showHotkey then return; end

    if action then
        local key = BindPadCore.GetBindingKeyFromAction(action);
        if not key then
            action = BindPadCore.GetBaseForMorphingSpell(action);
            key = BindPadCore.GetBindingKeyFromAction(action);
        end
        if key then
            GameTooltip:AddLine(BINDPAD_TOOLTIP_KEYBINDING..BindPadCore.GetBindingText(key, "KEY_"), 0.8, 0.8, 1.0);
            GameTooltip:Show();
        end
    end
end

function BindPadCore.GameTooltipSetItemByID(self, itemID)
    BindPadCore.InsertBindingTooltip(concat("ITEM ", GetItemInfo(itemID)));
end

function BindPadCore.GameTooltipSetBagItem(self, bag, slot)
    local itemID = GetContainerItemID(bag, slot);
    if itemID then
        BindPadCore.InsertBindingTooltip(concat("ITEM ", GetItemInfo(itemID)));
    end
end

function BindPadCore.GameTooltipSetSpellByID(self, spellID)
    BindPadCore.InsertBindingTooltip(concat("SPELL ", GetSpellInfo(spellID)));
end

function BindPadCore.GameTooltipSetSpellBookItem(self, slot, bookType)
    BindPadCore.InsertBindingTooltip(concat("SPELL ", GetSpellBookItemName(slot, bookType)));
end

function BindPadCore.GameTooltipSetAction(self, slot)
    BindPadCore.InsertBindingTooltip(BindPadCore.GetActionCommand(slot));
end

hooksecurefunc(GameTooltip, "SetItemByID", function(...) return BindPadCore.GameTooltipSetItemByID(...) end);
hooksecurefunc(GameTooltip, "SetBagItem", function(...) return BindPadCore.GameTooltipSetBagItem(...) end);
hooksecurefunc(GameTooltip, "SetSpellByID", function(...) return BindPadCore.GameTooltipSetSpellByID(...) end);
hooksecurefunc(GameTooltip, "SetSpellBookItem", function(...) return BindPadCore.GameTooltipSetSpellBookItem(...) end);
hooksecurefunc(GameTooltip, "SetAction", function(...) return BindPadCore.GameTooltipSetAction(...) end);

function BindPadCore.ShowDialog(text)
    BindPadCore.CancelDialogs();

    local dialog = BindPadDialogFrame;
    dialog.text:SetText(text);
    local height = 32 + dialog.text:GetHeight() + 8 + dialog.okaybutton:GetHeight();
    dialog:SetHeight(height);

    local co = coroutine.running();
    -- Making closures with current local value of co.
    dialog.okaybutton:SetScript("OnClick",
        function(self)
            self:GetParent():Hide();
            coroutine.resume(co, true);
        end);
    dialog.cancelbutton:SetScript("OnClick",
        function(self)
            self:GetParent():Hide();
            coroutine.resume(co, false)
        end);
    dialog:Show();

    return coroutine.yield();
end

function BindPadCore.CancelDialogs()
    local dialog = BindPadDialogFrame;
    if dialog:IsShown() then
        dialog.cancelbutton:Click();
    end
end

BindPadCore.HotKeyList = {};
BindPadCore.CreateFrameQueue = {};

function BindPadCore.InitHotKeyList()
    for k, button in pairs(ActionBarButtonEventsFrame.frames) do
        BindPadCore.CreateFrameQueue[button:GetName()] = "ActionBarButtonTemplate";
    end
end

function BindPadCore.AddHotKey(name, GetAction)
    if BindPadCore.HotKeyList[name] then return; end

    local button = _G[name];
    if not button then return; end

    local hotkey = _G[name.."HotKey"];
    if not hotkey then return; end

    local info = {};
    info.GetAction = GetAction;
    info.button = button;
    info.hotkey = hotkey;

    info.bphotkey = button:CreateFontString(name.."BPHotKey", "ARTWORK", "NumberFontNormalSmallGray");
    info.bphotkey:SetJustifyH("RIGHT")
    info.bphotkey:SetSize(button:GetWidth(), 10)
    info.bphotkey:SetPoint("TOPRIGHT", button, "TOPRIGHT", 0, -3)
    info.bphotkey:Show();

    -- Copying the range indicator color change.
    hooksecurefunc(info.hotkey, "SetVertexColor",
        function(self, red, green, blue)
            return info.bphotkey:SetVertexColor(red, green, blue)
        end);

    BindPadCore.HotKeyList[name] = info;
end

function BindPadCore.AddAllHotKeys()
    for buttonname,buttontype in pairs(BindPadCore.CreateFrameQueue) do
        if buttontype == "ActionBarButtonTemplate" then
            BindPadCore.AddHotKey(buttonname,
                function(info)
                    return info.button.action;
                end);
        elseif buttontype == "LibActionButton" then
            if _G[buttonname].GetAction then
                BindPadCore.AddHotKey(buttonname,
                    function(info)
                        local type, action = info.button:GetAction();
                        if type == "action" then return action; else return nil; end
                    end);
            end
        end
    end
    table.wipe(BindPadCore.CreateFrameQueue);
end

function BindPadCore.UpdateAllHotkeys()
    local function f()
        BindPadCore.ticker_UpdateAllHotkeys = nil;
        BindPadCore.AddAllHotKeys();
        for name,info in pairs(BindPadCore.HotKeyList) do
            BindPadCore.OverwriteHotKey(info);
        end
    end

    -- Don't do it twice.
    local ticker = BindPadCore.ticker_UpdateAllHotkeys;
    if ticker ~= nil then
        ticker:Cancel();
    end

    -- It's hard work, so do it later with 0.1 sec delay.
    -- BindPadTimerFrame:StartTimer(0.1, f);
    BindPadCore.ticker_UpdateAllHotkeys = C_Timer.NewTicker(0.1, f, 1) ;
end

function BindPadCore.OverwriteHotKey(info)
    if BindPadVars.showHotkey then
        local action = BindPadCore.GetActionCommand(info:GetAction());
        if action then
            local key = BindPadCore.GetBindingKeyFromAction(action);
            if not key then
                action = BindPadCore.GetBaseForMorphingSpell(action);
                key = BindPadCore.GetBindingKeyFromAction(action);
            end
            if key then
                -- BindPad's ShowHotKey
                info.bphotkey:SetText(BindPadCore.GetBindingText(key, "KEY_", 1));
                info.bphotkey:SetAlpha(1);

                -- Making original hotkey transparent.
                info.hotkey:SetAlpha(0);
                return;
            end
        end
    end

    -- Restoring original hotkey
    info.bphotkey:SetAlpha(0);
    info.hotkey:SetAlpha(1);
end

function BindPadCore.GetActionCommand(actionSlot)
    if not actionSlot then
        return nil;
    end
    local type, id, subType, subSubType = GetActionInfo(actionSlot);
    if type == "spell" then
        return concat("SPELL ", GetSpellInfo(id));
    elseif type == "item" then
        return concat("ITEM ", GetItemInfo(id));
    elseif type == "macro" then
        return concat("MACRO ", GetMacroInfo(id));
    else
        return nil;
    end
end

function BindPadCore.CreateFrameHook(frameType, frameName, parentFrame, inheritsFrame, id)
    if frameType == "CheckButton" and inheritsFrame then
        if frameName then
            if frameName:match("$parent") and parentFrame:GetName() then
                frameName = frameName:gsub("$parent", parentFrame:GetName())
            end
            if not _G[frameName] then return end
            if inheritsFrame == "ActionBarButtonTemplate" then
                BindPadCore.CreateFrameQueue[frameName] = "ActionBarButtonTemplate";
            end
            if string.find(inheritsFrame, "SecureActionButtonTemplate%s*,%s*ActionButtonTemplate") then
                BindPadCore.CreateFrameQueue[frameName] = "LibActionButton";
            end
        end
    end
end
hooksecurefunc("CreateFrame", BindPadCore.CreateFrameHook);

BindPadCore.useBindPadSlot = 0;
function BindPadCore.CreateBindPadSlot(usenum)
    local NUM_SLOTS_PER_ROW = 6;

    for i = min(usenum+1, BindPadCore.useBindPadSlot+1), max(usenum, BindPadCore.useBindPadSlot) do
        local button = _G["BindPadSlot"..i];
        if button == nil then
            button = CreateFrame("CheckButton", "BindPadSlot"..i, BindPadSlotButtonContainer, "BindPadSlotTemplate", i);
        end
        if i <= usenum then
            if ( i == 1 ) then
                button:SetPoint("TOPLEFT", BindPadSlotButtonContainer, "TOPLEFT", 6, -6);
            elseif ( mod(i, NUM_SLOTS_PER_ROW) == 1 ) then
                button:SetPoint("TOP", "BindPadSlot"..(i-NUM_SLOTS_PER_ROW), "BOTTOM", 0, -10);
            else
                button:SetPoint("LEFT", "BindPadSlot"..(i-1), "RIGHT", 13, 0);
            end
            button:Enable();
            button:Show();
        else
            button:Hide();
            button:Disable();
            button:ClearAllPoints();
        end
    end

    BindPadScrollFrameFooter:SetPoint("TOPLEFT", "BindPadSlot"..(floor((usenum-1)/NUM_SLOTS_PER_ROW)*NUM_SLOTS_PER_ROW+1), "BOTTOMLEFT", 0, 0);

    BindPadCore.useBindPadSlot = usenum;
    BindPadScrollFrameNumber:SetFormattedText(BINDPAD_TEXT_SLOTS_SHOWN, usenum);
    if usenum > BINDPAD_MAXSLOTS_DEFAULT then
        BindPadShowLessSlotButton:Enable();
    else
        BindPadShowLessSlotButton:Disable();
    end
end

function BindPadShowLessSlotButton_OnClick()
    local tabInfo = BindPadCore.GetTabInfo(BindPadVars.tab);
    tabInfo.numSlot = tabInfo.numSlot - 42;
    BindPadFrame_OnShow();
end

function BindPadShowMoreSlotButton_OnClick()
    local tabInfo = BindPadCore.GetTabInfo(BindPadVars.tab);
    tabInfo.numSlot = tabInfo.numSlot + 42;
    BindPadFrame_OnShow();
end

function BindPadCore.GetSlotInfo(id, newFlag)
    return BindPadCore.GetSlotInfoInTab(BindPadVars.tab, id, newFlag);
end

function BindPadCore.GetTabInfo(tab)
    if tab == BINDPAD_GENERAL_TAB then
        if BindPadVars.numSlot == nil then
            BindPadVars.numSlot = BINDPAD_MAXSLOTS_DEFAULT;
        end

        return BindPadVars;
    else
        local character = BindPadCore.character;
        local profileNum = BindPadCore.GetCurrentProfileNum();
        if not BindPadVars[character][profileNum] then
            BindPadVars[character][profileNum] = {};
        end
        local profile = BindPadVars[character][profileNum];

        local tabname = "CharacterSpecificTab"..(tab - BINDPAD_GENERAL_TAB);
        if not profile[tabname] then
            profile[tabname] = {};
            for newid = 1, BINDPAD_MAXSLOTS_DEFAULT do
                local oldid = newid + (tab-2) * BINDPAD_MAXSLOTS_DEFAULT;
                -- Relocating old SlotInfo into the new table.
                profile[tabname][newid] = profile[oldid];
                profile[oldid] = nil;
            end
            if profile[tabname].numSlot == nil then
                profile[tabname].numSlot = BINDPAD_MAXSLOTS_DEFAULT;
            end
        end

        return profile[tabname];
    end
end

function BindPadCore.GetSlotInfoInTab(tab, id, newFlag)
    if not BindPadCore.character then
        BindPadFrame_OutputText("DEBUG: Something wrong.  Please report this message to the author of BindPad.");
        return nil;
    end

    if tab == nil then
        tab = 1;
    end

    if id == nil then
        return nil;
    end

    local tabInfo = BindPadCore.GetTabInfo(tab);
    if not tabInfo[id] then
        if newFlag then
            tabInfo[id] = {};
        end
    else
        if not newFlag and tabInfo[id].type == nil then
            tabInfo[id] = nil;
        end
    end

    return tabInfo[id];
end

function BindPadCore.AllSlotInfoIter()
    local function f()
        for tab = 1, 4 do
            local numSlot = BindPadCore.GetTabInfo(tab).numSlot
            for id = 1, numSlot do
                local padSlot = BindPadCore.GetSlotInfoInTab(tab, id, nil);
                if padSlot then
                    coroutine.yield(padSlot);
                end
            end
        end
    end

    return coroutine.wrap(f);
end

function BindPadCore.HidePopup()
    BindPadMacroPopupFrame:Hide();
    BindPadBindFrame:Hide();
end

function BindPadCore.HideSubFrames()
    BindPadCore.HidePopup();
    HideUIPanel(BindPadMacroFrame);
end

function BindPadCore.SaveBindings(which)
    if which == 1 or which == 2 then
        SaveBindings(which);
    else
    -- GetCurrentBindingSet() sometimes returns invalid number at login.
    -- Just ignoreing this.  As far as I know there is no good way to avoid this.
    -- BindPadFrame_OutputText("GetCurrentBindingSet() returned:"..(which or "nil"));
    end
end

function BindPadCore.GetBaseForMorphingSpell(spellAction)
    if not BindPadCore.morphingSpellCache then
        BindPadCore.morphingSpellCache = {};
        local i;
        local bookType = BOOKTYPE_SPELL;
        for i = 1, BindPadCore.GetSpellNum(bookType), 1 do
            local skillType, spellId = GetSpellBookItemInfo(i, bookType)
            if "SPELL" == skillType then
                local morphSpellName = string.upper(GetSpellBookItemName(i, bookType));
                local baseSpellName = GetSpellInfo(spellId);
                if string.upper(baseSpellName) ~= morphSpellName then
                    BindPadCore.morphingSpellCache["SPELL "..morphSpellName] = "SPELL "..baseSpellName
                end
            end
        end
    end

    if BindPadCore.morphingSpellCache[string.upper(spellAction)] then
        return BindPadCore.morphingSpellCache[string.upper(spellAction)];
    else
        return spellAction;
    end
end

function BindPadFrame_ForAllCharactersToggle(self)
    local padSlot = BindPadCore.selectedSlot;
    padSlot.isForAllCharacters = (not not self:GetChecked());

    local key = GetBindingKey(padSlot.action);
    if key then
        -- Re-bind same existing keybinding to update BindPadVars.GeneralKeyBindings.
        BindPadCore.ManuallySetBinding(key, padSlot.action)
    end
end

function BindPadCore.GetBindingKeyFromAction(action)
    local key = GetBindingKey("CLICK BindPadKey:"..action);
    if key then
        -- Check if this keybind is ready to use. (or residue)
        if BindPadKey:GetAttribute("*type-"..action) then
            return key;
        end
    end
    return nil;
end

function BindPadCore.WaitForEvent(event, func)
    if BindPadCore.JobFrame == nil then
        BindPadCore.JobFrame = CreateFrame("FRAME", "BindPadCoreJobFrame");
    end
    local stack = nil;
    if BindPadCore.eventProc[event] ~= nil then
        stack = BindPadCore.eventProc[event];
    else
        BindPadCore.JobFrame:RegisterEvent(event)
        local function OnEvent(self, event2, ...)
            local run = BindPadCore.eventProc[event2];
            if run ~= nil then
                BindPadCore.eventProc[event2] = nil;
                run(event2, ...);
            end
        end
        BindPadCore.JobFrame:SetScript("OnEvent", OnEvent);
    end
    local function f(...)
        BindPadCore.JobFrame:UnregisterEvent(event)
        func(...);
        if stack ~= nil then
            stack(...);
        end
    end
    BindPadCore.eventProc[event] = f;
end
