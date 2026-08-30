# 正式发布与 GitHub 手工操作指南

最后更新：2026-08-30。本指南规定 Enput Method 的正式发布流程：脚本只在本地构建、打包、验证并创建本地 Git tag；推送和 GitHub Release 页面操作由发布者手工完成。

## 发布目标与边界

- 唯一发布主线是本地 `main`。`main` 建立自已认可的 `codex/translation-copy-button`，以后所有正式发布均从 `main` 创建。
- 版本参数为 `0.1.0` 形式；本地 Git tag 固定为 `v0.1.0`；发布目录和 ZIP 名为 `EnputMethod-0.1.0-win-x64`。
- 发布 ZIP 是给不会编译的用户的完整安装介质，不提交到 Git。它必须含安装器、卸载器、各自运行时依赖、TSF DLL、Overlay、词库种子、词表、主题、Emoji 资源和许可证/归属文件。
- ZIP **不包含** .NET Runtime。用户电脑缺少 .NET 9 Desktop Runtime x64 时，按 Windows 提示安装即可；发布包内 `README.txt` 会提前说明这一要求。
- 静态词库随包提供：`payload\Resources\enput.seed.db` 会在安装时准备为 `C:\Program Files\Enput Method\Resources\enput.db`，不依赖联网下载。
- 用户配置、快捷键、日志和学习频率不放在 ZIP 的词库资源中；升级不覆盖它们，默认卸载也保留它们。

## 单命令创建本地发布物

在已提交且干净的 `main` 工作区中执行：

```powershell
.\scripts\publish-release.ps1 -Version 0.1.0
```

该命令按以下顺序执行：

1. 检查当前分支必须为 `main`，且工作区没有已跟踪、未跟踪或暂存变更。
2. 检查版本号格式，并检查本地与 `origin` 中均不存在 `v0.1.0`。远端查询最多等待 15 秒；超时或 SSH 失败即停止。任何一处存在同名 tag，同一版本均不能复用。
3. 扫描已跟踪文件中的常见私钥、GitHub Token、AWS 访问密钥，以及 `.env`、`.pem`、`.key`、`.pfx`、`.p12` 文件。命中即拒绝发布，必须人工确认并处理。
4. 以 `Release|x64` 重建安装器、卸载器、TSF 与 Overlay。
5. 创建 `artifacts\release\EnputMethod-0.1.0-win-x64\`，并验证其 `payload`。
6. 生成同名 ZIP，解压到临时目录，确认只有一个版本化顶层目录，再对解压后的安装器运行 `--verify-package`。
7. 生成同名 `.zip.sha256` 校验文件。
8. 仅在上述步骤全部成功后，创建本地注释 tag `v0.1.0`。

脚本绝不执行 `git commit`、`git push`、创建远程 tag 或创建 GitHub Release。构建/验证失败时不会创建 tag；同名 tag 或同名产物目录/ZIP 已存在时会停止，不覆盖旧发布物。

## 首次连接 GitHub 仓库

以下命令只需在本地 `main` 已建立、GitHub 仓库仍为空时执行一次：

```powershell
git remote add origin git@github.com:djabboc/EnputMethod.git
git push -u origin main
```

远程地址中的 SSH 用户名是 `git@github.com`，`@` 前**不能**带反斜杠。首次直接执行 `git push` 时，如果 Git 提示 `main has no upstream branch`，使用上面的 `git push -u origin main`；成功后 `main` 会跟踪 `origin/main`，以后的日常代码推送可直接使用 `git push`。

SSH Key 只用于 Git 推送。电脑有多个 SSH 私钥且 GitHub 未选择正确密钥时，可在本仓库设置指定身份：

```powershell
git config core.sshCommand "ssh -i ~/.ssh/<私钥文件名> -o IdentitiesOnly=yes"
```

`<私钥文件名>` 必须由发布者替换为本机实际私钥。不要把真实私钥路径、私钥文件或其内容写入 Git、文档或 GitHub Release。GitHub 网页上传 Release 资产不依赖 `gh` CLI 或 Token，因此本流程不要求额外登录命令。

## 每次正式发布的手工步骤

### 1. 发布前检查

确认 `main` 包含要发布的全部提交，并确认没有敏感内容。建议至少复核：

```powershell
git switch main
git status --short
git log -1 --oneline
git fetch --tags origin
```

`git status --short` 必须没有输出。`git fetch --tags origin` 会让本地获知远程已有 tag。发布脚本还会直接只读查询 `origin`，最多等待 15 秒；远程查询失败、超时或发现同名 tag 时，脚本会在构建前停止。

### 2. 本地构建、打包、验证并创建 tag

```powershell
.\scripts\publish-release.ps1 -Version 0.1.0
```

成功后应得到：

```text
artifacts\release\EnputMethod-0.1.0-win-x64\
artifacts\release\EnputMethod-0.1.0-win-x64.zip
artifacts\release\EnputMethod-0.1.0-win-x64.zip.sha256
本地 tag：v0.1.0
```

在继续前，可任选一台测试机或当前机器从 ZIP 解压目录运行安装器，接受 UAC，并在重开目标编辑器后确认 Enput 已切换、候选、Emoji 和翻译正常。

### 3. 手工推送代码和 tag

只有本地验证成功后才执行：

```powershell
git push origin main
git push origin v0.1.0
```

若第二条失败，绝不能以同一版本重新打另一个 tag；先停止并查明远程状态。Git tag 是不可变发布标识，不得强推、移动或复用。

### 4. 在 GitHub 页面创建 Release

1. 打开 `https://github.com/djabboc/EnputMethod/releases`。
2. 选择 **Draft a new release**。
3. 在 **Choose a tag** 中选择已推送的 `v0.1.0`，不要在网页创建新 tag。
4. Release title 填写 `Enput Method v0.1.0`。
5. 用中文说明本次主要功能、修复、系统要求与升级注意事项。
6. 上传以下两个文件：
   - `EnputMethod-0.1.0-win-x64.zip`
   - `EnputMethod-0.1.0-win-x64.zip.sha256`
