---
name: lua-wow-performance
description: Lua 5.1 performance guidelines for World of Warcraft addon development
user-invocable: false
---

# Lua 5.1 Performance Guidelines

**Goal:** Minimize memory allocations and optimize execution time.

## Local Variable Caching (Critical)
Cache global lookups at file scope - global access is slow:
```lua
-- DO: Cache at file scope
local floor = math.floor
local band = bit.band
local UnitHealth = UnitHealth
local GetTime = GetTime

-- DON'T: Access globals in hot paths
local function update()
    return math.floor(GetTime())  -- Two global lookups per call
end
```

## Avoid Table Allocations in Loops
```lua
-- DON'T: Creates new table every call
local function getData()
    return { x = 1, y = 2 }
end

-- DO: Reuse pre-allocated tables
local dataCache = { x = 0, y = 0 }
local function getData()
    dataCache.x = 1
    dataCache.y = 2
    return dataCache
end
```

## String Operations
```lua
-- DON'T: String concatenation creates garbage
local msg = "Player: " .. name .. " HP: " .. hp

-- DO: Use string.format (single allocation)
local msg = string.format("Player: %s HP: %d", name, hp)

-- BETTER for hot paths: Avoid string creation entirely
```

## Table Pooling for Temporary Objects
```lua
-- Reuse tables instead of creating/discarding
local pool = {}
local function acquire()
    return table.remove(pool) or {}
end
local function release(t)
    wipe(t)  -- WoW API: clears table without dealloc
    pool[#pool + 1] = t
end
```

## Numeric Operations
```lua
-- Use locals for repeated calculations
local x = someValue
local x2 = x * x  -- Reuse intermediate results

-- Prefer multiplication over division
local half = x * 0.5  -- Faster than x / 2

-- Use bit operations for powers of 2
local doubled = bit.lshift(x, 1)  -- x * 2
local halved = bit.rshift(x, 1)   -- x / 2 (integer)
```

## Loop Optimization
```lua
-- Cache length outside loop
local len = #items
for i = 1, len do
    -- use items[i]
end

-- Use numeric for-loops over pairs/ipairs when possible
-- pairs/ipairs create iterator closures

-- Avoid function calls in loop conditions
for i = 1, GetNumItems() do  -- DON'T: calls every iteration
```

## Closure Avoidance
```lua
-- DON'T: Creates new function every call
local function setup(callback)
    frame:SetScript("OnUpdate", function() callback() end)
end

-- DO: Define functions once at file scope
local function onUpdate()
    -- implementation
end
frame:SetScript("OnUpdate", onUpdate)
```

## WoW-Specific Optimizations
```lua
-- Use C_Timer.After instead of OnUpdate for delayed actions
-- Throttle OnUpdate handlers (don't run every frame)
local elapsed = 0
local THROTTLE = 0.1
local function onUpdate(self, dt)
    elapsed = elapsed + dt
    if elapsed < THROTTLE then return end
    elapsed = 0
    -- actual work
end

-- Batch API calls when possible
-- Cache UnitGUID results when target doesn't change
```

## Memory-Critical Patterns
- Never create tables in `OnUpdate` handlers
- Pre-size arrays with known sizes: `local t = {nil, nil, nil, nil}`
- Use `wipe(t)` instead of `t = {}` to reuse table memory
- Avoid varargs (`...`) in hot paths - they allocate
- Use `select(n, ...)` sparingly - prefer indexed access
