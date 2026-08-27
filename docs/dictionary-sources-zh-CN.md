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
