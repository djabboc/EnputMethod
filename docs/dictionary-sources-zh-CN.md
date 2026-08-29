# 词典数据来源

## 默认完整英中词典

- 数据集：ECDICT 1.0.28
- 上游地址：`https://github.com/skywind3000/ECDICT`
- 内容：英文词条、英文释义、中文释义、词性，以及上游提供的扩展字段。
- 许可：MIT License，Copyright (c) 2025 Linwei。
- 安装方式：安装程序首次运行时从上游 CSV 下载并转换为本地 `translations.ecdict.jsonl`。该文件位于 `%LOCALAPPDATA%\Enput Method`，不会提交到源代码仓库，也不会覆盖 `translations.json` 中的用户词条。

## 小型自定义词典

`translations.json` 用于用户自行维护的词条和额外语言映射。运行时优先查询此文件，因此用户可覆盖完整词典中的释义。

## 日文映射

当前日文映射仅包含随程序提供的少量示例词条。完整英日映射计划使用 JMdict；该数据集采用 CC BY-SA 4.0，导入前必须保留其署名和相同许可要求。


## Emoji 数据与显示（2026-08-29）

Emoji 词典使用可编辑的 `emoji.json`，每个条目包含 Unicode Emoji、关键词和可选优先级。安装器合并随版本提供的新关键词和优先级，不删除用户自定义条目。Overlay 使用随安装包部署的 Twemoji PNG 资产以获得彩色渲染；资产遵循 `EnputMethod.Overlay/TWEMOJI-LICENSE.txt` 中的许可。

JSON 文件可能将非 BMP Emoji 序列化成 UTF-16 代理对，例如 `\\uD83D\\uDD25`。运行时必须合并该对并输出合法 UTF-8；不得将高、低代理项分别编码。

## 句子联想与补充义项（2026-08-29）

`suggestions.json` 是可编辑的高频英语续写词典。输入完整触发词时，输入法先显示该词本身，再显示可直接提交的完整短语，最后才显示普通前缀候选。例如 `can` 提供 `can i help you?`、`can you help me?` 等短语。安装器以合并方式添加新的短语，不删除用户已有条目、短语或更高优先级。

`translations.json` 也可存放对 ECDICT 缺失义项的补充；运行时优先使用它。例如 `braces` 补充“牙套；牙齿矫正器”、背带、支撑物和花括号等义项。安装器会迁移早期 `{ "word": { ... } }` 的用户词典格式到 `entries` 数组，再合并内置条目，保留原有释义。

这两类内置补充数据只覆盖高频场景，不能替代受许可约束的大规模语料或词典。ECDICT 的中文释义质量和覆盖范围仍受其上游数据限制。