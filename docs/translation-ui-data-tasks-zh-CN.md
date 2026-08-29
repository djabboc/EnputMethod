# 翻译 UI 与数据层任务书

## 本批次顺序

1. [x] 修正翻译正文：隐藏内部来源，默认过滤为英文和中文，清洗已知 ECDICT 标记，并补数据/码点回归。
2. [x] 翻译窗改为无激活富文本窗口，支持长文本滚动、任意边和角缩放，以及 `config.json` 尺寸持久化。
3. [x] 构建并运行原生、协议、WPF 自动化和系统级安装验证。结果记录在 `0be9934`；默认配置已确认写入 `["en", "zh-CN"]` 和 `380 x 280`。
4. [ ] 在真实宿主中验收 `braces`、`hug`、日语开关、任意边缩放与持久化、以及 VS Code Emoji 字体兼容性。
5. [x] SQLite 运行时迁移：使用 Windows 内置 `winsqlite3.dll`，安装包携带 `enput.seed.db`，TSF 只查询 SQLite；现有 JSON/JSONL 在一次事务导入和验证后删除，不保留回退。

## SQLite 实现状态

- Schema version 为 `1`；包含 words、suggestions、emoji/emoji_keyword、translation_entry、translation_part、translation_meaning、translation_example 与 metadata。
- 使用 Windows 10+ 自带的 `winsqlite3.dll`，无需 NuGet、额外原生 DLL 或网络依赖。
- 安装阶段写入 `enput.db.pending`，完成事务与行数验证后原子替换为 `enput.db`，并写入 `enput.db.ready`。
- JSON/JSONL 仅作为旧版本的一次性导入输入；导入后删除。新安装包不包含 JSON 词库文件。
- 自动自检覆盖 schema、`he` 前缀、`can i help you?`、`fire`、`saw`、`braces`、`hug` 与旧文件不存在。
