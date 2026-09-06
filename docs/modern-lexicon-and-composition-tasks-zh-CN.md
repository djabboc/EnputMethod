# 现代词库、符号输入与候选预览任务书

创建日期：2026-09-05。

## 目标

补齐高频现代实体、流行文化和地点词条；修复 Shift 符号键被误作数字直选的问题；新增可配置的候选预览 composition，且不改变候选检索依据。

## 范围与验收

| 编号 | 需求 | 实现边界 | 自动化验收 | 状态 |
| --- | --- | --- | --- | --- |
| M-01 | `spiderman`、`bars`、`Washington DC`、`AT&T`、`R&B` 等可显示现代中文释义 | 安装器向 SQLite 写入有来源标识的内置补充翻译，不替代 ECDICT/CC-CEDICT 记录；升级时只替换 `enput-modern-*` 内置来源 | 最终 Release 包、系统安装与已安装 SQLite 词库校验通过 | `AT&T` / `R&B` 真实翻译窗通过；其余词条待扩展真实 F3 矩阵 |
| M-02 | `thewhitehouse`、`Donald` 能召回 `The White House`、`Donald Trump` | 高优先级短语支持无空格保序匹配，且 SQL 检索不因展示大小写排除实体 | TSF Release DLL 已安装；SQLite 词库校验通过 | 真实候选窗口与提交通过 |
| M-03 | `Shift+7` 输入 `&`，`R&B` 和 `AT&T` 不触发第 7 候选 | 延迟处理单独按下的 Shift 取消动作；带 Shift 的顶排数字按其可打印符号处理 | 原生按键路由单元测试 | 原生、安装和真实宿主候选/翻译窗口通过 |
| M-04 | 上下键浏览候选时，composition 可预览当前候选 | `previewSelectedCandidateInComposition` 默认启用；继续输入或退格保留原始查询，左右键提升预览候选为可编辑文本 | 原生预览状态单元测试与真实宿主观察 | 默认预览与 `Washington D|C` 真实宿主通过；关闭配置仍待扩展矩阵 |
| M-05 | 文档、总账与开发日志同步 | 记录数据来源边界、配置、回归步骤与真实宿主限制 | 文档复核 | 已完成 |
| M-06 | composition 中间位置允许正常编辑 | 光标不在末尾时，空格、`0`-`9` 与任何可打印符号均插入 composition；不触发确认或直选 | 原生纯逻辑回归；真实 TSF 宿主自动化 | 原生回归、Release 构建、系统重装通过；真实宿主自动化因无交互桌面阻塞 |
| M-07 | composition 末尾保留快捷确认，并允许数字绕过 | 仅末尾的空格和 `1`-`9` 保留原有确认；`shortcut.json.bypassCandidateSelectionModifiers` 默认 `Control`，按住后顶排和小键盘数字作为文本输入 | 原生纯逻辑回归、重装后的真实 TSF 宿主自动化 | 原生回归、Release 构建、系统重装通过；真实宿主自动化因无交互桌面阻塞 |
| M-08 | 对现有 SQLite 词库执行全量审计 | 新增 `--audit-lexicon`，对每张词库表完整性、无效/孤立记录、大小写变体冲突和现有来源/类型字段覆盖做全表扫描，输出 JSON 报告；绝不根据拼写猜测实体类别 | 已安装词库运行审计并人工复核报告 | 已完成：`integrity_check=ok`，无无效/孤立/外键问题；唯一变体键为预期的 `polish` |

## 数据边界

