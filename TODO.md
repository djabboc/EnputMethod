# Enput Method TODO

## Phase 1: Configuration Contract

- [ ] Decide the public configuration filename: retain `conf.json` or migrate to `config.json`.
  - Preserve backward compatibility so an existing user configuration is not lost.
  - Document the final location under `%LOCALAPPDATA%\Enput Method`.
- [ ] Change the default candidate count from `4` to `9`.
  - Clamp the value to the supported keyboard-selection range, `1` through `9`.
- [ ] Add and validate these configuration fields:
  - `candidateCount`: default `9`.
  - `layout`: `vertical` or `horizontal`; default `vertical`.
  - `appendSpaceAfterSelection`: boolean; default `true`.
  - `fontFamily`: default Windows UI font.
  - `fontSize`: default readable candidate-window size.
  - `opacity`: default fully opaque value.
  - `theme`: default `dark`.
- [ ] Replace the current narrow configuration reader with a JSON parser that rejects invalid values safely and falls back to defaults.

## Phase 2: Candidate Window Behavior

- [ ] Render candidates vertically by default and horizontally when configured.
- [ ] After a numeric selection, append one space when `appendSpaceAfterSelection` is enabled.
  - Do not append an extra space when the selected word is immediately followed by punctuation or an explicit user action that should not receive one.
- [ ] Apply configured font family, size, and opacity to the floating candidate window.
- [ ] Keep the window non-activating, positioned at the TSF composition caret, and usable with number keys `1` through `9`.
- [ ] Test repeated selection, backspace, Enter, Escape, Space, no-match input, and input in Notepad and EmEditor.

## Phase 3: Themes

- [ ] Create a `themes/` folder with four bundled JSON theme files:
  - `dark.json` (default)
  - `light.json`
  - `eye-care.json`
  - `paper.json`
- [ ] Define theme fields for background, foreground, border, selected-row colors, border width, corner radius, padding, row height, and shadow.
- [ ] Load the theme selected by `theme` in the user configuration, with a safe fallback to `dark`.
- [ ] Design the candidate-window renderer so themes control both colors and shape without changing input logic.

## Phase 4: Dictionary And Distribution

- [ ] Keep `dictionary.txt` user-editable and document its ordered, one-word-per-line format.
- [ ] Add a larger curated default English dictionary with useful frequency ordering.
- [ ] Ensure installer updates never overwrite existing user configuration, dictionary, or custom theme files.
- [ ] Update installer, uninstaller, README, Chinese update notes, architecture notes, and release verification steps.

## Completion Criteria

- [ ] A clean installation creates the default configuration, dictionary, and four themes.
- [ ] Editing the configuration changes candidate count, layout, automatic spacing, font, size, opacity, and theme without rebuilding the input method.
- [ ] The default dark vertical candidate window shows nine numbered choices and adds a space after numeric selection.
- [ ] All changes pass Release build, COM activation, installed-DLL verification, and manual multi-selection tests in Notepad and EmEditor.
