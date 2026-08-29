# 当前技术栈

最后更新：2026-08-29。本文描述当前已安装、已验证版本的实际技术边界，不把计划中的替换方案当作现状。

## 1. 平台与构建基础

| 类别 | 当前选择 | 作用与约束 |
| --- | --- | --- |
| 操作系统 | Windows 10/11 x64 | TSF、COM 注册、WPF、Windows `winsqlite3.dll` 都是 Windows 专用。 |
| 原生工具链 | Visual Studio 2022 Build Tools，MSVC v143，C++20 | 编译 x64 `EnputMethod.Tsf.dll` 与原生单元测试。必须安装 Desktop development with C++ 工作负载。 |
| 托管运行时 | .NET 9 SDK，`net9.0-windows` | 构建 WPF Overlay、安装器、卸载器和 WPF 自动化测试。纯协议测试使用 `net9.0`。 |
| 构建系统 | MSBuild + SDK-style .csproj | 原生 `.vcxproj` 必须由 Visual Studio 的 `MSBuild.exe` 构建；`dotnet build` 不能单独导入 C++ targets。 |
| 目标架构 | x64 | 当前 TSF DLL 仅能被 x64 编辑器加载；x86 应用需要另行提供 x86 DLL。 |
| 权限模型 | 安装/卸载请求管理员权限 | COM 和 TSF profile 写入 HKLM；用户词库和配置在 LocalAppData，无需管理员权限。 |

推荐完整构建命令是：

```powershell
.\scripts\run-regression.ps1 -Configuration Release
```

它用 Visual Studio MSBuild 生成安装包，再运行原生、Overlay 自动化和系统安装验证。不要以 `dotnet build EnputMethod.Installer.csproj` 的失败来判断项目不可构建：该命令不会自动配置 C++ `VCTargetsPath`。

## 2. 解决方案与项目

| 项目/目录 | 技术 | 职责 |
| --- | --- | --- |
| `EnputMethod.Tsf` | C++20、Win32、COM、TSF、SQLite C API | 进程内输入法 DLL；捕获键盘、维护 composition、查询词库、生成候选、最终写入编辑器。 |
| `EnputMethod.Overlay` | C#、WPF、.NET 9、Named Pipes | 独立候选窗和翻译窗；只渲染数据、处理鼠标意图，绝不直接写入目标编辑器。 |
| `EnputMethod.Installer` | C#、WPF、.NET 9、P/Invoke SQLite | 建库/迁移、数据导入、系统级注册、部署与安装完整性检查。 |
| `EnputMethod.Uninstaller` | C#、WPF、.NET 9 | 注销 TSF/COM 并删除已部署组件。 |
| `EnputMethod.Tsf.Tests` | C++20 控制台程序 | 原生纯逻辑回归，例如数字选词、JSON Unicode 代理项、翻译文本清洗和保序匹配。 |
| `EnputMethod.Overlay.ProtocolTests` | C# .NET 9 控制台程序 | 验证 JSON Lines 协议、客户端连接、消息和诊断流程。 |
| `EnputMethod.Overlay.AutomationTests` | C# WPF 测试程序 | 验证候选窗布局、前景仲裁、命中区域、字号换算、翻译富文本和大小持久化。 |
| `EnputMethod.Overlay.TestHost` | C# WPF 测试宿主 | 为 Overlay 交互/视觉调试提供独立受控进程。 |
| `scripts` | PowerShell | 统一构建、测试、安装验证和 Emoji 资源导入。 |

安装器项目是发布入口。它在构建后将 TSF DLL、Overlay 发布输出、默认配置、主题、词表、SQLite seed 数据库和 WordNet 词组源放入同一个安装包目录。

## 3. 输入法核心：TSF 与 COM

`EnputMethod.Tsf.dll` 是 in-process COM 服务器，主要实现：

- `ITfTextInputProcessorEx`：激活、停用和线程管理器对接。
- `ITfKeyEventSink`：处理按键、决定是否吃掉按键或透传给应用。
- `ITfCompositionSink`：维护未确认的 TSF composition。
- `ITfEditSession`：所有编辑操作在对应 TSF context 的读写 edit session 中完成。

