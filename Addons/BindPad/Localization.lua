
local P = NORMAL_FONT_COLOR_CODE.."%s"..FONT_COLOR_CODE_CLOSE

BINDING_HEADER_BINDPAD = "BindPad";
BINDING_NAME_TOGGLE_BINDPAD = "Toggle BindPad";
BINDPAD_TITLE = "BindPad";
BINDPAD_TITLE_1 = "BindPad Profile1";
BINDPAD_TITLE_2 = "BindPad Profile2";
BINDPAD_TITLE_3 = "BindPad Profile3";
BINDPAD_KEYBINDINGS_TITLE = "Keybinding";
BINDPAD_MACRO_TITLE = "Create BindPad Macro";

BINDPAD_TEXT_GENERAL_TAB = "General";
BINDPAD_TEXT_SPECIFIC_TAB = "%s Specific";
BINDPAD_TEXT_SPECIFIC_EXTRA_TAB2 = "2";
BINDPAD_TEXT_SPECIFIC_EXTRA_TAB3 = "3";
BINDPAD_TEXT_EXIT = "Exit";
BINDPAD_TEXT_TEST = "Test";
BINDPAD_TEXT_UNBIND = "Unbind";
BINDPAD_TEXT_PRESSKEY = "Press a key to bind";
BINDPAD_TEXT_KEY = "Current Key: ";
BINDPAD_TEXT_NOTBOUND = "Not Bound";
BINDPAD_TEXT_FAST_TRIGGER = "Fast Trigger"
BINDPAD_TEXT_CONFIRM_BINDING = P.." is currently bound to \n"..P.."\n\nDo you want to bind "..P.." to \n"..P.."?";
BINDPAD_TEXT_CANNOT_PLACE = "ERROR: %s can not be placed in BindPad slot.";
BINDPAD_TEXT_CANNOT_BIND = "Cannot change key bindings while in combat."
BINDPAD_TEXT_OBSOLATED = "Older version's savefile for BindPad is now obsolated and deleted.  Sorry for inconvenience.";
BINDPAD_TEXT_ARE_YOU_SURE = "REALLY? ARE YOU SURE?";
BINDPAD_TEXT_CONFIRM_CHANGE_BINDING_PROFILE = "You need to activate Character Specific Key Bindings mode of Blizard-UI.  Click Okay if you want to change Key Bindings mode now.";
BINDPAD_TEXT_CONFIRM_CONVERT = "Are you sure you want to convert this %s \"%s\" into a BindPad Macro?";
BINDPAD_TEXT_SHOW_HOTKEY = "Show Hotkeys";
BINDPAD_TEXT_SAVE_ALL_KEYS = "Save All Keys";
BINDPAD_TEXT_FOR_ALL_CHARACTERS = "For all characters";
BINDPAD_TEXT_ERR_UNIQUENAME = "You must enter unique name for BindPadMacro.";
BINDPAD_TEXT_ERR_SPELL_INCOMBAT = "Cannot pickup spell icon while in combat.";
BINDPAD_TEXT_ERR_MACRO_INCOMBAT = "Cannot pickup macro icon while in combat.";
BINDPAD_TEXT_ERR_BINDPADMACRO_INCOMBAT = "Cannot edit BindPadMacro while in combat.";
BINDPAD_TEXT_CREATE_PROFILETAB = "Created new profile.  All icons are duplicated now.";
BINDPAD_TEXT_SLOTS_SHOWN = "%d Slots shown";
BINDPAD_TOOLTIP_MACRO = "Macro: ";
BINDPAD_TOOLTIP_COMPANION = "Companion: ";
BINDPAD_TOOLTIP_EQUIPMENTSET = "Equipmentset: ";
BINDPAD_TOOLTIP_BINDPADMACRO = "BindPadMacro:%s";
BINDPAD_TOOLTIP_DOWNRANK = "Down Ranking to : ";
BINDPAD_TOOLTIP_KEYBINDING = "KeyBinding: ";
BINDPAD_TOOLTIP_UNKNOWN_SPELL = "Unknown spell: ";
BINDPAD_TOOLTIP_OPENSPELLBOOK = "Open Spellbook";
BINDPAD_TOOLTIP_OPENBAG = "Open All Bags";
BINDPAD_TOOLTIP_OPENMACRO = "Open Macros Panel";
BINDPAD_TOOLTIP_TAB1 = "General Slots";
BINDPAD_TOOLTIP_GENERAL_TAB_EXPLAIN = "For common icons used for every characters and every specs.";
BINDPAD_TOOLTIP_TAB2 = "%s Specific Slots";
BINDPAD_TOOLTIP_SPECIFIC_TAB_EXPLAIN = "For icons specific to current character and current spec.";
BINDPAD_TOOLTIP_TAB3 = "%s Specific Slots 2nd Tab";
BINDPAD_TOOLTIP_TAB4 = "%s Specific Slots 3rd Tab";
BINDPAD_TOOLTIP_SAVE_ALL_KEYS = "Automatically save&restore all keys of Blizzard's Key Bindings Interface for each profile.";
BINDPAD_TOOLTIP_SHOW_HOTKEY =
   "Automatically shows hotkey text when you place 'BindPad'ed icons on ActionBars. Also shows keybindings in tooltips.";
