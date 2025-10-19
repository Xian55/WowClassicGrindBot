--[[
    Collections.lua

    This file provides data structures for managing collections of data over time.
    It is designed for the DataToColor addon.
]]

local Load = select(2, ...)
local DataToColor = unpack(Load)

local GetTime = GetTime

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
local TimedMap = {}
DataToColor.struct = TimedMap -- Assign to old name for backward compatibility

-- Constructor for a new TimedMap
function TimedMap:new(tickLifetime)
    local o = {
        entries = {},           -- The storage for key-value pairs
        tickLifetime = tickLifetime,
        lastChangedTick = 0,
        lastKey = -1
    }
    setmetatable(o, self)
    self.__index = self
    return o
end

-- Sets a value for a key.
function TimedMap:set(key, value)
    local entry = self.entries[key]
    if not entry then
        self.entries[key] = { value = value or key, dirty = 0 }
        return
    end

    entry.value = value or key
    entry.dirty = 0
end

-- Gets a key-value pair that is not dirty or has expired.
function TimedMap:getTimed(globalTick)
    local time = GetTime()
    for k, v in pairs(self.entries) do
        if v.dirty == 0 or (v.dirty == 1 and v.value - time <= 0) then
            if self.lastKey ~= k then
                self.lastKey = k
                self.lastChangedTick = globalTick
            end
            return k, v.value
        end
    end
end

-- Gets a key-value pair, ignoring dirty status.
function TimedMap:getForced(globalTick)
    for k, v in pairs(self.entries) do
        if self.lastKey ~= v.value then
            self.lastKey = v.value
            self.lastChangedTick = globalTick
        end
        return k, v.value
    end
end

-- Resets the value of all entries to the current time.
function TimedMap:forcedReset()
    for _, v in pairs(self.entries) do
        v.value = GetTime()
    end
end

-- Gets the value for a specific key.
function TimedMap:value(key)
    return self.entries[key].value
end

-- Checks if a key exists.
function TimedMap:exists(key)
    return self.entries[key] ~= nil
end

-- Marks an entry as dirty.
function TimedMap:setDirty(key)
    self.entries[key].dirty = 1
end

-- Marks an entry as dirty after a certain time has passed.
function TimedMap:setDirtyAfterTime(key, globalTick)
    if self:exists(key) and math.abs(globalTick - self.lastChangedTick) >= self.tickLifetime then
        self:setDirty(key)
    end
end

-- Checks if an entry is dirty.
function TimedMap:isDirty(key)
    return self.entries[key].dirty == 1
end

-- Removes an entry from the map.
function TimedMap:remove(key)
    self.entries[key] = nil
end

-- Removes an entry if it has expired.
function TimedMap:removeWhenExpired(key, globalTick)
    if self:exists(key) and math.abs(globalTick - self.lastChangedTick) >= self.tickLifetime then
        self:remove(key)
        return true
    end
    return false
end

-- Returns an iterator for the entries.
function TimedMap:iterator()
    return pairs(self.entries)
end