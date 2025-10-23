-- Backport SOUNDKIT for pre-WoD clients (Cataclysm 4.3.4)
if SOUNDKIT then return end

setmetatable(_G, {
    __index = function(_, key)
        if key == "SOUNDKIT" then
            local t = setmetatable({}, {
                __index = function(_, soundName)
                    return string.lower(soundName)
                end
            })
            rawset(_G, "SOUNDKIT", t)
            return t
        end
    end
})
