# Enput Method Incident Review

## Outcome

The final verified behavior is a TSF English input method in the Chinese input-method group. It displays a floating candidate window near the text cursor, keeps the typed prefix in the application, and commits the visible candidate with `1` through `9`. Two consecutive selections were verified in Notepad without a crash.

## Why The Work Took So Long

Several independent faults looked like one symptom: switching to Enput without getting usable suggestions. We initially treated this as a suggestion-logic problem and repeated installation tests before proving which DLL Windows had actually loaded. That was the main process failure.

| Layer | Fault | Observable symptom | Correct evidence |
| --- | --- | --- | --- |
| Native deployment | The initial DLL used Debug C++ runtime dependencies. Normal applications could not load those dependencies. | Enput could appear in the input list but provided no behavior. | COM activation failed with `0x8007007E`; dependency inspection showed Debug CRT DLLs. |
| COM registration | An old `HKCU\\Software\\Classes\\CLSID` Enput entry pointed to a Debug DLL. `HKCR` gives that entry precedence over the correct `HKLM` entry. | Reinstalling copied the Release DLL, but applications still loaded the Debug path. | Separate HKCU/HKLM inspection showed conflicting paths; deleting only the Enput HKCU key made COM activation succeed. |
| Updating in-use DLLs | TSF DLLs remain mapped in text-service hosts and applications, including the Codex/ChatGPT process. Copying over the fixed filename failed. | Installer reported failure when updating an otherwise valid build. | Process-module inspection showed the old DLL loaded by ChatGPT. |
| Candidate feature | The first implementation was inline completion, which did not match the requested candidate-window interaction. | `he` became `hello` rather than showing selectable alternatives. | Product behavior review, not a loading failure. |
| Candidate crash | The candidate word array declared 40 elements but contained 38. A lookup with fewer than four matches reached null entries and constructed a string from a null pointer. | First selection could work; a later selection could crash Notepad or EmEditor. | Source count check found `40 != 38`; correcting it and repeating two selections removed the crash. |

## Corrective Changes

1. Release builds link the C++ runtime statically, so the TSF DLL has no Debug runtime dependency.
2. Installer and uninstaller explicitly load the adjacent native DLL instead of relying on DLL search order.
3. Installation removes only the legacy Enput user-level CLSID registration before writing the machine-level registration.
4. Deployment uses versioned DLL filenames (`EnputMethod.Tsf.8.dll` for the current release), avoiding overwrite failures when an older DLL is mapped.
5. Suggestions publish a non-activating WPF Overlay and keep a paged candidate list with up to nine visible items.
6. Candidate-word capacity is derived from the real count and is checked before Release builds during this repair cycle.

## Required Validation Order

Do these checks before asking for a manual typing test:

1. Verify the installed DLL hash equals the Release output and has no Debug CRT dependencies.
2. Read both HKLM and HKCU COM paths, then verify the effective HKCR path points at the installed Release DLL.
3. Create the COM class by CLSID. Failure here means an application test is not meaningful.
4. Open a new target application and confirm it has loaded the expected versioned DLL.
5. Test the requested workflow: show candidates, select one candidate, then select a second candidate in the same application.

This order separates deployment, registration, loading, and input behavior. It prevents repeated reinstall-and-guess cycles.

## Configuration Compatibility

The native configuration reader accepts valid UTF-8 JSON both with and without a UTF-8 byte-order mark. This matters because Windows editors can add the mark when users save `config.json`. Invalid JSON falls back to safe defaults. The installer recognizes the legacy `conf.json` filename and migrates custom content to `config.json`; it replaces only the known old default of `candidateCount: 4` with the current default configuration.

## Why A Target Application Must Be Reopened After A DLL Update

TSF activates the COM text service inside each target application's process. The process keeps the active service object and its DLL mapped for its lifetime. Updating COM registration changes only future activation; it does not replace an already-loaded service in an existing Notepad or editor process. Close and reopen a target application before testing a DLL update, then confirm the process loaded the expected versioned DLL. This is required for DLL updates, not for ordinary edits to `conf.json` or `dictionary.txt`, which the service reads for the next candidate query.


## 2026-08-29 Follow-up: Service/UI Split Incidents

The earlier incident review describes the original native candidate window. It is historical. The current production shape is a C++ in-process TSF service plus an out-of-process WPF Overlay. This introduced a separate class of failures: candidate state could be published without a visible window, old pipe actions could target a newer page, multiple editors could leave multiple clickable windows, installation could race a running Overlay, and tests could type into the wrong active IME.

The corrective contract is now explicit: every host has a `clientId`; every view has a monotonic `stateId`; Overlay returns intention only; C++ validates the state and performs TSF edits. Pipe work is asynchronous and bounded, windows are non-activating and foreground-gated, and the installer verifies deployed Overlay files as well as the TSF DLL. `run-regression.ps1` covers these mechanical invariants, but it cannot establish that Windows selected Enput inside an arbitrary application.

Two data-format incidents were also added to the validation order. First, an installer-written Unicode surrogate pair such as `\\uD83D\\uDD25` must decode as a single four-byte UTF-8 Emoji; otherwise a present `fire` entry is unusable. Second, an ECDICT value containing literal `\\n` or `\\r\\n` must be normalized before display. Both now have native regression tests. The WPF font contract is likewise explicit: configuration uses points; the renderer converts to 96-DPI WPF units.
