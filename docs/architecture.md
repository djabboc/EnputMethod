# Architecture

## Components

`EnputMethod.Tsf` is an in-process COM DLL that implements `ITfTextInputProcessor`, `ITfKeyEventSink`, and `ITfCompositionSink`.

When the Enput profile is active, alphabetic keystrokes are handled in a TSF edit session. The service starts a composition, writes the typed prefix plus an optional suffix, and selects the suffix. This presents completion without opening a separate candidate window.

The WPF application provides installation, uninstallation, and a shortcut to Windows typing settings. Its application manifest requests administrator privileges because TSF service registration is system-level.

## Registration

Installation performs these operations:

1. Registers the COM in-process server under `HKLM\Software\Classes\CLSID`.
2. Registers the text service and `en-US` language profile through TSF.
3. Registers the TSF keyboard category.
4. Writes the `Ctrl + Shift` Windows language/layout toggle setting for the installing user.

The system stores the DLL path at install time. Do not remove the setup output directory while the input method is installed. Use the setup application's uninstall command before deleting its active DLL.

## Suggestions

Suggestions are intentionally small and deterministic for this prototype. The dictionary is compiled into `TsfTextService.cpp`; it can later be replaced by a frequency-ranked dictionary, learned history, or a language model while retaining the same TSF composition flow.

## Current Limits

- The service is x64-only, so x86 applications need a matching x86 TSF DLL before they can use it.
- Suggestions are inline single completions, not a multi-row candidate window.
- Windows owns global profile switching. The service configures `Ctrl + Shift`, but it deliberately does not capture `Ctrl + Space` system-wide.
