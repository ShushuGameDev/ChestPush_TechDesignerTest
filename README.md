# 项目文档入口

进入本仓库后，请先阅读本文件，再阅读模块索引和本次任务对应的模块文档，最后进入脚本、场景和资源。

## 阅读顺序

1. 本文件 `README.md`
2. [脚本模块索引](Docs/ScriptModules/README.md)
3. 任务涉及的模块文档
4. 对应的实现文件与 Unity 资源

Agent 的执行与验证规则见 [AGENTS.md](AGENTS.md)。

## 项目概览

本仓库为 Unity 2022.3 URP 的 3D 推箱子项目。运行时使用单一主场景，从 JSON 构建关卡；关卡编辑工具在 Unity Editor 中使用。

## 文档索引

- [脚本模块索引](Docs/ScriptModules/README.md)：模块职责、依赖、场景挂载、输入、事件、存档和风险。
- [关卡数据](Docs/ScriptModules/LevelData.md)
- [格子规则](Docs/ScriptModules/GridRules.md)
- [关卡流程](Docs/ScriptModules/GameFlow.md)
- [关卡编辑器](Docs/ScriptModules/LevelEditor.md)

新增项目级规范时，在此添加链接。具体实现细节、调参过程和日期记录放在对应模块文档，不堆放在本入口。

## 文档维护

- 新增模块：创建 `Docs/ScriptModules/<模块名>.md`，并更新本文件与模块索引。
- 修改已有模块：同步更新受影响的模块文档；涉及多个模块时分别更新。
- 修改项目目录或项目级约定：更新本文件中的概览或索引。
- 只记录已验证的当前状态；尚未实现的内容明确标为“计划中”。
