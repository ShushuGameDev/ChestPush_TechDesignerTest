# 关卡流程

## 职责

在一个主场景内提供标题、选关、游戏、胜利特效、结算与分支下一关流程；加载 Addressables 关卡和特效资源，保存本地通关进度。

## 主要文件

已实现：`Assets/Scripts/GameFlow/` 中的 `GameFlowController`、`SceneMenuUI`、`LevelPresenter`、`DemoUI`、`ProgressStore`。

## 依赖与协作边界

依赖[关卡数据](./LevelData.md)、[格子规则](./GridRules.md)、uGUI 和 TextMeshPro。UI 和动画消费规则结果，不能自行修改格子状态。

## 场景或 Prefab 挂载点

`Assets/Scenes/MainScene.unity` 为唯一运行时场景。控制器通过 `RuntimeInitializeOnLoadMethod` 创建；`MenuCanvas` 挂载 `SceneMenuUI` 并引用 `MainMenu`、`ChooseLevel`、三个首页按钮及 `GridField`、`HardLevel`。首页和选关页复用场景 UI；加载、结算、错误提示和 HUD 使用动态 uGUI。表现层按关卡范围配置 45° 俯视正交相机，并允许绕关卡中心按 45° 步进平滑旋转。

## 输入、事件与对外接口

首页“从头开始”加载目录中的首个可进入入口关卡，“选关”打开选关页，“退出”在编辑器停止 Play Mode、在播放器退出应用。选关页的 `HardLevel` 仅显示目录中实际存在的难度按钮；按关卡地址文件名首段分组（`tutorial`/`guide`、`easy`、`medium`/`normal`、`hard` 分别显示教程、简单、中等、困难），`GridField` 按 `selectOrder` 生成该难度的关卡按钮。未解锁关卡显示锁定并禁用；返回按钮或 Esc 回到首页。WASD 或方向键按当前视角映射到最近的四向格子轴并逐格移动，Q 逆时针、E 顺时针平滑旋转视角 45°，空格交互，Z 撤回，R 重开，游戏中 Esc 返回选关；菜单也有对应按钮。运行时在玩家四周的对应世界方向投影 W/A/S/D 按键提示；提示每帧按相机的实时朝向更新，旋转过渡中也保持与画面方向一致。每个提示以低饱和蓝灰色半透明箭头指向实际移动方向，按键文字保持直立。旋转过渡期间暂停接收操作；视角旋转不修改格子状态和操作数。所有箱子在目标点时锁定操作并记录通关，清除 HUD 后在每个完成目标点上方播放绿色粒子爆发；等待全部粒子消失，再短暂停顿后显示下一关选择。多后继关卡按目录顺序展示。目标节点的 `Any`/`All` 解锁策略决定是否能进入。

## 资源引用与生命周期

运行时加载总表、目录和关卡，并预加载 Addressables 地址 `ChestPush/Effects/VictoryBurstGreen` 的 `Assets/Prefabs/Effects/VictoryBurstGreen.prefab`。粒子参数和 URP 材质保存在该预制体及同目录材质资产中；通关时实例化，播放结束后销毁实例，控制器销毁时释放 Addressables 句柄。`DemoUI` 从 `Assets/Resources/Font/SourceHanSansSC-Regular.otf` 加载动态菜单、HUD 与方向提示字体；方向提示使用不拦截点击的 Screen Space Overlay 半透明箭头，箭头由 uGUI 图形程序化组成，不依赖特殊字体字形，也不创建场景序列化对象。`SceneMenuUI` 在页面切换时补齐场景按钮引用，只清理由它生成的关卡按钮，场景菜单引用缺失时流程回退到动态菜单；生成的难度、关卡和返回按钮使用 `Assets/Resources/Texture/Light.png`，文字为黑色；按当前难度在 `HardLevel`、`GridField` 下创建按钮，切换难度或重进选关页时销毁旧按钮；实例化 Addressables Prefab，换关时释放实例与加载句柄。

## 存档字段

本地进度 JSON 保存已通关关卡 ID 和最近选择的关卡 ID。

## 验证方式

优先检查脚本编译、Unity Console、胜利预制体和材质、Addressables 登记、场景序列化引用及关卡目录资源；默认不运行 PlayMode。

## 已知风险与待办

目录当前有 `easy_00_guide` 到 `easy_04_pair` 五个简单难度关卡；其余难度在对应关卡写入目录后出现。粒子效果和完整通关流程尚未在播放器中实测；`easy_02_turn` 当前缺少目标点，不能完成该关。首版出口标记不参与结算。打包前需构建 Addressables 内容。
