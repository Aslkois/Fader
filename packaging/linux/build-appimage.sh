#!/usr/bin/env bash
# Builds a FADER AppImage into dist/linux/. Run this on Linux only.
#
# FADER's current UI (src/Fader.App) is WPF and its audio engine is WASAPI, both Windows-only.
# This script packages a Linux front-end project, which does not exist yet. Point it at that
# project with FADER_LINUX_PROJECT once it does (default: src/Fader.Linux/Fader.Linux.csproj).
#
#   packaging/linux/build-appimage.sh
#   ARCH=aarch64 packaging/linux/build-appimage.sh
#
# Environment:
#   FADER_LINUX_PROJECT  .csproj of the Linux front-end
#   FADER_EXE_NAME       name of the published executable (default: Fader)
#   ARCH                 x86_64 or aarch64 (default: this machine)
#   VERSION              release version (default: <Version> in Directory.Build.props)
#   APPIMAGETOOL         path to appimagetool (default: appimagetool on PATH)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"

project="${FADER_LINUX_PROJECT:-$root/src/Fader.Linux/Fader.Linux.csproj}"
exe_name="${FADER_EXE_NAME:-Fader}"
arch="${ARCH:-$(uname -m)}"
appimagetool="${APPIMAGETOOL:-appimagetool}"
app_id="io.github.aslkois.Fader"

fail() { echo "error: $*" >&2; exit 1; }

[[ "$(uname -s)" == "Linux" ]] || fail "AppImages must be built on Linux."

case "$arch" in
    x86_64)  rid="linux-x64" ;;
    aarch64) rid="linux-arm64" ;;
    *) fail "unsupported ARCH '$arch' (use x86_64 or aarch64)." ;;
esac

command -v dotnet >/dev/null || fail "dotnet not found. Install the .NET 10 SDK (see packaging/README.md)."
command -v "$appimagetool" >/dev/null || fail "appimagetool not found. Install it or set APPIMAGETOOL (see packaging/README.md)."

if [[ ! -f "$project" ]]; then
    fail "Linux front-end project not found: $project
FADER's UI is currently WPF (Windows-only), so there is nothing to package for Linux yet.
Create a Linux front-end that uses src/Fader.Core, then rerun with FADER_LINUX_PROJECT set."
fi
if grep -q -- '-windows</TargetFramework' "$project"; then
    fail "$project targets Windows. The Linux front-end must target a plain netX.Y framework."
fi

version="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n1)}"
[[ -n "$version" ]] || fail "could not read <Version> from Directory.Build.props; set VERSION."

build="$root/build/appimage"
appdir="$build/FADER.AppDir"
out="$root/dist/linux"
rm -rf "$build"
mkdir -p "$appdir/usr/lib/fader" "$appdir/usr/share/applications" \
         "$appdir/usr/share/metainfo" "$appdir/usr/share/icons/hicolor/256x256/apps" "$out"

echo "Publishing $project for $rid ..."
dotnet publish "$project" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -o "$appdir/usr/lib/fader"

[[ -x "$appdir/usr/lib/fader/$exe_name" ]] || fail "published executable '$exe_name' not found; set FADER_EXE_NAME."

sed "s/@EXE_NAME@/$exe_name/" "$here/AppRun" > "$appdir/AppRun"
chmod +x "$appdir/AppRun"

install -m 644 "$here/$app_id.desktop" "$appdir/$app_id.desktop"
install -m 644 "$here/$app_id.desktop" "$appdir/usr/share/applications/$app_id.desktop"
install -m 644 "$here/fader.png" "$appdir/fader.png"
install -m 644 "$here/fader.png" "$appdir/usr/share/icons/hicolor/256x256/apps/fader.png"
ln -sf fader.png "$appdir/.DirIcon"
sed "s/@VERSION@/$version/; s/@DATE@/$(date -u +%Y-%m-%d)/" \
    "$here/$app_id.metainfo.xml" > "$appdir/usr/share/metainfo/$app_id.metainfo.xml"

target="$out/FADER-$version-$arch.AppImage"
rm -f "$target"
ARCH="$arch" "$appimagetool" "$appdir" "$target"
(cd "$out" && sha256sum "$(basename "$target")" > "$(basename "$target").sha256")

echo
echo "Built $target"