- 内置补充首批覆盖 `Spider-Man`、`The White House`、`Donald Trump`、`Washington DC`、`AT&T`、`R&B`、`ChatGPT`、`OpenAI`、`TikTok`、`GitHub`、`Discord`、`K-pop`，以及 `bars`、`meme`、`rizz`、`stan`、`slay`、`doomscrolling`、`deepfake`、`livestream`、`vlog`、`cosplay`、`e-sports` 的指定义项；不宣称覆盖所有现代词汇或实时网络语。
- `bars` 的补充义项标记为说唱/网络语境，避免把该语义伪装成唯一常规释义。
- 专有名词候选保留产品指定的大小写；带空格的实体存入短语索引，无空格或含符号的实体（如 `AT&T`、`R&B`）存入单词索引；翻译使用独立条目键，不依赖候选的显示大小写。
- 现有 `words` 和 `suggestions` 是早期索引表，没有“逐条来源”和“词类/实体类别”列；审计会明确把它们报告为结构性缺口，不能由 `Canada`、`polish` 等表面形式反推类型。后续接入权威来源时，必须把来源原始标签、稳定标识、类别和许可作为逐条数据导入。

## 编辑规则

| composition 光标位置 | 空格 | `1`-`9` | `0` / 其他可打印字符 |
| --- | --- | --- | --- |
| 中间 | 插入文本 | 插入文本 | 插入文本 |
| 末尾 | 保留快速确认 | 保留候选直选 | 插入文本 |
| 末尾且按住 `bypassCandidateSelectionModifiers` | 保留快速确认 | 插入文本 | 插入文本 |

默认绕过修饰键为 `Control`。该设置只绕过数字直选，不改变 `Ctrl+字母` 等宿主快捷键的既有行为。升级时安装器只会向已有 `shortcut.json` 添加缺失字段，不会覆盖用户已有快捷键。

### 中间编辑后的 composition 状态

当光标位于 composition 中间而插入空格、数字或可打印符号时，composition **保持活动**，光标停在新字符之后。这样用户仍可继续在候选文本中插入 `12345`、删除、移动光标或重新获得候选；若新文本没有可用候选，候选窗隐藏，但文本不会因此自动提交。只有确认、取消或其它明确的结束操作才会结束 composition。

宿主编辑器可能继续把这段未确认文本显示为带下划线的 composition。这是 TSF 的正常状态标记，不是候选窗仍可见或文本已提交的信号。若要在中间输入后立即取消下划线，必须把候选状态从 composition 解耦，并会破坏当前的原子选词替换与继续编辑语义，因此本轮不采用。

## 词库审计

安装器支持 `Install Enput Method.exe --audit-lexicon`。它只读已安装的运行词库，向 `%LOCALAPPDATA%\Enput Method\UserData\lexicon-audit.json` 写入审计报告。报告覆盖 SQLite 完整性、各表计数、空/无效记录、大小写变体归一化冲突、翻译孤儿记录、缺少释义的翻译条目和外键检查，并明确当前旧表不具备逐条来源/类型的事实。它不会下载词源、不会修改词库、不会把小写或首字母大写猜成专名。

2026-09-06 已安装库审计结果：`words=370778`、`suggestions=62502`、`translationEntries=824025`；SQLite 完整性为 `ok`，无空/无效词条、无归一化不一致、无孤立翻译记录、无外键问题。唯一多变体归一化键为 `polish`，对应产品刻意保留的 `polish` / `Polish` 独立词形。报告同时确证：`words` 与 `suggestions` 的逐条来源和类型覆盖均为 0，后续权威词源导入前必须完成该数据模型升级。

## 真实宿主验收

重装后，关闭并重新打开目标应用，确认语言栏中已选择 Enput Method：

1. `spiderman` 和 `bars` 高亮后按 F3，确认中文释义包含“蜘蛛侠”和说唱/网络语境的 `bars`。
2. 输入 `thewhitehouse`、`Donald`、`washingtondc`，确认实体候选和指定大小写；输入完整 `AT&T` 和 `R&B` 后按 F3，确认品牌/音乐风格释义。
3. 输入 `AT` 后按 `Shift+7` 再输入 `T`，以及输入 `R` 后按 `Shift+7` 再输入 `B`；编辑区不得提交候选文本。
4. 输入 `Wash`，按下方向键高亮 `Washington`；启用预览时 composition 应变为高亮候选，继续输入或退格后应恢复基于原始 `Wash` 的检索。
5. 将 `previewSelectedCandidateInComposition` 设为 `false` 后重复第 4 项；composition 保持原始输入，候选高亮和翻译仍更新。
