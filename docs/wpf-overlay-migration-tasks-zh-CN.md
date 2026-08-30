# WPF Overlay 迁移任务书

## 目标

将 Enput Method 的候选窗、翻译窗和后续设置界面迁移到独立的 C# WPF 伴随进程，同时保留原生 C++ TSF DLL 作为唯一的输入服务和文本提交方。

## 不可变边界

- `EnputMethod.Tsf.dll` 继续作为进程内 COM/TSF 服务，被每个目标应用加载。
- C++ Host 独占 `ITfTextInputProcessorEx`、composition、`ITfEditSession`、键盘拦截、caret 查询和文本提交。
- WPF 不加载进目标应用，不实现 COM host，不直接操作 `ITfContext` 或目标应用文本。
- WPF Overlay 通过本地 IPC 接收不可变视图状态，只回传用户意图（选择、翻页、关闭）。
- Overlay 失联、超时或崩溃时，C++ 宿主必须保持 TSF 组合输入（composition）和按键路径可用；本项目仅使用 WPF 候选与翻译界面，不回退显示原生候选窗。

## IPC 契约

传输使用当前用户会话内的命名管道。每个 Host 连接使用稳定 `clientId`，每个会话状态含递增 `stateId`，避免多应用之间串线或迟到的鼠标动作提交已经过期的候选。

### 宿主 -> Overlay

- `showCandidates`：状态 ID、屏幕坐标、候选文本、当前页、总页数、高亮项、布局、主题和模式标记。
- `showTranslation`：状态 ID、候选窗边界和翻译视图模型。
- `hide`：状态 ID 和窗口种类。

### Overlay -> 宿主

- `selectCandidate`：状态 ID 与候选索引。
- `previousPage`、`nextPage`：状态 ID。
- `dismiss`：状态 ID。

Host 只接受与其当前状态 ID 相同的动作，并在收到有效动作后自行创建 TSF edit session。

## 任务 1：项目与契约基础

- [x] 在解决方案增加 `EnputMethod.Overlay` WPF x64 项目。
- [x] 以 JSON Lines 定义 IPC 消息和严格的输入验证。
- [x] Overlay 可同时接受多个 Host 管道连接，并为每个 Host 维护独立窗口；由 Native Host 自动启动留待任务 3。
- [x] 验收：Overlay 可独立运行，接收测试消息并输出动作消息；无 TSF 或 COM 依赖。

## 任务 2：WPF 非激活窗口

- [x] 实现候选和翻译两个透明、无任务栏、无激活的 WPF 窗口。
- [x] 通过 `HwndSource`/Win32 扩展样式设置 `WS_EX_NOACTIVATE`、`WS_EX_TOOLWINDOW`。
- [x] 实现竖排/横排、页脚、模式标记、主题、Emoji、翻译滚动和鼠标命中。
- [ ] 验收：在独立测试宿主中点击候选不抢焦点，窗口位置和尺寸稳定。

当前进度：候选和翻译窗口、`WS_EX_NOACTIVATE`、候选点击及翻页动作回传、基础翻译滚动、主题/字体/透明度映射和高 DPI 坐标换算已实现；解决方案包含独立的 `EnputMethod.Overlay.TestHost` 管道测试宿主。Emoji 专用绘制和人工焦点验收仍待完成。

## 任务 3：原生宿主连接与仅 WPF 界面行为

- [x] C++ Host 在需要显示候选时发布视图状态；Overlay 动作转换为已有候选窗动作。
- [x] 安装器构建、打包并部署 Overlay 运行时文件到 `Program Files\Enput Method\Overlay`。
- [x] 弃用原生候选与翻译窗的显示路径；C++ 只负责 TSF、输入状态、IPC 与文本提交。
- [x] 增加状态 ID、异步连接、伴随进程启动和断线检测；管道 I/O 不阻塞按键或 TSF edit session。
- [ ] 验收：Overlay 未启动、关闭或发送过期动作时，输入不丢失、过期动作不会提交，且 WPF 重连后仅显示当前状态。

## 任务 4：迁移候选 UI

- [x] 将候选显示完全切换至 Overlay；原生候选窗不再参与显示。
- [x] 用 Overlay 鼠标动作覆盖选择、前后翻页、关闭和悬停反馈。
- [ ] 验收：Notepad、EmEditor、VS Code、浏览器和 ChatGPT 中的键盘、鼠标、连续联想、Emoji、分页和边缘定位都正确。

## 任务 5：迁移翻译 UI 与发布

- [x] 将翻译视图、主题字段和滚动条迁入 Overlay。
- [x] 为 IPC 和视图模型增加离线测试；保留完整 Release|x64 构建。
- [x] 安装器部署 Overlay 与全部运行时文件；更新前关闭已安装的 Overlay，卸载器只注销 Windows 输入法注册，保留 Overlay、静态资源与用户数据。
- [ ] 验收：Overlay 崩溃、重启、目标程序切换和安装更新均不会使 TSF 宿主崩溃。

## 实施顺序

先完成任务 1 的契约与独立可运行 Overlay，再接入 Native Host。不得先删除或禁用原生候选窗。


## 2026-08-29 进度更新

- [x] 已安装构建中，Overlay 是唯一的候选和翻译渲染界面；原生窗口不再作为视觉回退。
- [x] 客户端/会话模型按宿主隔离窗口和动作，抑制后台宿主窗口，拒绝过期 `stateId` 动作，并在候选为空或失焦后隐藏界面。
- [x] 安装、Overlay 部署、协议、多宿主路由、前景仲裁、彩色 Emoji 资源、翻页器边缘布局以及 18pt 到 WPF 字号换算均已有自动化覆盖。
- [x] 已安装的 Overlay 现可接收原生主题和 18pt 配置，并能处理翻译文本中的字面量转义换行。
- [~] 仍需在记事本、EmEditor、VS Code、浏览器和 ChatGPT 中进行真实应用验证。自动测试无法断言 Windows 选择的是 Enput，而不是另一个活动输入法。

上方原始任务勾选仅作历史记录。当前统一的人工验收矩阵见 `development-issue-ledger-zh-CN.md`。
