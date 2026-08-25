# Enput Method

Enput Method is a prototype Windows English input method built with the Text Services Framework (TSF). It is registered as a Windows input profile and can be selected alongside system and third-party input methods.

## Features

- System-level TSF input profile in the Chinese input-method group
- Floating candidate window with up to four English suggestions
- `1` through `4` select the corresponding candidate; `Tab` selects the first
- User-editable JSON configuration, external dictionary, and four candidate-window themes
- `Space` preserves the typed prefix and starts the next word
- Uses the existing Windows `Ctrl + Shift` Chinese-group switching behavior

## Requirements

- Windows 10 or Windows 11, x64
- Visual Studio 2022 with Desktop development with C++
- .NET 9 SDK
- Administrator approval during installation

## Build

Open `EnputMethod.sln` in Visual Studio, select `Release|x64`, and build the solution. The installer builds the native TSF DLL with a static C++ runtime before copying it to its output directory.

## Install and Use

Run `EnputMethod.Installer.exe` to install or `EnputMethod.Uninstaller.exe` to remove it. Both programs open a window, request UAC approval, perform the operation only after the user clicks its button, display the result, and close after the result is confirmed.

Run each executable from its complete build-output folder. The executable requires its adjacent `.dll`, `.deps.json`, `.runtimeconfig.json`, and `EnputMethod.Tsf.dll` files.

Windows can cache text services. If Enput does not appear in the existing `Ctrl + Shift` rotation immediately after installation, switch to another input method and back, then sign out and sign in once. `Ctrl + Space` is not claimed as a system-wide input-method switch shortcut.

For the candidate-window prototype, type a prefix such as `he`. The document retains `he` while a floating window shows up to the configured number of matching words. Press the matching number key to select a word, or press `Tab` to select the first candidate. Press `Space` to keep only the typed prefix and start the next word.

The Release installer explicitly loads its adjacent TSF DLL, clears a stale per-user Enput COM registration left by older builds, and registers the current service at a versioned DLL path such as `C:\Program Files\Enput Method\EnputMethod.Tsf.8.dll`. This allows an update to proceed while an earlier Enput DLL remains mapped in another application.

User-editable settings are stored in `%LOCALAPPDATA%\Enput Method\config.json`. It supports candidate count, layout, automatic spaces, font family, font size, opacity, and the theme name. `conf.json` from older releases remains supported and is migrated when possible. The accompanying `dictionary.txt` contains 370,763 ordered words; common words are first for useful suggestions, followed by a complete word list for broad coverage. The installer creates configuration, dictionary, and theme files only when they are missing, so updates preserve user changes.

## Repository Layout

- `EnputMethod.Tsf/`: native C++ TSF text service
- `EnputMethod.Installer/`: standalone WPF installer
- `EnputMethod.Uninstaller/`: standalone WPF uninstaller
- `docs/`: architecture and maintenance notes
- `EnputMethod.sln`: Visual Studio solution

See [Architecture](docs/architecture.md) for implementation and registration details.
See [Root Cause Analysis](docs/root-cause-analysis.md) for the deployment and candidate-window incident review.
See [Update Notes](docs/update-notes-zh-CN.md) for the Chinese configuration and update guide.
