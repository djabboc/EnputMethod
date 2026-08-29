# 安装与联合验证流程

本流程用于每次修改 TSF DLL、安装器、候选逻辑或快捷键后进行发布前验证。它将需要系统授权和人工输入的步骤明确交给用户确认，避免代理擅自改变系统输入法状态或把未执行的手工测试报告为通过。

## 1. 构建

1. 使用 Visual Studio MSBuild 执行 `Release|x64` 全量构建。
2. 构建必须零错误；记录警告是否为零。
3. 确认安装器输出目录至少包含 `EnputMethod.Installer.exe`、`EnputMethod.Tsf.dll`、`.deps.json`、`.runtimeconfig.json`、`config.json`、`shortcut.json`、`dictionary.txt` 和主题文件。

## 2. 安装

每次验证默认执行无人值守安装：

```powershell
.\scripts\install-and-verify.ps1 -Configuration Release
```

脚本以管理员权限启动安装器的命令行模式，不显示安装器窗口，也不需要点击“安装”。非管理员终端仍可能显示一次 Windows UAC 提示。

该模式会检查安装包完整性、注册 TSF DLL，然后逐个比较已注册 DLL 和 Program Files 中 Overlay 文件的 SHA-256。失败时会将详细异常写入 `%LOCALAPPDATA%\Enput Method\install-verification.log`，并在脚本终端中输出。

SQLite 词库验证还会确认 `enput.db` 与 `enput.db.ready` 存在、schema version 正确、`he`、`can i help you?`、`fire`、`saw`、`braces`、`hug` 可查询，并确认旧词库 JSON/JSONL 文件不存在。

需要观察安装器窗口或人工排查时，再直接运行 `EnputMethod.Installer.exe` 并点击“安装”。

若安装失败，先检查以下事实，不要直接重复安装：

- 安装器输出目录是否完整。
- 当前注册表路径和实际 DLL 是否存在。
- 已安装 DLL 是否是旧二进制。
- 错误码是否来自复制、COM 注册或 TSF Profile 注册。

## 3. 重开目标应用

安装或更新后，关闭并重新打开 Notepad、EmEditor、ChatGPT 等目标应用，再开始输入测试。TSF 服务作为进程内 COM DLL 加载；已经打开的应用可能继续保留旧 DLL。单纯切换输入法不会保证 DLL 卸载。

安装完成提示中建议先切换到其他输入法再切回 Enput，其主要目的，是让 Windows 刷新输入法 Profile 的可见性和当前选择。它不能替代重开目标应用；版本化 DLL 路径解决的是旧 DLL 文件被映射时无法覆盖的问题。

## 4. 人工验收矩阵

对每一项先说明操作和预期，再由用户回复“是”或“否”。发生失败时记录实际文本、候选窗行为和目标应用名称。

| 场景 | 操作 | 预期 |
| --- | --- | --- |
| 组合内编辑 | 输入 `hel`，左移一次，输入 `s` | 左移时候选窗仍显示；文本为 `hesl`。 |
| 标点提交 | 输入 `he`、`?`、空格 | 文本为 `he? `；标点后候选窗关闭。 |
| 候选分页 | 输入有多页候选的前缀，使用 `+`、`-` | 页码变化正确，前缀不变。 |
| 精确选择 | 输入有多个候选的前缀，按上下键后按 Tab | Tab 提交当前高亮项，并按配置追加空格。 |
| 边界稳定性 | 在首项按住上键；在第一页按住 `-` | 无重复显示/隐藏闪烁。 |
| 跨应用 | 在 Notepad、EmEditor 和 ChatGPT 重复关键场景 | 文本顺序和快捷键行为一致。 |

候选框、大小写、Emoji、翻译和配置组合的完整人工清单维护在 `development-issue-ledger-zh-CN.md`。发布前按该清单集中验收，避免遗漏已修复但尚未人工确认的项目。

## 5. 提交与记录

1. 仅在构建和对应验收通过后提交该任务代码。
2. 每个任务独立提交，不将无关重构混入。
3. 在 `development-tasks-zh-CN.md` 写入构建、安装和人工验收结果。
4. 未执行的项目必须明确标为待验证。

## 本轮结果

2026-08-27 的联合验证已通过：

- 固定 `.8.dll` 路径导致的安装失败已修复，旧文件存在时可以安装新 DLL。
- 组合内左移后继续输入得到正确文本。
- `he`、`?`、空格得到 `he? `。
- 分页、上下选择、Tab 提交和选中项主题样式均正常。
- 在候选页边界按住上键或 `-`，以及正常下翻页时，候选窗无明显闪烁。


## 2026-08-29 Automated Regression Baseline

Use the following command for a complete local release check:

```powershell
.\scripts\run-regression.ps1 -Configuration Release
```

It builds the installer and TSF service, runs native candidate/JSON/translation-line-break tests, Overlay protocol and multi-host tests, WPF foreground/pagination/color-Emoji/18pt font-scale automation, then invokes unattended installation verification. A successful run proves the registered TSF DLL and installed Overlay files match the package; it does not prove that a specific target application has already reloaded the new DLL or that Windows switched that application to Enput. Those remain explicit manual checks.
