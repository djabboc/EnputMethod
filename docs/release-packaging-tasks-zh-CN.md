# 一站式发布与资源目录改造任务书

状态：待架构决策。创建日期：2026-08-29。

本任务将工程整理为“源码、本地构建产物、用户发布包”三层，并把安装体验改造成用户只需解压、双击安装或卸载即可完成的产品交付。本文是实施约束和验收标准；在“资源部署根目录”决策确认前，不修改安装注册路径或删除现有用户数据。

## 1. 当前工程主体

从运行职责看，当前项目有四个执行主体：

| 主体 | 当前项目 | 职责 |
| --- | --- | --- |
| 输入法服务 | `EnputMethod.Tsf` | x64 原生 C++ TSF/COM DLL；唯一能写入目标编辑器的组件。 |
| 输入法 UI | `EnputMethod.Overlay` | 独立 WPF 进程；渲染候选、Emoji、翻译、分页和鼠标意图。 |
| 安装器 | `EnputMethod.Installer` | WPF GUI、SQLite 初始化/升级、部署与 TSF/COM 注册。 |
| 卸载器 | `EnputMethod.Uninstaller` | WPF GUI、TSF/COM 注销和已部署 Overlay 清理。 |

从用户产品视角，安装器与卸载器组成一个发布主体：用户拿到一个包含两个入口程序和完整 payload 的目录。TSF DLL 和 Overlay 是该产品的内部运行组件，不应要求用户单独操作。

## 2. 目标目录层级

### A. 源码工程：进入 Git

仓库根目录继续只保存源代码、项目文件、测试、脚本、文档和**发布默认资源源文件**。包括：

- C++ TSF、C# WPF Overlay、安装器、卸载器、测试项目和 PowerShell 脚本。
- `config.json`、`shortcut.json`、主题、默认词表、seed SQLite、WordNet 导入源、Twemoji 资源和许可文件。
- 发布任务书、技术栈、测试和数据来源文档。

源码工程不得提交构建输出、临时导入文件、安装后的用户词库、用户配置、日志或发布压缩包。

### B. 本地构建目录：不进入 Git

建议统一为 `artifacts\local\<Debug|Release>\`。每种配置都必须有可直接运行、可直接安装、资源完整的目录，用于日常调试：

```text
artifacts\local\Release\
  Install Enput Method.exe
  Uninstall Enput Method.exe
  payload\
    EnputMethod.Tsf.dll
    Overlay\
    Resources\
      config.json
      shortcut.json
      themes\
      dictionary.txt
      enput.seed.db
      wordnet-phrases.txt
      EmojiAssets\
      attribution\
```

Debug 和 Release 的文件互不复用。调试一律从此目录或发布目录启动，而不是从散落的 `bin` 目录手动拼接 DLL。

### C. 发布目录：不进入 Git

建议统一为 `artifacts\release\EnputMethod-<version>-win-x64\`，由发布脚本从 Release 构建产物复制并验证：

```text
EnputMethod-<version>-win-x64\
  Install Enput Method.exe
  Uninstall Enput Method.exe
  README.txt
  payload\
    EnputMethod.Tsf.dll
    Overlay\
    Resources\
      defaults\
      dictionary\
      emoji\
      licenses\
