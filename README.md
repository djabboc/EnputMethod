# Enput Method

Enput Method is a prototype Windows English input method built with the Text Services Framework (TSF). It is registered as a Windows input profile and can be selected alongside system and third-party input methods.

## Features

- System-level TSF input profile in the Chinese input-method group
- English inline completion from a small built-in word list
- `Tab` accepts the highlighted completion
- `Space` preserves the typed prefix and starts the next word
- Uses the existing Windows `Ctrl + Shift` Chinese-group switching behavior

## Requirements

- Windows 10 or Windows 11, x64
- Visual Studio 2022 with Desktop development with C++
- .NET 9 SDK
- Administrator approval during installation

## Build

Open `EnputMethod.sln` in Visual Studio, select `Debug|x64` or `Release|x64`, and build the solution. The setup application builds the native TSF DLL before copying it to its output directory.

## Install and Use

Run `EnputMethod.Installer.exe` to install or `EnputMethod.Uninstaller.exe` to remove it. Both programs open a window, request UAC approval, perform the operation only after the user clicks its button, display the result, and close after the result is confirmed.

Run each executable from its complete build-output folder. The executable requires its adjacent `.dll`, `.deps.json`, `.runtimeconfig.json`, and `EnputMethod.Tsf.dll` files.

Windows can cache text services. If Enput does not appear in the existing `Ctrl + Shift` rotation immediately after installation, switch to another input method and back, then sign out and sign in once. `Ctrl + Space` is not claimed as a system-wide input-method switch shortcut.

For the inline completion prototype, type a prefix such as `hel`. The remaining letters of `hello` appear selected. Press `Tab` to accept the full word, or `Space` to keep only the prefix.

## Repository Layout

- `EnputMethod.Tsf/`: native C++ TSF text service
- `EnputMethod.Installer/`: standalone WPF installer
- `EnputMethod.Uninstaller/`: standalone WPF uninstaller
- `docs/`: architecture and maintenance notes
- `EnputMethod.sln`: Visual Studio solution

See [Architecture](docs/architecture.md) for implementation and registration details.
