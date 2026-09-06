# 词条身份模型与权威词源迁移任务书

创建日期：2026-09-06。

## 用户提出的核心判断

早期将所有英文文本先转为小写检索键，再把不同来源的释义合并，便于快速导入，但把“检索便利”错误地当成了“词条身份”。`polish` / `Polish` 已证明这个模型会让候选、词性、中文释义与英文释义互相污染；继续增加例外条目只会提高维护成本。

结论：不能根据拼写、首字母或输入样式推断词性、专名或规范大小写。词源必须显式提供词条 ID、规范词形、类别、词义、来源与许可；输入的小写键仅作为召回索引。

## 目标模型

```text
输入文本 -> 归一化索引 -> 精确规范词形 -> lexeme ID -> 词义/翻译
```

| 词条 ID | 规范词形 | 类别 | 示例释义 |
| --- | --- | --- | --- |
| `enput:lexeme:polish:common` | `polish` | `common-word` | 擦亮、润色、光泽、上光剂 |
| `enput:lexeme:Polish:language` | `Polish` | `language-ethnonym` | 波兰的、波兰人、波兰语 |

翻译窗口必须接收当前候选的精确词条，而不是将显示文本小写后汇总查询。对尚未迁移的旧来源，只有在没有精确词条记录时才允许按旧小写键回退。

## 实施阶段

| 编号 | 内容 | 边界 | 状态 |
| --- | --- | --- | --- |
| L-01 | SQLite schema 3 增加 `lexeme` 与按 `lexeme_id` 关联的释义表 | 保存规范词形、归一化键、类别、来源、许可与优先级；不靠猜测填充 | 已安装验证通过 |
| L-02 | 将当前受版本控制的现代/大小写词条迁入词条 ID 表 | `polish` / `Polish` 必须独立，F3 不得混合；旧 `translation_*` 仅作未迁移条目回退 | 已安装验证通过 |
| L-03 | 修正旧翻译回退查询 | 有精确大小写文本时只合并精确文本对应来源；不得继续合并大小写不同记录 | 已安装验证通过 |
| L-04 | 更新审计、安装断言、任务书和开发总账 | 审计明确报告逐条来源/类别覆盖，不把旧表伪装成已迁移 | 已完成 |
| L-05 | 引入权威结构化词源并全量重建 | 通用词保留 WordNet/ECDICT 回退；词典词形/词义候选为 Wiktionary 数据导出，实体候选为 Wikidata，地名候选为 GeoNames；先确认许可、体积、增量策略与打包影响 | 待用户确认来源组合后实施 |
| L-06 | 候选呈现规范大小写约束 | `word_case_variant.canonical_case_required` 是词库显式字段，不从拼写推断；同一归一化键可同时保留普通词项与“规范大小写不可改写”项，Overlay 以加粗和主题强调色标识后者 | 已完成：数据、协议、WPF 样式和真实候选窗口验收通过 |
| L-07 | 大小写前缀弱提升与独立自适应身份 | 精确匹配优先；前缀层内优先输入大小写一致项，随后允许独立学习频率覆盖；旧频率只迁移到普通项 | 原生回归、覆盖安装和真实 TSF/Overlay 验收通过 |

## 词源边界

- WordNet 可继续提供一般词义与短语补充，但其当前导入无法作为规范大小写或专名类别来源。
- ECDICT 可继续提供广覆盖英中释义，但不能单独决定词条身份或大小写语义。
- Wiktionary 适合提供 `polish` / `Polish` 等区分词形和词义；其模板解析、许可和署名需要单独落实。
- Wikidata 适合人物、机构、语言和品牌等实体的规范标签与稳定 ID；GeoNames 适合地名。它们不能代替普通词典词义。
- 本轮不下载或声称已导入新的大型外部词源；先修正数据模型和已有受版本控制词条的身份关系。

## 验收

1. 输入并高亮 `polish` 后，F3 只显示普通词义，不得出现波兰语、波兰人、哥白尼或地名。
2. 输入并高亮 `Polish` 后，F3 只显示语言/民族/形容词义，不得出现上光剂、擦亮或润色。
3. 已安装数据库含 schema 3、两个不同 lexeme ID、来源与类别；审计报告将它们计入逐条覆盖。
4. Release 构建、安装、`--verify-lexicon`、`--audit-lexicon` 通过；在附着交互 Windows 桌面时，运行 `scripts\run-tsf-integration-tests.ps1 -Configuration Release`，由隔离 x64 WPF 宿主为自身启用已安装 Profile，以 `SendInput` 和真实文本/光标断言验收。测试窗口不可附着时必须记录为阻塞，不能用其它检查替代。

