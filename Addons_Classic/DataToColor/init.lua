local AceAddon, AceAddonMinor = _G.LibStub('AceAddon-3.0')
local AddOnName, Engine = ...

local CallbackHandler = _G.LibStub('CallbackHandler-1.0')
local E = AceAddon:NewAddon(AddOnName, "AceConsole-3.0", "AceEvent-3.0", "AceTimer-3.0", "AceComm-3.0", "AceSerializer-3.0")

E.callbacks = E.callbacks or CallbackHandler:New(E)
E.C = {} -- Constants
E.C.Spell = {} -- Spells
E.C.MIRRORTIMER = {} -- Mirror Timers
E.C.ActionType = {} -- GetActionType
E.C.Loot = {} -- Loot

E.S = {} -- Storage
E.R = {} -- Runtime

Engine[1] = E
_G[AddOnName] = E

do
	E.Libs = {}
	E.LibsMinor = {}
	function E:AddLib(name, major, minor)
		if not name then return end

		-- in this case: `major` is the lib table and `minor` is the minor version
		if type(major) == 'table' and type(minor) == 'number' then
			E.Libs[name], E.LibsMinor[name] = major, minor
		else -- in this case: `major` is the lib name and `minor` is the silent switch
			E.Libs[name], E.LibsMinor[name] = _G.LibStub(major, minor)
		end
	end

	E:AddLib('AceAddon', AceAddon, AceAddonMinor)

	local ver = select(4, GetBuildInfo())
	if ver <= 11400 then
		--print('load 2.0')
		E:AddLib('RangeCheck', 'LibRangeCheck-2.0')
	else
		--print('load 3.0')
		E:AddLib('RangeCheck', 'LibRangeCheck-3.0')
	end
end