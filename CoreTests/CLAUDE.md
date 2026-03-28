# CoreTests

## Build

```powershell
.\build.ps1
```

## Run

The project uses `<OutputType>WinExe</OutputType>` which detaches stdout from the terminal.
`run.ps1` handles `--no-build`, `-c Release`, and `| Out-Host` automatically.

```powershell
.\run.ps1 [global flags] <suite> [suite args]
```

## Global flags

| Flag | Description |
|------|-------------|
| `--log-times` | Enable overall timing statistics |
| `--no-gpu` | Disable GPU acceleration |
| `--no-log-update` | Disable per-iteration update logging in NPC tests |
| `--dxgi` | Use DXGI screen capture instead of WGC (default) |
| `--delay <ms>` | Set delay in milliseconds between iterations (default: 150) |

## Suites

### Help

```powershell
.\run.ps1
```

### npc - NPC Name Finder

Args: `[NpcNames...] [count]`

Defaults (Friendly|Neutral, 100 iterations):
```powershell
.\run.ps1 npc
```

Enemy only:
```powershell
.\run.ps1 npc enemy
```

Enemy + Neutral with 10000 iterations:
```powershell
.\run.ps1 npc enemy neutral 10000
```

All types with timing stats:
```powershell
.\run.ps1 --log-times npc enemy friendly neutral corpse nameplate
```

Without GPU:
```powershell
.\run.ps1 --no-gpu npc
```

Stats only (no per-iteration logging), GPU, default delay:
```powershell
.\run.ps1 --log-times --no-log-update --delay 150 npc friendly neutral 500
```

Stats only, CPU (no GPU), default delay:
```powershell
.\run.ps1 --log-times --no-log-update --no-gpu --delay 150 npc friendly neutral 500
```

Stats only, fast iterations (10ms delay):
```powershell
.\run.ps1 --log-times --no-log-update --delay 10 npc friendly neutral 500
```

GPU vs CPU comparison (run both, compare stats):
```powershell
.\run.ps1 --log-times --no-log-update npc enemy neutral 1000
.\run.ps1 --log-times --no-log-update --no-gpu npc enemy neutral 1000
```

DXGI capture with stats:
```powershell
.\run.ps1 --log-times --no-log-update --dxgi npc friendly neutral 500
```

WGC vs DXGI comparison:
```powershell
.\run.ps1 --log-times --no-log-update npc friendly neutral 500
.\run.ps1 --log-times --no-log-update --dxgi npc friendly neutral 500
```

All enemy types, high iteration count:
```powershell
.\run.ps1 --log-times --no-log-update npc enemy 10000
```

All types combined:
```powershell
.\run.ps1 --log-times --no-log-update npc enemy friendly neutral corpse nameplate 1000
```

NpcNames values: `Enemy`, `Friendly`, `Neutral`, `Corpse`, `NamePlate`

### input - Mouse & Keyboard Input

```powershell
.\run.ps1 input
```

### cursor-grab - Cursor Type Classification

```powershell
.\run.ps1 cursor-grab
```

### cursor-compare - Cursor Classification Performance

```powershell
.\run.ps1 cursor-compare
```

### minimap - Minimap Node Finder

Default (100 samples):
```powershell
.\run.ps1 minimap
```

With timing stats:
```powershell
.\run.ps1 --log-times minimap
```

### find-target - Find Target By Cursor

Args: `[NpcNames...]`

Defaults (Friendly|Neutral):
```powershell
.\run.ps1 find-target
```

Enemy targets:
```powershell
.\run.ps1 find-target enemy
```

Enemy + Neutral:
```powershell
.\run.ps1 find-target enemy neutral
```

### pather - PPather Pathfinding

Args: `[expansion]`

Defaults to SoM:
```powershell
.\run.ps1 pather
```

TBC expansion:
```powershell
.\run.ps1 pather tbc
```

Wrath expansion:
```powershell
.\run.ps1 pather wrath
```

Expansion values: `SoM`, `TBC`, `Wrath`, `Cata`, `Mop`, `Retail`