输入字母后，服务创建或更新活动 composition，编辑器内的前缀仍是未确认文本。候选确认时同一 composition 范围会原子替换成最终候选；取消时 composition 终止且不提交。这就是记事本等应用在候选存在期间显示下划线的原因。Enput 不设置 `ITfDisplayAttribute`，下划线的颜色和式样由宿主应用决定。

候选窗的鼠标点击只会回传 `selectCandidate`、`previousPage`、`nextPage` 或 `dismiss` 意图。C++ 服务检查来源 `clientId` 和单调递增的 `stateId` 后，再由自己创建异步 TSF edit session 写入文本。这个约束避免过期 UI 操作写入错误的编辑器。

Windows profile 位于中文输入法组；系统已有的 `Ctrl + Shift` 轮换行为可以切换到 Enput。服务不全局占用 `Ctrl + Space`。

## 4. UI：独立 WPF Overlay

候选与翻译 UI 已从原生 DLL 拆分为 `EnputMethod.Overlay.exe`：

- 使用 WPF 的非激活窗口，鼠标操作不会抢走目标编辑器的输入焦点。
- 每个 TSF 连接持有唯一 `clientId`，每个连接分别拥有候选窗和翻译窗。
- 仅前景编辑器对应的窗口可见、可点击；失焦或断连会隐藏/关闭对应 Surface。
- 启动后在 Application Idle 阶段创建屏幕外的隐藏候选窗进行预热，避免首次真实输入把 WPF 建窗延迟暴露给用户。
- 候选窗采用三段页脚布局：`<` 固定左侧，页码在中间，`>` 固定右侧；按钮有独立命中区域、tooltip 和悬停色。
- 翻译窗使用只读 `RichTextBox + FlowDocument`，用不同 Run 渲染标题、语言标签、词性、义项和例句；支持滚动、鼠标选择、右键 Copy、四边/四角缩放和尺寸防抖持久化。
- Emoji 候选优先显示随安装包分发的 Twemoji PNG，而不是依赖宿主字体的彩色 Emoji 支持。

WPF 的字号单位是 DIP，而配置的 `fontSize` 是 pt。Overlay 固定使用 `pt * 96 / 72` 转换，所以默认 `18pt` 以 `24 DIP` 渲染。

## 5. TSF 与 Overlay 通信

| 方面 | 当前实现 |
| --- | --- |
| 传输 | 当前用户本机 Named Pipe：`\\.\pipe\EnputMethod.Overlay.v1`。 |
| 格式 | UTF-8 JSON Lines；一行一个完整消息。 |
| 主消息 | `candidates`、`translation`、`hide`；Overlay 回传选择、翻页、dismiss 等 action。 |
| 顺序保护 | 每个发布批次带 `clientId` 与递增 `stateId`；双方忽略不匹配和旧状态。 |
| 性能策略 | Pipe 连接、确认与发送不阻塞 TSF 按键路径；连接失败短退避重试，安装更新期间较长退避。 |
| 诊断 | 原生与托管 Overlay 使用同一个 `Local\EnputMethod.OverlayDiagnostics.v1` 命名互斥体协调的诊断记录。 |

候选消息携带候选列表、页码、选中索引、屏幕位置、主题、模式标记和配置快照。翻译消息携带结构化翻译视图及候选窗边界，Overlay 据此将翻译窗放在候选窗侧方并约束到工作区。

## 6. 数据、检索与许可

运行时词库只使用 `C:\Program Files\Enput Method\Resources\enput.db`，以 Windows 自带 `winsqlite3.dll` 查询。没有 JSON/JSONL 运行时回退。JSON 只保留给用户可编辑设置：`%LOCALAPPDATA%\Enput Method\UserData` 中的 `config.json`、`shortcut.json`；主题是 Program Files 下的静态资源。

| 数据 | 运行时位置/表 | 用途 |
| --- | --- | --- |
| `dictionary.txt` | `C:\Program Files\Enput Method\Resources` 普通 UTF-8 文本 | 有序单词前缀候选；发布升级只补齐缺失文件。 |
| `words` | SQLite | 已导入的基础单词索引。 |
| `suggestions` | SQLite | 关联短语、多词短语、下一词建议。 |
| `emoji`、`emoji_keyword` | SQLite | Emoji、关键词、优先级。 |
| `translation_*` | SQLite | ECDICT、CC-CEDICT 和补充翻译记录。 |
| `metadata` | SQLite | schema 版本、`builtinPhraseVersion` 等迁移标识。 |
| 候选频率 | `HKCU\Software\Enput Method\CandidateFrequency` | 自适应候选排序；配置关闭后不再读取/写入学习数据。 |

