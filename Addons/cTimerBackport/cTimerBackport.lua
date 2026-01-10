-- Optimized backport of C_Timer for Cataclysm 4.3.4 (Lua 5.1)
if C_Timer then return end

C_Timer = {}

-------------------------------------------------------------
-- Internal state
-------------------------------------------------------------
local timers = {}
local frame = CreateFrame("Frame")

-------------------------------------------------------------
-- Locals for speed
-------------------------------------------------------------
local tinsert = table.insert

-------------------------------------------------------------
-- Metatable for WoD-style instance methods
-------------------------------------------------------------
local TimerProto = {}
TimerProto.__index = TimerProto

function TimerProto:Cancel()
    for i = 1, #timers do
        if timers[i] == self then
            timers[i] = timers[#timers]
            timers[#timers] = nil
            break
        end
    end
end

-------------------------------------------------------------
-- Core update loop
-------------------------------------------------------------
frame:SetScript("OnUpdate", function(_, elapsed)
    local count = #timers
    local i = 1
    while i <= count do
        local t = timers[i]
        t.remaining = t.remaining - elapsed

        if t.remaining <= 0 then
            -- run callback
            t.callback(t.arg)

            if t.repeating then
                if t.iterations then
                    t.iterations = t.iterations - 1
                    if t.iterations <= 0 then
                        timers[i] = timers[count]
                        timers[count] = nil
                        count = count - 1
                    else
                        t.remaining = t.duration
                        i = i + 1
                    end
                else
                    -- infinite repeating
                    t.remaining = t.duration
                    i = i + 1
                end
            else
                -- one-shot, remove
                timers[i] = timers[count]
                timers[count] = nil
                count = count - 1
            end
        else
            i = i + 1
        end
    end
end)

-------------------------------------------------------------
-- Timer factory
-------------------------------------------------------------
local function NewTimer(seconds, callback, repeating, iterations)
    local t = setmetatable({
        duration   = seconds,
        remaining  = seconds,
        callback   = callback,
        repeating  = repeating,
        iterations = iterations,
    }, TimerProto)
    tinsert(timers, t)
    return t
end

-------------------------------------------------------------
-- Public API
-------------------------------------------------------------
function C_Timer.After(seconds, callback)
    return NewTimer(seconds, callback, false)
end

function C_Timer.NewTicker(seconds, callback, iterations)
    return NewTimer(seconds, callback, true, iterations)
end

function C_Timer.NewTimer(seconds, callback)
    return NewTimer(seconds, callback, false)
end

function C_Timer.Cancel(timer)
    if timer and timer.Cancel then
        timer:Cancel()
    end
end
