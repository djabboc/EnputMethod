# 安装与联合验证流程

最后更新：2026-09-06。本文区分本地包验证、系统安装验证与真实宿主验收；前一层通过不代表后一层已通过。

## 1. 生成可用发布包

```powershell
.\scripts\build-local-package.ps1 -Configuration Release
```

该脚本使用 Visual Studio MSBuild 构建 x64 TSF、Overlay、安装器和卸载器，在 `artifacts\local\Release` 生成完整包，并用安装器的 `--verify-package` 做非修改性的 payload 检查。根目录必须直接包含：

```text
Install Enput Method.exe
Uninstall Enput Method.exe
EnputMethod.Installer.dll / .deps.json / .runtimeconfig.json
EnputMethod.Uninstaller.dll / .deps.json / .runtimeconfig.json
payload\EnputMethod.Tsf.dll
payload\Overlay\...
payload\Resources\...
```

特别检查主题位于 `payload\Resources\themes`。TSF 运行时从已安装 `Resources\themes` 读取主题，不能把主题扁平放到 Resources 根目录。

## 2. 系统安装验证

```powershell
.\scripts\install-and-verify.ps1 -Configuration Release `
  -InstallerPath .\artifacts\local\Release\Install` Enput` Method.exe
```

也可执行完整链路：

```powershell
.\scripts\run-regression.ps1 -Configuration Release
```

该步骤会显示一次 UAC，因为它写入 HKLM COM/TSF profile 并部署到 Program Files。它不需要人工点击安装按钮：安装器的 `--install-and-verify` 是无界面模式，脚本通过进程退出码和日志判断结果。

成功后检查：

- 注册的 TSF DLL 位于 `C:\Program Files\Enput Method\EnputMethod.Tsf.<build-id>.dll`。
- `C:\Program Files\Enput Method\Overlay` 与安装包 Overlay 文件一致。
- `C:\Program Files\Enput Method\Resources` 含 `enput.db`、`enput.db.ready`、`themes\dark.json`、`wordnet-phrases.txt` 及其它 payload 静态资源。
- `%LOCALAPPDATA%\Enput Method\UserData\config.json` 默认字号为 18（新用户），`shortcut.json` 存在。
- `%LOCALAPPDATA%\Enput Method\UserData\install-verification.log` 包含最终验证结果。

覆盖安装前可对 `UserData\config.json`、`shortcut.json` 计算 hash；再次安装后 hash 必须不变。静态资源与 SQLite 在 Program Files，发布升级只补齐缺失静态文件，不应写回 AppData。

## 3. 发布包验证

```powershell
.\scripts\publish-release.ps1 -Version 0.1.0
```

产物为 `artifacts\release\EnputMethod-0.1.0-win-x64` 和同名 ZIP。ZIP 解压后应只有这一个顶层目录，安装器和卸载器必须在它的根目录，不得再嵌套 `Release`。

安装成功后，关闭发布目录中的所有程序，再将整个解压目录移动或删除。因为注册表指向 Program Files 而非解压目录，已安装输入法仍应可被 Windows 加载。要卸载则应先从完整发布目录运行 `Uninstall Enput Method.exe`；卸载成功后可以删除该目录。卸载默认保留 UserData 和候选学习频率。

## 4. 自动化真实 TSF 验收

适用于改动 TSF 按键路由、composition、候选提交、光标编辑、已安装词库或注册/部署行为。执行前必须已经完成第 1、2 节的 Release 打包和授权重装；更新后已打开的程序必须退出，因为其进程内仍可能加载旧 TSF DLL。

```powershell
.\scripts\run-tsf-integration-tests.ps1 -Configuration Release
```

该脚本先构建 `EnputMethod.Tsf.IntegrationTests`，再启动独立的 x64 WPF 文本宿主。测试进程通过 `ITfInputProcessorProfileMgr` 只为自己的进程启用已安装的 Enput Profile，不修改用户正在使用的输入法；随后以 Windows `SendInput` 发送实际按键，并从该宿主的真实编辑控件读取 composition/已提交文本和光标位置作断言。它验证的是“安装后的 TSF 是否实际接收按键并改变编辑器状态”，不是调用内部候选函数的模拟测试。

### 4.1 候选 UI 的强制通过条件

候选文本可以在 TSF 内部生成并被数字键提交，同时 Overlay 完全没有显示。凡是改动候选窗口、Overlay 协议、候选元数据或候选性能，不能只断言编辑框结果；同一无干扰测试轮次还必须证明：

1. 测试宿主对应的 Overlay client 已收到 `ready`，TSF 记录 `client.connected`。
2. 当前 `clientId` / `stateId` 的 `showCandidates` 已被 WPF 接收，且没有 `candidate.skipped overlay-not-connected` 或 `publish-failed`。
3. 候选窗口存在、可见、非空，并属于当前前台测试宿主；需要验证样式时，还要读取实际 WPF 元素或截图像素，而不能只解析协议 JSON。
4. F3 场景必须单独证明翻译窗口可见且内容属于当前精确候选；候选提交成功不能替代翻译 UI 断言。
5. 测试结束前 Overlay 仍可接受新连接；“进程存在”不等于管道监听健康。

`EnputMethod.Tsf.IntegrationTests` 已实现上述门禁。候选与翻译窗口分别使用稳定标题 `Enput Candidate Overlay` 和 `Enput Translation Overlay`；测试枚举可见顶层窗口，检查尺寸和进程归属，并把当前测试 PID/client 的 TSF 与 WPF 新增日志作为同轮证据。测试还在加载 TSF 前设置仅限当前进程的 `ENPUT_TEST_DISABLE_CANDIDATE_FREQUENCY_PERSISTENCE=1`，运行前后比较 `HKCU\Software\Enput Method\CandidateFrequency` 的完整快照。2026-09-06 的最终 Release 覆盖安装后，完整无干扰轮次以退出码 0 通过；候选窗和翻译窗均真实可见，旧“内部提交成功但 UI 缺失”的假阳性已被关闭。

候选提交在这个自动化宿主中使用 Enput 内置数字直选：WPF `TextBox` 会自行消费 Tab，即使开启 `AcceptsTab` 也不保证把 Tab 交给 TSF key sink。因此 Tab 不是这条自动化链路的有效证据，仍须在 Notepad、VS Code 或用户目标编辑器中单独抽测。候选顺序不得被测试硬编码；需要特定候选时，应发送上下方向键并读取实际 composition 预览逐项定位，再按 Enter 提交并断言文本和光标。例如 `pol` 本身是精确第一候选，测试 `polish` / `Polish` 时不能假定它们固定对应数字 `1` / `2`。

仅当自动化窗口附着交互 Windows 桌面时，该测试才有效。若 `SendInput` 被拒绝、自动化桥接未发现原生窗口，或当前测试进程未能激活 Enput Profile，必须标记为“阻塞”，不能用构建、安装、SQLite 校验或 WPF UI 单元测试替代。对于不涉及 Overlay 的核心 TSF 用例，成功标准是脚本退出码为 0；对于候选或翻译 UI，用例还必须满足 4.1 的连接、消息和窗口可见性门禁，单独出现成功文本不够。

真实 `SendInput` 与用户同时操作鼠标/键盘会竞争前台焦点。测试宿主在每个字符、功能键和等待断言期间检查前台 HWND 与 WPF 键盘焦点；检测到测试窗口失焦时返回专用退出码 `2`，不把随后落入其它窗口或未进入 composition 的文本误报为产品缺陷。运行脚本等待 2 秒后最多自动重试 3 次，只接受完整无干扰轮次的退出码 `0`；连续三次失焦应报告为交互干扰阻塞。

测试 EXE 返回 .NET 代码 `0xe0434352` 时，必须读取其标准错误或托管异常堆栈，不能归类为“未知软件异常”。例如当前会话的 `SendInput failed` 表示交互桌面不可用，而非 TSF DLL 逻辑失败。安装器的验证日志也必须是尽力写入：用户目录不可写时校验应保留真实退出状态，并将日志错误输出到标准错误。

候选词典元数据必须由一次词库查询随候选批量携带。测试短前缀时应额外确认候选出现没有逐项 SQLite 查询：不得在候选去重、排序、分页或 Overlay 渲染循环中 prepare/bind/step 单个词条。该类查询位于 TSF 编辑会话热路径，会直接造成输入延迟或宿主无响应。

普通词与短语的前缀查询还必须有明确上限，不能把整个词典前缀集转为候选页。上限内优先保留完整匹配和词库优先级高的候选；每次改动候选查询时，检查诊断中的页面数，短前缀不得扩展到数千页。

### 4.2 历史测试问题与固定规则

| 曾出现的问题 | 为什么会误导 | 固定规则 |
| --- | --- | --- |
| 构建、原生单元测试、包校验或 SQLite 断言通过 | 不能证明 Windows 加载了新 DLL，也不能证明真实按键与窗口 | 分层报告；运行时结论必须在授权重装、关闭旧宿主后做真实桌面验收 |
| 自动输入成功，但实际仍是其它输入法 | `SendInput` 只负责发键，不负责证明 Enput Profile | 测试进程必须用 `ITfInputProcessorProfileMgr` 激活 Enput，并从真实 composition 结果确认接管 |
| WPF `TextBox` 中 Tab 选词失败 | 宿主可先消费 Tab，属于宿主路由假阴性 | 自动化用内置数字直选覆盖提交；Tab 在能把该键交给 TSF 的目标编辑器单测 |
| 假定下移一次就是 `Washington DC`，或假定 `polish` 固定是第几个 | 精确候选、词库顺序和自适应排序会改变序号 | 用 Up/Down 读取 composition 预览定位特定候选；另保留普通数字直选用例 |
| 用户同时点击鼠标后文本异常 | 全局 `SendInput` 的按键落入其它前台窗口 | 按键前后及等待期间检查 HWND/键盘焦点；退出码 `2` 作废并有界重试 |
| 候选可以提交，但没有候选弹窗 | 内部 `candidates_` 与 WPF Overlay 是两条链路；旧测试只验证前者 | 候选 UI 必须满足 4.1，Overlay 未连接时测试必须失败 |
| Overlay 进程存在，TSF 仍反复 `overlay-not-connected` | 管道监听任务可失败，而单实例进程继续持有 Mutex，阻止健康实例接管 | 进程存在不能作为健康证据；必须探测管道、消息和窗口，监听失败要被观察并退出或恢复 |
| 测试程序弹 `0xe0434352` | 未捕获托管异常被 Windows 当作应用崩溃显示 | 顶层捕获、写标准错误、返回明确非零退出码，不弹系统异常框 |
| 前一子测试失败，后续成功后脚本仍返回成功 | `$LASTEXITCODE` 被后续进程覆盖 | 每个子进程结束后立即检查并传播退出码 |
| Overlay 协议升级后测试仍发送字符串候选 | 旧 fixture 没覆盖真实对象协议 | 协议 schema、生产序列化和测试 fixture 同步更新，至少保留拒绝旧格式的断言 |
| 候选功能正确但宿主迟滞或无响应 | 候选循环逐项查询 SQLite，或一次加载数千页 | 元数据随查询批量返回，前缀结果有界，并做短前缀真实耗时/页面数回归 |

## 5. 人工真实宿主验收

安装或更新后完全关闭并重新打开 Notepad、VS Code、浏览器/ChatGPT 等测试宿主，再在语言栏明确选择 Enput Method。TSF DLL 是进程内 COM 服务，已打开应用不会自动加载新 DLL；自动输入 `he` 也不能证明实际已切换到 Enput。

至少验证：基础 composition、数字/Tab/鼠标选词、`+`/`-` 翻页、活动输入中的 Shift/Escape 取消、输入关键词后的 F2 Emoji、F3 富文本翻译、两个宿主的前景切换，以及目标编辑器字体对新 Emoji 的显示。对于资源管理器，Enput 激活但没有活动组合输入时，F2 必须重命名、F5 必须刷新、Escape/Shift 必须保持应用原语义。完整矩阵见 `debugging-and-testing-zh-CN.md` 和 `development-issue-ledger-zh-CN.md`。
