# 安装与联合验证流程

最后更新：2026-08-29。本文区分本地包验证、系统安装验证与真实宿主验收；前一层通过不代表后一层已通过。

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

## 4. 真实宿主验收

安装或更新后完全关闭并重新打开 Notepad、VS Code、浏览器/ChatGPT 等测试宿主，再在语言栏明确选择 Enput Method。TSF DLL 是进程内 COM 服务，已打开应用不会自动加载新 DLL；自动输入 `he` 也不能证明实际已切换到 Enput。

至少验证：基础 composition、数字/Tab/鼠标选词、`+`/`-` 翻页、Shift/Escape 取消、F2 Emoji、F3 富文本翻译、两个宿主的前景切换，以及目标编辑器字体对新 Emoji 的显示。完整矩阵见 `debugging-and-testing-zh-CN.md` 和 `development-issue-ledger-zh-CN.md`。