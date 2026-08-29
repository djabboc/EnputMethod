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

The installer deploys `dictionary.txt`, `themes`, the validated `enput.seed.db`, and the resulting SQLite `enput.db` to `C:\Program Files\Enput Method\Resources`; Overlay files are under the same Program Files product root. It creates `%LOCALAPPDATA%\Enput Method\UserData\config.json` and `shortcut.json` only when absent, migrating compatible old settings without overwriting a user value. Configuration and shortcuts remain JSON user settings; themes are static package resources. Lexicon content is SQLite-only: legacy JSON/JSONL data is imported transactionally once, validated, and then deleted. The native service opens the Program Files `enput.db` through Windows `winsqlite3.dll` and has no JSON/JSONL lexicon fallback.

## Suggestions

Word-prefix candidates remain sourced from the ordered `dictionary.txt`. Phrase suggestions, Emoji keywords, and translation records are queried from indexed SQLite tables. The configured candidate count limits only the current page; every matching word remains available through paging. Number keys map to the current page.

## Appearance

`config.json` controls vertical or horizontal layout, automatic trailing spaces after selection, font family, font size, opacity, and the active theme. `shortcut.json` maps actions to one or more key names. A theme controls background, foreground, selected-row colors and border, border color and width, corner radius, padding, row height, and shadow size. Translation-window fields use the `translation` prefix and separately control width, maximum height, colors, border, padding, corner radius, and scrollbar colors. Bundled themes are `dark`, `light`, `eye-care`, and `paper`.

## Candidate and Association Ranking

Candidate generation is deterministic local lookup, not a neural language model. For active input it merges four de-duplicated tiers in this fixed order: exact word/phrase, a phrase continuation whose trigger is the current input, ordinary word/phrase prefixes, then ordered-subsequence approximate matches for inputs of at least three characters. Phrase matching ignores spaces and Emoji matching also ignores `_` and `-`. Frequency learning only reorders candidates inside each tier, so it cannot move an approximate result ahead of an exact or prefix result.

After a candidate is committed, the service separately looks up records triggered by the committed text. Next-word records are shown directly; a stored full phrase is reduced to the suffix after the committed text. When no record exists, the service uses a fixed common-word fallback. Consequently, current sentence association is phrase-table lookup and fallback, not context-aware sentence prediction.
## Current Limits

- The service is x64-only, so x86 applications need a matching x86 TSF DLL before they can use it.
- The candidate window supports both keyboard and non-activating mouse selection. Candidate ranking preserves exact-match priority and dictionary order for unseen words; selections are learned locally under `HKCU\\Software\\Enput Method\\CandidateFrequency`. Set `adaptiveCandidateRanking` to `false` in `config.json` to disable both ranking and new learning records; delete that registry key to reset learned ranking.
- Windows owns global profile switching. Enput is placed in the Chinese group so the user's existing `Ctrl + Shift` behavior can include it; it does not capture `Ctrl + Space` system-wide.


## WPF Overlay Architecture (2026-08-29)

The candidate and translation UI is no longer rendered by the legacy Win32 windows. `EnputMethod.Overlay.exe` is installed under `Program Files\Enput Method\Overlay` and owns non-activating WPF windows. It receives JSON Lines messages over the current-user named pipe, maintains separate candidate and translation windows per `clientId`, and returns only user intent: candidate selection, page movement, or dismissal. The C++ host validates the matching `stateId` and creates the TSF edit session itself.

The Overlay process is started and connected asynchronously. At WPF application idle it creates and hides one off-screen, non-activating candidate window, then reuses it for the first Host so its native window creation and first render do not delay the first candidate. Pipe acknowledgement is not allowed to block the key-event path; when no Overlay pipe exists the host retries after 25ms, while installation updates retain a 250ms retry delay. Empty-candidate updates hide the associated surface; a foreground-owner check prevents a background application from leaving a usable candidate window on screen. Installation stops a running installed Overlay before deployment and verifies all installed Overlay files against the package.

The configuration contract uses points for `fontSize`. Native GDI/DirectWrite already interprets that setting as points; the WPF renderer explicitly converts points to 96-DPI WPF units (`pt * 96 / 72`). Translation strings are normalized after JSON parsing so a source value containing literal `\\n`, `\\r`, or `\\r\\n` displays as a real line break.


## Composition Rendering Boundary

While a typed prefix has not been committed or selected, the TSF service keeps it in an active `ITfComposition` so a candidate selection can replace the entire range atomically. The host application owns the visible rendering of that composition, including its underline. Enput does not set a TSF display attribute for underline color or thickness. Therefore the presence of an underline is caused by Enput's active-composition state, while its actual appearance is application-defined. The current design intentionally retains this behavior; candidate UI and composition state are not yet decoupled.

## 词组与取消快捷键（2026-08-29）

安装器将 Princeton WordNet 3.1 派生的 62,319 条多词短语一次性导入 SQLite `suggestions`，并用 `metadata.builtinPhraseVersion` 确保已有用户数据库只升级一次。高优先级补充覆盖通用地名和经济、商业、心理学、计算机、工程、法律术语；`newyork`、`machinelearning` 等无空格输入由紧凑保序短语匹配召回。`wordnet-phrases.txt` 仅用于安装导入，TSF 运行时仍只读 `enput.db`。

`shortcut.json` 的 `cancelComposition` 是多按键动作，默认 `["Escape", "Shift"]`。配置中的任一按键都会先被 TSF 捕获并执行同一取消路径：终止当前未确认 composition、隐藏候选；Emoji 模式无输入时退出模式。安装只补充缺失配置字段，不覆盖用户已自定义的数组。

## 发布与资源边界（2026-08-29）

发布架构采用“安装介质与运行根分离”。`artifacts\local\<Configuration>` 是开发调试包，`artifacts\release\EnputMethod-<version>-win-x64` 是用户 ZIP 的顶层内容；二者都是 Git 忽略的产物。它们的根目录都有名称明确的安装器、卸载器和 `payload`。

安装器从 `payload` 部署静态资源与 Overlay 到 `C:\Program Files\Enput Method`，并由 TSF 的本机注册函数将版本化 `EnputMethod.Tsf.<build-id>.dll` 注册在该目录。运行时通过已加载 TSF 模块的真实路径定位 `Resources` 和 `Overlay`，因此不依赖发布 ZIP 的解压位置。主题在 `Resources\themes`，SQLite 词库在 `Resources\enput.db`。

`%LOCALAPPDATA%\Enput Method\UserData` 只保存用户配置、快捷键和诊断/安装日志；候选学习频率继续在 HKCU。安装只初始化缺失的用户配置文件，升级不覆盖已有值；卸载只删除 TSF Profile、分类和 COM 注册；Program Files 的版本化 DLL、Overlay 和静态资源保留，以兼容已打开宿主并支持立即重装。旧 AppData 根目录下的 `config.json`、`shortcut.json` 及静态词库会按各自边界一次性迁移。
