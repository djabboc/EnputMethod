# Architecture

## Components

`EnputMethod.Tsf` is an in-process COM DLL that implements `ITfTextInputProcessorEx`, `ITfKeyEventSink`, and `ITfCompositionSink`.

When the Enput profile is active, alphabetic keystrokes are handled in a TSF edit session. The service starts a composition and writes only the typed prefix. Before a navigation, punctuation, editing, or application shortcut key is passed through, it synchronously commits that composition so the target application's selection cannot diverge from a stale TSF range. It obtains caret coordinates with `ITfContextView::GetTextExt`, then publishes immutable candidate and translation view models to the independent WPF Overlay through a per-host named-pipe connection. The TSF service retains every match, supports page navigation, and tracks the highlighted candidate; it remains the only component allowed to commit text.

The independent WPF installer and uninstaller each show an operation window. Their manifests request administrator privileges because TSF service registration is system-level. After the user clicks the operation button, the program shows its result and closes when the result is acknowledged.

## Registration

Installation performs these operations:

1. Removes the obsolete per-user Enput COM registration, which otherwise overrides the machine registration through `HKCR`.
2. Registers the COM in-process server under `HKLM\Software\Classes\CLSID`.
3. Registers the text service and the `zh-CN` language profile through TSF.
4. Registers the TSF keyboard and immersive-support categories.

The installer explicitly loads the TSF DLL adjacent to its executable, copies it to a filename derived from its timestamp and size, then registers that installed path. The versioned deployment filename allows an update to complete when an earlier DLL remains mapped in a running application. Reinstalling the same DLL reuses an identical deployed file. The installer and uninstaller can therefore be kept or moved as complete output folders after installation. Use the uninstaller before manually deleting an installed DLL.

The installer creates `%LOCALAPPDATA%\Enput Method\config.json`, `shortcut.json`, `dictionary.txt`, and four JSON themes from bundled defaults when those files are missing. `conf.json` from older releases is migrated without overwriting custom content. On theme updates it adds only fields absent from an existing valid bundled theme, preserving every user-supplied value. The native service parses the JSON configuration and shortcut action arrays, supports UTF-8 files with or without a BOM, and caches the dictionary until its timestamp or size changes.

## Suggestions

Suggestions are sourced from the ordered user dictionary. The bundled dictionary combines the Google 10,000 English word list with `dwyl/english-words` `words_alpha.txt`, then removes duplicates. The configured candidate count limits only the current page; every matching word remains available through paging. Number keys map to the current page.

## Appearance

`config.json` controls vertical or horizontal layout, automatic trailing spaces after selection, font family, font size, opacity, and the active theme. `shortcut.json` maps actions to one or more key names. A theme controls background, foreground, selected-row colors and border, border color and width, corner radius, padding, row height, and shadow size. Translation-window fields use the `translation` prefix and separately control width, maximum height, colors, border, padding, corner radius, and scrollbar colors. Bundled themes are `dark`, `light`, `eye-care`, and `paper`.

## Current Limits

- The service is x64-only, so x86 applications need a matching x86 TSF DLL before they can use it.
- The candidate window supports both keyboard and non-activating mouse selection. Candidate ranking preserves exact-match priority and dictionary order for unseen words; selections are learned locally under `HKCU\\Software\\Enput Method\\CandidateFrequency`. Set `adaptiveCandidateRanking` to `false` in `config.json` to disable both ranking and new learning records; delete that registry key to reset learned ranking.
- Windows owns global profile switching. Enput is placed in the Chinese group so the user's existing `Ctrl + Shift` behavior can include it; it does not capture `Ctrl + Space` system-wide.


## WPF Overlay Architecture (2026-08-29)

The candidate and translation UI is no longer rendered by the legacy Win32 windows. `EnputMethod.Overlay.exe` is installed under `Program Files\Enput Method\Overlay` and owns non-activating WPF windows. It receives JSON Lines messages over the current-user named pipe, maintains separate candidate and translation windows per `clientId`, and returns only user intent: candidate selection, page movement, or dismissal. The C++ host validates the matching `stateId` and creates the TSF edit session itself.

The Overlay process is started and connected asynchronously. Pipe acknowledgement is not allowed to block the key-event path; empty-candidate updates hide the associated surface; a foreground-owner check prevents a background application from leaving a usable candidate window on screen. Installation stops a running installed Overlay before deployment and verifies all installed Overlay files against the package.

The configuration contract uses points for `fontSize`. Native GDI/DirectWrite already interprets that setting as points; the WPF renderer explicitly converts points to 96-DPI WPF units (`pt * 96 / 72`). Translation strings are normalized after JSON parsing so a source value containing literal `\\n`, `\\r`, or `\\r\\n` displays as a real line break.
