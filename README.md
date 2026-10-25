# FADER

A per-application audio mixer for Windows. Set the volume of each app separately, group apps into profiles, and flip the whole mix with one hotkey.

## Features

- **Per-app volume and mute.** Every app that is playing audio gets its own fader and meter, built on WASAPI sessions.
- **Profiles.** A profile sets a volume for a whole category (games, chat, music, browsers, system) or for one specific app. A specific app always wins over its category.
- **LOCK IN.** Applies a profile, such as "game up, everything else down", and puts every level back when you turn it off. It can restore the previous levels, keep the current ones, or ask each time. Optionally it can also push levels back to the profile target if another program changes them.
- **Chat controls.** Mute, raise or lower Discord and similar chat apps without leaving your game. The list of chat apps is editable.
- **Global hotkeys.** Toggle LOCK IN, toggle chat mute, chat volume up and down, restore levels, and show or hide the window.
- **Tray and on-screen feedback.** Lives in the system tray, closes to the tray by default, and shows a small overlay when a hotkey fires.
- **Light and dark themes.** Follows the system theme or can be set manually.

## Requirements

- Windows 10 or 11
- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (see `global.json`)

## Build and run

```powershell
dotnet build Fader.sln
dotnet run --project src/Fader.App
```

## Release build

To build a standalone `Fader.exe` (no .NET install needed) into `dist/windows/`:

```powershell
powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
```

See [packaging/README.md](packaging/README.md) for details, including the prepared Linux AppImage packaging.

## Tests

```powershell
dotnet test
```

The integration tests in `tests/Fader.Tests/Integration` play a very quiet tone on the default output device, so they are skipped by default. To run them, set `FADER_AUDIO_TESTS=1` first:

```powershell
$env:FADER_AUDIO_TESTS = "1"; dotnet test
```

## Project layout

| Project | Purpose |
| --- | --- |
| `src/Fader.Core` | Platform-independent logic: mixer model, profiles, LOCK IN, hotkey bindings, settings, quick actions |
| `src/Fader.Audio` | WASAPI audio engine and device monitoring, using [NAudio](https://github.com/naudio/NAudio) |
| `src/Fader.Native` | Win32 interop: hotkeys, tray icon, message window, icon extraction, startup registration |
| `src/Fader.App` | WPF application: views, view models, themes, tray menu and on-screen display |
| `tests/Fader.Tests` | Unit and integration tests |
| `tools` | Helper scripts, such as `make-icons.ps1` for regenerating the icons |

## License

[MIT](LICENSE)
