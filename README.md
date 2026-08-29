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

Run `EnputMethod.Installer.exe` to install or `EnputMethod.Uninstaller.exe` to remove it. Both programs open a window, request UAC approval, perform the operation only after the user clicks its button, display the result, and close after the result is confirmed.

Run each executable from its complete build-output folder. The executable requires its adjacent `.dll`, `.deps.json`, `.runtimeconfig.json`, and `EnputMethod.Tsf.dll` files.

Windows can cache text services. If Enput does not appear in the existing `Ctrl + Shift` rotation immediately after installation, switch to another input method and back, then sign out and sign in once. `Ctrl + Space` is not claimed as a system-wide input-method switch shortcut.

For the candidate-window prototype, type a prefix such as `he`. The document retains `he` while a floating window shows the configured number of matching words per page. Press `-` or `+` to move between pages, or use Up and Down to move the highlighted candidate across page boundaries. The mouse can select a candidate or use the previous/next page controls without taking focus. Press the matching number key to select a word on the current page, or press `Tab` to select the highlighted word. If an association exists, selecting a word or pressing Space keeps the candidate window open with the next-word and phrase suggestions. Navigation and punctuation first commit the active composition and are then passed to the target application, so ordinary in-place editing does not overwrite text.

The Release installer explicitly loads its adjacent TSF DLL, clears a stale per-user Enput COM registration left by older builds, and registers the current service at a versioned DLL path derived from the DLL build timestamp and size. This allows an update to proceed while an earlier Enput DLL remains mapped in another application, and allows an unchanged build to be installed again safely.

User-editable settings are stored in `%LOCALAPPDATA%\Enput Method\config.json`; shortcut actions are stored independently in `shortcut.json`. An action accepts an array of key names. The defaults are `selectCurrent: ["Tab"]`, `previousPage: ["Minus", "NumpadSubtract"]`, `nextPage: ["Plus", "NumpadAdd"]`, `selectPrevious: ["Up"]`, `selectNext: ["Down"]`, `toggleEmojiMode: ["F2"]`, and `toggleTranslationWindow: ["F3"]`. `config.json` supports candidate count, layout, automatic spaces, `preserveCase`, `avoidScreenEdges`, font family, font size, opacity, and the theme name. `conf.json` from older releases remains supported and is migrated when possible. The accompanying `dictionary.txt` contains 370,763 ordered words; `suggestions.json` provides phrase and next-word associations; `emoji.json` maps keywords to emoji; and `translations.json` provides the multilingual translation schema. The bundled translation values are demonstration data only. Import licensed dictionary data before representing translations, parts of speech, or examples as authoritative. The installer creates missing configuration, shortcut, dictionary, and data files. For bundled themes it preserves every existing value and adds only newly introduced missing fields.
Set `adaptiveCandidateRanking` to `false` in `config.json` to preserve dictionary order and stop recording new candidate selections. The setting defaults to `true`.

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


## Current Development Status (2026-08-29)

The UI has been split from the input service. `EnputMethod.Tsf.dll` remains the in-process C++ TSF/COM service. Candidate and translation windows are rendered by the installed `EnputMethod.Overlay` WPF companion through local named pipes. Each application connection has a `clientId` and monotonic `stateId`; stale actions cannot select a newer candidate page, and only the foreground editor's Overlay windows remain visible.

`fontSize` is a point value. The default is `18`; the WPF Overlay converts it to 24 device-independent pixels so it visually matches the native 18pt configuration. Emoji candidates use bundled Twemoji color PNG assets. The installer preserves user emoji entries while merging bundled keywords and priority. The native JSON reader correctly decodes escaped Unicode surrogate pairs, including `fire -> 🔥`.

For translation data, ECDICT entries can contain literal `\\n` or `\\r\\n`; the TSF service normalizes them to actual line breaks before publishing the WPF view. Run `scripts\run-regression.ps1 -Configuration Release` for the Release build, native tests, Overlay protocol/foreground tests, and installed-file verification. The remaining real-application acceptance matrix is maintained in `docs/development-issue-ledger-zh-CN.md`.


## Active Composition Appearance

Before a candidate is committed, the typed prefix remains an active TSF composition so selecting a candidate can replace it safely. Some applications render active compositions with an underline; the application controls that visual appearance. This is currently intentional and can differ between editors.
