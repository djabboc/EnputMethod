# WPF Overlay 迁移任务书

## 目标

将 Enput Method 的候选窗、翻译窗和后续设置界面迁移到独立的 C# WPF 伴随进程，同时保留原生 C++ TSF DLL 作为唯一的输入服务和文本提交方。

## 不可变边界

- `EnputMethod.Tsf.dll` 继续作为进程内 COM/TSF 服务，被每个目标应用加载。
- C++ Host 独占 `ITfTextInputProcessorEx`、composition、`ITfEditSession`、键盘拦截、caret 查询和文本提交。
- WPF 不加载进目标应用，不实现 COM host，不直接操作 `ITfContext` 或目标应用文本。
- WPF Overlay 通过本地 IPC 接收不可变视图状态，只回传用户意图（选择、翻页、关闭）。
- Overlay 失联、超时或崩溃时，C++ Host 必须继续以原生候选窗工作；输入不可被阻塞。

## IPC 契约

传输使用当前用户会话内的命名管道。每个 Host 连接使用稳定 `clientId`，每个会话状态含递增 `stateId`，避免多应用之间串线或迟到的鼠标动作提交已经过期的候选。

### Host -> Overlay

- `showCandidates`：状态 ID、屏幕坐标、候选文本、当前页、总页数、高亮项、布局、主题和模式标记。
- `showTranslation`：状态 ID、候选窗边界和翻译视图模型。
- `hide`：状态 ID 和窗口种类。

### Overlay -> Host

- `selectCandidate`：状态 ID 与候选索引。
- `previousPage`、`nextPage`：状态 ID。
- `dismiss`：状态 ID。

Host 只接受与其当前状态 ID 相同的动作，并在收到有效动作后自行创建 TSF edit session。

## 任务 1：项目与契约基础

- [x] 在解决方案增加 `EnputMethod.Overlay` WPF x64 项目。
- [x] 以 JSON Lines 定义 IPC 消息和严格的输入验证。
- [x] Overlay 可同时接受多个 Host 管道连接，并可作为后台伴随进程运行；由 Native Host 自动启动留待任务 3。
- [x] 验收：Overlay 可独立运行，接收测试消息并输出动作消息；无 TSF 或 COM 依赖。

## 任务 2：WPF 非激活窗口

- [x] 实现候选和翻译两个透明、无任务栏、无激活的 WPF 窗口。
- [x] 通过 `HwndSource`/Win32 扩展样式设置 `WS_EX_NOACTIVATE`、`WS_EX_TOOLWINDOW`。
- [x] 实现竖排/横排、页脚、模式标记、主题、Emoji、翻译滚动和鼠标命中。
- [ ] 验收：在独立测试宿主中点击候选不抢焦点，窗口位置和尺寸稳定。

当前进度：候选和翻译窗口、`WS_EX_NOACTIVATE`、候选点击及翻页动作回传、基础翻译滚动、主题/字体/透明度映射和高 DPI 坐标换算已实现；解决方案包含独立的 `EnputMethod.Overlay.TestHost` 管道测试宿主。Emoji 专用绘制和人工焦点验收仍待完成。

## 任务 3：Native Host 连接与回退

- [x] C++ Host 在需要显示候选时发布视图状态；Overlay 动作转换为已有候选窗动作。
- [x] 安装器构建、打包并部署 Overlay 运行时文件到 `Program Files\Enput Method\Overlay`。
- [x] 保留 `CandidateWindow` 与 `TranslationWindow` 作为连接前和断线后的回退实现。
- [x] 增加状态 ID、异步连接、伴随进程启动和断线检测；管道 I/O 不阻塞按键或 TSF edit session。
- [ ] 验收：Overlay 未启动、关闭或发送过期动作时，输入不丢失且原生候选窗仍可用。

## 任务 4：迁移候选 UI

- [x] 将正常候选显示切换至 Overlay，原生候选窗只用作故障回退。
- [x] 用 Overlay 鼠标动作覆盖选择、前后翻页、关闭和悬停反馈。
- [ ] 验收：Notepad、EmEditor、VS Code、浏览器和 ChatGPT 中的键盘、鼠标、连续联想、Emoji、分页和边缘定位都正确。

## 任务 5：迁移翻译 UI 与发布

- [x] 将翻译视图、主题字段和滚动条迁入 Overlay。
- [x] 为 IPC 和视图模型增加离线测试；保留完整 Release|x64 构建。
- [x] 安装器部署 Overlay 与全部运行时文件；更新前关闭已安装的 Overlay，卸载器清理 Overlay 而保留用户数据。
- [ ] 验收：Overlay 崩溃、重启、目标程序切换和安装更新均不会使 TSF 宿主崩溃。

## 实施顺序

先完成任务 1 的契约与独立可运行 Overlay，再接入 Native Host。不得先删除或禁用原生候选窗。
