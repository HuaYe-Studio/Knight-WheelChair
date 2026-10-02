# KWC / 轮椅骑士

Knight WheelChair 是用于 **2026–2027 学年基础开发课程**的 2.5D 俯视角类吸血鬼生存 Demo，目标平台为 PC。七名成员以各自后续授课模块为开发边界，完成移动、自动攻击、敌人、成长和最终 Boss 的教学项目。

> 首先要“适合教”，其次才是“好玩”。

## 当前项目状态

**项目初始化 / 开发准备阶段。** 本目录已经是 Unity 项目根目录，可以作为后续 GitHub 仓库根目录。

已具备六个模块的开发位置、公共读取契约、GameManager 最小骨架、简单对象池、数值配置、两个入口场景、Git 规则和 PR 模板。场景能进入 Play Mode 并切换到 Game 开发场景。

**当前没有可游玩的完整 Gameplay。** 玩家移动、地图碰撞、AI、攻击/掉落/成长、Spawn、正式 UI 和存档均留给对应 Owner。开发面板、预览相机和空场景是工程启动检查设施，不代表已确认最终界面或视角。

当前未执行本项目的 `git init`、初始提交、远端创建或推送，没有创建教学 Tag。首次建库步骤见 [GitGuide](GitGuide.md#2-首次建库主程一次性操作)。

## 文档导航

| 文档                                 | 职责                              |
| ---------------------------------- | ------------------------------- |
| [KWC_GDD.md](KWC_GDD.md)           | 设计事实来源：玩法、Scope、模块职责、Owner与课程安排 |
| [数值设计.md](数值设计.md)                 | 设计事实来源：具体数值、公式、成长与波次参数          |
| [Architecture.md](Architecture.md) | 代码和Unity工程的组织方式、状态所有权、依赖与公共边界   |
| [GitGuide.md](GitGuide.md)         | 新人可照着操作的分支、提交、PR、冲突和教学Tag流程     |

技术文档不覆盖设计源。发现未定规则时先记录并与相关 Owner/策划确认，不以“默认值”代替决策。

## Unity 与依赖

- **实际创建、编译和运行版本：Unity 2022.3.62f3c1**，revision `1623fc0bbb97`，与 GDD 完全一致。
- 标准 3D / **Built-in Render Pipeline**，未切换 URP/HDRP。
- Input System `1.14.0`，Active Input Handling = Input System Package (New)。未提前绑定未确认的移动按键。
- Cinemachine `2.10.3`、TextMeshPro `3.0.7`、UGUI `1.0.0`；导入 TMP Essential Resources，不导入示例或第三方素材。
- `packages-lock.json` 由 Unity 解析生成；Test Framework `1.1.33` 与 NUnit `1.0.6` 是官方包的传递依赖。
- Visible Meta Files、Force Text；保留标准3D项目的基础物理设置，移动平面/碰撞关系等待联合确认。

本次使用本机 `D:\.DevSpace\2022.3.62f3c1\Editor\Unity.exe`。仓库不依赖此绝对路径，其他成员在自己的 Unity Hub 中安装并选择同一版本即可。首次打开时等待 Package Manager 解析和脚本编译。

## 如何开始开发

1. 由主程完成首次上传后，Clone 实际仓库；当前没有可代填的远端地址。
2. Unity Hub → Add project from disk，选择**本仓库根目录**，使用 `2022.3.62f3c1` 打开。
3. 阅读 README、两份设计源，再阅读 Architecture 和 GitGuide。
4. 打开 `Assets/KWC/Scenes/MainMenu.unity`，进入 Play Mode；点击 **Start - load Game scaffold**，确认状态为 Playing。Game 当前应为空开发场景；可以用开发按钮重新载入。
5. 从更新后的 main 创建 `feature/<功能>` 分支。
6. 在自己的 `Modules/<模块>` 中开发，参考该目录 README；按实际交付创建 `Prefabs/<模块>` 和 `Art/<模块>`。
7. 完成自己功能及相关联调验证，附带 `.meta`、配置与文档变更，提交 PR。
8. 主程/相关 Owner Review，通过后 Squash 合并到 main。

例如日常新功能：

```powershell
git switch main
git pull --ff-only origin main
git switch -c feature/player-movement
```

其他命令和冲突处理见 [GitGuide](GitGuide.md)。不要在 main 直接开发。

## 当前工程内容

```text
项目根目录/
  KWC_GDD.md、数值设计.md       设计源
  Architecture.md、GitGuide.md、README.md
  .gitignore、.gitattributes、.editorconfig
  .github/pull_request_template.md
  Assets/KWC/
    Core/                     GameState、AttributeType、3个公共接口
    Modules/                  Map / Player / Enemy / Combat / UI / GameSystem
    Data/                     8份ScriptableObject资产
    Prefabs/GameSystem/       GameManager.prefab
    Scenes/                   MainMenu.unity、Game.unity
    Art/                      资源组织说明
    Editor/                   初始化工具与骨架集成验证工具
  Assets/TextMesh Pro/         官方基础UI资源
  Packages/                   manifest和lock
  ProjectSettings/            Unity生成并配置的项目设置
```

`GameConfig` 组合 PlayerBase、Weapon、Enemy1、Boss、CombatRules、AttributeGrowth、Waves；运行时值不写入 SO。属性的 S₀ 引用 PlayerBase，波次提供 E1 HP/Attack；避免复制状态。Wave 10 的确切 E1 生成量未确认，`TryGetActualSpawnCount` 返回 false，必须处理，不能把占位0当成设计。

`GameManager` 随场景创建/销毁，不是 Singleton；当前仅接通场景入口和状态容器。暂停、升级界面关闭、正式波次、死亡裁决尚未接通。`PrefabPool` 先调用初始化委托再启用对象，保护重复/跨池回收；组件状态复位仍由 Owner 负责。

不使用 asmdef，不提前建立大量接口、Manager 或空 Prefab。正式 Player/Enemy1/Boss/Projectile/ExpOrb/HealthPack/SpawnWarning 的组合方案已写进 Architecture，等待 Owner 实现。

## 验证与本机产物

2026-10-02 已完成：

- 指定版本真实创建工程，脚本编译通过；两个场景及配置引用验证通过。
- Play Mode：MainMenu → Game、状态事件、终局后重开、仅保留一个新 GameManager。
- 对象池：停用时初始化、复用同一对象、跨池/重复归还拒绝、批量回收、初始化异常后的恢复。
- 配置与设计源核对；普通波次45项参数逐项一致，Boss与玩家基础参数一致。
- `.meta`、GUID引用及Git忽略规则检查；Library/Temp/Logs/UserSettings/Builds不进入上传列表。
- Windows x64 Development Build 成功，Unity 正常以返回码0退出；本机产物为 `Builds/Windows/KWC.exe`。这是可启动的开发骨架，不是完整游戏发行版。

本机 `Logs` 保存 `unity-create.log`、`unity-setup.log`、`unity-tmp-import.log`、`unity-playmode.log`、`playmode-result.txt` 和 `unity-build.log`；这些是本机诊断产物，不提交。Editor 启动可能输出在线账户 access token 更新警告，但本机许可证可用，不影响上述已执行检查。

最终资产检查：94个meta，无缺失、无重复GUID；KWC场景/Prefab/SO引用可解析，入口文档无断链。Git忽略检查使用 `Logs/git-validation` 临时仓库，不是本项目的正式Git初始化。

需要复查公共骨架时，先关闭当前项目的其他 Unity 实例。在仓库根目录 PowerShell 执行（替换编辑器路径）：

```powershell
$unity = 'D:\.DevSpace\2022.3.62f3c1\Editor\Unity.exe'
$project = (Get-Location).Path
New-Item -ItemType Directory -Path Logs -Force | Out-Null

# 不加 -quit：检查器在完成Play Mode后退出；保留图形设备以验证正常编辑器运行。
& $unity -batchmode -projectPath $project -executeMethod KWC.Editor.ScaffoldChecks.StartPlayModeSmokeTest -logFile "$project/Logs/unity-playmode.log" | Out-Host

# 构建骨架，输出到已忽略的 Builds/Windows。
& $unity -batchmode -nographics -quit -projectPath $project -executeMethod KWC.Editor.ScaffoldChecks.BuildWindows -logFile "$project/Logs/unity-build.log" | Out-Host
```

查找日志中的 `KWC_PLAYMODE_PASS` / `KWC_BUILD_PASS`，同时检查进程结果和错误。工具只验收当前骨架，不替代各 Owner 的 Gameplay 测试。`ProjectSetup.CreateScaffold` 是初始化记录，已有资产会拒绝重建，**克隆后不需要运行**。命令参数参考 [Unity 2022.3 命令行说明](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html)。

## 开发原则与Git协作

> 策划控制游戏范围，主程控制工程整体，Owner控制模块内部。

保持 P0 范围，不加入 P1 功能、新敌人、新武器或教学不需要的复杂框架。公共接口、Core、状态机、共享数据结构和项目设置需主程 Review。main 保持可运行，一个功能一个分支，PR合并，稳定版本再打教学Tag。详细步骤见 [GitGuide.md](GitGuide.md)。
