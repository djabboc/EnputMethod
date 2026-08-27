# 高级输入功能开发任务书

## 执行规则

每项任务只包含一个可验收功能：实现后执行 `Release|x64` 构建和相应的静态/产物检查，创建独立提交，再开始下一项。全部任务完成后等待用户执行安装后手工测试。

## 数据边界

翻译、词性、例句与短语属于内容数据，不应由程序猜测或伪造权威性。程序将提供可追溯的 JSON 词典格式、来源字段和内置演示数据。要发布大规模词典前，必须导入已确认授权的来源数据，并保留许可证和出处。

## 任务 9：候选排序和大小写

- 完整匹配词始终排在候选首项，例如输入 `heal` 时 `heal` 排第一。
- 新增 `preserveCase` 配置；启用时按用户的首字母大写、全大写或原样规则转换候选。
- Caps Lock 开启时生成全大写候选，并在候选窗显示大写模式标记。
- 验收：`heal`、`I`、`Ent` 和 Caps Lock 场景的首项与大小写正确。

## Task 9 verification record

- Completed: full exact matches are retained and sorted before prefix matches; candidate casing observes `preserveCase`, with a CAPS marker when Caps Lock is on.
- Static verification: Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors.

## 任务 10：鼠标和位置

- 候选行支持鼠标点击提交。
- 候选窗提供可点击的上一页、下一页控件。
- 新增 `avoidScreenEdges` 配置，在靠近屏幕边缘时自动选择可见位置。
- 验收：鼠标选择、鼠标翻页及边缘位置均不抢焦点且文本正确。

## Task 10 verification record

- Completed: mouse clicks select a candidate or activate the previous/next page controls through a TSF edit session, without activating the candidate window.
- Completed: `avoidScreenEdges` defaults to `true` and clamps the window to the active monitor work area, preferring placement above the text when there is no room below.
- Static verification: Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors.

## 任务 11：连续联想和短语词典

- 选择一个候选后保留候选窗，并基于已提交词显示下一个建议。
- 支持词组、短句和固定搭配；候选提交以短语为单位。
- 词典迁移为 JSON，支持词条、短语、排序和下一词关联。
- 验收：输入并选择 `hello` 后显示关联建议；选择短语得到完整短语文本。

## Task 11 verification record

- Completed: `suggestions.json` supports ordered entries, phrase candidates, and next-word associations while keeping the existing `dictionary.txt` compatible.
- Completed: choosing a candidate (or committing a word with Space) starts a zero-length TSF composition only when an association exists, so the next suggestions remain visible at the caret.
- Static verification: Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors.

## 任务 12：Emoji 模式

- 在 `shortcut.json` 中新增 emoji 模式切换动作。
- emoji 模式将关键词映射为 emoji 候选，例如 `smile` 显示笑脸。
- 验收：模式切换、关键词候选、选择提交和退出行为正确。

## Task 12 verification record

- Completed: `toggleEmojiMode` is a configurable shortcut with the default `F2`; the candidate window shows an `EMOJI` mode marker.
- Completed: `emoji.json` maps editable keyword arrays to Unicode emoji candidates; the default `smile` keyword returns a grinning face.
- Static verification: Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors.

## 任务 13：翻译模式

- 在 `shortcut.json` 中新增翻译窗口切换动作。
- 新增非激活翻译窗口，显示当前高亮词/短语的词性、多语言释义、例句和来源。
- JSON 词典支持每个词条的多语言映射、词性、例句与来源。
- 验收：高亮候选变化时翻译窗口同步更新；窗口可由快捷键开关。

## Task 13 verification record

- Completed: `toggleTranslationWindow` is configurable with the default `F3`; a separate non-activating window tracks the highlighted candidate.
- Completed: `translations.json` supports part of speech, multiple language mappings, example text, and a source field. Bundled entries are explicitly demonstration data, not an authoritative corpus.
- Static verification: Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors.

## 任务 14：发布准备

- 更新 README、架构、配置说明和安装验证矩阵。
- 全量 Release 构建通过，默认配置和数据文件均进入安装器输出。
- 所有实现完成后等待用户进行安装后手工测试。

## Task 14 verification record

- Completed: README documents candidate priority, mouse interaction, continuous suggestions, case and edge settings, emoji mode, translation mode, shortcuts, and all installed data files.
- Completed: installer output contains `suggestions.json`, `emoji.json`, and `translations.json` alongside the existing configuration and dictionary files.
- Static verification: final Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors. Manual installation testing is intentionally deferred until all tasks are complete.

## Post-release correction: candidate lookup performance

- Cause: duplicate detection added for phrase integration used a linear scan for every matching dictionary word, which made short prefixes grow quadratically against the large word list.
- Correction: both regular and associated candidate paths use case-insensitive hash sets for duplicate detection.
- Static verification: Release|x64 build passed on 2026-08-27 with 0 warnings and 0 errors.

## Pending manual verification: full translation dictionary

- Change: commit `5983741` adds an installer-managed ECDICT download and a file-backed lookup path for 770,000+ English-to-Chinese entries.
- Source and license: ECDICT 1.0.28 under the MIT License; see `docs/dictionary-sources-zh-CN.md`.
- Current status: the small installed JSON dictionary and window placement are verified. The first-install download, conversion, and non-demo word lookup remain pending manual verification.
- Follow-up test: after installation completes with network access, enter a non-demo word such as `abandon`, open translation with `F3`, and verify English definition, Chinese meaning, and part of speech.
