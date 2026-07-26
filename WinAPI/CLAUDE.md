# WinAPI — Win32 interop

Three files, no logic. Every P/Invoke the bot needs lives here so the rest of the
solution can stay declarative — and so the Windows dependency stays in one place.

```
NativeMethods.cs     user32 / gdi32 / dwmapi imports (input, window, cursor, DWM, monitors)
ExecutablePath.cs    full path of a running Process
StringOrderUtil.cs   NaturalStringComparer via StrCmpLogicalW ("item2" before "item10")
```

## Rules

**`[LibraryImport]`, not `[DllImport]`.** The assembly declares
`[assembly: DisableRuntimeMarshalling]`, so signatures must be blittable and source
generated. That is why you see `nint` rather than `IntPtr`-with-marshalling, explicit
`[return: MarshalAs(UnmanagedType.Bool)]` on `BOOL` returns, and `Point`/`RECT` structs
passed by `ref` or `out`. Adding a `[DllImport]` here, or a signature with a `string`
that needs marshalling, will not compile the way you expect — pick the `W` entry point
and marshal explicitly.

**Pick the entry point deliberately.** `PostMessage` is bound to `PostMessageA`,
`VkKeyScanExW` and `StrCmpLogicalW` to the wide variants. The suffix is part of the
contract; do not "tidy" it away.

**This project is Windows-only and must stay isolated.** It is referenced by the bot
side (`Core`, `Game`, `BlazorServer`) and **never** by the pathing chain — `DataConfig`,
`SharedLib` and `PPather` are plain `net10.0` precisely so they run on macOS/Linux. If
you find yourself wanting a `WinAPI` reference from any of those, the abstraction
belongs in an interface instead (see `IWowScreen`, `IRectProvider`).

## Areas covered

Input posting (`PostMessage`, `SetCursorPos`/`GetCursorPos`), keyboard-layout
translation (`GetKeyboardLayout`, `MapVirtualKeyA`, `VkKeyScanExW` — used to map a
character to a virtual key on the user's actual layout, so non-US keyboards bind
correctly), window geometry (`GetWindowRect`, `ClientToScreen`/`ScreenToClient`),
DWM-aware bounds (`dwmapi`, so the capture rect excludes the invisible resize border),
cursor inspection (`GetCursorInfo`, `DrawIconEx` for classifying the cursor bitmap) and
monitor lookup (`MonitorFromWindow`).
