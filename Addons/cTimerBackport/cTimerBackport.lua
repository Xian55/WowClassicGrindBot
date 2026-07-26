-- Backport of C_Timer for legacy clients (Cataclysm 4.3.4, MoP 5.4.8 - Lua 5.1).
-- C_Timer arrived in 6.0; DataToColor's Mail.lua / SellJunk.lua / SetupDefaultBindings.lua
-- need it, and declare this addon in ## OptionalDeps so it loads first.
if C_Timer then return end

C_Timer = {}

-------------------------------------------------------------
-- Internal state
-------------------------------------------------------------
local timers = {}
local frame = CreateFrame("Frame")

local tremove = table.remove

-------------------------------------------------------------
-- WoD-style instance methods
-------------------------------------------------------------
local TimerProto = {}
TimerProto.__index = TimerProto

function TimerProto:Cancel()
    self.cancelled = true
end

function TimerProto:IsCancelled()
    return self.cancelled == true
end

-------------------------------------------------------------
-- Core update loop
--
-- Iterates BACKWARDS and removes with table.remove rather than the swap-with-last
-- trick. Both details matter, because callbacks routinely mutate this list:
--
--   * A callback may schedule another timer (Mail.lua's Tick re-arms itself with
--     C_Timer.After). Appends land at the end, i.e. at indices this backwards pass
--     has already gone by, so a new timer cannot fire in the same frame it was
--     created and cannot shift anything still to be visited.
--   * A callback may Cancel a timer, including its own.
--
-- The previous version cached `local count = #timers` up front and swap-removed
-- against it. Once a callback appended, that cached count pointed at the wrong slot:
-- removing the last live timer self-assigned and then nil'd it, leaving the appended
-- one stranded at a higher index with a hole below it. The next frame then read
-- timers[1] == nil while #timers still reported 2, and every subsequent frame threw
-- "attempt to index local 't' (a nil value)" - hundreds of times, because OnUpdate
-- runs continuously.
--
-- Going backwards over a freshly-read #timers keeps the array hole-free, so no
-- defensive nil check is needed.
-------------------------------------------------------------
frame:SetScript("OnUpdate", function(_, elapsed)
    for i = #timers, 1, -1 do
        local t = timers[i]

        if t.cancelled then
            tremove(timers, i)
        else
            t.remaining = t.remaining - elapsed

            if t.remaining <= 0 then
                local fire = true

                -- Decide this timer's fate BEFORE running it: a callback that
                -- cancels or reschedules must not be undone afterwards, and one
                -- that errors must not leave an expired entry firing every frame.
                if t.repeating then
                    if t.iterations then
                        t.iterations = t.iterations - 1
                        if t.iterations <= 0 then
                            tremove(timers, i)
                        else
                            t.remaining = t.remaining + t.duration
                        end
                    else
                        t.remaining = t.remaining + t.duration
                    end
                else
                    tremove(timers, i)
                end

                if fire then
                    -- Blizzard hands the ticker to its callback.
                    t.callback(t)
                end
            end
        end
    end
end)

-------------------------------------------------------------
-- Timer factory
-------------------------------------------------------------
local function NewTimer(seconds, callback, repeating, iterations)
    local t = setmetatable({
        duration   = seconds or 0,
        remaining  = seconds or 0,
        callback   = callback,
        repeating  = repeating,
        iterations = iterations,
        cancelled  = false,
    }, TimerProto)

    timers[#timers + 1] = t
    return t
end

-------------------------------------------------------------
-- Public API
-------------------------------------------------------------
function C_Timer.After(seconds, callback)
    NewTimer(seconds, callback, false)
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
