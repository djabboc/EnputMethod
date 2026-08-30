# 图标、复制反馈与候选避让任务书

最后更新：2026-08-30。开发分支固定为 `codex/translation-copy-button`。

## 目标与范围

| 目标 | 实现边界 | 验收 |
| --- | --- | --- |
| 输入法图标 | 默认多尺寸 `enput.ico` 随安装包部署到 `C:\Program Files\Enput Method\Resources`。TSF Profile 的 `IconFile` 只指向该目录。 | 语言栏与输入法列表读取同一已安装 ICO；安装验证检查注册表路径、索引和文件。 |
| 自定义 ICO | `config.json` 的 `inputMethodIcon` 只能是 Resources 根目录的 `.ico` 文件名，拒绝绝对路径、目录穿越和非 ICO 扩展名。缺失或不安全时回退 `enput.ico`。 | 自定义文件不被升级安装覆盖；修改配置并重新安装后才重新注册 Profile。 |
| Copy 确认 | 翻译窗保持非激活；完整翻译写入剪贴板成功后，按钮显示绿色 `✓ Copied` 1.6 秒。 | 成功时有可见反馈；超时恢复 Copy；剪贴板异常时不虚报成功。 |
| 候选避让 | TSF 协议发送完整 composition 矩形；WPF 与原生回退窗口优先将候选置于下方，底部不足时置于上方，并横向收束到工作区。 | 顶部、底部、左/右边缘不遮挡 composition 且不越过工作区，超高候选窗使用记录的最小冲突回退。 |

## 实施顺序

1. 部署并注册默认 ICO，补齐可配置文件名与安装验证。
2. 将 Copy 按钮作为唯一复制入口，提供成功确认状态，不恢复旧的鼠标选区实验。
3. 将组合文本边界加入 Overlay 协议，使用纯函数覆盖所有屏幕边缘，再复用到原生回退窗口。
4. 运行原生、Overlay、发布包和系统安装验证；重开真实宿主后检查 Windows UI 缓存结果。

## 完成记录

- [x] 默认 ICO、受限配置、TSF Profile 重新注册和安装验证。
- [x] Copy 成功反馈与自动化超时回归。
- [x] WPF/原生候选定位和顶部、底部、右侧回归。
- [ ] Release 包实际安装后，在语言栏、输入法列表和底部编辑位置进行人工验收。

## 用户操作

默认图标无需配置。自定义时将 `custom.ico` 放到 `C:\Program Files\Enput Method\Resources`，在 `%LOCALAPPDATA%\Enput Method\UserData\config.json` 写入 `"inputMethodIcon": "custom.ico"`，再运行发布目录的安装程序。不要填写外部绝对路径；安装器不会覆盖已经存在的自定义 ICO。