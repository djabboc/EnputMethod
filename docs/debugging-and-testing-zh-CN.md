# 调试与测试方案

最后更新：2026-08-30。本方案将“构建成功”“自动化通过”“系统安装已验证”“真实输入法宿主已验收”严格分开。任何一层通过都不能自动代表更高层通过。

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
- 原生候选定位：顶部保持下方、底部翻到 composition 上方、右侧收束到工作区。

### 3.2 Overlay 协议和 WPF 自动化

```powershell
.\scripts\run-overlay-tests.ps1 -Configuration Release
```

该脚本先运行原生测试，再构建 Overlay，并执行：

- `EnputMethod.Overlay.ProtocolTests`：JSON Lines、连接/断连、`clientId`、`stateId`、诊断消息。
- `EnputMethod.Overlay.AutomationTests`：候选页脚三段布局、composition 矩形协议、顶部/底部/右侧避让、候选/翻译隐藏、前景编辑器仲裁、非激活交互、emoji 彩色资源、18pt 换算、翻译富文本、Copy 的成功勾选和超时恢复、尺寸保存和旧消息不得覆盖用户手动尺寸。

### 3.3 完整发布与安装

```powershell
.\scripts\run-regression.ps1 -Configuration Release
```

顺序为：Visual Studio MSBuild 构建安装器和其依赖 -> 原生测试 -> Overlay 测试 -> `install-and-verify.ps1`。最后一步会弹出 UAC；接受后会执行安装、部署验证和 SQLite 词库验证。

`install-and-verify.ps1` 使用 `Start-Process -Wait -PassThru` 检查 GUI 安装器的显式退出码，避免 PowerShell 对 WinExe 的 `$LASTEXITCODE` 误报。它还调用安装器的 `--verify-lexicon`，验证 `enput.db`、ready 标记、schema、基本词前缀、emoji、翻译、句子短语，以及 `new york`、`computer science`、`machine learning`、`contract law` 等领域短语。

## 4. 安装后排查顺序

1. 运行完整回归并确认退出码为零。
2. 查看 `%LOCALAPPDATA%\Enput Method\UserData\install-verification.log`。成功记录应包含 SQLite 词库验证通过（日志原文为 `SQLite lexicon verification passed`）。
3. 确认 `C:\Program Files\Enput Method\Resources\enput.db`、`enput.db.ready` 和 `enput.ico` 存在；验证 Enput Profile 的 `IconFile` 也在该 Resources 目录且 `IconIndex=0`。
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
| 后续联想取消 | 输入 `hello` 后按 Tab，看到 `world` 等后续候选后分别按 Escape、左 Shift、右 Shift | 已提交的 `hello`（及配置的尾随空格）保持不变；只隐藏后续候选和翻译窗，不向编辑区写入空文本；随后输入字符或空格不应复活旧候选。 |
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

## 11. 一站式包与资源边界验证（2026-08-29）

修改安装、卸载、资源或发布脚本后，先运行：

```powershell
.\scripts\build-local-package.ps1 -Configuration Release
.\scripts\publish-release.ps1 -Version <version>
```

前者必须通过安装器的 `--verify-package`，后者生成的 ZIP 解压后只允许有一个 `EnputMethod-<version>-win-x64` 顶层目录；安装器和卸载器必须直接位于其根目录，不能被额外的 `Release` 目录包住。验证 manifest 包含 `payload\Resources\themes\*.json`，主题误落在 Resources 根目录时会被拒绝。

系统安装验证应使用 `run-regression.ps1` 或 `install-and-verify.ps1`。成功判据为已注册 DLL、Overlay 和静态 `enput.db` 位于 `C:\Program Files\Enput Method`，用户配置/日志位于 `%LOCALAPPDATA%\Enput Method\UserData`。升级前后对 UserData 的 `config.json` 与 `shortcut.json` 计算 hash，必须保持相同；AppData 根目录不应重新生成静态数据库、主题或发布词表。卸载后 Enput 的 TSF Profile、分类和 COM 注册必须不存在；Program Files 产品目录、DLL、Overlay 和静态词库应保留，UserData 与频率学习也应保留。

包完整性、原生逻辑、协议和受控 WPF 自动化不等于真实输入法已被目标应用选中。系统安装会显示 UAC，真实宿主仍需关闭重开并在语言栏确认 Enput。

## 12. 安装/卸载故障与输入法恢复（2026-08-29）

安装器、SQLite 验证和包完整性验证分别写入 `%LOCALAPPDATA%\Enput Method\UserData\install-verification.log`、`lexicon-verification.log`、`package-verification.log`。不要再用最后一次包检查日志判断安装注册的结果。

`0x80004002` 是 `E_NOINTERFACE`。当前 TSF DLL 会把失败阶段一并写入安装结果：Program Files 部署、TSF profile manager、服务注册、中文 profile 添加/启用、TSF category manager、键盘分类或 immersive 分类。排查时先关闭所有使用 Enput 的应用、重启 Windows，再从新的 `artifacts\local\Release` 包以管理员权限运行安装器；读取 `install-verification.log` 中的 HRESULT 与阶段，不要只记录 UI 的首行。

