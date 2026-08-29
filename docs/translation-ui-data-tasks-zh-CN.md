# 翻译 UI 与数据层任务书

## 本批次顺序

1. 修正翻译正文：隐藏内部来源，默认过滤为英文和中文，清洗已知 ECDICT 标记，并补数据/码点回归。
2. 翻译窗改为无激活富文本窗口，支持长文本滚动、任意边和角缩放，以及 `config.json` 尺寸持久化。
3. 构建并运行原生、协议、WPF 自动化和安装验证；在真实宿主中验收 `braces`、`hug`、日语开关、缩放及 VS Code Emoji 字体。
4. 将 SQLite 作为下一阶段独立迁移，不与当前 UI 修复混合。

## SQLite 后续任务

- 选择 Windows 原生 SQLite 依赖及许可分发方式。
- 定义 `entries`、`meanings`、`examples`、`sources`、`metadata` 和 schema version；保留语言、词性、来源优先级与许可信息。
- 提供 ECDICT、CC-CEDICT 和用户 JSON 的离线导入器；用户 JSON 永远作为可编辑覆盖层。
- 提供 JSONL 回退和升级/失败回滚策略。
- 为精确词、词形、模糊词和短语查询建立启动、首查、热查、索引体积及覆盖率基准。
- 仅在基准证明收益且迁移可回退时，将 SQLite 设为默认后端。
