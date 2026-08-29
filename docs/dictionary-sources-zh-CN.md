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
## CC-CEDICT 英中补充索引（2026-08-29）

- 数据集：CC-CEDICT 1.0，下载自 MDBG 官方导出。
- 上游地址：`https://www.mdbg.net/chinese/dictionary?page=cc-cedict`
- 用途：安装时将词典中英文定义反向建立为英文词形到中文词头的索引 `translations.cc-cedict.jsonl`，用于补充 ECDICT 缺失的中文义项；不替换用户自定义词条，也不替换 ECDICT 的英文定义。
- 许可：CC BY-SA 4.0。每次成功安装都会在 `%LOCALAPPDATA%\Enput Method\CC-CEDICT-ATTRIBUTION.txt` 写入来源和许可链接。对包含此派生索引的分发版本必须保留署名并遵守相同方式共享义务。
- 网络行为：首次缺失时下载；已有有效索引时复用。下载失败不会阻止安装，输入法继续使用用户词典与 ECDICT。

词典记录中的 `source` 是内部溯源字段，不属于翻译正文。翻译窗口不显示它，以免将 JSON 片段或许可链接误当成释义；ECDICT 和 CC-CEDICT 的许可信息仍必须通过本文件、安装包许可和 `CC-CEDICT-ATTRIBUTION.txt` 保留。

## JSONL 与 SQLite 决策（2026-08-29）

当前 `translations.ecdict.jsonl` 与 `translations.cc-cedict.jsonl` 已按英文键排序，运行时对精确词形作二分查询；在仅显示当前候选词的场景下，性能仍可接受，因此本轮不迁移。

SQLite 已成为下一阶段的数据层任务，触发范围包括多词典来源优先级、词形还原、模糊与全文检索、短语/句子语料、例句筛选、原子更新和索引版本迁移。迁移任务必须先实现统一 schema、离线导入器、许可与版本元数据、查询基准和 JSONL 回退，再用覆盖率与延迟数据决定默认后端；不得直接把现有用户 `translations.json` 删除或强制转换。
