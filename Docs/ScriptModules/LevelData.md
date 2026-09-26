# 关卡数据

## 职责

维护稳定关卡 ID、方块总表、每关 JSON、目录 JSON 和保存前校验。每关文件名使用 `难度_编号_特征.json`；文件名可变，关卡 ID 不变。地基、墙体和箱子以 1 米网格定位。

## 主要文件

已实现：`Assets/Scripts/LevelData/` 中的 `LevelModels`、`LevelCodec`、`LevelValidator`、`GraphProgression`；`Assets/Data/BlockCatalog.asset`；`Assets/Levels/LevelGraph.json`。目录当前登记 `easy_00_guide` 至 `easy_04_pair` 五个简单难度关卡。

## 依赖与协作边界

依赖 Unity Addressables 和 Newtonsoft JSON。关卡 JSON 只存方块 ID，不存预制体地址；总表把 ID 映射到类型、通行属性和 Addressables 预制体。格子规则消费解析后的纯数据，不直接读文件。

## 场景或 Prefab 挂载点

无；总表是 ScriptableObject 资产。地基、墙体、箱子和标记为 Addressables Prefab。

## 输入、事件与对外接口

`LevelCodec` 读取/输出 JSON，枚举以字符串保存；反序列化时替换初始化列表，避免默认地基层重复。`LevelValidator` 与 `GraphValidator` 返回带位置或节点 ID 的诊断；编辑器以非严格模式保存草稿，运行时和项目检查以严格模式要求出生点、箱子、目标及传送出口。目标点与箱子不得同格，这项规则在草稿模式下也生效。坐标为 `(layerId,x,z)`，地基层指定整数 `baseY`，后续层至少高 2。非地基对象必须有地基支撑。`ProjectValidator` 还检查关卡文件与目录节点、Addressables 地址的一致性。

## 资源引用与生命周期

编辑器维护总表和 JSON 的 Addressables 登记；项目校验还检查[关卡流程](./GameFlow.md)使用的胜利粒子预制体地址。运行时加载关卡和总表后由流程模块统一释放句柄。

## 存档字段

关卡 JSON：`schemaVersion`、`levelId`、`layers`、`placements`。目录 JSON：`schemaVersion`、`entryLevelIds`、`nodes`，节点记录 Addressables 地址、位置、选关顺序、解锁策略和有序后继。

## 验证方式

`ChestPush.Tests` 的 JSON 往返、非法引用与解锁用例；`Tools > ChestPush > Validate Project`、Unity Console 和 Addressables 引用检查。

## 已知风险与待办

`easy_02_turn` 当前有一个箱子、没有目标点，严格项目校验报告 `TargetCount`；尚未进行 PlayMode 验证。墙顶行走、楼梯、坡道和出口标记判定仍在计划中。打包前需构建 Addressables 内容。
