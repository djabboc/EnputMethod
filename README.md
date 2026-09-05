# Enput Method

Enput Method 是一个基于文本服务框架（TSF）的 Windows 英文输入法原型。它作为 Windows 输入法配置文件注册，可与系统和第三方输入法并列选择。

## 功能

- 位于中文输入法组的系统级 TSF 输入法配置文件。
- 靠近文本光标的悬浮分页候选框，支持键盘和鼠标选词。
- `1` 至 `9` 选择当前页相应项目；`Tab` 选择高亮项目。
- 完整单词匹配始终优先；可配置的大小写保持和 Caps Lock 标记使提交文本可预测。
- 可配置的多按键快捷键、词典、短语/下一词联想、Emoji 模式、翻译窗口和四套候选主题。
- 选词后可以保留候选窗，继续显示下一词或短语联想。
- 使用 Windows 既有的中文组 `Ctrl + Shift` 切换行为。

## 开发环境要求

- Windows 10 或 Windows 11，x64。
- Visual Studio 2022，并安装“使用 C++ 的桌面开发”工作负载。
- .NET 9 SDK。
- 安装时需要管理员授权。

## 构建

在 Visual Studio 中打开 `EnputMethod.sln`，选择 `Release|x64`，再构建解决方案。安装器会先以静态 C++ 运行库构建原生 TSF DLL，再复制到输出目录。

## 安装与使用

普通用户下载并解压发布 ZIP 后，在解压目录运行 `Install Enput Method.exe`，接受 Windows UAC 提示。发布包不携带 .NET Runtime；若 Windows 提示缺少 .NET 9 Desktop Runtime x64，按提示安装后重试。发布目录的顶层应直接包含：

```text
Install Enput Method.exe
Uninstall Enput Method.exe
payload\
```

安装会把已注册的 TSF DLL、WPF 悬浮界面（Overlay）和全部静态资源部署到 `C:\Program Files\Enput Method`。发布目录只是安装介质，安装成功后可以移动或删除，不会破坏输入法。需要卸载时，从完整解压的发布目录运行 `Uninstall Enput Method.exe`；卸载会移除 Enput 的 TSF、Profile、分类和 COM 注册，但会保留 Program Files 下的版本化 DLL、Overlay 和静态资源，以兼容已打开的应用并支持不重启立即重装。卸载成功后可删除解压目录。

两个 WPF 启动器在耗时操作期间都会显示阶段文字和进度条。安装会停止旧 Overlay、复制 `payload`、注册 TSF、初始化用户设置、准备静态 SQLite 词库并验证安装结果。卸载会显示注销和最终验证进度，不会要求用户关闭所有编辑器，也不会删除正在被已打开宿主映射的运行文件。

静态产品内容不放在 AppData：`enput.db`、主题、词典数据、WordNet 来源、内置 Twemoji 资源和运行二进制文件位于 `C:\Program Files\Enput Method`。只有用户会修改的状态保存在 `%LOCALAPPDATA%\Enput Method\UserData`：`config.json`、`shortcut.json`、安装/Overlay 日志；自适应词频数据保存于注册表。升级只在配置和快捷键缺失时初始化默认文件，不覆盖用户设置。默认卸载保留用户数据和学习排序，便于之后重新安装。

Windows 可能缓存文本服务。若安装后 Enput 没有立即出现在现有 `Ctrl + Shift` 切换序列中，可先切换到其他输入法再切回，必要时注销并重新登录。TSF DLL 更新后，已打开的目标应用仍映射旧进程内 DLL，必须关闭并重新打开该应用再测试新版本。

输入如 `he` 的前缀时，编辑区域保留 `he`，悬浮候选窗按页显示匹配词。按 `-` 或 `+` 翻页；使用上下键跨页移动高亮候选。鼠标可选择候选，或使用上一页/下一页控件，且不会抢走编辑器焦点。按当前页对应数字键选择单词，或按 `Tab` 选择高亮单词。`F2` 在文本宿主中可进入 Emoji 模式；在文件资源管理器空闲时会交给重命名。已有组合输入或候选联想时，Shift 和 Escape 可取消活动组合输入，`F3` 显示富文本翻译视图；`F5` 和空闲 Escape/Shift 始终交给当前应用。翻译窗口标题栏的 `Copy` 按钮复制完整翻译。

`fontSize` 使用点数，默认值为 `18`；WPF Overlay 将其转换为 24 个设备无关像素，使视觉大小与原生 18pt 配置一致。Emoji 候选使用内置 Twemoji 彩色 PNG 资源。SQLite 词库直接保存 Unicode 文本，例如 `fire -> 🔥` 和 `saw -> 🪚`。

## 仓库结构

- `EnputMethod.Tsf/`：原生 C++ TSF 文本服务。
- `EnputMethod.Installer/`：独立 WPF 安装器。
- `EnputMethod.Uninstaller/`：独立 WPF 卸载器。
- `docs/`：架构、开发、测试与维护文档。
- `EnputMethod.sln`：Visual Studio 解决方案。

实现和注册细节见[架构说明](docs/architecture.md)。

部署和候选窗事故复盘见[事故复盘](docs/root-cause-analysis.md)。

中文配置和更新指南见[更新说明](docs/update-notes-zh-CN.md)。

可重复构建、安装和联合人工测试流程见[安装验证](docs/installation-validation-zh-CN.md)。

完整问题历史、延期决策和当前人工验收清单见[开发问题总账](docs/development-issue-ledger-zh-CN.md)。

当前组件、运行时、数据、IPC、部署和配置细节见[技术栈说明](docs/technology-stack-zh-CN.md)。

分层构建、安装、自动化和真实宿主验证流程见[调试与测试](docs/debugging-and-testing-zh-CN.md)。

一站式发布、资源根目录和进度界面方案见[发布打包任务书](docs/release-packaging-tasks-zh-CN.md)。从本地打包到 GitHub Release 页面上传的完整人工流程见[正式发布手册](docs/release-manual-zh-CN.md)。

## 当前开发状态（2026-08-29）

界面已从输入服务中拆分。`EnputMethod.Tsf.dll` 仍是进程内 C++ TSF/COM 服务。候选和翻译窗口由已安装的 `EnputMethod.Overlay` WPF 伴随进程通过本地命名管道绘制。每个应用连接都有 `clientId` 和单调递增的 `stateId`；过期动作不能选择新候选页，只有前景编辑器的 Overlay 窗口保持可见。

翻译数据中，ECDICT 条目可能含有字面量 `\\n` 或 `\\r\\n`；TSF 服务会在发布 WPF 视图前将其标准化为真实换行。安装新 TSF DLL 后，测试前需关闭并重新打开目标编辑器：已打开进程会继续映射旧 DLL。在旧 JSON 数据型 DLL 已迁移其数据文件后运行它，会使 F2、F3 看起来失效。可运行 `scripts\run-regression.ps1 -Configuration Release`，完成发布构建、原生测试、Overlay 协议/前景测试和已安装文件验证。正式交付使用 `scripts\publish-release.ps1 -Version <version>`；它默认创建 ZIP，并对该 ZIP 解压后的实际安装包再做完整性验证。剩余真实应用验收矩阵维护在 `docs/development-issue-ledger-zh-CN.md`。

## 活动组合输入的外观

候选尚未提交时，输入前缀保持为活动 TSF 组合输入，因此选词可以安全替换它。部分应用会为活动组合输入绘制下划线；该视觉外观由应用控制。目前这是有意保留的行为，不同编辑器的显示可能不同。