```

交付给用户的压缩包只包含该顶层目录。用户解压后看到明确命名的“安装”和“卸载”入口；卸载成功后可直接删除整个解压目录。

## 3. 当前问题与改造目标

| 当前行为 | 问题 | 目标 |
| --- | --- | --- |
| 安装器把配置、主题、词表、SQLite 和导入中间数据放入 `%LOCALAPPDATA%\Enput Method`。 | 静态资源、用户数据和运行数据混在用户目录；不满足集中静态资源管理。 | 静态 payload 集中到一个受控资源根；用户可变数据与默认静态资源分离。 |
| Overlay 部署到 `Program Files\Enput Method\Overlay`，DLL 使用版本化部署路径。 | 安装/卸载资源位置分散，发布包和已安装结构不直观。 | 安装和卸载围绕一个明确的产品资源根工作。 |
| 安装/卸载 GUI 点击后执行同步任务。 | 首次词库迁移、资源复制、Overlay 停止、注册操作可能造成“卡住”的观感。 | 显示阶段文本、进度条、禁用重复点击、成功/失败终态和可复制错误说明。 |
| 当前本地构建依赖多个项目输出目录。 | 不适合日常直接验证，也不能直接压缩发布。 | 脚本生产一个资源完整、可验证的 Local/Release 产品目录。 |

## 4. 待确认的架构决策

### D-01：静态资源的最终部署根目录

必须在以下两种方案中选择一种，不能混用。

| 方案 | 资源位置 | 优点 | 代价 |
| --- | --- | --- | --- |
| A. 常规安装目录 | `Program Files\Enput Method\Resources`，安装器从发布包复制。 | 最符合 Windows 安装程序习惯；用户可移动或删除原解压目录而不影响已安装输入法；权限和卸载范围明确。 | 卸载器必须完整删除 Program Files 产品目录；发布包本身不是运行根。 |
| B. 解压目录即运行目录 | 发布包内 `payload\Resources`，TSF 直接注册该路径。 | 所有静态资源始终和用户看到的两个程序放在一起；卸载后删除该目录即可清理全部文件。 | 已安装时用户不能移动、重命名或删除解压目录；压缩包不得在临时目录中直接运行；路径变更会使注册 DLL 失效。 |

推荐方案 A：它仍满足静态资源不进入 AppData、发布包包含完整资源、卸载后可删除解压目录，同时不把输入法的可用性绑定到用户保留某个下载目录。方案 B 更“便携”，但对于进程内 TSF DLL 风险明显更高。

### D-02：用户可变数据的保留策略

`config.json`、`shortcut.json`、用户词典、自适应频率、翻译框尺寸和 SQLite 词库会随着使用改变。需要明确：

- 安装/升级：只在不存在时用发布包的默认配置初始化，绝不覆盖已有用户值。
- 卸载：默认保留用户设置和学习数据，还是提供“同时删除用户数据”的可选项？

静态资源不会放入 AppData；但若保留用户个性化配置和学习数据，它们仍需要一个用户数据根。推荐位置为 `%LOCALAPPDATA%\Enput Method\UserData`，仅包含可变数据，不包含 Twemoji、默认主题、默认字典或发布资源。

## 5. 实施任务

### P-01：产物目录与 Git 边界

- [ ] 新增 `artifacts/` 到 `.gitignore`。
- [ ] 规定并文档化 `artifacts/local/Debug`、`artifacts/local/Release`、`artifacts/release`。
- [ ] 确保生成目录只由脚本创建，不影响源码目录的 `bin/obj`。

验收：`git status` 不出现 build/release 产物；Local 目录在没有 Visual Studio 的用户机器上可完整运行安装/卸载入口。

### P-02：发布 payload 清单与默认资源

- [ ] 定义单一 `payload` 清单：TSF DLL、Overlay、所有 Emoji、词库、seed DB、WordNet、默认配置/快捷键/主题、所有许可文件。
- [ ] 让安装器和卸载器都从相对 `payload` 路径工作，不再假设开发机 `bin` 结构。
- [ ] 发布包始终携带默认配置；安装仅在用户配置不存在时初始化，不合并或覆盖现有用户字段。
- [ ] 不把任何静态资源复制到 AppData；清理当前把主题、默认字典和导入源写入 LocalAppData 的路径。

验收：新用户安装后静态资源只在 D-01 决定的资源根；升级前后用户配置 hash 不变；发布包缺少任一必需资源时安装器在开始前报出具体缺失文件。

### P-03：安装与卸载进度 UX

- [ ] 安装器、卸载器界面增加确定/不确定进度条、当前阶段文本、运行中状态和禁用按钮。
- [ ] 安装在后台任务执行，UI 保持响应；阶段至少包括：预检、停止旧 Overlay、准备用户数据、构建/升级 SQLite、部署 payload、注册/注销 TSF、文件一致性验证、完成。
- [ ] 卸载阶段至少包括：预检、停止 Overlay、注销 TSF/COM、删除产品资源根、处理用户数据策略、完成。
- [ ] 失败状态保留窗口，显示可复制的操作/错误摘要；成功后由用户确认关闭，不自动吞掉结果。
- [ ] `--install-and-verify` 和 `--verify-lexicon` 保持无交互、可自动化模式，不能依赖 GUI 进度控件。

验收：首次安装的大词库导入期间进度会继续更新；双击按钮不会启动两个并发安装；失败后用户可看到阶段和错误码。

### P-04：资源路径与 TSF/Overlay 启动

- [ ] 让 TSF 从已注册 DLL 的实际目录计算 Overlay 和资源根，而不是硬编码 Program Files 或 AppData。
- [ ] 让 Overlay 从该资源根读取 Emoji、默认主题和其它静态资源。
- [ ] 在 D-01 方案 A 下，安装器部署完整的单一 Program Files 产品树；在方案 B 下，验证 release 根不得移动。
- [ ] 卸载器使用记录的已注册路径/产品根清理，而不是依赖当前启动目录是否仍为原始版本。

验收：更新后重开宿主可以加载新版 TSF 和 Overlay；卸载后注册表不再指向不存在 DLL；删除发布目录不会影响方案 A 的已安装输入法。

### P-05：发布脚本

- [ ] 新增 `scripts/build-local-package.ps1`：构建指定 Debug/Release，清空并生成 `artifacts/local/<Configuration>`，运行包完整性检查。
- [ ] 新增 `scripts/publish-release.ps1`：仅允许 Release x64；调用本地包构建，创建版本化发布目录、生成 `README.txt`、运行文件 hash/完整性验证，并可选创建 zip。
- [ ] 版本号来源必须单一且可追溯，例如 Git tag 或发布参数；脚本不允许静默覆盖已有不同版本的发布目录。
- [ ] 发布说明写明：解压、运行安装、更新后重开编辑器、卸载后删除目录；若采用方案 B，明确“安装期间不可移动目录”。

验收：在干净工作区执行一个命令可得到可解压分发的发布包；将该目录复制到另一位置后，方案 A 的安装/卸载均通过。

### P-06：验证与回归

- [ ] 扩展安装完整性检查，验证 payload 相对路径、资源根和默认配置策略。
- [ ] 自动化验证“已有用户配置不被更新”和“静态资源未写入 AppData”。
- [ ] 验证安装/卸载进度阶段顺序及失败状态，不依赖人工观察时间。
- [ ] 保留 `run-regression.ps1` 的 TSF、Overlay、SQLite 和系统注册验证，并让它使用 Local Release 包而非散落的项目输出。
- [ ] 手工验证：首次安装、覆盖安装、卸载、删除发布目录、重新安装、已打开宿主重开加载、F2/F3、词组、Shift 取消、两个宿主前景切换。

## 6. 非目标与兼容性

- 本任务不改变候选算法、SQLite schema 内容或用户可见快捷键语义。
- 不重新引入 JSON/JSONL 运行时词库回退。
- 已安装旧版本必须能升级或给出明确迁移提示；不允许静默丢弃用户配置或学习频率。
- 用户已打开的编辑器仍必须重开，这是 TSF in-process DLL 的 Windows 约束，进度条不能消除该要求。

## 7. 完成定义

1. 用户下载一个 zip，解压后能清楚看到安装和卸载入口；其余文件位于明确的 `payload` 目录。
2. 安装和卸载都显示阶段和进度，不会在长时间 SQLite/资源操作中看似无响应。
3. 静态资源不进入 AppData；发布包采用默认配置；已有用户配置、学习数据和用户词典不被发布更新覆盖。
4. 本地 Debug/Release 目录和发布目录均完整、可验证、被 Git 忽略。
5. 自动回归、系统安装/卸载验证、配置保留验证和真实宿主矩阵通过。
6. 文档明确资源根、用户数据策略、发布命令、目录结构、升级和删除步骤。