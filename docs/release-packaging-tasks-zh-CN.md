# 一站式发布与资源目录改造任务书

状态：已实现并通过本地发布包与自动化回归验证。最后更新：2026-08-29。

## 1. 已选架构

采用方案 A：发布包只是安装介质，安装后运行时根目录固定在 `C:\Program Files\Enput Method`。Windows 注册表中的 TSF COM 路径也指向该目录中的版本化 DLL，而不是解压目录。

因此用户可以在安装成功后任意移动或删除发布 ZIP 的解压目录；已安装输入法不会受影响。卸载器仍需要从完整发布目录启动，因为它用随包携带的 TSF DLL 调用注销导出函数。卸载完成后，该解压目录也可以删除。

方案 B（未采用）会直接注册解压目录中的 DLL。它看似更便携，但移动、改名或删除解压目录后，注册表仍指向旧路径，输入法会失效。这就是两种方案“是否能删除发布目录”的根本区别。

## 2. 目录与数据边界

```text
仓库（进入 Git）
  源码、默认配置、主题、词库源、测试、文档、脚本

artifacts\local\<Debug|Release>\（不进入 Git）
  Install Enput Method.exe
  Uninstall Enput Method.exe
  payload\
    EnputMethod.Tsf.dll
    Overlay\
    Resources\

artifacts\release\EnputMethod-<version>-win-x64\（不进入 Git）
  Install Enput Method.exe
  Uninstall Enput Method.exe
  README.txt
  payload\
```

安装后的运行目录：

```text
C:\Program Files\Enput Method\
  EnputMethod.Tsf.<build-id>.dll
  Overlay\
  Resources\
    enput.db
    enput.db.ready
    dictionary.txt
    wordnet-phrases.txt
    enput.seed.db
    themes\
    其它发布静态资源
```

唯一的用户可变目录：

```text
%LOCALAPPDATA%\Enput Method\UserData\
  config.json
  shortcut.json
  install-verification.log
  overlay-diagnostics.log
```

安装只在 `config.json`、`shortcut.json` 不存在时使用包内默认值初始化。旧版 `%LOCALAPPDATA%\Enput Method\config.json`、`shortcut.json` 会一次性迁移到 `UserData`，前提是目标文件尚不存在。旧版静态 `enput.db` 和 ready 标记会迁移至 Program Files 的 `Resources`，不会再在 AppData 保留静态词库副本。

升级会更新 Overlay；静态资源只补齐缺失文件，用户配置、快捷键、日志和学习频率均不覆盖。卸载注销 TSF、停止 Overlay、删除 `C:\Program Files\Enput Method`，默认保留 `UserData` 与 `HKCU\Software\Enput Method\CandidateFrequency`，便于以后重装恢复个人设置。当前没有“同时删除用户数据”的界面选项。

## 3. 发布体验

发布 ZIP 的顶层只有一个版本目录。用户解压后直接看到：

1. `Install Enput Method.exe`：请求 UAC，在后台执行预检、停止旧 Overlay、部署 UI/静态资源、注册 TSF、初始化用户数据、SQLite 迁移和一致性验证。窗口会持续显示当前阶段和进度，重复点击被禁用。
2. `Uninstall Enput Method.exe`：请求 UAC，在后台注销 TSF、停止 Overlay、删除 Program Files 产品目录并验证结果。窗口会显示进度；成功后明确说明用户数据仍保留、解压目录可删除。

安装器会拒绝缺少 TSF、Overlay、关键 Twemoji、默认配置、主题、seed DB 或 WordNet 归属文件的包。安装后会校验已注册 TSF DLL 位于 Program Files，Overlay 文件与 payload 的 hash 相同，且必需静态资源存在。

## 4. 脚本与验收

```powershell
# 生成完整本地调试/测试包，并做非修改性的包完整性检查
.\scripts\build-local-package.ps1 -Configuration Release

# 生成面向用户的版本化目录；-Zip 同时生成压缩包
.\scripts\publish-release.ps1 -Version 0.1.0 -Zip

# 原生候选逻辑与 WPF Overlay 自动化
.\scripts\run-overlay-tests.ps1 -Configuration Release

# 完整本地包、自动化测试和需要 UAC 的系统安装验证
.\scripts\run-regression.ps1 -Configuration Release
```

本轮已执行：Release 卸载器构建（0 警告、0 错误）、本地包完整性验证、`run-overlay-tests.ps1 -Configuration Release`（原生测试、10 个协议用例、多宿主管道、前景仲裁）、带 `-Zip` 的发布目录/压缩包结构检查，以及 `install-and-verify.ps1` 的 UAC 系统安装验证。后者确认 TSF 注册、Program Files 静态资源、SQLite 自检均通过，并确认覆盖安装不改变已有 UserData 配置和快捷键 hash。

系统卸载仍会修改 Windows 的输入法注册，须在接受 UAC 后单独验收；它不能由纯结构检查替代。真实应用的候选、F2/F3、焦点切换和字体兼容性验收也仍需要确认 Enput 已被选中。

## 5. 非目标

本任务不改变候选算法、SQLite schema 或快捷键语义；不重新引入 JSON/JSONL 运行时词库回退；不提供 x86 TSF 产物。发布目录和本地构建目录均由脚本管理并被 `.gitignore` 排除。
## 6. 后续修正（2026-08-29）

本地包构建改为对安装器和卸载器执行 MSBuild `Rebuild`，避免 C++ TSF 的已变更 DLL 被增量构建漏暂存。包验证现在同时检查最新 payload；原生导出 `GetEnputRegistrationStage` 仅用于将安装/注销 HRESULT 定位到明确的 Windows TSF 阶段。

卸载不应把“文件被宿主加载”视为包不完整。安装器会停止 Overlay，但不能强制终止 Explorer、编辑器、浏览器等宿主的进程内 TSF DLL。卸载器会列出占用进程；关闭它们或重启后重试才会删除 Program Files 产品目录。默认用户数据仍保留。
## 7. 安装修正（2026-08-29）

安装器现在把整个部署、词库准备、TSF 注册和验证视为单一互斥事务。若已有安装器正在运行，第二个实例会报告占用而不会并发写入 SQLite staging 文件。词库准备在 TSF 注册前完成，避免注册后已打开的宿主重新启动 Overlay 并锁住 `enput.db`。已有且能通过 schema/数据校验的静态数据库会保留，只补 `enput.db.ready`；检测到 ECDICT/CC-CEDICT 来源时不重复下载。无界面安装验证会返回真实进程退出码，`install-and-verify.ps1` 可以可靠拦截失败。

本轮提升权限验证已通过：TSF DLL 已注册到 `C:\Program Files\Enput Method`，Overlay 与静态资源完整，SQLite 验证成功，Microsoft Pinyin 仍为默认输入法。卸载的目录删除仍需要关闭所有加载旧 TSF DLL 的宿主，或重启 Windows 后再运行最新卸载器。