卸载前，Enput 可能已被 Explorer、编辑器、浏览器或聊天客户端作为进程内 DLL 加载。常规卸载只注销 Profile、分类和 COM 注册，不删除 `C:\Program Files\Enput Method` 中的 DLL、Overlay 或资源；已打开宿主因而始终保有依赖，新应用也不能再激活 Enput。卸载不需要关闭应用或重启；遗留运行文件会由后续安装复用。

若需要立即恢复中文输入，先注销 **Enput 自己** 的 CLSID/Profile，并将默认输入法设为 Microsoft Pinyin 的 `0804:{81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E}{FA550B04-5AD7-411F-A5AC-CA038EC515D7}`。这不修改微软拼音的注册。随后重启 Windows，使所有仍映射旧 Enput DLL 的进程卸载模块。
## 13. 安装事务、退出码与词库占用（2026-08-29）

`Install Enput Method.exe --install-and-verify` 必须以实际安装结果退出：成功为 `0`，失败为非零。WPF 无窗口模式必须调用 `Shutdown(Environment.ExitCode)`；只设置 `Environment.ExitCode` 再普通退出，PowerShell 的 `Start-Process -Wait -PassThru` 可能得到错误的成功结果。

安装全程持有 `Local\EnputMethod.Installer.Installation.v1` 互斥锁。第二个安装器不得与第一个并发操作 `Program Files\Enput Method\Resources`；应明确提示正在安装，而不是碰撞固定的 `enput.db.pending`。数据库临时文件使用带 GUID 的私有名称。

顺序必须是：停止/部署 Overlay，初始化用户配置，准备静态 SQLite 词库，最后注册 TSF。若先注册 TSF，已运行宿主可能立即拉起 Overlay 并占用 `enput.db`，使词库替换失败。升级遇到无 `enput.db.ready` 但可验证的既有数据库时，仅补完成标记并保留数据库；已含 ECDICT 或 CC-CEDICT 来源的数据不得重复联网下载。2026-08-29 的 UAC 安装验证已通过。
## 14. 无感卸载与立即重装验证（2026-08-29）

常规卸载的完成条件是 Enput 的 TSF Profile、分类和 COM 注册已经移除，不是删除安装目录。输入法 DLL 以进程内方式加载；保留版本化 DLL、Overlay 和 `Resources` 可避免已打开宿主在退出前丢失数据库或 UI 依赖。保留的文件不会被新应用激活，因为 COM/Profile 入口已移除。

卸载器支持 `--uninstall-and-verify` 无界面验证：必须以非零退出码报告注销失败，并写入 `uninstall-verification.log`。2026-08-29 已执行真实 UAC 链路：注销后确认 DLL、Overlay、`enput.db` 仍在、Enput CLSID 已删除、Microsoft Pinyin 默认项不变；随后不重启直接运行 `install-and-verify.ps1`，安装和 SQLite 验证通过。
## 15. TSF STA 线程与失败回滚（2026-08-30）

WPF 的 `Task.Run` 使用 MTA 线程。TSF `ITfInputProcessorProfiles` 和 `ITfCategoryMgr` 注册必须在 STA 中执行；GUI 安装若把原生调用放在 MTA，`CoInitializeEx(COINIT_APARTMENTTHREADED)` 返回 `RPC_E_CHANGED_MODE`，旧代码却继续执行，可能在分类阶段失败。无界面 WPF 启动线程通常是 STA，因此不能把其成功结果当作 GUI 成功的证据。

安装器与卸载器的按钮和 `--install-and-verify` / `--uninstall-and-verify` 现在统一经专用 STA 工作线程调用原生 TSF。原生层在 STA 初始化失败时立即返回，不做任何注册变更。GUI 结果也会覆盖写入 `install-verification.log`，以免日志停留在上一次成功结果。

TSF 注册是事务：先注册服务与语言 Profile，再注册核心键盘分类，最后启用 Profile；`TF_E_ALREADY_EXISTS` 对键盘分类视为幂等成功，immersive 分类只作为可选能力。任一失败会删除 Enput 自己的 Profile、分类、HKCU/HKLM `CTF\\TIP\\{9C8945D5-...}` 根和 COM CLSID，保留原始 HRESULT/阶段。绝不触及 Microsoft Pinyin 的 `{81D4E9C9-...}`。本轮已执行真实 UAC：STA 卸载清理残留 -> 检查 COM/TIP 不存在 -> STA 重装 -> 安装、SQLite 与 Microsoft Pinyin 默认项验证通过。

安装器在报告成功前还会读取 HKLM 中 Enput 自己的 `LanguageProfile\\0x00000804\\{55F31085-...}` 与核心键盘分类 `{34745C63-...}`。因此 COM DLL 虽已注册、但输入法 Profile 或核心分类缺失的状态会被判定为安装失败，而不会误报成功。

`scripts/uninstall-and-verify.ps1` 用发布目录中的无界面卸载器执行 UAC 测试：确认 Enput 的 HKCU/HKLM COM 与 TIP 根均不存在，Program Files 中为已打开宿主保留的 DLL、Overlay、SQLite 静态词库仍在，用户 `config.json` 未变，并比较卸载前后所有非 Enput 的 Windows 输入法项。`scripts/run-regression.ps1` 现在执行“安装 -> 卸载 -> 立即重装”，覆盖不重启重装和 STA 工作线程路径。
