# 调试与测试方案

最后更新：2026-08-29。本方案将“构建成功”“自动化通过”“系统安装已验证”“真实输入法宿主已验收”严格分开。任何一层通过都不能自动代表更高层通过。

## 1. 测试层级与结论标准

| 层级 | 证明什么 | 不能证明什么 |
| --- | --- | --- |
| 静态检查 | 工作区差异无空白错误、文件被正确打包 | 不证明运行时行为。 |
| 原生单元测试 | 可脱离 TSF 宿主验证纯算法和编码逻辑 | 不证明 Windows 已选择 Enput profile。 |
| Overlay 协议/自动化 | Pipe 协议、WPF 窗口逻辑、前景仲裁和布局 | 不证明原生 DLL 已被应用重新加载。 |
| 发布构建 | C++ DLL、WPF Overlay、安装包能在 Release x64 一起生成 | 不证明安装权限和已安装文件正确。 |
| 系统安装验证 | 注册、部署文件、SQLite 词库和默认配置正确 | 不证明手工输入来自 Enput 而非微软输入法。 |
| 真实宿主验收 | 在已确认激活 Enput 的应用中，组合、候选、鼠标、快捷键和 UI 的最终体验 | 受应用、字体、Windows 缓存和焦点影响，需要人工观察。 |

所有 bug 报告先写清楚属于哪一层，并记录：构建提交、Windows 版本、应用和版本、是否重开应用、是否确认 Enput 已激活、输入序列、预期、实际、截图/日志位置。

## 2. 环境检查

要求：Windows x64、Visual Studio 2022 Build Tools 的 C++ Desktop 工作负载、MSVC v143、Windows SDK、.NET 9 SDK、管理员权限。

检查命令：

```powershell
Test-Path "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
dotnet --info
```

原生项目不要用 `dotnet build` 作为唯一检查。若出现 `Microsoft.Cpp.Default.props` 或 `VCTargetsPath` 缺失，说明使用了错误的构建入口；改用 `scripts\run-tsf-tests.ps1` 或 `scripts\run-regression.ps1`，它们调用 Visual Studio MSBuild。

## 3. 日常构建与快速回归

### 3.1 原生 TSF 逻辑

```powershell
.\scripts\run-tsf-tests.ps1 -Configuration Release
```

此脚本编译并运行 `EnputMethod.Tsf.Tests.exe`。当前覆盖：

- 顶排数字和小键盘数字的候选索引映射及越界处理。
- JSON UTF-16 代理对解码：`🔥`、`🪚`、Saint Helena `🇸🇭`。
- 翻译文本的字面 `\\n`/`\\r\\n` 正规化。
- 词性句点与 `{{or}}` 标记清洗。
- 自适应候选排序与关闭排序后的字典顺序。
- 保序不完整匹配：`hpy -> happy`、`pignose -> pig_nose`、`empirestate -> Empire State Building`、`newyork -> new york`、`machinelearning -> machine learning`。

### 3.2 Overlay 协议和 WPF 自动化

```powershell
.\scripts\run-overlay-tests.ps1 -Configuration Release
```

该脚本先运行原生测试，再构建 Overlay，并执行：

- `EnputMethod.Overlay.ProtocolTests`：JSON Lines、连接/断连、`clientId`、`stateId`、诊断消息。
- `EnputMethod.Overlay.AutomationTests`：候选页脚三段布局、边缘避让、候选/翻译隐藏、前景编辑器仲裁、非激活交互、emoji 彩色资源、18pt 换算、翻译富文本、复制菜单、尺寸保存和旧消息不得覆盖用户手动尺寸。

### 3.3 完整发布与安装

```powershell
.\scripts\run-regression.ps1 -Configuration Release
```

顺序为：Visual Studio MSBuild 构建安装器和其依赖 -> 原生测试 -> Overlay 测试 -> `install-and-verify.ps1`。最后一步会弹出 UAC；接受后会执行安装、部署验证和 SQLite 词库验证。

`install-and-verify.ps1` 使用 `Start-Process -Wait -PassThru` 检查 GUI 安装器的显式退出码，避免 PowerShell 对 WinExe 的 `$LASTEXITCODE` 误报。它还调用安装器的 `--verify-lexicon`，验证 `enput.db`、ready 标记、schema、基本词前缀、emoji、翻译、句子短语，以及 `new york`、`computer science`、`machine learning`、`contract law` 等领域短语。

## 4. 安装后排查顺序

