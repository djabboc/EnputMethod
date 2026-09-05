# 架构说明

## 组件

`EnputMethod.Tsf` 是一个进程内 COM DLL，实现 `ITfTextInputProcessorEx`、`ITfKeyEventSink` 和 `ITfCompositionSink`。

Enput 配置文件处于活动状态时，字母按键在 TSF 编辑会话中处理。服务会启动组合输入（composition），并仅把已输入前缀写入目标应用。在放行导航、标点、编辑或应用快捷键前，服务会同步提交该组合输入，避免目标应用的选区与过期 TSF 范围不一致。它通过 `ITfContextView::GetTextExt` 取得光标坐标，再经每个宿主独立的命名管道，向独立的 WPF 悬浮界面（Overlay）发布不可变的候选和翻译视图模型。TSF 服务保留全部匹配项、支持翻页并记录当前高亮候选；只有它有权提交文本。

独立的 WPF 安装器与卸载器各自显示操作窗口。由于 TSF 服务注册是系统级操作，其清单请求管理员权限。用户点击操作按钮后，程序显示结果，用户确认结果后才关闭。

## 注册

安装会执行以下操作：

1. 删除过时的 Enput 用户级 COM 注册，否则它会通过 `HKCR` 覆盖计算机级注册。
2. 在 `HKLM\Software\Classes\CLSID` 下注册 COM 进程内服务器。
3. 通过 TSF 注册文本服务和 `zh-CN` 语言配置文件。
4. 注册 TSF 键盘类别和沉浸式支持类别。

安装器会显式加载与自身可执行文件相邻的 TSF DLL，把它复制为由时间戳和大小导出的文件名，再注册该安装路径。带版本的部署文件名使旧 DLL 仍映射在运行中应用时也能完成更新。重新安装相同 DLL 会复用相同的已部署文件。因此，安装后可以完整保留或移动安装器、卸载器所在的输出目录。手动删除已安装 DLL 前应先使用卸载器。

安装器将 `dictionary.txt`、`themes`、已验证的 `enput.seed.db` 和最终 SQLite 数据库 `enput.db` 部署到 `C:\Program Files\Enput Method\Resources`；Overlay 文件部署在同一 Program Files 产品根目录下。只有缺失时才创建 `%LOCALAPPDATA%\Enput Method\UserData\config.json` 和 `shortcut.json`，并迁移兼容的旧设置，绝不覆盖用户已有值。配置和快捷键仍是 JSON 用户设置；主题是静态包资源。词库内容只使用 SQLite：旧 JSON/JSONL 数据会一次性以事务方式导入、验证后删除。原生服务通过 Windows `winsqlite3.dll` 打开 Program Files 中的 `enput.db`，不提供 JSON/JSONL 词库回退路径。

## 联想来源

普通单词前缀候选仍来自有序的 `dictionary.txt`。短语联想、Emoji 关键词和翻译记录通过带索引的 SQLite 表查询。配置的候选数量只限制当前页，所有匹配词仍可通过翻页查看。数字键仅映射当前页中的项目。

## 外观与配置

`config.json` 控制竖排或横排布局、选词后自动补空格、字体系列、字号、不透明度及活动主题。`shortcut.json` 将动作映射到一个或多个按键名。取消、翻页、选词和翻译快捷键只在已有活动组合输入或候选联想时捕获；`F2` 在文本宿主中仍可从空闲状态进入 Emoji 模式，但文件资源管理器空闲时会交给重命名。`F5` 及空闲 Escape/Shift 始终交给当前应用。主题控制背景、前景、选中行颜色和边框、边框颜色及宽度、圆角、内边距、行高和阴影大小。翻译窗口字段使用 `translation` 前缀，单独控制宽度、最大高度、颜色、边框、内边距、圆角和滚动条颜色。内置主题为 `dark`、`light`、`eye-care` 和 `paper`。

## 候选与关联排序

候选生成是确定性的本地查询，不是神经网络语言模型。对当前输入，它按固定顺序合并四个去重层级：完整单词/短语匹配、触发词等于当前输入的短语续写、普通单词/短语前缀，以及输入长度至少为三个字符时的保序子序列近似匹配。短语匹配忽略空格；Emoji 匹配还忽略 `_` 与 `-`。词频学习只会重排各层内部的候选，不能让近似匹配排到完整匹配或前缀匹配之前。

提交候选后，服务会单独查询以已提交文本触发的记录。下一词记录直接显示；保存的完整短语会被裁剪为已提交文本之后的后缀。没有记录时服务使用固定的常用词兜底序列。因此，当前的句子联想是短语表查询加兜底，不是结合上下文的句子预测。

## 当前限制