BINDPAD_TOOLTIP_FOR_ALL_CHARACTERS = 
   "Keybind for this icon will be carried over to all other characters.";
BINDPAD_TOOLTIP_CREATE_MACRO = "Create BindPad Macro";
BINDPAD_TOOLTIP_CLICK_USAGE1 = "Right click to edit macro\nLeft click to bind";
BINDPAD_TOOLTIP_CLICK_USAGE2 = "Right click to convert\nLeft click to bind";
BINDPAD_TOOLTIP_EXTRA_PROFILE = "Profile";
BINDPAD_TOOLTIP_PROFILE_CURRENTLY1 = "Currently assigned to %s";
BINDPAD_TOOLTIP_PROFILE_CURRENTLY2 = "Currently assigned to both %s and %s";
BINDPAD_TOOLTIP_PROFILE_CURRENTLY3 = "Currently assigned to %s, %s and %s";
BINDPAD_TOOLTIP_PROFILE_CURRENTLY4 = "Currently assigned to 4 specs";
BINDPAD_TOOLTIP_PROFILE_CLICK_FOR = "Click here to assign Profile%d to %s";
BINDPAD_TOOLTIP_SHOW_MORE_SLOT = "Show more slots";
BINDPAD_TOOLTIP_SHOW_LESS_SLOT = "Show less slots";

BINDPAD_TEXT_USAGE =
   "Usage: /bindpad [command] or /bp [command] \n"..
      "    /bindpad : Toggle BindPadFrame.\n" ..
      "    /bindpad list : List profiles in saved variables.\n" ..
      "    /bindpad delete REALMNAME_CHARACTERNAME : Delete a profile for the named character.\n"..
      "    /bindpad copyfrom REALMNAME_CHARACTERNAME : Copy a profile from the named character.\n"..
      "    Example: /bp copyfrom Blackrock_foobar";
BINDPAD_TEXT_DO_DELETE = "Successfully deleted profiles for %s.";
BINDPAD_TEXT_DO_DELETE_ERR_CURRENT = "Cannot delete profiles for current character.";
BINDPAD_TEXT_DO_ERR_NOT_FOUND = "Profile for %s is not found.";
BINDPAD_TEXT_DO_COPY = "Successfully duplicated profiles from %s.";
BINDPAD_TEXT_DO_COPY_ERR_CURRENT = "Cannot copy profiles from same character.";

--繁/簡本地化: xinsonic(xinsonic@gmail.com)
if(GetLocale() == "zhTW") then
	BINDING_HEADER_BINDPAD = "BindPad";
	BINDING_NAME_TOGGLE_BINDPAD = "開啟BindPad";
	BINDPAD_TITLE = "BindPad";
	BINDPAD_KEYBINDINGS_TITLE = "按鍵綁定";
	BINDPAD_MACRO_TITLE = "新建 BindPad 巨集";

	BINDPAD_TEXT_GENERAL_TAB = "共用";
	BINDPAD_TEXT_SPECIFIC_TAB = "%s 專用";
	BINDPAD_TEXT_SPECIFIC_EXTRA_TAB2 = "2";
	BINDPAD_TEXT_SPECIFIC_EXTRA_TAB3 = "3";
	BINDPAD_TEXT_EXIT = "離開";
	BINDPAD_TEXT_TEST = "測試";
	BINDPAD_TEXT_UNBIND = "取消綁定";
	BINDPAD_TEXT_PRESSKEY = "請按下您想綁定的按鍵";
	BINDPAD_TEXT_KEY = "目前綁定鍵: ";
	BINDPAD_TEXT_NOTBOUND = "未綁定";
	BINDPAD_TEXT_CONFIRM_BINDING = P.." 已經設為\n"..P.."\n\n您確定綁定要 "..P.." 為 "..P.." 嗎?";
	BINDPAD_TEXT_CANNOT_PLACE = "錯誤: %s 不能被放置在 BindPad 中。";
	BINDPAD_TEXT_CANNOT_BIND = "不能在戰鬥中設置按鍵綁定!"
	BINDPAD_TEXT_OBSOLATED = "舊版本的配置文件已經無法使用並且已經被刪除。很抱歉造成您的不便。";
	BINDPAD_TEXT_ARE_YOU_SURE = "真的? 您確定嗎?";
	BINDPAD_TEXT_CONFIRM_CHANGE_BINDING_PROFILE = "您需要開啟按鍵設定中的角色專用按鍵設定選項。若您想改變按鍵綁定模式請按確定。";
	BINDPAD_TEXT_CONFIRM_CONVERT = "您確認轉換 %s \"%s\" 為 BindPad 巨集?";
	BINDPAD_TEXT_SHOW_HOTKEY = "顯示綁定按鍵文字";
	BINDPAD_TOOLTIP_MACRO = "巨集: ";
	BINDPAD_TOOLTIP_BINDPADMACRO = "BindPad 巨集: %s";
	BINDPAD_TOOLTIP_DOWNRANK = "降低等級到: ";
	BINDPAD_TOOLTIP_KEYBINDING = "按鍵綁定: ";
	BINDPAD_TOOLTIP_UNKNOWN_SPELL = "未知法術: ";
	BINDPAD_TOOLTIP_OPENSPELLBOOK = "打開法術書";
	BINDPAD_TOOLTIP_OPENBAG = "打開所有背包";
	BINDPAD_TOOLTIP_OPENMACRO = "打開巨集指令視窗";
	BINDPAD_TOOLTIP_TAB1 = "共用";
	BINDPAD_TOOLTIP_TAB2 = "%s 專用";
	BINDPAD_TOOLTIP_TAB3 = "%s 專用 2";
	BINDPAD_TOOLTIP_TAB4 = "%s 專用 3";
	BINDPAD_TOOLTIP_SHOW_HOTKEY = "自動在動作條上顯示被 BindPad 綁定過的按鍵的快捷鍵文字.";
	BINDPAD_TOOLTIP_CREATE_MACRO = "新建 BindPad 巨集";
	BINDPAD_TOOLTIP_CLICK_USAGE1 = "右擊以編輯巨集\n左擊以進行綁定";
	BINDPAD_TOOLTIP_CLICK_USAGE2 = "右擊以轉換\n左擊以進行綁定";