7. 检查 Release 页面显示的 tag、标题、两个附件和文件大小；确认后点击 **Publish release**。

发布说明必须明确：该 ZIP 不包含 .NET Runtime；若 Windows 提示缺少 .NET 9 Desktop Runtime x64，按提示安装后重试；安装需要 UAC；更新后需重开已经打开的编辑器。

## 用户下载后的验证方式

用户下载 ZIP 后，可以在 PowerShell 中校验：

```powershell
Get-FileHash .\EnputMethod-0.1.0-win-x64.zip -Algorithm SHA256
```

所得十六进制哈希应与 `.zip.sha256` 文件开头的值一致。随后解压 ZIP，保持版本目录完整，运行 `Install Enput Method.exe`。

## 常见阻断与处理

| 阻断 | 含义与处理 |
| --- | --- |
| 当前不是 `main` | 切换到 `main`，不要从临时分支发布。 |
| 工作区不干净 | 先提交、还原或忽略变更；发布必须对应一个确定提交。 |
| `v<Version>` 已存在 | 该版本已使用。提升版本号，绝不覆盖 tag 或旧 ZIP。 |
| 远端 tag 查询超时或失败 | 检查网络、SSH 私钥、`core.sshCommand` 和远程地址。脚本会在 15 秒后停止，不构建、不生成 ZIP、不创建本地 tag；解决连接问题后重试。 |
| 敏感扫描命中 | 先判断是否真实凭据；真实凭据必须从历史和工作区移除并轮换，不能仅删除当前文件后继续发布。 |
| 安装器无法启动 | 用户需按 Windows 提示安装 .NET 9 Desktop Runtime x64；该 Runtime 按产品决定不随 ZIP 分发。 |

历史问题、安装边界和词库部署细节见 `development-issue-ledger-zh-CN.md`、`release-packaging-tasks-zh-CN.md` 与 `dictionary-sources-zh-CN.md`。