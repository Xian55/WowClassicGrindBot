--[[
    Collections.lua

    This file provides data structures for managing collections of data over time.
    It is designed for the DataToColor addon.
]]

local Load = select(2, ...)
local DataToColor = unpack(Load)

local GetTime = GetTime
local next = next
local pairs = pairs

--------------------------------------------------------------------------------
-- TimedQueue
-- A queue that releases one item at a time, after a specified tick lifetime.
-- This is used to iterate over a collection of items across multiple frames.
--------------------------------------------------------------------------------
local TimedQueue = {}
DataToColor.TimedQueue = TimedQueue

-- Constructor for a new TimedQueue
function TimedQueue:new(tickLifetime, defaultValue)
    local o = {
        head = {},              -- The current batch of items to process
        tail = {},              -- The next batch of items
        index = 1,              -- The current position in the head
        headLength = 0,         -- The number of items in the head
        tickLifetime = tickLifetime, -- How many ticks an item stays as the current value
        lastValue = defaultValue, -- The last value shifted from the queue
        lastChangedTick = 0,    -- The tick when the last value was changed
        defaultValue = defaultValue -- The value to return when the queue is empty
    }
    setmetatable(o, self)
    self.__index = self
    return o
end

-- Shifts an item from the queue if the lifetime has expired.
-- Otherwise, returns the last shifted item.
function TimedQueue:shift(globalTick)
    -- Check if it's time to get a new item
    if math.abs(globalTick - self.lastChangedTick) >= self.tickLifetime or self.lastValue == self.defaultValue then
        -- If we've processed all items in the head, swap with the tail
        if self.index > self.headLength then
            self.head, self.tail = self.tail, self.head
            self.index = 1
            self.headLength = #self.head
            -- If the new head is empty, we're done for now
            if self.headLength == 0 then
                self.lastValue = self.defaultValue
                return
            end
        end

        local value = self.head[self.index]
        self.head[self.index] = nil -- Clear the value from the old table
        self.index = self.index + 1

        self.lastValue = value
        self.lastChangedTick = globalTick

        return value
    end

    return self.lastValue
end

-- Adds an item to the tail of the queue.
function TimedQueue:push(item)
    return table.insert(self.tail, item)
end

-- Clears all items from the queue and resets state.
function TimedQueue:clear()
    self.head = {}
    self.tail = {}
    self.index = 1
    self.headLength = 0
    self.lastValue = self.defaultValue
    self.lastChangedTick = 0
end

-- Peeks at the next item to be shifted without actually shifting it.
function TimedQueue:peek()
    if self.index <= self.headLength then
        return self.head[self.index]
    elseif #self.tail > 0 then
        return self.tail[1]
    end

    return nil
end

--------------------------------------------------------------------------------
-- TimedMap
-- A map-like structure where entries can be marked as "dirty" and have a
-- time-based component for retrieval.
--------------------------------------------------------------------------------
local struct = {}
DataToColor.struct = struct -- Assign to old name for backward compatibility

-- Constructor for a new TimedMap
function struct:new(tickLifetime)
    local o = {
        entries = {},           -- The storage for key-value pairs
        tickLifetime = tickLifetime,
        lastChangedTick = 0,
        lastKey = -1
    }
    -- For backward compatibility, also provide .table as alias to .entries
    o.table = o.entries
    setmetatable(o, self)
    self.__index = self
    return o
end

-- Sets a value for a key.
function struct:set(key, value)
    local entry = self.entries[key]
    if not entry then
        self.entries[key] = { value = value or key, dirty = 0 }
        return
    end

    entry.value = value or key
    entry.dirty = 0
end

-- Gets a key-value pair that is not dirty or has expired.
-- Uses next() directly to avoid pairs() iterator allocation
function struct:getTimed(globalTick)
    local time = GetTime()
    local entries = self.entries
    local k, v = next(entries)
    while k do
        if v.dirty == 0 or (v.dirty == 1 and v.value - time <= 0) then
            if self.lastKey ~= k then
                self.lastKey = k
                self.lastChangedTick = globalTick
            end
            return k, v.value
        end
        k, v = next(entries, k)
    end
end

-- Gets a key-value pair, ignoring dirty status.
-- Uses next() directly to avoid pairs() iterator allocation
function struct:getForced(globalTick)
    local k, v = next(self.entries)
    if k then
        if self.lastKey ~= v.value then
            self.lastKey = v.value
            self.lastChangedTick = globalTick
            --print("forced changed: ", globalTick, " key:", k, " val: ", v.value)
        end
        return k, v.value
    end
end

-- Uses next() directly to avoid pairs() iterator allocation
function struct:forcedReset()
    local t = self.table
    local time = GetTime()
    local k, v = next(t)
    while k do
        v.value = time
        k, v = next(t, k)
    end
end

function struct:value(key)
    return self.table[key].value
end

function struct:exists(key)
    return self.table[key] ~= nil
end

function struct:setDirty(key)
    self.table[key].dirty = 1
end

function struct:setDirtyAfterTime(key, globalTick)
    if self:exists(key) and math.abs(globalTick - self.lastChangedTick) >= self.tickLifetime then
        self:setDirty(key)
    end
end

function struct:isDirty(key)
    return self.table[key].dirty == 1
end

function struct:remove(key)
    self.table[key] = nil
end

function struct:removeWhenExpired(key, globalTick)
    if self:exists(key) and math.abs(globalTick - self.lastChangedTick) >= self.tickLifetime then
        self:remove(key)
        return true
    end
    return false
end

function struct:iterator()
    return pairs(self.table)
end

-- Returns the underlying table for direct next() iteration (avoids pairs() allocation)
function struct:getTable()
    return self.table
end