1. 运行完整回归并确认退出码为零。
2. 查看 `%LOCALAPPDATA%\Enput Method\install-verification.log`。成功记录应为 SQLite lexicon verification passed。
3. 确认 `%LOCALAPPDATA%\Enput Method\enput.db` 和 `enput.db.ready` 存在。
4. 检查 `shortcut.json` 的 `cancelComposition` 已保留预期数组；默认应为 `Escape, Shift`。
5. 关闭所有待测宿主，再重新打开。TSF DLL 在编辑器进程内加载，已打开的应用不会自动切换到新 DLL。
6. 在语言栏明确选择 Enput Method；不要用“脚本已经输入了 he”判断输入法切换成功。

安装包完整性检查包含 TSF DLL、Overlay 发布文件和关键 Twemoji 资产、默认配置、主题、`enput.seed.db`、`wordnet-phrases.txt`、`WORDNET-ATTRIBUTION.txt`。安装器还会验证 Program Files 中已部署的 DLL/Overlay 与安装包 hash 一致。

## 5. Overlay 与管道调试

当候选窗不显示、显示后失效、多个编辑器各有错误窗口或点击错误候选时，按下列顺序排查：

1. 先确认真实输入文本仍处于 Enput 的活动 composition，而非微软输入法的候选。
2. 检查已打开宿主是否重开；旧 DLL 和新 Overlay 的组合会造成协议/数据能力不一致。
3. 查看 Overlay 进程是否已在 `Program Files\Enput Method\Overlay` 启动。
4. 检查诊断事件，重点是 `client.start`、`client.connected`、`candidate.published`、`pipe.received`、`candidate.presented`、`pipe.disconnected`。
5. 对照 `clientId` 确认消息和点击来自同一宿主；对照 `stateId` 确认点击没有作用于已过期的候选页。
6. 复跑 `run-overlay-tests.ps1`。若其通过而真实宿主失败，归类为 TSF/宿主边界或部署缓存问题，不要修改纯 WPF 布局来掩盖它。

冷启动慢时，记录从首次按键到 `candidate.presented` 的时间。若延迟发生在消息已送达之后，优先检查 Overlay 首次建窗/资源加载；若消息尚未发布，检查 SQLite 查询、TSF edit session 或 pipe 连接。不要只凭主观感觉将两类延迟混为一谈。

## 6. SQLite、词库和翻译调试

运行时唯一词库是 `enput.db`。排查 F2/F3 或短语时：

1. 确认旧 JSON/JSONL 没有被当作运行时回退；它们只能作为一次性迁移输入。
2. 执行安装器 `--verify-lexicon` 或完整安装验证。
3. 检查 metadata 中 `schemaVersion` 和 `builtinPhraseVersion`。
4. 检查 `suggestions` 是否存在 `new york`、`machine learning`、`computer science`、`contract law`；检查 `emoji_keyword` 的 `fire`、`saw`、`pig_nose`；检查 translation entry 的 `braces`、`hug`。
5. 若词条存在而候选没有出现，检查候选优先级、当前模式、最少三字符的近似匹配门槛、分页和已学习排序。
6. 若 F3 内容显示 JSON 字段或字面 `\\n`，说明旧 DLL/Overlay 未重开，或翻译正文的结构化发布被破坏；不要将 source/署名字段拼入正文。

词组数据更换时必须更新 `BuiltinPhraseVersion`，否则已有数据库会因为迁移标记而跳过导入。变更后的安装必须验证升级路径，而非只删除数据库测试首次安装。

## 6.1 联想算法回归

修改候选排序、短语语料、SQLite 索引、近似匹配或频率学习时，除运行原生测试外，应按下面顺序验证层级边界：

1. 精确优先：输入完整 `can`，`can` 必须早于所有 `can ...` 短语及其它前缀词。
2. 短语续写：输入 `can`，检查已有的 `can i help you?` 等数据排在普通前缀和近似候选之前。
3. 普通前缀：输入 `new`，验证 `new york` 与普通 `new...` 词均可召回，并按数据 priority/ordinal 与用户设置排序。
4. 保序近似：仅用三个或更多字符验证 `hpy`、`newyork`、`machinelearning`、`pignose`；确认反序输入例如 `pyh` 被拒绝。
5. 后续建议：提交 `how` 后选择 `are you`，确认不会重复 `how`；对没有关系数据的已提交词，确认显示的只是固定兜底词而非伪装成语义预测的结果。
6. 学习开关：分别使用 `adaptiveCandidateRanking=true/false`，确认学习只在同一层内部重排，关闭后不再写候选频率。
## 7. 快捷键、composition 与按键测试

候选显示后，使用以下最小矩阵：

