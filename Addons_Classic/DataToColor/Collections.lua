local Load = select(2, ...)
local DataToColor = unpack(Load)

local GetTime = GetTime

local TimedQueue = {}
DataToColor.TimedQueue = TimedQueue

function TimedQueue:new(tickLifetime, defaultValue)
    local o = {
        head = {}, tail = {}, index = 1, headLength = 0,
        tickLifetime = tickLifetime,
        lastValue = defaultValue,
        lastChangedTick = 0, defaultValue = defaultValue }
    setmetatable(o, self)
    self.__index = self
    return o
end

function TimedQueue:shift(globalTick)
    if math.abs(globalTick - self.lastChangedTick) >= self.tickLifetime or self.lastValue == self.defaultValue then
        if self.index > self.headLength then
            self.head, self.tail = self.tail, self.head
            self.index = 1
            self.headLength = #self.head
            if self.headLength == 0 then
                self.lastValue = self.defaultValue
                return
            end
        end
        local value = self.head[self.index]
        self.head[self.index] = nil
        self.index = self.index + 1

        self.lastValue = value
        self.lastChangedTick = globalTick

        return value
    end

    return self.lastValue
end

function TimedQueue:push(item)
    return table.insert(self.tail, item)
end

function TimedQueue:peek()
    if self.index <= self.headLength then
        return self.head[self.index]
    elseif #self.tail > 0 then
        return self.tail[1]
    end

    return nil
end

local struct = {}
DataToColor.struct = struct

function struct:new(tickLifetime)
    local o = {
        table = {},
        tickLifetime = tickLifetime,
        lastChangedTick = 0,
        lastKey = -1
    }
    setmetatable(o, self)
    self.__index = self
    return o
end

function struct:set(key, value)
    local entry = self.table[key]
    if not entry then
        self.table[key] = { value = value or key, dirty = 0 }
        return
    end

    entry.value = value or key
    entry.dirty = 0
end

function struct:getTimed(globalTick)
    local time = GetTime()
    for k, v in pairs(self.table) do
        if v.dirty == 0 or (v.dirty == 1 and v.value - time <= 0) then
            if self.lastKey ~= k then
                self.lastKey = k
                self.lastChangedTick = globalTick
                --print("changed: ", globalTick, " key:", k, " val: ", v.value)
            end
            return k, v.value
        end
    end
end

function struct:getForced(globalTick)
    for k, v in pairs(self.table) do
        if self.lastKey ~= v.value then
            self.lastKey = v.value
            self.lastChangedTick = globalTick
            --print("forced changed: ", globalTick, " key:", k, " val: ", v.value)
        end
        return k, v.value
    end
end

function struct:forcedReset()
    for _, v in pairs(self.table) do
        v.value = GetTime()
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