# Packaging

Release builds go to `dist/`, which is not committed. Attach the files there to a GitHub release instead.

## Windows

Produces one self-contained `Fader.exe` for Windows 10/11 x64. Users do not need .NET installed.

**Requirements:** Windows, [.NET SDK 10.0](https://dotnet.microsoft.com/download).

```powershell
powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
```

This runs the tests, then publishes with `src/Fader.App/Properties/PublishProfiles/win-x64.pubxml` into `dist/windows/`:

| File | |
| --- | --- |
| `Fader.exe` | The app: .NET runtime, WPF and all dependencies in one file (about 60 MB) |
| `Fader.exe.sha256` | Checksum |
| `LICENSE.txt` | MIT license |

Pass `-SkipTests` to skip the test run. The script uses `%USERPROFILE%\.dotnet\dotnet.exe` if it exists, otherwise `dotnet` from `PATH`.

Notes:

- On first launch the exe extracts WPF's native libraries to `%TEMP%\.net\Fader\`. This is normal for single-file WPF apps.
- Settings are stored in `%APPDATA%\Fader\`, as with a development build.
- The exe is not code-signed, so SmartScreen may warn on first run.
- Trimming is off because WPF does not support it.

## Linux (AppImage)

> **Status: prepared, not tested.** No AppImage has been built yet.

The current UI (`src/Fader.App`) is WPF and the audio engine (`src/Fader.Audio`) uses WASAPI. Both are Windows-only, and so is `src/Fader.Native`. Only `src/Fader.Core` is cross-platform: it targets plain `net10.0` and builds for `linux-x64`.

A Linux build therefore needs a Linux front-end project first. It needs:

- a cross-platform UI, such as Avalonia,
- an `IAudioEngine` implementation for PulseAudio or PipeWire,
- Linux versions of the tray icon, global hotkeys and autostart.

`packaging/linux/build-appimage.sh` packages that project. It expects it at `src/Fader.Linux/Fader.Linux.csproj`; set `FADER_LINUX_PROJECT` to use another path. Until the project exists, the script stops with an explanation.

### Files

| File | Purpose |
| --- | --- |
| `linux/build-appimage.sh` | Publishes self-contained, builds the AppDir, runs `appimagetool` |
| `linux/AppRun` | AppImage entry point |
| `linux/io.github.aslkois.Fader.desktop` | Desktop entry |
| `linux/io.github.aslkois.Fader.metainfo.xml` | AppStream metadata; the script fills in version and date |
| `linux/fader.png` | 256×256 icon, taken from `assets/fader.ico` |

### Build dependencies

- Linux x86_64 or aarch64
- .NET SDK 10.0: use your distro's packages or [dotnet-install.sh](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script):

  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
  ```

- [appimagetool](https://github.com/AppImage/appimagetool/releases), on `PATH` or set with `APPIMAGETOOL`:

  ```bash
  curl -L -o ~/.local/bin/appimagetool https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x ~/.local/bin/appimagetool
  ```

- `libfuse2` to run appimagetool and the AppImage. The package is `libfuse2` on Debian/Ubuntu (`libfuse2t64` on Ubuntu 24.04+) and `fuse-libs` on Fedora. Without FUSE, set `APPIMAGE_EXTRACT_AND_RUN=1`.
- Optional checks: `desktop-file-utils` (`desktop-file-validate`) and `appstream` (`appstreamcli validate`).

Users need `libicu` at runtime; most distros ship it. If the front-end sets `InvariantGlobalization=true`, it is not needed.

### Build

```bash
chmod +x packaging/linux/build-appimage.sh
packaging/linux/build-appimage.sh
```

Output: `dist/linux/FADER-<version>-<arch>.AppImage` plus a `.sha256`. The script uses these variables:

| Variable | Default |
| --- | --- |
| `FADER_LINUX_PROJECT` | `src/Fader.Linux/Fader.Linux.csproj` |
| `FADER_EXE_NAME` | `Fader` |
| `ARCH` | `uname -m` (`x86_64` or `aarch64`) |
| `VERSION` | `<Version>` in `Directory.Build.props` |
| `APPIMAGETOOL` | `appimagetool` |

To check the metadata before release:

```bash
desktop-file-validate packaging/linux/io.github.aslkois.Fader.desktop
appstreamcli validate --no-net build/appimage/FADER.AppDir/usr/share/metainfo/io.github.aslkois.Fader.metainfo.xml
```