| 场景 | 输入/操作 | 预期 |
| --- | --- | --- |
| 数字选词 | `he` 后按 `1` 至 `9` | 提交当前页对应候选，页外数字透传。 |
| Tab | `he` 后按 Tab | 提交高亮候选。 |
| Escape 取消 | `he` 后按 Escape | 候选隐藏，活动 composition 不提交。 |
| Shift 取消 | `he` 后按 Shift | 与 Escape 完全等价；不遗留 `he` 或候选窗。 |
| 自定义取消键 | 从 `cancelComposition` 移除 Shift 后按 Shift | Shift 恢复普通修饰键，不取消 composition。 |
| Emoji 退出 | F2 后无输入按 Escape/Shift | 退出 Emoji 模式。 |
| 近似词组 | `newyork`、`machinelearning` | 出现带空格的目标短语。 |
| Emoji 近似 | F2 后输入 `pignose` | 命中 `pig_nose` 的 Emoji。 |
| 翻页 | Emoji/普通候选多页时按 `+`、`-` 和鼠标 `<`、`>` | 仅翻页，不将控制字符写入编辑器。 |

组合文本下划线是正常现象：Enput 维持 active composition，宿主负责绘制样式。若用户要求候选仍在而下划线消失，属于“composition 与候选状态解耦”的新架构任务，不能通过简单隐藏 UI 修复。

## 8. 真实宿主验收矩阵

每次安装新 TSF DLL 后，至少关闭重开 Notepad、VS Code、一个浏览器/ChatGPT 和一个富文本编辑器。每个应用都必须先从语言栏确认 Enput Method 已激活。

- 基础输入：字母、Backspace、中间插入、左右移动、标点、空格、Enter。
- 候选：数字键、Tab、鼠标单击、上下选择、分页、精确匹配和自适应排序开关。
- 连续建议：选择 `how` 后选择 `are you`，不得生成 `how how are you`。
- Emoji：F2、`fire`、`water`、`bucket`、`cat`、`dog`、`pignose`、`saw`；记录编辑器字体造成的方框，但另行验证提交码点。
- 翻译：F3 的 `braces`、`hug`、`block`，检查中英文、`n.`、真实换行、不显示 source、富文本滚动/复制/缩放、翻译窗不覆盖候选窗。
- 多宿主：两个记事本分别输入后切换焦点；只有前景窗口可见且可点击。
- 性能：记录第一次激活 Enput 后首次候选与后续候选的耗时，区分 TSF/数据库耗时和 Overlay 预热耗时。

## 9. 失败分类与处理

| 症状 | 首要判断 | 常见处理 |
| --- | --- | --- |
| F2/F3 没反应 | 是否为旧 DLL 或未真正切换 Enput | 关闭重开宿主，确认语言栏，重新运行安装验证。 |
| 候选窗消失/卡顿 | 是否有 pipe 断连或旧 state | 查 Overlay 诊断，重跑协议/自动化测试，检查 `clientId/stateId`。 |
| 同时两个候选窗 | 前景仲裁是否失效 | 复跑多宿主自动化，记录窗口所有者和焦点切换序列。 |
| Emoji 方框 | 目标编辑器字体是否缺字 | 检查实际 Unicode 码点；这是宿主字体兼容性而非候选查询失败。 |
| 词组不出现 | SQLite 是否导入，输入是否满足匹配门槛 | 跑 lexicon 验证，检查 `builtinPhraseVersion`，输入至少三字符。 |
| 安装成功但行为未变 | 已打开应用仍持有旧 TSF DLL | 完全退出并重开应用；版本化部署不能热替换进程内 DLL。 |
| `dotnet build` 失败于 C++ props | 使用了不带 VS C++ 环境的 CLI | 改用项目脚本或 VS MSBuild。 |

## 10. 提交前最低标准

1. `git diff --check` 通过。
2. `run-tsf-tests.ps1 -Configuration Release` 通过。
3. 修改 Overlay、协议或 UI 时，`run-overlay-tests.ps1 -Configuration Release` 通过。
4. 修改安装、注册、SQLite schema、内置词库或发布物时，`run-regression.ps1 -Configuration Release` 通过并接受 UAC。
5. 修改 TSF 键盘/组合行为时，在至少一个已重开的真实宿主中确认已选 Enput 后手工验收。
6. 数据来源、许可、迁移版本、用户可见配置和已知限制同步写入文档。

当前问题历史、人工验收清单和延后项见 [development-issue-ledger-zh-CN.md](development-issue-ledger-zh-CN.md)；配置细节见 [update-notes-zh-CN.md](update-notes-zh-CN.md)。