## 实施记录

- 2026-09-06：schema 3 新增 `lexeme`、`lexeme_translation_entry`、`lexeme_translation_part`、`lexeme_translation_meaning` 与 `lexeme_translation_example`。受控现代条目以稳定 ID、二进制精确规范词形、类别、来源、许可和优先级写入；`polish` 与 `Polish` 分别使用 `enput:lexeme:polish:common` 和 `enput:lexeme:polish:language`。
- 2026-09-06：TSF 的 F3 查询先以候选的精确规范词形查询 `lexeme`；找到即只读取该词条关联释义。旧 `translation_*` 回退查询也改为：存在精确文本时只读取精确文本的来源，只有不存在精确文本时才兼容旧小写键。
- 2026-09-06：`--verify-lexicon` 精确断言两个 `polish` 词条 ID、正反向释义隔离；`--audit-lexicon` 报告词条表的来源、类别、许可覆盖及孤立关系。规范词形与可输入索引可因连字符、句点等不同，审计将此列为信息项而非错误。安装验证结果补记于此任务书和开发总账。
- 2026-09-06：最终 Release 包重装通过；已安装 `--verify-lexicon` 通过稳定 ID 与释义隔离断言。audit 显示 schema 3、31 个词条、逐条来源/类别/许可覆盖均为 31，所有无效和孤立关系均为 0。真实 TSF 自动化通过候选、紧凑短语、符号组合、数字直选和 `Washington D|C` 光标回归。
- 2026-09-06：用户确认候选中的特殊样式只表示“规范大小写不可自动改写”，不表示内部词条 ID 或由输入法猜测出的语义。schema 4 为受控大小写候选新增显式 `canonical_case_required`；`polish` 是普通项，`Polish` 是强制规范大小写项。候选协议将传递该样式标志，F3 仍按当前候选的精确词条查询。Release 包经 UAC 重装后部署新 TSF DLL，系统载荷校验与已安装 `--verify-lexicon` 均通过；本会话 `SendInput` 被拒绝，候选视觉自动化待具备交互桌面时执行。
- 2026-09-06：真实 TSF 自动化的可复用方法已写入 `installation-validation-zh-CN.md`。普通候选提交使用内置数字直选，以避免 WPF 宿主 Tab 路由造成假阴性；Tab 保留给实际目标编辑器抽测。需要特定候选时，测试通过 composition 预览定位，而不依赖词库排序。
- 2026-09-06：真实桌面集成测试增加失焦识别和重试。用户同时操作鼠标导致其它窗口取得前台时，该轮以退出码 `2` 作废；最终无干扰轮次通过 `polish` / `Polish` 内部候选、现代专名、符号 composition 与候选预览光标编辑。随后发现该轮没有候选弹窗，日志为 `overlay-not-connected`；撤回完整 UI 通过结论，Overlay 显示必须在新增可见性门禁后重验。
- 2026-09-06：候选频率身份从单一小写文本拆为 `ordinary|<lowercase>` 与 `canonical|<exact text>`。旧 `polish` 频率只归入普通词条，避免历史数据同时提升 `Polish`；同一身份的新旧键并存时取较大值。输入大小写只作为前缀层的弱排序提示：`polis` 的下一项为 `polish`，`Polis` 的下一项为 `Polish`，而完整 `polish` / `Polish` 各自保持精确第一项。
- 2026-09-06：Overlay 修复“进程存在但监听任务已故障”的生命周期。监听 Ready/Completion 可观察，`UnauthorizedAccessException` 等致命异常会记录并触发 WPF 退出，单实例 Mutex 随之释放；协议测试验证异常传播，进程级自动化再启动隔离的坏实例，断言退出码和 Mutex 释放，并由健康实例接管同一测试 pipe 收到 `ready`。真实集成宿主用稳定窗口标题枚举候选/翻译窗口，验证当前 client 的 pipe 消息，并在进程环境中禁用学习持久化后比较注册表快照。最终安装运行时输出 `Installed Enput TSF integration tests passed, including visible Overlay windows.`。
