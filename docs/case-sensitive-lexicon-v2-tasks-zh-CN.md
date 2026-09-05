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
| C-07 | 全程自动化的真实 TSF 验收 | 独立 x64 WPF 文本宿主通过 `ITfInputProcessorProfileMgr` 仅为自身进程激活已安装 Enput，并使用 `SendInput` 验证真实按键、composition、候选提交、F3 和光标 | `scripts\run-tsf-integration-tests.ps1 -Configuration Release` 在附着交互桌面的会话中通过 |

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
- 真实宿主验收待完成：必须完全关闭并重开目标编辑器后，选中 Enput 逐项检查候选、F3 翻译、`Shift+7` 符号输入和预览左移。当前自动化环境没有可控的 Windows 应用窗口，不能把自动化按键结果作为此项证据。
- 2026-09-05：按“全程自动化验收”要求新增 `EnputMethod.Tsf.IntegrationTests` 和 `run-tsf-integration-tests.ps1`。宿主会在隔离进程内激活实际已安装的 Enput profile，测试 `Chicago`、`Manhattan`、`polish`、`Polish`、现代短语、`AT&T`、`R&B` 和 `Washington D|C`。项目已零警告构建；当前 Codex 执行会话未附着交互 Windows 桌面，`SendInput` 被系统拒绝且自动化桥接报告原生窗口数为 0，故真实按键验收尚未通过，不能伪称完成。
