# Enput Method TODO

## Phase 1: Configuration Contract

- [x] Decide the public configuration filename: retain `conf.json` or migrate to `config.json`.
  - Preserve backward compatibility so an existing user configuration is not lost.
  - Document the final location under `%LOCALAPPDATA%\Enput Method`.
- [x] Change the default candidate count from `4` to `9`.
  - Clamp the value to the supported keyboard-selection range, `1` through `9`.
- [x] Add and validate these configuration fields:
  - `candidateCount`: default `9`.
  - `layout`: `vertical` or `horizontal`; default `vertical`.
  - `appendSpaceAfterSelection`: boolean; default `true`.
  - `fontFamily`: `Segoe UI`.
  - `fontSize`: `18` points; the WPF Overlay converts this to 24 device-independent pixels.
  - `opacity`: `1.0`.
  - `theme`: default `dark`.
- [x] Replace the current narrow configuration reader with a JSON parser that rejects invalid values safely and falls back to defaults.

## Phase 2: Candidate Window Behavior

- [x] Render candidates vertically by default and horizontally when configured.
- [x] After a numeric selection, append one space when `appendSpaceAfterSelection` is enabled.
  - Do not append an extra space when the selected word is immediately followed by punctuation or an explicit user action that should not receive one.
- [x] Apply configured font family, size, and opacity to the floating candidate window.
- [x] Keep the window non-activating, positioned at the TSF composition caret, and usable with number keys `1` through `9`.
- [x] Learn candidate selection frequency locally without changing exact-match priority or the order of unseen dictionary entries.
- [~] Repeated selection, backspace, Enter, Escape, Space, punctuation, cursor movement, no-match input, paging, and input in Notepad, EmEditor, and ChatGPT.
  - Many individual cases have passed user verification. The remaining cross-application regression and the latest mouse-pagination repair are listed for consolidated verification in `docs/development-issue-ledger-zh-CN.md`.

## Phase 3: Themes

- [x] Create a `themes/` folder with four bundled JSON theme files:
  - `dark.json` (default)
  - `light.json`
  - `eye-care.json`
  - `paper.json`
- [x] Define theme fields for background, foreground, border, selected-row colors and border, border width, corner radius, padding, row height, and shadow.
- [x] Load the theme selected by `theme` in the user configuration, with a safe fallback to `dark`.
- [x] Design the candidate-window renderer so themes control both colors and shape without changing input logic.

## Phase 4: Dictionary And Distribution

- [x] Keep `dictionary.txt` user-editable and document its ordered, one-word-per-line format.
- [x] Add a larger curated default English dictionary with useful frequency ordering.
- [x] Ensure installer updates never overwrite existing user configuration, dictionary, or custom theme files.
- [x] Update installer, uninstaller, README, Chinese update notes, architecture notes, and release verification steps.

## Completion Criteria

- [~] A clean installation creates the default configuration, dictionary, and four themes.
  - Existing-install update behavior is verified; a clean-install regression remains in the consolidated acceptance list.
- [x] Editing the configuration changes candidate count, layout, automatic spacing, font, size, opacity, and theme without rebuilding the input method.
- [x] The default dark vertical candidate window shows nine numbered choices and adds a space after numeric selection.
- [~] All changes pass Release build, COM activation, installed-DLL verification, and manual multi-selection tests in Notepad and EmEditor.
  - Release build and installed-DLL checks have passed. Current manual status and remaining cases are maintained in `docs/development-issue-ledger-zh-CN.md`.
