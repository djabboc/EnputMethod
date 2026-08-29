# 更新说明与配置

## 为什么更新后要重开记事本

这是 Windows TSF 输入法的进程加载方式决定的，不是记事本本身的问题。

当 Enput 在记事本中被激活时，Windows 会在记事本进程内创建输入法对象并加载对应 DLL。即使之后安装器更新了注册表和 DLL 路径，已经打开的记事本仍持有旧对象和旧 DLL，不能自动切换到新版。因此，验证输入法 DLL 更新时必须关闭并重新打开目标应用。

这也是此前更新时出现“安装已完成但行为没有变化”或“旧 DLL 无法覆盖”的关键原因。安装器现在采用版本化 DLL 路径，例如 `EnputMethod.Tsf.8.dll`，避免旧进程锁定文件后阻止更新；但已有应用仍需重开才能加载新版本。

修改词库或候选数量不属于 DLL 更新。输入法会在下一次查询候选时读取配置文件，切换一次输入法或重新输入前缀即可生效。

## 配置文件

安装后，配置目录为：

`%LOCALAPPDATA%\Enput Method`

其中的 `config.json` 默认内容为：

```json
{
  "candidateCount": 9,
  "layout": "vertical",
  "appendSpaceAfterSelection": true,
  "adaptiveCandidateRanking": true,
  "fontFamily": "Segoe UI",
  "fontSize": 18,
  "opacity": 1.0,
  "theme": "dark"
}
```

`candidateCount` 的合法范围是 `1` 到 `9`。候选窗显示多少项，就可以通过数字键 `1` 到对应数字选择多少项。超过 `9` 没有单键选择方式，因此会限制为 `9`。

- `layout`：`vertical` 为竖排，`horizontal` 为横排。
- `appendSpaceAfterSelection`：数字键或 `Tab` 选词后是否自动添加空格。
- `adaptiveCandidateRanking`：是否根据当前用户的历史选词频率调整候选排序。默认 `true`；设为 `false` 后保持词典顺序，且不再记录新的选择频率。
- `fontFamily` 与 `fontSize`：候选窗和翻译窗字体；`fontSize` 单位为点（pt），默认 `18`。WPF Overlay 会按 96/72 换算为设备无关像素，因此 18pt 实际渲染为 24 DIP。
- `opacity`：范围为 `0.2` 到 `1.0`。
- `theme`：`dark`、`light`、`eye-care` 或 `paper`。

旧版 `conf.json` 会被兼容读取；如为旧版默认内容，安装时会升级为新的 `config.json`。JSON 文件可以保存为带或不带 UTF-8 BOM 的格式。

## 词库文件

同一目录的 `dictionary.txt` 是普通 UTF-8 文本文件：一行一个英文单词，文件顺序就是候选优先级。当前默认词库有 370,763 个去重英文词：常用词频表排在前面，随后追加完整词表。因此常用候选保持靠前，同时 `xylem` 等较少见单词也可被检索。输入法会缓存词库，仅在文件大小或修改时间变化时自动重载。

例如希望输入 `th` 时优先显示 `this`，可将相关部分调整为：

```text
the
this
that
they
there
```

默认词库已经包含 `the`、`this`、`that`、`they`、`there`、`through`、`thank` 等常用词。安装更新不会覆盖已有的 `config.json`、`dictionary.txt` 或主题文件，因此可以安全维护自己的词库。

## 主题

主题目录为 `%LOCALAPPDATA%\Enput Method\themes`，包含四个默认文件：`dark.json`、`light.json`、`eye-care.json`、`paper.json`。主题文件可控制背景、前景、首选项颜色、边框、圆角、内边距、行高和阴影尺寸。主题修改会在下一次显示候选窗时读取。

翻译窗使用独立的主题字段：`translationBackground`、`translationForeground`、`translationTitleForeground`、`translationBorder`、`translationBorderWidth`、`translationCornerRadius`、`translationPadding`、`translationWidth`、`translationMaxHeight`、`translationScrollbarTrack` 和 `translationScrollbarThumb`。`translationMaxHeight` 低于完整释义高度时会显示滚动条；默认深色主题设置为 `160`，便于验证长释义的滚动。安装更新只会补齐主题中缺失的新字段，已有值不会被覆盖。


## 2026-08-29 UI and Data Corrections

- The default `fontSize` is now `18` points. A WPF `FontSize` is measured in device-independent pixels, not points; the Overlay therefore renders the configured value at `fontSize * 96 / 72`. Do not set 24 merely to obtain an 18pt visual size.
- Emoji candidates use installed Twemoji color assets and an expanded catalog. The installer merges the supplied keyword and priority updates without replacing user-added entries. The C++ JSON reader now combines escaped UTF-16 surrogate pairs, so an installed entry such as `"emoji":"\\uD83D\\uDD25"` is read as `🔥`.
- Full ECDICT translations may store line breaks as literal `\\n` or `\\r\\n`. The input service now converts those markers to real line breaks before the WPF translation window displays them. `block` is a regression example.
- Candidate pager placement is a three-region layout: previous button at the left frame edge, page text centered in available space, next button at the right frame edge. Its hover state is enabled only when movement is available.
