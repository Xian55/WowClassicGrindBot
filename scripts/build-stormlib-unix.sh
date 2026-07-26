#!/usr/bin/env bash
# Build StormLib as a shared library for macOS / Linux and drop it where
# StormDll.Resolve looks for it (<output>/MPQ/libstorm.{dylib,so}).
#
# Windows ships prebuilt StormLib_{x64,x86,arm64}.dll in PPather/MPQ/. Those are
# Windows PEs - including the arm64 one, which is Windows-on-ARM, not macOS - so
# a native bake on macOS/Linux needs this.
#
# IMPORTANT - do NOT add -DSTORM_UNICODE=ON here. That flag is Windows-only. On
# other platforms StormPort.h does `typedef char TCHAR`, so the archive-path
# arguments are UTF-8 `char*`; StormDll marshals them accordingly (see the
# SFileOpenArchive/SFileOpenPatchArchive wrappers). Mismatching the two fails
# SILENTLY - every archive simply "does not exist".
#
# Requires: cmake, a C++ toolchain (macOS: Xcode Command Line Tools,
#           `xcode-select --install`), git.
#
# Usage:  scripts/build-stormlib-unix.sh [install-dir]
#   install-dir defaults to PPather/MPQ so `dotnet build` copies it to output.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL_DIR="${1:-$REPO_ROOT/PPather/MPQ}"
WORK="${TMPDIR:-/tmp}/stormlib-build"

case "$(uname -s)" in
    Darwin) LIB_NAME="libstorm.dylib"; EXTRA_CMAKE=(-DCMAKE_OSX_ARCHITECTURES="$(uname -m)") ;;
    Linux)  LIB_NAME="libstorm.so";    EXTRA_CMAKE=() ;;
    *)      echo "This script is for macOS/Linux. Windows uses the prebuilt DLLs." >&2; exit 1 ;;
esac

for tool in cmake git; do
    command -v "$tool" >/dev/null 2>&1 || { echo "missing required tool: $tool" >&2; exit 1; }
done

mkdir -p "$WORK"
if [ ! -d "$WORK/StormLib/.git" ]; then
    echo "==> cloning StormLib"
    git clone --depth 1 https://github.com/ladislav-zezula/StormLib.git "$WORK/StormLib"
else
    echo "==> reusing $WORK/StormLib"
fi

if [ "$(uname -s)" = "Darwin" ]; then
    # Two upstream quirks in StormLib's APPLE shared-library branch:
    #
    #  1. `target_link_options(storm PRIVATE "-framework Carbon")` passes that as a
    #     SINGLE argument, which current clang rejects outright
    #     ("unknown argument: '-framework Carbon'"). Carbon is not needed to read
    #     MPQs, so drop it rather than splitting it.
    #  2. FRAMEWORK TRUE makes the output storm.framework/storm. Harmless but
    #     awkward - a plain libstorm.<ver>.dylib is what the resolver wants.
    #
    # Patched in the build copy only; upstream is left untouched.
    CML="$WORK/StormLib/CMakeLists.txt"
    if grep -q 'framework Carbon' "$CML"; then
        echo "==> patching StormLib's APPLE branch (Carbon link flag / framework output)"
        perl -0pi -e 's/^(\s*)(set_target_properties\(\$\{LIBRARY_NAME\} PROPERTIES FRAMEWORK TRUE\))/$1# disabled by build-stormlib-unix.sh: $2/m' "$CML"
        perl -0pi -e 's/^(\s*)(target_link_options\(\$\{LIBRARY_NAME\} PRIVATE "-framework Carbon"\))/$1# disabled by build-stormlib-unix.sh: $2/m' "$CML"
        grep -n 'disabled by build-stormlib-unix' "$CML" || true
    fi
fi

echo "==> configuring"
# STORM_USE_BUNDLED_LIBRARIES: StormLib vendors libtommath/libtomcrypt. Without
# this it hunts for system copies and ends up requiring pkg-config, which is not
# present in a bare Xcode Command Line Tools install.
cmake -S "$WORK/StormLib" -B "$WORK/build" \
    -DBUILD_SHARED_LIBS=ON \
    -DSTORM_USE_BUNDLED_LIBRARIES=ON \
    -DSTORM_SKIP_INSTALL=ON \
    -DCMAKE_BUILD_TYPE=Release \
    "${EXTRA_CMAKE[@]}"

echo "==> building"
cmake --build "$WORK/build" --config Release -j "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"

# Locate the built Mach-O/ELF. Two shapes to handle:
#  - Linux (and a plain macOS build): libstorm.so.<ver> / libstorm.<ver>.dylib
#  - macOS: StormLib's CMakeLists sets FRAMEWORK TRUE for APPLE shared builds, so
#    the output is storm.framework/storm with no extension. That file is still an
#    ordinary dylib, so dlopen loads it fine once copied under a .dylib name.
BUILT="$(find "$WORK/build" -type f \( -name 'libstorm*.dylib' -o -name 'libstorm.so*' \) 2>/dev/null | head -1)"
if [ -z "$BUILT" ]; then
    BUILT="$(find "$WORK/build" -type f -path '*storm.framework*' -name 'storm' 2>/dev/null | head -1)"
fi
[ -n "$BUILT" ] || { echo "build produced no shared library under $WORK/build" >&2; exit 1; }
echo "==> built $BUILT"

mkdir -p "$INSTALL_DIR"
cp -f "$BUILT" "$INSTALL_DIR/$LIB_NAME"

echo
echo "==> installed $INSTALL_DIR/$LIB_NAME"
file "$INSTALL_DIR/$LIB_NAME" || true
echo
echo "Next: dotnet build Utilities/BakeTool/BakeTool.csproj -c Release"
echo "The csproj copies PPather/MPQ/* to the output MPQ/ folder that"
echo "StormDll.Resolve probes."
