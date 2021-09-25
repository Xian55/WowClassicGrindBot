local Load = select(2, ...)
local DataToColor = unpack(Load)

-- Automatic Modulo function for Lua 5 and earlier
function DataToColor:Modulo(val, by)
    return val - math.floor(val / by) * by
end

-- This function is able to pass numbers in range 0 to 16777215
function DataToColor:integerToColor(i)
    -- r,g,b are integers in range 0-255
    -- then we turn them into 0-1 range
    return {bit.band(bit.rshift(i,16),255) / 255, bit.band(bit.rshift(i,8),255) / 255, bit.band(i,255) / 255};
end

-- This function is able to pass numbers in range 0 to 9.99999 (6 digits)
-- converting them to a 6-digit integer.
function DataToColor:fixedDecimalToColor(f)
    --[[
    if f > 9.99999 then
        --DataToColor:error("Number too big to be passed as a fixed-point decimal")
        return {0}
    elseif f < 0 then
        return {0}
    end
    --]]
    -- "%f" denotes formatting a string as floating point decimal
    -- The number (.5 in this case) is used to denote the number of decimal places
    --local f6 = tonumber(string.format("%.5f", 1))
    -- Makes number an integer so it can be encoded
    --local i = math.floor(f * 100000)
    return DataToColor:integerToColor(math.floor(f * 100000))
end

-- Returns bitmask values.
-- MakeIndexBase2(1, 4) --> returns 16
-- MakeIndexBase2(0, 9) --> returns 0
function DataToColor:MakeIndexBase2(number, power)
    if number ~= nil and number > 0 then
        return math.pow(2, power)
    end
    return 0
end

function DataToColor:sum24(number)
    return number % 0x1000000
end

-- Pass in a string to get the upper case ASCII values. Converts any special character with ASCII values below 100
function DataToColor:StringToASCIIHex(str)
    -- Converts string to upper case so only 2 digit ASCII values
    -- All lowercase letters have a decimal ASCII value >100, so we only uppercase numbers which are a mere 2 digits long.
    str = string.sub(string.upper(str), 0, 6)
    -- Sets string to an empty string
    local ASCII = ''
    -- Loops through all of string passed to it and converts to upper case ASCII values
    for i = 1, string.len(str) do
        -- Assigns the specific value to a character to then assign to the ASCII string/number
        local c = string.sub(str, i, i)
        -- Concatenation of old string and new character
        ASCII = ASCII .. string.byte(c)
    end
    return tonumber(ASCII)
end



-- Check if two tables are identical
function DataToColor:ValuesAreEqual(t1, t2, ignore_mt)
    local ty1 = type(t1)
    local ty2 = type(t2)
    if ty1 ~= ty2 then return false end
    -- non-table types can be directly compared
    if ty1 ~= 'table' and ty2 ~= 'table' then return t1 == t2 end
    -- as well as tables which have the metamethod __eq
    local mt = getmetatable(t1)
    if not ignore_mt and mt and mt.__eq then return t1 == t2 end
    for k1, v1 in pairs(t1) do
        local v2 = t2[k1]
        if v2 == nil or not DataToColor:ValuesAreEqual(v1, v2) then return false end
    end
    for k2, v2 in pairs(t2) do
        local v1 = t1[k2]
        if v1 == nil or not DataToColor:ValuesAreEqual(v1, v2) then return false end
    end
    return true
end