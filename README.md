# Enput Method

Enput Method is a prototype Windows English input method built with the Text Services Framework (TSF). It is registered as a Windows input profile and can be selected alongside system and third-party input methods.

## Features

- System-level TSF input profile: `Enput Method - English`
- English inline completion from a small built-in word list
- `Tab` accepts the highlighted completion
- `Space` preserves the typed prefix and starts the next word
- Installs the Windows `Ctrl + Shift` language/layout switching setting

## Requirements

- Windows 10 or Windows 11, x64
- Visual Studio 2022 with Desktop development with C++
- .NET 9 SDK
- Administrator approval during installation

## Build

Open `EnputMethod.sln` in Visual Studio, select `Debug|x64` or `Release|x64`, and build the solution. The setup application builds the native TSF DLL before copying it to its output directory.

## Install and Use

Run `EnputMethod.Setup.Fixed.exe` from the setup output directory and approve the Windows UAC prompt. Then switch away from the current input method and back to `Enput Method - English`.

Windows can cache text services. If the input method or the `Ctrl + Shift` mapping does not refresh immediately, sign out and sign in once. `Ctrl + Space` is not claimed as a system-wide input-method switch shortcut.

For the inline completion prototype, type a prefix such as `hel`. The remaining letters of `hello` appear selected. Press `Tab` to accept the full word, or `Space` to keep only the prefix.

## Repository Layout

- `EnputMethod.Tsf/`: native C++ TSF text service
- `EnputMethod.Setup/`: WPF installer and control panel
- `docs/`: architecture and maintenance notes
- `EnputMethod.sln`: Visual Studio solution

See [Architecture](docs/architecture.md) for implementation and registration details.