词组由安装器导入。公开 Princeton WordNet 3.1 官方归档被机械提取为 62,319 条二至五词 lemma，安装时一次性插入 `suggestions`；高优先级补充包含 `new york`、`machine learning`、`software engineering` 等。`wordnet-phrases.txt` 只作为安装源，TSF 不会在运行时读取它。具体来源、hash 与署名在 `EnputMethod.Installer/WORDNET-ATTRIBUTION.txt` 和 [dictionary-sources-zh-CN.md](dictionary-sources-zh-CN.md)。

检索优先级为精确匹配、短语续写/前缀匹配，再到长度至少三字符的保序近似匹配。紧凑短语比较忽略空格，因此 `newyork` 可匹配 `new york`，`machinelearning` 可匹配 `machine learning`。Emoji 关键字比较还忽略下划线和连字符。

安装/迁移采用 pending 数据库、事务、验证、原子替换和 `enput.db.ready` 标记。只有成功后才会删除旧 JSON/JSONL 词库文件。

## 6.1 当前候选与联想算法

这里的“联想”分为两条独立路径：**输入尚未确认时的候选生成**，以及**选词提交后的后续建议**。当前没有神经网络、LLM、词向量、语言模型概率或在线推理；所有结果来自本地有序词表、SQLite `suggestions` 和用户的选词频率。

### 输入中的候选生成

每次输入改变时，TSF 将输入小写化，分别收集四个互不混合的候选层，再按固定层级合并、大小写去重：

| 层级 | 数据与规则 | 示例 |
| --- | --- | --- |
| 1. 精确匹配 | `words.normalized == input`，或短语 candidate 与 input 相同。 | `can` 优先于 `candle`。 |
| 2. 短语续写 | `suggestions.kind = 1` 的 candidate 以输入加空格开头，且 trigger 与输入相同。 | 输入 `can` 时，`can i help you?` 属于本层。 |
| 3. 普通前缀 | 有序词表和 SQLite 短语 candidate 以输入开头。短语按 `priority DESC, ordinal` 查询；普通单词保持词典 ordinal。 | `new` 可得到 `new york`，普通单词如 `newspaper` 同属本层。 |
| 4. 保序近似匹配 | 仅输入至少 3 个字符后启用。SQL 先以首字符和 `LIKE %a%b%c%` 缩小集合，再逐字符验证字符顺序；短语比较忽略空格，Emoji 还忽略 `_`、`-`。 | `hpy -> happy`、`newyork -> new york`、`machinelearning -> machine learning`、`pignose -> pig_nose`。 |

合并顺序永远是 `精确 -> 短语续写 -> 前缀 -> 近似`。每一层内部以不区分大小写的文本去重；前一层已经出现的候选不会在后一层重复。近似查询分别限制普通词 96 条、短语 48 条，以避免短输入把 SQLite 查询扩大成全表扫描。所有结果随后按 `candidateCount` 分页，当前配置上限为 9 项/页。

`adaptiveCandidateRanking=true` 时，`HKCU\Software\Enput Method\CandidateFrequency` 中已选过的候选只会在**所属层级内部**按降序频率稳定前移；未选过的候选保持原词典/数据 ordinal。因此学习不会把 `hpy` 的近似结果挪到精确 `happy` 或前缀结果之前。设为 `false` 后不重排，也不继续记录新的选词频率。

### 选词后的关联建议

选中并提交一个候选后，服务调用 `QuerySuggestions(committedText)`：

1. `kind = 0` 的记录是直接后续候选，原样显示。
2. `kind = 1` 的完整短语必须以 `committedText + " "` 开头；服务只显示后缀，避免重复提交。例如提交 `how` 后，`how are you` 显示为 `are you`，再次选择后得到 `how are you`，而不是 `how how are you`。
3. 若该词没有任何关联记录，才显示固定兜底序列：`the`、`to`、`and`、`a`、`is`、`of`、`for`、`in`、`that`。这不是上下文预测，因此它不能根据上文区分 `Can I help you?` 等语义。
4. 此阶段也可在结果内部应用用户频率排序，但不会产生新的词或跨词预测。

