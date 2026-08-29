# 词典数据来源

## 当前运行时存储（2026-08-29）

所有运行时词库均存储在 `C:\Program Files\Enput Method\Resources\enput.db`，TSF 只查询 SQLite。安装包包含基础 `enput.seed.db`；已安装旧版本 AppData 根目录的 SQLite 会优先迁移到 Program Files，旧 JSON/JSONL 词库会在事务导入、schema 与基本查询验证成功后删除。`suggestions.json`、`emoji.json`、`translations.json` 和两个 `translations.*.jsonl` 均不是运行时回退文件，新安装包也不再包含词库 JSON。

`%LOCALAPPDATA%\Enput Method\UserData` 中的 `config.json`、`shortcut.json` 是用户设置文件；主题 JSON 是 Program Files 静态资源，和词库存储无关。

## 默认完整英中词典

- 数据集：ECDICT 1.0.28
- 上游地址：`https://github.com/skywind3000/ECDICT`
- 内容：英文词条、英文释义、中文释义、词性，以及上游提供的扩展字段。
- 许可：MIT License，Copyright (c) 2025 Linwei。
- 安装方式：完整 ECDICT 数据导入 SQLite 的 `translation_*` 表；下载或旧版本迁移过程产生的 JSONL 仅作短暂导入输入，成功后删除。

## 小型补充词典

基础翻译、额外语言映射和 ECDICT 数据统一存入 SQLite，并按来源 rank 合并。当前版本不提供直接编辑数据库的 UI；用户词条编辑接口应在后续以 SQLite 写入 API 实现，不能重新引入 JSON 覆盖层。

## 日文映射

当前日文映射仅包含随程序提供的少量示例词条。完整英日映射计划使用 JMdict；该数据集采用 CC BY-SA 4.0，导入前必须保留其署名和相同许可要求。


## Emoji 数据与显示（2026-08-29）

Emoji 词典存储在 SQLite 的 `emoji` 与 `emoji_keyword` 表中，每项包含 Unicode Emoji、关键词和优先级。Overlay 使用随安装包部署的 Twemoji PNG 资产以获得彩色渲染；资产遵循 `EnputMethod.Overlay/TWEMOJI-LICENSE.txt` 中的许可。

JSON 文件可能将非 BMP Emoji 序列化成 UTF-16 代理对，例如 `\\uD83D\\uDD25`。运行时必须合并该对并输出合法 UTF-8；不得将高、低代理项分别编码。

## 句子联想与补充义项（2026-08-29）

高频英语续写词典存储在 SQLite 的 `suggestions` 表。输入完整触发词时，输入法先显示该词本身，再显示可直接提交的完整短语，最后才显示普通前缀候选。例如 `can` 提供 `can i help you?`、`can you help me?` 等短语。

`braces` 等补充义项也位于 SQLite；例如“牙套；牙齿矫正器”、背带、支撑物和花括号等义项按来源 rank 与 ECDICT 合并。

这两类内置补充数据只覆盖高频场景，不能替代受许可约束的大规模语料或词典。ECDICT 的中文释义质量和覆盖范围仍受其上游数据限制。
## CC-CEDICT 英中补充索引（2026-08-29）

- 数据集：CC-CEDICT 1.0，下载自 MDBG 官方导出。
- 上游地址：`https://www.mdbg.net/chinese/dictionary?page=cc-cedict`
- 用途：安装时将词典中英文定义反向建立为英文词形到中文词头的 SQLite 索引，用于补充 ECDICT 缺失的中文义项；不替换 ECDICT 的英文定义。
- 许可：CC BY-SA 4.0。每次成功安装都会在 `%LOCALAPPDATA%\Enput Method\CC-CEDICT-ATTRIBUTION.txt` 写入来源和许可链接。对包含此派生索引的分发版本必须保留署名并遵守相同方式共享义务。
- 网络行为：首次缺失时下载；已有有效索引时复用。下载失败不会阻止安装，输入法继续使用用户词典与 ECDICT。

词典记录中的 `source` 是内部溯源字段，不属于翻译正文。翻译窗口不显示它，以免将 JSON 片段或许可链接误当成释义；ECDICT 和 CC-CEDICT 的许可信息仍必须通过本文件、安装包许可和 `CC-CEDICT-ATTRIBUTION.txt` 保留。

## SQLite 决策（2026-08-29）

已采用 Windows 内置 `winsqlite3.dll`。Schema version 为 `1`，导入采用 pending 数据库、事务、校验和原子替换，失败时保留原 `enput.db`。没有 JSONL 回退：旧文件仅在成功导入后删除。后续扩展包括词形还原、模糊/全文检索、短语语料、例句筛选和 schema version 升级。

## WordNet 多词短语（2026-08-29）

- 数据集：Princeton WordNet 3.1，下载自官方词典归档：`https://wordnetcode.princeton.edu/wn3.1.dict.tar.gz`。
- 导入内容：从名词、动词、形容词和副词索引提取二至五词的英文 lemma；下划线转换为空格，得到 62,319 条短语。
- 覆盖：除通用地名和常用固定短语外，包含经济学、商业、心理学、计算机科学、工程和法律等学科术语。内置高优先级补充覆盖 `machine learning`、`software engineering`、`new york`、`empire state building` 等 WordNet 不完整或需要优先召回的短语。
- 安装方式：安装器在 SQLite `suggestions` 表一次性导入，并以 `metadata.builtinPhraseVersion` 记录版本。运行时仍只查询 `enput.db`，不读取该文本文件或任何 JSON 词典。
- 署名：安装包内 `WORDNET-ATTRIBUTION.txt` 保留原始来源、下载日期、派生文件 SHA-256 和 WordNet 许可说明。