# HeadlessServer — the bot without a UI

Same bot as `BlazorServer`, minus the web front end: a console `Exe` driven entirely by
command-line options. Used for running several bots, for unattended runs, and as the
working directory that benchmarks chdir into.

```
Program.cs           config + CommandLineParser + Serilog, then hands off
HeadlessServer.cs    Run / RunLoadOnly
RunOptions.cs        the whole CLI surface
headless_appsettings.json   options (note the prefix - NOT appsettings.json)
data_config.json, addon_config.json, frame_config.json   as per BlazorServer
run.bat / rundev.bat / build.bat / install.bat / loadall.bat
```

## Running

```powershell
.\build.bat
.\run.bat -c Warrior_1.json -m Local -p <wow-pid>
```

`run.bat` passes `--no-build --no-restore`, so **build first** or you run a stale binary.

## Options

| flag | meaning |
|---|---|
| `-c, --classconfig` | class profile from `Json\class\` (e.g. `Warrior_1.json`) |
| `-m, --mode` | `Local` / `RemoteV1` / `RemoteV3` |
| `-p, --pid` | WoW process id — needed when several clients run |
| `-r, --reader` | screen reader backend |
| `--hostv1/--portv1`, `--hostv3/--portv3` | remote pathing endpoints |
| `-n, --viz` | path visualizer |
| `-g, --gpu` | GPU capture |
| `-t/-s/-v` | targeting / skinning / target-vs-add overlays |
| `--loadonly` | load the class config, report, exit — no bot |

`--loadonly` is the cheap validation path: `loadall.bat` uses it to parse every profile
in `Json\class\` and catch a broken one without attaching to the game.

## Things that will bite you

**Config file is `headless_appsettings.json`, not `appsettings.json`.** Copying settings
from `BlazorServer` into the wrong filename here silently changes nothing.

**Options come from both places.** `Program.cs` builds configuration from the json files
**and** `AddCommandLine(args)`, then `CommandLineParser` parses `RunOptions` separately.
Hierarchical keys (`--Pathing:Engine=Navmesh`) reach configuration; the short flags above
reach `RunOptions`. They are two different mechanisms — do not expect one to populate the
other.

**This is the working directory benchmarks assume.** `NavmeshCorpus.SetWorkingDirectory()`
chdirs here so `DataConfig`'s relative `..\json` resolves. Moving or renaming this
project breaks the benchmark harness.

**One bot per process, one process per WoW client.** `-p` exists because `WowProcess`
otherwise picks the first matching client. For several bots, give each its own instance
and its own in-process navmesh — see the README's "Running multiple bots".

**Shutdown must release input.** Same rule as `BlazorServer`; see `Game/CLAUDE.md`.
