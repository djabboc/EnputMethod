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
  "inputMethodIcon": "enput.ico",
  "translationLanguages": ["en", "zh-CN"],
  "translationWindowWidth": 380,
  "translationWindowHeight": 280,
  "theme": "dark"
}
```

`candidateCount` 的合法范围是 `1` 到 `9`。候选窗显示多少项，就可以通过数字键 `1` 到对应数字选择多少项。超过 `9` 没有单键选择方式，因此会限制为 `9`。

- `layout`：`vertical` 为竖排，`horizontal` 为横排。
- `appendSpaceAfterSelection`：数字键或 `Tab` 选词后是否自动添加空格。
- `adaptiveCandidateRanking`：是否根据当前用户的历史选词频率调整候选排序。默认 `true`；设为 `false` 后保持词典顺序，且不再记录新的选择频率。
- `fontFamily` 与 `fontSize`：候选窗和翻译窗字体；`fontSize` 单位为点（pt），默认 `18`。WPF Overlay 会按 96/72 换算为设备无关像素，因此 18pt 实际渲染为 24 DIP。
- `opacity`：范围为 `0.2` 到 `1.0`。
- `inputMethodIcon`：语言栏和输入法列表使用的 ICO 文件名。默认 `enput.ico`，文件必须位于 `C:\Program Files\Enput Method\Resources`。可以放入自定义 `.ico` 后填写其文件名；拒绝外部绝对路径、目录名和非 ICO 扩展名，缺失文件回退默认图标。该配置只有重新运行安装程序并重开待测宿主后才会写入 Windows TSF Profile。
- `translationLanguages`：翻译窗显示的语言代码数组。默认仅为 `"en"` 与 `"zh-CN"`；添加 `"ja-JP"` 后才显示随词典提供的日文映射。例如可设为 `["en", "zh-CN", "ja-JP"]`。未列出的语言不会显示。
- `translationWindowWidth` 与 `translationWindowHeight`：翻译窗初始宽高，合法范围分别为 `260`-`1200` 和 `160`-`900` WPF DIP。可以直接编辑；也可以拖动翻译窗任意边或角，停止拖动后会自动写回这两个值。
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

翻译窗使用独立的主题字段：`translationBackground`、`translationForeground`、`translationTitleForeground`、`translationPartForeground`、`translationLabelForeground`、`translationExampleForeground`、`translationExampleBackground`、`translationBorder`、`translationBorderWidth`、`translationCornerRadius`、`translationPadding`、`translationScrollbarTrack` 和 `translationScrollbarThumb`。窗口尺寸由 `config.json` 的 `translationWindowWidth` 与 `translationWindowHeight` 统一控制并持久化；旧主题中的 `translationWidth` 与 `translationMaxHeight` 仅为旧版本兼容字段，不再决定实际窗口大小。翻译正文使用只读富文本 `FlowDocument`，词性、语言标签、义项和例句分别渲染，长内容可以滚动；标题栏 Copy 按钮复制完整翻译，成功后短暂显示 `✓ Copied`，窗口仍保持不抢输入焦点。

## 保序不完整匹配

输入至少三个字符后，普通候选、短语和 Emoji 关键字支持保序但不连续的匹配：`hpy` 可找到 `happy`，`pignose` 可找到 `pig_nose`，`empirestate` 可找到 `empire state building`。精确匹配、短语续写和前缀匹配始终优先于这类近似结果。Emoji 模式也支持配置的 `-`、`+` 和小键盘加减键翻页。


## 2026-08-29 界面与数据修正

- 默认 `fontSize` 现在为 `18` 点。WPF 的 `FontSize` 使用设备无关像素而非点数，因此 Overlay 会按 `fontSize * 96 / 72` 渲染配置值。不要为得到 18pt 的视觉大小而把值写成 24。
- Emoji 候选使用已安装的 Twemoji 彩色资源和扩充目录。安装器会合并提供的关键词和优先级更新，不替换用户新增词条。C++ JSON 读取器现会合并转义的 UTF-16 代理对，因此已安装词条如 `"emoji":"\\uD83D\\uDD25"` 会读取为 `🔥`。
- 完整 ECDICT 翻译可能把换行保存为字面量 `\\n` 或 `\\r\\n`。输入服务会在 WPF 翻译窗口显示前将其转换为真实换行；`block` 是回归用例。
- 候选翻页器采用三区布局：上一页按钮固定在左侧边缘，页码文字在可用空间中居中，下一页按钮固定在右侧边缘。只有确实可以翻动时才启用悬停状态。

## 2026-08-29 翻译窗口与数据修正

- 词典的 `source` 字段不再出现在翻译正文。ECDICT 的 MIT 信息和 CC-CEDICT 的 CC BY-SA 4.0 署名仍被保留；后者写入 `%LOCALAPPDATA%\Enput Method\CC-CEDICT-ATTRIBUTION.txt`，不能删除。
- 释义导入会清除已知的 ECDICT 模板标记 `{{or}}`，并将行首 `n`、`v`、`vt` 等已知词性前缀规范成 `n.`、`v.`、`vt.`。已有句点不重复添加，其他标点不作通用删除。
- `hug` 在完整 ECDICT 中本来就有英中释义；此前只见 `source` 是 UI 把来源作为正文显示导致的误判，不是数据缺失。更新后应关闭并重新打开目标宿主，再按 F3 验证。
- `🪚` 是 Unicode `U+1FA9A`。Enput 提交的码点已通过回归；VS Code 内显示方框表示当前编辑器字体或 Windows Emoji 字体没有该字形，输入法不能替目标编辑器补字形。Saint Helena 使用区域指示符 `S`、`H`，即 `🇸🇭`；瑞士是 `🇨🇭`。两者均增加了码点校验，避免通过旗帜外观误判。

## 2026-08-29：词组扩展与取消联想快捷键

安装器新增 Princeton WordNet 3.1 派生的 62,319 条多词短语，并在已有用户数据库升级时自动导入一次。无空格输入支持匹配含空格短语，例如 `newyork` 可匹配 `new york`，`machinelearning` 可匹配 `machine learning`。精确匹配和前缀匹配仍优先于保序近似结果。

`shortcut.json` 新增可配置数组：

```json
"cancelComposition": ["Escape", "Shift"]
```

默认的 `Escape` 和 `Shift` 都会取消当前未确认的组合文本并关闭候选窗。Emoji 模式在没有输入内容时按该动作会退出 Emoji 模式。用户可从数组移除 `Shift` 或改为其他已支持按键；安装升级只补充缺失字段，不覆盖已有自定义值。

## 2026-09-05：功能键交还给当前应用

`F2` 和 `F3` 仍可在 `shortcut.json` 中配置为 Emoji 和翻译操作，但只有 Enput 已有活动组合输入或候选联想时才会捕获。空闲状态下它们会直接交给当前应用，因此资源管理器的 `F2` 重命名和 `F5` 刷新不会因 Enput 已激活而失效。要查询 Emoji，可先输入关键词（例如 `fire`），再按 F2 切换当前候选。

## 一站式安装与卸载（2026-08-29）

发布 ZIP 解压后，直接运行根目录的 `Install Enput Method.exe`。安装器会显示进度，部署运行组件与静态资源到 `C:\Program Files\Enput Method`，并把 TSF 注册表路径指向那里。因此安装成功后，下载目录或解压目录可以任意移动、删除，不会影响已经安装的输入法。

用户配置现在在 `%LOCALAPPDATA%\Enput Method\UserData\config.json`，快捷键在同目录 `shortcut.json`；旧版根目录中的两项设置会在新文件不存在时迁移。主题、SQLite、词表、Emoji 和其它静态资源不在 AppData，而在 `C:\Program Files\Enput Method\Resources` 或 `Overlay`。升级不会覆盖已有配置；卸载器会移除 Program Files 的产品文件和输入法注册，但默认保留用户配置与学习频率。

要彻底移除个人数据，可在卸载完成后手动删除 `%LOCALAPPDATA%\Enput Method\UserData`，并按需要删除 `HKCU\Software\Enput Method\CandidateFrequency`。这不是默认卸载动作，避免误删用户自定义设置。
