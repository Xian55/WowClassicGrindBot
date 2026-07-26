# Windows on ARM64 (Apple Silicon)

> Running the bot on Apple silicon via a Windows 11 ARM64 VM. Everything here is
> optional — it is only relevant if you are not on an x64 Windows machine.

---

The bot runs inside a **Windows 11 ARM64** guest (for example a Parallels/VMware VM on an Apple Silicon Mac). A default `AnyCPU` build runs as a **native ARM64** process, so the correct native `StormLib` is selected automatically at runtime (`x64` / `x86` / `arm64`) by `NativeLibrary.SetDllImportResolver`.

**Native ARM64 (recommended):**
1. Build (or obtain from a trusted source) `StormLib.dll` for ARM64 from the official [StormLib](https://github.com/ladislav-zezula/StormLib) source and place it as `PPather\MPQ\StormLib_arm64.dll`:
   ```
   git clone https://github.com/ladislav-zezula/StormLib
   cmake -S StormLib -B build -A ARM64 -DBUILD_SHARED_LIBS=ON
   cmake --build build --config Release
   ```
   Verify it is an ARM64 binary (`dumpbin /headers StormLib_arm64.dll` → `machine (AA64)`).
2. Build/run as usual (`dotnet run --project BlazorServer -c Release`). The bot drives a natively ARM64 WoW client (`WowClassic-arm64.exe`) — it reads the screen and sends input, so guest/client architecture do not need to match.

**x64 emulation fallback** (if you cannot build the ARM64 `StormLib` yet):
* Install the **x64** .NET 10 Desktop + ASP.NET Core runtimes (they run under the built-in x64 emulation).
* Run x64 explicitly so the existing `StormLib_x64.dll` is used:
  ```
  dotnet run --project BlazorServer -c Release --arch x64
  ```
* Do **not** hard-code `<PlatformTarget>x64</PlatformTarget>` in the `.csproj` — that would override the native ARM64 path for everyone.

> ⚠️ Do not run prebuilt `StormLib`/DLL patches attached to forum/issue posts by unknown accounts — build the native binary yourself from the official source above.

---

Back to the [README](../README.md).
