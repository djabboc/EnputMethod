# Enput Method

Enput Method is a prototype Windows English input method built with the Text Services Framework (TSF). It is registered as a Windows input profile and can be selected alongside system and third-party input methods.

## Features

- System-level TSF input profile in the Chinese input-method group
- Floating, paged candidate window with keyboard and mouse selection
- `1` through `9` select the corresponding item on the current page; `Tab` selects the highlighted item
- Exact word matches are always first; configured case preservation and a Caps Lock marker keep submitted text predictable
- Configurable multi-key shortcut actions, external dictionary, phrase/next-word suggestions, emoji mode, translation window, and four candidate-window themes
- Selecting a word can keep the candidate window open for its next-word or phrase suggestions
- Uses the existing Windows `Ctrl + Shift` Chinese-group switching behavior

## Requirements

- Windows 10 or Windows 11, x64
- Visual Studio 2022 with Desktop development with C++
- .NET 9 SDK
- Administrator approval during installation

## Build

Open `EnputMethod.sln` in Visual Studio, select `Release|x64`, and build the solution. The installer builds the native TSF DLL with a static C++ runtime before copying it to its output directory.

## Install and Use

For users, download and extract a release ZIP, then run `Install Enput Method.exe` from the extracted directory and accept Windows UAC. The release root visibly contains:

```text
Install Enput Method.exe
Uninstall Enput Method.exe
payload\
```

Installation deploys the registered TSF DLL, WPF Overlay and every static resource under `C:\Program Files\Enput Method`. The release directory is only an installation medium, so after installation it may be moved or deleted without breaking the input method. To remove the product, close target editors, run `Uninstall Enput Method.exe` from a complete extracted release directory, then delete that directory after successful uninstall.

Both WPF launchers show stage text and a progress bar while their long-running work is in progress. Installation disables repeated clicks while it stops an old Overlay, copies payload files, registers TSF, initializes user settings, prepares the static SQLite lexicon and verifies the installed result. Uninstall similarly reports unregistering, stopping Overlay, deleting Program Files resources and final verification.

Static product content is kept out of AppData: `enput.db`, themes, dictionary data, WordNet source, bundled Twemoji assets and runtime binaries are under `C:\Program Files\Enput Method`. Only user-mutated state resides in `%LOCALAPPDATA%\Enput Method\UserData`: `config.json`, `shortcut.json`, installation/Overlay logs and the registry-backed adaptive frequency data. Upgrades initialize default config and shortcuts only when they are missing and do not overwrite existing user settings. Uninstall removes Program Files content but preserves user data and learned ranking for a later reinstall.

Windows can cache text services. If Enput does not appear in the existing `Ctrl + Shift` rotation immediately after installation, switch to another input method and back, then sign out and sign in once. Existing target applications must be closed and reopened after TSF updates because they keep the old in-process DLL mapped.

For the candidate-window prototype, type a prefix such as `he`. The document retains `he` while a floating window shows the configured number of matching words per page. Press `-` or `+` to move between pages, or use Up and Down to move the highlighted candidate across page boundaries. The mouse can select a candidate or use the previous/next page controls without taking focus. Press the matching number key to select a word on the current page, or press `Tab` to select the highlighted word. Shift and Escape cancel an active composition. F2 enables Emoji mode and F3 shows the rich-text translation view.

`fontSize` is a point value. The default is `18`; the WPF Overlay converts it to 24 device-independent pixels so it visually matches the native 18pt configuration. Emoji candidates use bundled Twemoji color PNG assets. The SQLite lexicon stores Unicode text directly, including `fire -> 🔥` and `saw -> 🪚`.

## Repository Layout

- `EnputMethod.Tsf/`: native C++ TSF text service
- `EnputMethod.Installer/`: standalone WPF installer
- `EnputMethod.Uninstaller/`: standalone WPF uninstaller
- `docs/`: architecture and maintenance notes
- `EnputMethod.sln`: Visual Studio solution

See [Architecture](docs/architecture.md) for implementation and registration details.
See [Root Cause Analysis](docs/root-cause-analysis.md) for the deployment and candidate-window incident review.
See [Update Notes](docs/update-notes-zh-CN.md) for the Chinese configuration and update guide.
See [Installation Validation](docs/installation-validation-zh-CN.md) for the repeatable build, installation, and joint manual-test procedure.
See [Development Issue Ledger](docs/development-issue-ledger-zh-CN.md) for the complete problem history, deferred decisions, and the current consolidated manual acceptance list.
See [Technology Stack](docs/technology-stack-zh-CN.md) for the current component, runtime, data, IPC, deployment, and configuration details.
See [Debugging and Testing](docs/debugging-and-testing-zh-CN.md) for the layered build, installation, automated, and real-host verification procedure.
See [Release Packaging Tasks](docs/release-packaging-tasks-zh-CN.md) for the approved one-stop distribution, resource-root, and progress-UX migration plan.


## Current Development Status (2026-08-29)

The UI has been split from the input service. `EnputMethod.Tsf.dll` remains the in-process C++ TSF/COM service. Candidate and translation windows are rendered by the installed `EnputMethod.Overlay` WPF companion through local named pipes. Each application connection has a `clientId` and monotonic `stateId`; stale actions cannot select a newer candidate page, and only the foreground editor's Overlay windows remain visible.

`fontSize` is a point value. The default is `18`; the WPF Overlay converts it to 24 device-independent pixels so it visually matches the native 18pt configuration. Emoji candidates use bundled Twemoji color PNG assets. The SQLite lexicon stores Unicode text directly, including `fire -> 🔥` and `saw -> 🪚`.

For translation data, ECDICT entries can contain literal `\\n` or `\\r\\n`; the TSF service normalizes them to actual line breaks before publishing the WPF view. After installing a new TSF DLL, close and reopen the target editor before testing: an already-open process keeps the previous in-process DLL mapped. Running a prior JSON-based DLL after its data files have been migrated makes F2 and F3 appear broken. Run `scripts\run-regression.ps1 -Configuration Release` for the Release build, native tests, Overlay protocol/foreground tests, and installed-file verification. The remaining real-application acceptance matrix is maintained in `docs/development-issue-ledger-zh-CN.md`.


## Active Composition Appearance

Before a candidate is committed, the typed prefix remains an active TSF composition so selecting a candidate can replace it safely. Some applications render active compositions with an underline; the application controls that visual appearance. This is currently intentional and can differ between editors.
