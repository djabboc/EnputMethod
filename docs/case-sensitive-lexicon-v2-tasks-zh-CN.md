# 大小写语义词典与可编辑预览任务书

创建日期：2026-09-05。

## 目标

将“大小写”从候选显示样式提升为词典数据的一部分。检索可使用无大小写前缀和紧凑短语匹配，但候选、提交文本与翻译必须使用词典记录的规范文本；精确大小写的词典记录优先于仅大小写不同的记录。

## 范围与验收

| 编号 | 需求 | 实现边界 | 验收 |
| --- | --- | --- | --- |
| C-01 | `Chicago`、`Manhattan`、`New York`、`Monte Carlo`、`The White House` 等候选保留词典规范大小写 | 不再依据用户输入调用通用大小写转换；WordNet 原始小写数据不被伪装为具有规范大小写 | 候选显示和提交文本与补充词典记录一致 |
| C-02 | `polish` 与 `Polish` 可作为不同词典记录查询和翻译 | SQLite schema 2 增加规范词形索引；翻译按候选精确文本优先查找 | `polish` 和 `Polish` 分别显示对应释义 |
| C-03 | `Taylor Swift`、`Michael Jackson` 可由前缀召回 | 补充受版本控制的现代专名短语和中文释义；不声称 WordNet 覆盖当代人物 | 输入 `Taylor`、`Michael` 时可见规范候选 |
| C-04 | `AT&T`、`R&B` 同时支持前缀候选和完整 composition 查询 | Shift 符号写入活动 composition，不作为候选直选或提前提交 | `AT` / `R` 可见候选；完整输入后 F3 可查释义 |
| C-05 | 浏览 `Washington DC` 后左移显示 `Washington D|C` | 左右编辑键将当前预览候选提升为可编辑 composition，再移动光标 | 不回退为旧查询 `W|a` |
| C-06 | 词库迁移、安装和记录同步 | schema/词库版本升级后重装；校验已安装数据库与真实宿主 | Release 构建、SQLite 校验、重开宿主验收 |
| C-07 | 全程自动化的真实 TSF 验收 | 独立 x64 WPF 文本宿主通过 `ITfInputProcessorProfileMgr` 仅为自身进程激活已安装 Enput，并使用 `SendInput` 后读取真实编辑控件；普通直选使用数字键，特定候选通过方向键预览定位。候选 UI 还必须验证 Overlay `ready`、当前 client/state 消息和实际窗口可见性 | 已通过：候选窗、翻译窗、文本、光标、大小写顺序和频率隔离在同一无干扰轮次通过 |

## 数据原则

- WordNet 导入文件本身已小写化，不能从中可靠恢复专名大小写；规范专名只来自有明确文本的受版本控制补充词典。
- 检索键是小写辅助索引，不是展示文本，也不是翻译词义的唯一标识。
- 只有在词典明确提供多个大小写变体时才保留多个语义记录，例如 `polish` 与 `Polish`。
- 不以“输入首字母是否大写”猜测专名；输入大小写只用于在已有变体之间优先选择精确记录。

## 实施记录

