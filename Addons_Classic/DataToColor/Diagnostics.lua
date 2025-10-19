local Load = select(2, ...)
local DataToColor = unpack(Load)

local format = format

local CreateFrame = CreateFrame
local GetCVarBool = GetCVarBool
local ResetCPUUsage = ResetCPUUsage
local debugprofilestop = debugprofilestop
local UpdateAddOnCPUUsage = UpdateAddOnCPUUsage
local GetAddOnCPUUsage = GetAddOnCPUUsage

local num_frames = 0
local function OnUpdate()
	num_frames = num_frames + 1
end

local fcpu = CreateFrame('Frame')
fcpu:Hide()
fcpu:SetScript('OnUpdate', OnUpdate)

local toggleMode, debugTimer, cpuImpactMessage = false, 0, 'Consumed %sms per frame. Each frame took %sms to render.'
function DataToColor:GetCPUImpact()
	if not GetCVarBool('scriptProfile') then
		DataToColor:Print('For `/dccpu` to work, you need to enable script profiling via: `/console scriptProfile 1` then reload. Disable after testing by setting it back to 0.')
		return
	end

	if not toggleMode then
		ResetCPUUsage()
		toggleMode, num_frames, debugTimer = true, 0, debugprofilestop()
		DataToColor:Print('CPU Impact being calculated, type /dccpu to get results when you are ready.')
		fcpu:Show()
	else
		fcpu:Hide()
		local ms_passed = debugprofilestop() - debugTimer
		UpdateAddOnCPUUsage()

		local per, passed = ((num_frames == 0 and 0) or (GetAddOnCPUUsage('DataToColor') / num_frames)),
			((num_frames == 0 and 0) or (ms_passed / num_frames))
		DataToColor:Print(format(cpuImpactMessage, per and per > 0 and format('%.3f', per) or 0,
			passed and passed > 0 and format('%.3f', passed) or 0))
		toggleMode = false
	end
end
