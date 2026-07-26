# BlazorServer — the bot, with a web UI

The main entry point. Composes `Core` + `Game` + `Frontend` into a running bot and
serves the browser UI on **:5000**. Almost no logic of its own — if you are changing
behaviour, the change usually belongs in `Core`, and if you are changing a page, in
`Frontend`.

```
Program.cs                  host + DI composition
DependencyInjection.cs      wires Core/Game/Frontend services
MdnsAdvertisingService.cs   advertises http://wowbot.local over mDNS
app.manifest                per-monitor DPI awareness (required for correct capture)
addon_config.json           addon layout, written by the in-app configurator
frame_config.json           screen frame rectangles, written by the configurator
data_config.json            DataConfig Root for this host
appsettings.json            Pathing / Navmesh / SplineFollower options
run.bat / build.bat         opens the browser, then `dotnet run -c Release --no-build`
```

## Running

```powershell
.\build.bat
.\run.bat [-- args]
```

`run.bat` launches `http://localhost:5000` first, then starts the host with
`--no-build`, so **build first** or you run a stale binary.

## Things that will bite you

**The `*.json` config files here are live state, not just settings.**
`addon_config.json` and `frame_config.json` are rewritten by the in-app configurator
against the user's actual screen. Do not hand-edit them expecting them to survive, and
do not treat a diff in them as a code change.

**`data_config.json` decides where all data comes from**, and it is read from the
**process working directory** — so this file, not the repo root's, is what a running
BlazorServer uses. See `DataConfig/CLAUDE.md`.

**DPI awareness is load-bearing.** `app.manifest` declares `PerMonitor` /
`dpiAware=true`. Without it the captured screen rectangle does not match what the addon
draws, and every pixel read is wrong. Do not drop the manifest to "simplify" packaging.

**`out*.log` files litter this directory.** They are gitignored (`*.log`) and are
Serilog output from previous runs — not fixtures. Ignore them when scanning the folder.

**mDNS is best-effort.** `MdnsAdvertisingService` learns the real Kestrel port from the
`ApplicationStarted` event rather than assuming 5000, and hostname comes from
`MDNS_HOSTNAME`. A failure here must never stop the bot starting.

**Stopping must release input before tearing down components.** See `Game/CLAUDE.md` —
ordering at shutdown is how held movement keys get stranded.