- 2026-09-05：SQLite schema 升级至 2，新增 `word_case_variant`。检索仍使用小写索引；候选文本、提交文本和翻译使用词典中保存的规范文本。`polish` 与 `Polish` 是独立记录，精确输入大小写优先。
- 2026-09-05：补充 `AT&T`、`R&B`、地点、机构和当代人物等规范词形与短语。`&` 是活动 composition 的可输入字符，`Shift+7` 不会被候选数字选择逻辑吞掉；完整词形可继续用于候选和 F3 翻译。
- 2026-09-05：浏览候选时，左右键先将当前预览提升为可编辑 composition，再移动光标。示例：`Wa` 预览 `Washington DC` 后按左键，结果为 `Washington D|C`。
- 2026-09-05：Release 原生回归通过，覆盖带 Shift 的顶排数字不直选、`AT&T` / `R&B` 组合字符插入和 `Washington D|C` 光标状态；Release 安装包已重建并重装。`--verify-lexicon` 以退出码 0 通过，已安装数据库时间戳为 23:35:50，schema 2 与词条断言已生效。安装包 SHA-256：`FA07D07922B37E522F965D2B206F83EE15BAD3E3496730E6DCDC46A1760620CB`。
- 2026-09-05 初次真实宿主验收受阻：当时自动化环境没有可控的 Windows 应用窗口，不能把自动化按键结果作为此项证据；该历史阻塞已由 2026-09-06 的交互桌面测试解除。
- 2026-09-05：按“全程自动化验收”要求新增 `EnputMethod.Tsf.IntegrationTests` 和 `run-tsf-integration-tests.ps1`。宿主会在隔离进程内激活实际已安装的 Enput profile，测试 `Chicago`、`Manhattan`、`polish`、`Polish`、现代短语、`AT&T`、`R&B` 和 `Washington D|C`。项目已零警告构建；当前 Codex 执行会话未附着交互 Windows 桌面，`SendInput` 被系统拒绝且自动化桥接报告原生窗口数为 0，故真实按键验收尚未通过，不能伪称完成。
- 2026-09-06：schema 2 的候选变体模型不足以承载翻译身份，已升级至 schema 3 的 `lexeme` 模型；后续实现与验收移至 `lexeme-identity-v3-tasks-zh-CN.md`，避免把 `polish` / `Polish` 问题伪装为单一大小写优先级规则。
- 2026-09-06：真实 TSF 自动化的方法已固化到 `installation-validation-zh-CN.md`。执行时必须先完成 Release 重装；隔离 x64 WPF 宿主只为自身启用已安装 Profile，以 `SendInput` 驱动真实 TSF，再读取文本和光标作断言。候选提交使用内置数字直选；需要特定候选时通过 composition 预览定位，Tab 保留给目标编辑器抽测；交互桌面不可用时必须记录为阻塞。
- 2026-09-06：发现 schema 4 首版在候选去重与翻页时，对每个候选单独查询 `word_case_variant.canonical_case_required`。短前缀可产生数百至数千次 SQLite prepare/bind/step，直接阻塞 TSF 按键编辑会话，表现为候选框迟滞或宿主无响应。修复为词库查询批量返回 `{ text, canonicalCaseRequired }`，候选去重、分页和 Overlay 只使用本次查询随附的内存元数据；不允许在候选热路径逐项访问 SQLite。`polish` / `Polish` 双候选语义保持不变。
- 2026-09-06：测试宿主增加前台 HWND 与键盘焦点守卫。用户同时操作鼠标使测试失焦时返回退出码 `2`，脚本等待 2 秒后最多重试 3 次，只接受完整无干扰轮次。无干扰轮次覆盖了规范大小写内部候选、现代专名、`AT&T` / `R&B` 及 `Washington D|C` 的核心 TSF 状态，但当时 Overlay 未连接且没有候选弹窗；故该结果不能关闭候选 UI 验收。
- 2026-09-06：完成大小写前缀弱提升和独立学习身份。完整精确匹配仍优先；例如 `polis` 本身是词典精确项，之后才按输入样式排列 `polish` / `Polish`。普通项使用 `ordinary|<lowercase>`，强制规范大小写项使用 `canonical|<exact text>`；旧无前缀键只迁移到普通项。学习频率可在同一候选层覆盖弱大小写提示，但不能跨越精确、续写、前缀和近似层级。
- 2026-09-06：Overlay 监听器新增可观察的 Ready/Completion 生命周期；致命监听异常写入诊断并关闭进程，释放单实例 Mutex。候选与翻译窗口使用稳定原生标题，真实集成测试逐场景断言窗口可见、尺寸有效和所属进程，并核对当前 client 的 WPF 消息。最终 Release 覆盖安装后完整轮次通过，且 `HKCU\Software\Enput Method\CandidateFrequency` 测试前后完全一致。