end

if(GetLocale() == "zhCN") then
	BINDING_HEADER_BINDPAD = "BindPad";
	BINDING_NAME_TOGGLE_BINDPAD = "开启BindPad";
	BINDPAD_TITLE = "BindPad";
	BINDPAD_KEYBINDINGS_TITLE = "按键绑定";
	BINDPAD_MACRO_TITLE = "创建 BindPad 宏";

	BINDPAD_TEXT_GENERAL_TAB = "共用";
	BINDPAD_TEXT_SPECIFIC_TAB = "%s 专用";
	BINDPAD_TEXT_SPECIFIC_EXTRA_TAB2 = "2";
	BINDPAD_TEXT_SPECIFIC_EXTRA_TAB3 = "3";
	BINDPAD_TEXT_EXIT = "离开";
	BINDPAD_TEXT_TEST = "测试";
	BINDPAD_TEXT_UNBIND = "取消绑定";
	BINDPAD_TEXT_PRESSKEY = "请按下您想绑定的按键";
	BINDPAD_TEXT_KEY = "当前绑定键: ";
	BINDPAD_TEXT_NOTBOUND = "未绑定";
	BINDPAD_TEXT_CONFIRM_BINDING = P.." 已经设为\n"..P.."\n\n您确定绑定要 "..P.." 为 "..P.." 吗?";
	BINDPAD_TEXT_CANNOT_PLACE = "错误: %s 不能被放置在 BindPad 中。";
	BINDPAD_TEXT_CANNOT_BIND = "不能在战斗中设置按键绑定!"
	BINDPAD_TEXT_OBSOLATED = "旧版本的配置文件已经无法使用并且已经被删除。很抱歉造成您的不便。";
	BINDPAD_TEXT_ARE_YOU_SURE = "真的? 您确定吗?";
	BINDPAD_TEXT_CONFIRM_CHANGE_BINDING_PROFILE = "您需要开启按键设定中的角色专用按键设定选项。若您想改变按键绑定模式请按确定。";
	BINDPAD_TEXT_CONFIRM_CONVERT = "您确认转换 %s \"%s\" 为 BindPad 宏?";
	BINDPAD_TEXT_SHOW_HOTKEY = "显示绑定按键文字";
	BINDPAD_TOOLTIP_MACRO = "宏: ";
	BINDPAD_TOOLTIP_BINDPADMACRO = "BindPad 宏: %s";
	BINDPAD_TOOLTIP_DOWNRANK = "降低等级到: ";
	BINDPAD_TOOLTIP_KEYBINDING = "按键绑定: ";
	BINDPAD_TOOLTIP_UNKNOWN_SPELL = "未知法术: ";
	BINDPAD_TOOLTIP_OPENSPELLBOOK = "打开法术书";
	BINDPAD_TOOLTIP_OPENBAG = "打开所有背包";
	BINDPAD_TOOLTIP_OPENMACRO = "打开宏窗口";
	BINDPAD_TOOLTIP_TAB1 = "共用";
	BINDPAD_TOOLTIP_TAB2 = "%s 专用";
	BINDPAD_TOOLTIP_TAB3 = "%s 专用 2";
	BINDPAD_TOOLTIP_TAB4 = "%s 专用 3";
	BINDPAD_TOOLTIP_SHOW_HOTKEY = "自动在动作条上显示被 BindPad 绑定过的按键的快捷键文字。";
	BINDPAD_TOOLTIP_CREATE_MACRO = "创建 BindPad 宏";
	BINDPAD_TOOLTIP_CLICK_USAGE1 = "右击以编辑宏\n左击以进行绑定";
	BINDPAD_TOOLTIP_CLICK_USAGE2 = "右击以转换\n左击以进行绑定";
end
