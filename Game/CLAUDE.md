# Game — the game process and input

Everything that touches the running WoW process: finding it, sending it keys and mouse
events, and the screen-capture interfaces. Small, but this is where a bug moves the
player's character, so it gets more care than its size suggests.

```
WoWProcess/WowProcess.cs      finds the process, reads its path + FileVersion
Input/IInput.cs               keyboard contract
Input/IMouseInput.cs          mouse contract
Input/InputWindowsNative.cs   the Win32 implementation (PostMessage based)
Input/WowProcessInput.cs      the layer everything else uses; tracks what is held
Input/InputDuration.cs        press-duration constants
WoWScreen/IWowScreen.cs       capture contract (implementations live in Core/WinAPI)
```

## Input is stateful — that is the whole risk

`WowProcessInput` keeps a `keysDown` belief and **short-circuits on it**: `KeyDown`
returns early if it thinks the key is already down, and `KeyUp` returns early if it
thinks it is already up (unless `forced: true`). Two consequences that have both caused
real bugs:

* **Clearing the belief without releasing strands the key.** The game keeps holding it,
  and because `KeyUp` now believes it is up, the release can never be issued — a held
  movement key runs the character on forever. `Reset()` releases first, *then* clears,
  and releases the four movement keys **unconditionally** because a key the user pressed
  themselves is exactly the case the belief cannot describe.
* **An early `return` on a stop/exit path skips the release.** Runaway forward movement
  and endless jumping have both come from this. When adding a bail-out to a goal or an
  input path, make the release unconditional (`forced: true`) rather than conditional on
  believed state.

Ordering matters too: whoever stops the agent must release keys before tearing down the
components that would have released them.

## Other things worth knowing

**Input goes through `PostMessage`, not `SendInput`.** Keys are posted to the WoW window
handle, so the game does not need foreground focus and other windows are unaffected.
That also means modifiers are posted as explicit `WM_KEYDOWN`/`WM_KEYUP` pairs around
the key, and extended-key lParam flags have to be built by hand — see
`MakeKeyDownLParam`/`MakeKeyUpLParam`.

**Press durations are randomised.** `PressRandom` adds `Random.Shared.Next(maxDelay)` to
the requested duration. Do not "fix" a flaky timing test by removing that.

**`WowProcess` matches a list of known executable names** and throws at construction when
none is running, so it is a hard dependency for anything in the bot path — that is why
`PathingAPI` and the utilities do not reference this project.

**Screen capture is an interface here, implemented elsewhere.** `IWowScreen` /
`IGpuTextureProvider` stay abstract so `SharedLib` and the pathing chain remain
platform-neutral; the WGC/DXGI implementations live in `Core/WoWScreen` and `WinAPI`.