- 服务当前仅提供 x64 版本，因此 x86 应用必须有对应的 x86 TSF DLL 才能使用。
- 候选框同时支持键盘选词和不抢焦点的鼠标选词。候选排序保持完整匹配优先，未学习词保持词典顺序；选词记录会本地保存在 `HKCU\\Software\\Enput Method\\CandidateFrequency`。在 `config.json` 将 `adaptiveCandidateRanking` 设置为 `false` 可同时禁用排序学习与新学习记录；删除该注册表键可重置已学习排序。
- Windows 负责全局输入法切换。Enput 放在中文组中，用户原有的 `Ctrl + Shift` 行为可包含它；程序不会全局截获 `Ctrl + Space`。

## WPF 悬浮界面架构（2026-08-29）

候选和翻译界面不再由旧 Win32 窗口绘制。`EnputMethod.Overlay.exe` 安装在 `Program Files\Enput Method\Overlay` 下，负责不抢焦点的 WPF 窗口。它通过当前用户的命名管道接收 JSON Lines 消息，为每个 `clientId` 分别维护候选和翻译窗口，并且只返回用户意图：选词、翻页或关闭。C++ 宿主校验对应的 `stateId`，并自行创建 TSF 编辑会话。

Overlay 进程异步启动和连接。WPF 应用空闲时会创建并隐藏一个屏幕外的不抢焦点候选窗口，首次宿主可复用它，避免原生窗口创建和首次绘制拖慢首个候选。管道确认不能阻塞按键事件路径；不存在 Overlay 管道时，宿主会在 25 毫秒后重试，安装更新则保留 250 毫秒重试延迟。空候选更新会隐藏对应界面；前台所有者检查可阻止后台应用在屏幕上留下可操作候选窗。安装时会在部署前停止已运行的已安装 Overlay，并对所有已安装 Overlay 文件与安装包进行验证。

配置约定中 `fontSize` 使用点数。原生 GDI/DirectWrite 已按点数解释该设置；WPF 渲染器显式转换为 96 DPI 的 WPF 单位，即 `pt * 96 / 72`。翻译字符串在 JSON 解析后标准化，因此源值包含字面量 `\\n`、`\\r` 或 `\\r\\n` 时会显示为真实换行。

## 组合输入的绘制边界

在已输入前缀尚未提交或选中时，TSF 服务会把它保留在活动 `ITfComposition` 中，使候选选择能原子替换整个范围。目标应用负责该组合输入的可见绘制，包括下划线。Enput 不设置 TSF 显示属性来指定下划线颜色或粗细。因此，下划线的存在由 Enput 的活动组合输入状态导致，但具体样式由应用程序决定。当前设计有意保留这一行为；候选界面与组合输入状态尚未拆分。

## 词组与取消快捷键（2026-08-29）

安装器将 Princeton WordNet 3.1 派生的 62,319 条多词短语一次性导入 SQLite `suggestions`，并用 `metadata.builtinPhraseVersion` 确保已有用户数据库只升级一次。高优先级补充覆盖通用地名和经济、商业、心理学、计算机、工程、法律术语；`newyork`、`machinelearning` 等无空格输入由紧凑保序短语匹配召回。`wordnet-phrases.txt` 仅用于安装导入，TSF 运行时仍只读 `enput.db`。

`shortcut.json` 的 `cancelComposition` 是多按键动作，默认 `["Escape", "Shift"]`。仅在活动组合输入或候选联想存在时，配置中的任一按键才会被 TSF 捕获并执行同一取消路径：终止当前未确认的组合输入、隐藏候选；Emoji 模式无输入时退出模式。空闲状态仍交给当前应用。安装只补充缺失配置字段，不覆盖用户已自定义的数组。

## 发布与资源边界（2026-08-29）

发布架构采用“安装介质与运行根分离”。`artifacts\local\<Configuration>` 是开发调试包，`artifacts\release\EnputMethod-<version>-win-x64` 是用户 ZIP 的顶层内容；二者都是 Git 忽略的产物。它们的根目录都有名称明确的安装器、卸载器和 `payload`。

安装器从 `payload` 部署静态资源与 Overlay 到 `C:\Program Files\Enput Method`，并由 TSF 的本机注册函数将版本化 `EnputMethod.Tsf.<build-id>.dll` 注册在该目录。运行时通过已加载 TSF 模块的真实路径定位 `Resources` 和 `Overlay`，因此不依赖发布 ZIP 的解压位置。主题在 `Resources\themes`，SQLite 词库在 `Resources\enput.db`。

`%LOCALAPPDATA%\Enput Method\UserData` 只保存用户配置、快捷键和诊断/安装日志；候选学习频率继续在 HKCU。安装只初始化缺失的用户配置文件，升级不覆盖已有值；卸载只删除 TSF Profile、分类和 COM 注册；Program Files 的版本化 DLL、Overlay 和静态资源保留，以兼容已打开宿主并支持立即重装。旧 AppData 根目录下的 `config.json`、`shortcut.json` 及静态词库会按各自边界一次性迁移。