因此，当前系统的“句子联想”是**手工/语料导入的短语和后续词查表**，不是上下文语言模型。WordNet 导入扩大了多词短语覆盖，但它本身不提供句子频率、语法或上下文概率。要实现真正基于前文的句子补全，需要新增 n-gram/语料频率、语境窗口和排序模型，属于后续功能而不是当前行为。
## 7. 配置与快捷键

用户配置目录是 `%LOCALAPPDATA%\Enput Method\UserData`。

- `config.json`：候选数、纵横布局、选词后空格、候选排序、字体、透明度、主题、翻译语言、翻译框初始尺寸。
- `shortcut.json`：每个动作映射一个或多个按键。默认 F2 为 Emoji，F3 为翻译，`cancelComposition` 为 `["Escape", "Shift"]`。
- `themes/*.json`：候选与翻译窗口的颜色、边框、圆角、内边距、行高、阴影、滚动条和语义文本色。

安装器仅在 UserData 中的配置或快捷键文件不存在时复制默认文件，绝不覆盖已有用户值。因此用户移除 `Shift` 后，更新不会重新强制加入它。

## 8. 部署、注册与更新

安装器使用管理员权限完成以下工作：

1. 清除旧的用户级 COM 注册，避免它覆盖机器级注册。
2. 将 DLL 复制到 Program Files 下以时间戳和大小派生的版本化文件名。
3. 写入 HKLM CLSID InprocServer32 注册。
4. 注册 TSF text service、`zh-CN` language profile、键盘与 immersive-support category。
5. 复制 Overlay 和资源，停止可能正在运行的旧 Overlay，再对部署文件进行 hash 一致性验证。
6. 创建或升级用户配置、主题、SQLite 词库和数据归属文件。

版本化 DLL 允许旧编辑器进程仍映射旧 DLL 时完成更新。但已打开的记事本、VS Code、浏览器或 ChatGPT 仍必须完全关闭并重新打开，才能加载新 DLL。仅切换输入法不够。

## 9. 已知边界

- 当前没有 x86 产物。
- 系统全局输入法切换属于 Windows；自动按键脚本不能证明测试时真正切换到了 Enput。
- 目标编辑器字体可能缺少较新的 Emoji，例如 `U+1FA9A`，此时提交的码点正确但编辑区会显示方框。
- 词典规模已显著扩展，但不等同于覆盖所有专业术语、例句或多语言释义；数据许可和来源必须持续记录。
- WPF UI 自动化可验证协议和布局，不替代真实跨应用 TSF 交互验收。

## 10. 一站式发布与运行目录（2026-08-29）

发布不要求终端用户安装 Visual Studio 或 .NET SDK。`scripts\publish-release.ps1 -Version <version> -Zip` 产出自包含的 Windows x64 ZIP；用户在解压根目录运行 WPF `Install Enput Method.exe` 或 `Uninstall Enput Method.exe`。两个启动器各自携带相邻的托管 DLL、deps 和 runtimeconfig，运行时资源位于共同的 `payload`。

安装时需要管理员权限：原生 TSF 注册函数把 DLL 复制为 `C:\Program Files\Enput Method\EnputMethod.Tsf.<build-id>.dll` 并写入 HKLM；WPF 安装器把 Overlay 与静态资源部署到同一产品根。静态 SQLite、主题、词表、Emoji 资源和归属文件都在 `C:\Program Files\Enput Method\Resources` 或 `Overlay`，不再放入 AppData。

`%LOCALAPPDATA%\Enput Method\UserData` 是唯一的文件型用户状态边界，包含 `config.json`、`shortcut.json`、`install-verification.log`、`overlay-diagnostics.log`。这与 `HKCU\Software\Enput Method\CandidateFrequency` 的学习状态一起，在升级和默认卸载时保留。安装/卸载窗口使用 WPF `ProgressBar` 和后台 `Task`，使复制、SQLite 初始化、注册与注销期间 UI 保持响应。
