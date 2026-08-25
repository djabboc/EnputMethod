# Architecture

## Components

`EnputMethod.Tsf` is an in-process COM DLL that implements `ITfTextInputProcessorEx`, `ITfKeyEventSink`, and `ITfCompositionSink`.

When the Enput profile is active, alphabetic keystrokes are handled in a TSF edit session. The service starts a composition, writes the typed prefix plus an optional suffix, and selects the suffix. This presents completion without opening a separate candidate window.

The independent WPF installer and uninstaller each show an operation window. Their manifests request administrator privileges because TSF service registration is system-level. After the user clicks the operation button, the program shows its result and closes when the result is acknowledged.

## Registration

Installation performs these operations:

1. Removes the obsolete per-user Enput COM registration, which otherwise overrides the machine registration through `HKCR`.
2. Registers the COM in-process server under `HKLM\Software\Classes\CLSID`.
3. Registers the text service and the `zh-CN` language profile through TSF.
4. Registers the TSF keyboard and immersive-support categories.

The installer explicitly loads the TSF DLL adjacent to its executable, copies it to `C:\Program Files\Enput Method\EnputMethod.Tsf.2.dll`, then registers that installed path. The versioned deployment filename allows the update to complete when an earlier DLL remains mapped in a running application. The installer and uninstaller can therefore be kept or moved as complete output folders after installation. Use the uninstaller before manually deleting the installed DLL.

## Suggestions

Suggestions are intentionally small and deterministic for this prototype. The dictionary is compiled into `TsfTextService.cpp`; it can later be replaced by a frequency-ranked dictionary, learned history, or a language model while retaining the same TSF composition flow.

## Current Limits

- The service is x64-only, so x86 applications need a matching x86 TSF DLL before they can use it.
- Suggestions are inline single completions, not a multi-row candidate window. Applications can choose not to visually highlight the pending suffix; `Tab` accepts it and continued typing replaces it.
- Windows owns global profile switching. Enput is placed in the Chinese group so the user's existing `Ctrl + Shift` behavior can include it; it does not capture `Ctrl + Space` system-wide.
