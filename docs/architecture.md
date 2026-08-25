# Architecture

## Components

`EnputMethod.Tsf` is an in-process COM DLL that implements `ITfTextInputProcessorEx`, `ITfKeyEventSink`, and `ITfCompositionSink`.

When the Enput profile is active, alphabetic keystrokes are handled in a TSF edit session. The service starts a composition and writes only the typed prefix. It positions a non-activating Win32 candidate window beside the composition using `ITfContextView::GetTextExt`; the window displays one to nine matching words. The service keeps keyboard ownership and commits the corresponding word with its number key.

The independent WPF installer and uninstaller each show an operation window. Their manifests request administrator privileges because TSF service registration is system-level. After the user clicks the operation button, the program shows its result and closes when the result is acknowledged.

## Registration

Installation performs these operations:

1. Removes the obsolete per-user Enput COM registration, which otherwise overrides the machine registration through `HKCR`.
2. Registers the COM in-process server under `HKLM\Software\Classes\CLSID`.
3. Registers the text service and the `zh-CN` language profile through TSF.
4. Registers the TSF keyboard and immersive-support categories.

The installer explicitly loads the TSF DLL adjacent to its executable, copies it to a versioned path such as `C:\Program Files\Enput Method\EnputMethod.Tsf.8.dll`, then registers that installed path. The versioned deployment filename allows the update to complete when an earlier DLL remains mapped in a running application. The installer and uninstaller can therefore be kept or moved as complete output folders after installation. Use the uninstaller before manually deleting the installed DLL.

The installer creates `%LOCALAPPDATA%\Enput Method\config.json`, `dictionary.txt`, and four JSON themes from bundled defaults when those files are missing. `conf.json` from older releases is migrated without overwriting custom content. The native service parses the JSON configuration, supports UTF-8 files with or without a BOM, and caches the dictionary until its timestamp or size changes. Existing user files are never overwritten by an update.

## Suggestions

Suggestions are sourced from the ordered user dictionary. The bundled dictionary combines the Google 10,000 English word list with `dwyl/english-words` `words_alpha.txt`, then removes duplicates. Candidate enumeration is capped at nine items because the prototype maps them directly to `1` through `9`.

## Appearance

`config.json` controls vertical or horizontal layout, automatic trailing spaces after selection, font family, font size, opacity, and the active theme. A theme controls background, foreground, selected-row colors, border color and width, corner radius, padding, row height, and shadow size. Bundled themes are `dark`, `light`, `eye-care`, and `paper`.

## Current Limits

- The service is x64-only, so x86 applications need a matching x86 TSF DLL before they can use it.
- The candidate window supports keyboard selection only; mouse selection, paging, and frequency ranking are not implemented.
- Windows owns global profile switching. Enput is placed in the Chinese group so the user's existing `Ctrl + Shift` behavior can include it; it does not capture `Ctrl + Space` system-wide.
