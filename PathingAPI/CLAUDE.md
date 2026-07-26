# PathingAPI — standalone pathing server

Hosts `PPatherService` over HTTP (+ Swagger, + a Blazor route visualiser) without a game
client attached. Used as **V1 Remote** by the bot, as the bake/authoring host, and as the
map you open at `/Leaflet`.

## Run

```powershell
dotnet run --project PathingAPI -c Release -- --exp=cata
```

| Argument | Meaning |
|---|---|
| `--exp=<client>` | `som`/`tbc`/`wrath`/`cata`/`mop`/`legacy_*`. Defaults to `som`. Decides `/dbc`, `/tiles` and the navmesh era. |
| `--Pathing:Engine=` | `Navmesh` (default) or `SpotAStar` |
| `--bake=<continent>` | background navmesh bake at startup; `all` for every continent |
| `--bake-area=<continent>` | area-id grid + subzone bounds extraction |
| `--urls http://…` | override the port (default 5001) |

## Layout

```
Controllers/PPatherController.cs  the whole HTTP surface (routes, bake, stats, viz)
AnTcp/AnTcpPathServer.cs          AmeisenNavigation binary protocol, opt-in
RateLimit/                        one-at-a-time filter (see below)
Pages/Leaflet.razor               the map page
Hubs/                             SignalR for live route drawing
Startup.cs                        DI, era selection, startup bake hooks
```

## Things that will bite you

**One client at a time.** `RateLimitFilter` is a `static bool isBusy` that answers a
concurrent request with **429**, and the V1 client turns that into an empty route —
indistinguishable from "no path exists". It dates from the SpotAStar era when a search
cost seconds. Do not point several bots at one instance; see the README's
"Running multiple bots".

**The service is a singleton with a two-call search.** `SetLocations` then `DoSearch`
over shared state, plus `PathJitterYards` / `PathEdgeMarginYards` / `lastStartIndoors`.
Anything new that searches must serialize (`AnTcpPathServer` uses a `SemaphoreSlim`).

**Switching continent disposes the engine.** `Initialise(mapId)` calls `Reset()` on a
different map, dropping resident tiles. Two callers on different continents thrash.

**The era comes from `DataConfig.Exp`, never from the URL.** `Pages/Leaflet.razor` used
to trust the `{expansion}` route segment (defaulting to `"som"`), which rendered one
era's art against another's data. It now overrides from the server and logs the
mismatch.

**`Json/MPQ` is not era-aware** — it is one directory for whatever client you point it
at. Running `--exp=cata` with wrath archives in there silently loads wrath geometry and
only fails on a continent wrath lacks. For a different client, build a mirror root of
junctions whose `MPQ` points at that client's `Data`.

**A running instance locks the build output.** Solution builds fail with MSB3027 naming
the pid. Compile-check with `-c Debug` instead of killing the user's server.

**`GET SelfTest` reports what the *active engine* needs, not MPQ.** For `Navmesh` it
counts baked `.dnm` per continent for the active era and returns
`{ok, engine, exp, era, detail, mpqPresent, totalTiles, continents[]}`; for `SpotAStar`
it falls back to the archive check, because that engine has no tile cache. It used to be
`MPQSelfTest()` alone, which reported failure on every tiles-only or CASC install even
though pathing worked perfectly.

## AnTCP (binary, opt-in)

Speaks AmeisenNavigation's protocol so a `RemoteV3`-configured bot can use this server
instead of the external binary. Enable in `appsettings.json`:

```json
"Pathing": { "AnTcp": { "Enabled": true, "Ip": "127.0.0.1", "Port": 47110 } }
```

Wire format, little-endian:

```
request   int32 size(payload+1) | byte type | int32 mapId | int32 flags | float start[3] | float end[3]
response  int32 size(payload+1) | byte type | Vector3[n]        (single all-zero Vector3 = no path)
```

Only `PATH` (type 0) is implemented — it is the only message `RemotePathingAPIV3` ever
sends. Concurrent clients queue rather than 429.

**The z trap:** when the client has no height it sends `area.LocTop / 2`, and `LocTop`
is a world **Y** bound, not a height — for Elwynn that is -3969 against terrain at 82.
AmeisenNavigation absorbs it with a very tall poly-search extent; this engine keeps tight
vertical extents so multi-floor buildings resolve, so `SnapImplausibleZ` puts any z more
than 128 yd off the surface back on it. Do not "fix" this by widening the resolver.
