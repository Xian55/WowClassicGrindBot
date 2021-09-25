local Load = select(2, ...)
local DataToColor = unpack(Load)

DataToColor.C.unitPlayer = "player"
DataToColor.C.unitTarget = "target"
DataToColor.C.unitPet = "pet"
DataToColor.C.unitPetTarget = "pettarget"
DataToColor.C.unitTargetTarget = "targettarget"
DataToColor.C.unitNormal = "normal"

-- Creature Types
DataToColor.C.Humanoid = "Humanoid"
DataToColor.C.Elemental = "Elemental"
DataToColor.C.Mechanical = "Mechanical"
DataToColor.C.Totem = "Totem"

-- Character's name
DataToColor.C.CHARACTER_NAME = UnitName(DataToColor.C.unitPlayer)
DataToColor.C.CHARACTER_GUID = UnitGUID(DataToColor.C.unitPlayer)
_, DataToColor.C.CHARACTER_CLASS = UnitClass(DataToColor.C.unitPlayer)

-- Actionbar power cost
DataToColor.C.MAX_POWER_TYPE = 1000000
DataToColor.C.MAX_ACTION_IDX = 1000

-- Spells
DataToColor.C.Spell.AutoShot = "Auto Shot"
DataToColor.C.Spell.Shoot = "Shoot"

-- Item / Inventory
DataToColor.C.ItemPattern = "(m:%d+)"