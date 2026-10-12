# Enemy / AI · 王勤伟

本目录放 Enemy1、Boss FSM/移动/命中机会/动画接线/AI Debug；Prefab 交付到 `Assets/KWC/Prefabs/Enemy`。

读取 `IPlayerContext`、Map 碰撞事实和配置，向 Combat 提交伤害。Combat Health 拥有 HP；Enemy 处理 Dead 行为，Game System 负责生成和回收。Enemy1 首次接触立即攻击。Boss 仅 Dash 造成伤害，单次最多一次，蓄力结束才锁位置快照，撞墙进入 Recovery。

复用时清掉目标、Dead、冷却、Dash快照/命中记录。非击杀的波末回收不能请求掉落。持续接触使用配置节拍，不会因 5 秒超时重进而额外首击；真正脱离后重接触是否重置仍待确认。绕障与 Wander 选点依 GDD 的待确认项处理。不要添加敌人种类或招式。

### 本次阶段交付范围

改动涵盖 Enemy1/Boss Controller、Brain/状态、Combat Bridge 接口、FSM Core、Enemy 自检与本说明。HP 同步和死亡通知均携带 `LifeId`；普通 HP 回调只更新事实，只有当前生命的显式死亡事件可进入 Dead。纯逻辑验证已覆盖 Enemy1、Boss 生命周期，12 秒连续接触和自然冲刺无 FSM Error。尚未接入真实 Enemy/Combat/Map，也未验证完整刷怪、战斗、死亡与回收链路。当前工作区没有 `ProjectSettings` 改动。

> 本 README 只维护**对外接口**与**待确认项**。内部实现（三层分离、约定落实、目录组织）见 Architecture 与代码注释，不在这里重复。

---

## 一、对外接口

命名空间 `KWC.Enemy`，不使用 `.asmdef`。所有依赖由 Game System 在场景接线时**显式调用 `Initialize` 注入**（Architecture 第 3 节）；不做全局查找、不用 Inspector 序列化接口、不设静态 Instance。

### 1.1 我方需要别人提供的

| 需求 | 由谁提供 | 类型 | 现状 |
|---|---|---|---|
| 玩家位置 | Player 实现 `KWC.Core.IPlayerContext` | Core 已有 | 可用 |
| 移动执行 | Game System 接线注入 `IMovementHandler` | 已提供 `SimpleMovementHandler` | 可用，仅驱动 Transform |
| 接触事实 | 物理 + Layer Matrix | 接口 `IContactSource` | **占位实现**：`PhysicsContactSource` 用「transform 相等」判定；Player 中仅为 `internal`，仍会编译进 Player，正式交付前需替换或移除 |
| 撞墙事实 | Map 提供通行性查询 | 接口 `IWallCheck` | **未实现**，运行期用 `NoWallCheck`（永远报「没墙」） |
| 追击目标点 | 本模块自带 `IWanderPointSource` | 接口 | 可用，**不做可达性校验** |
| HP 事实 | Combat Health 往里报告 | `FactHealthSource` | 接口已定，**尚无真实调用方** |
| 配置 | Game System 的 SO | `Enemy1Config`、`BossConfig` | 可用 |
| 每波 E1 数值 | Game System 波次表 N12 | `Initialize` 的 `waveHp` / `waveAttack` | 接口已定 |

### 1.2 我方提供给别人的

| 提供 | 谁调用 | 说明 |
|---|---|---|
| `Enemy1Controller.Initialize(...)`<br>`BossController.Initialize(...)` | Game System | 唯一的接线入口，签名见下 |
| `ResetForReuse()` | Game System | 池化复用时**必须**调用，见 1.4 |
| `HealthFacts`（`FactHealthSource`） | Combat Health | 通过它把真实 HP 报告进来 |
| `OnHealthReported(lifeId, current, max)` | Combat Health 经桥接 | 只接受当前生命编号，只同步 HP 事实，不推断死亡 |
| `ReportEnemyDied(lifeId)` | Combat Health 经桥接 | **死亡的唯一入口**，返回 false 表示 lifeId 过期 |
| `LifeId` / `DeathReportArmed` | 流程、Debug、测试 | 只读。lifeId 用于判重与识别迟到回调 |
| `CurrentStateId` / `TimeInState` / `TransitionCount` / `LiveStateCount` | UI、Debug、测试 | 只读诊断属性 |
| `Machine` | 测试 / Debug | 返回运行时状态机实例；可用于审计和测试，不是只读快照，生产调用方不应经它驱动转移 |
| `PlayerDamageRequest`（定义在 `Bridge/ICombatBridge.cs`） | Combat 实现 `ICombatBridge` | 伤害请求与死亡通知，见 1.3 |

**接线签名（当前实现，可直接照抄）：**

```csharp
// Enemy1.prefab
void Initialize(IPlayerContext playerContext, IMovementHandler movement,
    IContactSource contactSource, ICombatBridge combatBridge, Enemy1Config config,
    FactHealthSource healthSource, float waveHp, float waveAttack);

// BossKnightWheelChair.prefab
void Initialize(IPlayerContext playerContext, IMovementHandler movement, ICombatBridge combatBridge,
    IWallCheck wallCheck, IWanderPointSource wanderPointSource, BossConfig config,
    FactHealthSource healthSource, float waveHp, int wanderRandomSeed);
```

### 1.3 与 Combat 的接口（**待 Review**）

Enemy 只发请求，不写 HP、不读 Combat 内部。全部集中在 `Bridge/ICombatBridge.cs`，
占位实现是 `Bridge/LogOnlyCombatBridge.cs`：

```csharp
// 出向：Enemy -> Combat
bool TrySubmitPlayerDamage(PlayerDamageRequest request);
void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss, int lifeId);

// 入向：Combat -> Enemy
void ReportEnemyHealth(MonoBehaviour enemy, int lifeId, float current, float max);
void ReportEnemyDied(MonoBehaviour enemy, int lifeId);
```

- `PlayerDamageRequest` 携带：`Attacker` / `Damage` / `HitPoint` / `Kind`（`Contact` 或 `Dash`）。
- 返回 `true` 只表示**请求已交出**，不代表玩家掉血；结果归 Combat。
- 当前实现是 `LogOnlyCombatBridge` 占位：只记日志、**故意不伪造伤害**。契约谈定后**只需替换这一个文件**，State 与 Brain 不用改。

**死亡通道（重要，涉及重复通知）：**

- **死亡是事件，不是推断。** `OnHealthReported(lifeId, current, max)` 只更新 HP 事实，本模块不会用「血量 <= 0」去触发死亡；`LifeId` 不匹配的 HP 回调与死亡事件都会拒绝。
  之前那样做会重复触发：零血时每一次重复上报都满足条件，于是掉落发两次。
- Combat Health Owner 提供 HP 同步与死亡事件；Enemy Controller 负责验证 `lifeId` 并接收。Game System/池化 Owner 在新生命初始化/复用时负责调用 `ResetForReuse()` 并将当前 `LifeId` 交给 Combat。Combat 必须在血量归零时调用 `ReportEnemyDied(enemy, lifeId)`，这是进入 Dead 的唯一入口。
- **lifeId 必须原样回传**：它由 `LifeId` 属性读出，每次池化复用递增。Combat 在采集 HP/死亡结果时就要捕获该编号，并随异步/延迟回调原样携带；不得在发送回调前重新读取对象的当前编号。编号不符的 HP 与死亡输入都会被拒绝。
- 反向 `NotifyEnemyDied` 同样带 lifeId：流程若在回调到达时发现对象已被复用，应当用 lifeId 判断并忽略，否则会给新一轮实例重复掉落。
- **终态锁定**：状态机进入终态（`IsTerminal`，即 Dead）后会拒绝一切转移请求（含事件打断），并清空已排队的转移。这是「Dead → ContactAttack → Dead」往返的防线；离开终态只能通过 `ResetForReuse()`。

### 1.4 生命周期约定（Game System ）

- `Initialize(...)` 结束后对象**立刻可用**，不需要再等 `Start`。
- `PrefabPool.Rent` 会先调用初始化委托再激活，因此 `OnEnable` 能读到本轮数据。
- 池化复用**必须调用 `ResetForReuse()`**：它清 HP、重新武装死亡闸门并递增 lifeId、清攻击间隔、Dash 冷却、快照、命中记录、决策计时，并通过 `IMovementHandler.ResetMovement` 恢复移动能力。
- 波末清理、重开、场景卸载请用 `ResetForReuse()` 或直接禁用；**不要自己 Destroy 借出的实例**（Architecture 第 10 节）。
- Enemy 不自行 `SetActive(false)`、不调用 `Time.timeScale`、不订阅全局事件。
- 决策节拍默认 0.1 s；若要外部统一控制暂停，请用 `Tick(dt)` 而不改内部时钟。

---

## 二、待确认项

分四类：**阻塞联调**（不定就写不下去）、**需要别人交付**、**需要策划拍板**、**dev 占位值**。

### 2.1 阻塞联调：必须主程 + Combat Owner 确认

| 项 | 现状 | 影响 |
|---|---|---|
| 伤害请求签名 | `ICombatBridge` / `PlayerDamageRequest` 仅为**提案** | 接不了真实伤害 |
| 阵营识别方式 | 未定 | Combat 无法判断攻击方 |
| 命中载荷字段 | `Attacker`/`Damage`/`HitPoint`/`Kind` 为提案 | 可能需要补方向、暴击、来源 id |
| 返回值语义 | 现为 `bool`（送达/未送达） | 是否要返回「生效/被忽略/无敌中」 |
| Combat Health 的伤害入口 | `FactHealthSource.ReportHealth` 已留好，**无调用方** | 敌我掉血都不会发生 |
| `LifeId` / HP / 死亡通道 | 提供方：Enemy Controller；调用方：Combat Health（HP 与死亡入向）、流程（死亡出向接收）；Game System/池化 Owner 管理复用并把当前 ID 传给 Combat。已在本模块登记，仍需 Combat 与 Game System Owner 确认接入点和持有方式 | 回调必须携带发起时捕获的生命编号；不得在迟到回调时重新读取当前编号 |
| `IMovementHandler.ResetMovement` | 提供方：Enemy 接口；调用方：Enemy Controller；实现方：移动执行器（当前 `SimpleMovementHandler`）；池化时由 Game System/池化 Owner 间接触发 | 接口和调用已落地；新移动实现须恢复朝向、启用状态及自身运动缓存 |
| `IWallCheck.TryMove` | 提供方：Enemy 接口；调用方：Boss Brain/状态；实现方：Map Owner | 当前 `NoWallCheck` 是占位实现，只允许位移；待 Map 接入正式查询 |
| 玩家 0.5 秒无敌由谁负责 | 未定 | 可能引起重复扣血 |

### 2.2 等待其他 Owner 交付

| 项 | 依赖 | 现状 |
|---|---|---|
| 撞墙查询 `IWallCheck` | **Map Owner** | 运行期 `NoWallCheck` 永远报「没墙」→ 表 N04 的 `Wall → Stop` **无法触发**；启动时报警一次 |
| 接触过滤（Layer Matrix） | **主程 + Map** | `PhysicsContactSource` 用「transform 相等」兜底，每次使用警告一次 |
| Collider 类型 / 移动平面 | **主程** | 当前只驱动 Transform，未使用 CharacterController / Rigidbody |
| Boss 是否走对象池 | Game System Owner | 未定；Boss 数量少，Owner 可选择直接创建/销毁 |
| 重复生命周期的迟到回调防护 | Game System Owner | Architecture 第 108 行要求协商；当前用 `ResetForReuse` + 死亡闩锁 |

### 2.3 需要策划拍板（当前取保守读法，未定优先级）

| 项 | 当前实现 | 说明 |
|---|---|---|
| 持续接触节奏 / 重接触是否重置 | 连续接触时攻击节拍持续运行，不按 5 秒强制退出；脱离后停止计时，重接触是否重置待确认 | 当前持续接触不会重入状态并获得额外首击 |
| 首次冲刺时机 | 冷却好了就冲，**不含距离门槛** | 见 2.4；距离门槛曾导致冲刺不可达，已移除 |
| 绕障 | `IWanderPointSource` **不做可达性校验** | 待 Map 接口 + 策划规则 |
| **Approach↔Wander 门槛重合** | 进出门槛同为既定阈值 `PreferredDistance = 8`，**带宽 0.9 已撤回** | 表 N04 只给一个阈值，第二个阈值需策划决定。**已知风险**：玩家停在 8 附近会造成判定反复翻转（逻辑测试实测 10 次采样翻转 9 次）。不发明默认值，登记待确认 |
| 死亡与结算先后、同时死亡优先级 | 未处理 | Architecture 第 108 行 |
| 敌人互相阻挡 | 未实现 | 未知是否需要 |

### 2.4 已修复（Review 反馈，回归测试覆盖）

| 问题 | 根因 | 修法 | 测试 |
|---|---|---|---|
| **P1 Boss 无法自然进入冲刺** | ① 冲刺只从 `Wander` 起手，而 `Wander` 里 `ShouldApproach` 优先于冲刺判断，玩家一超出 `PreferredDistance` 就转回 `Approach` 走路（6 < 玩家 10），冲刺分支不可达；② `CanStartDash` 自身又要求 `ShouldApproach`，进一步锁死；③ 转移表缺 `Approach → DashPrepare` 边 | 在 GDD 意图下把起手判断提到 `BossController.Tick`（状态机之前），Approach 与 Wander 都能起手；`CanStartDash` 判据改为「活着 + 有玩家 + 冷却就绪」；补齐两条转移边 | `TestBossStartsDashFromBothStates`、`TestBossDashIsNotDeadBranch` |
| **P1 死亡中断未清除过期转移** | 终态没有锁定：已排队的转移在死亡后仍会执行，形成 `Dead → ContactAttack → Dead` 往返，一次死亡发两次通知 | 状态机增加终态锁定：进入 `IsTerminal` 状态时清空待处理转移与打断队列，之后拒绝一切转移请求；离开终态只能走 `ResetToInitial` | `TestTerminalStateIsStable` |
| **P1 三个 MonoBehaviour 与文件名不匹配** | Unity 要求脚本文件名与类名一致，`SimpleMovementHandler` / `PhysicsContactSource` / `LogOnlyCombatBridge` 挤在别的文件里，保存并重导入 Prefab 后变成 Missing Script | 拆为同名脚本（`SimpleMovementHandler.cs`、`PhysicsContactSource.cs`、`LogOnlyCombatBridge.cs`），接口留原文件，清理孤儿 meta | 需在 Unity 中重导入 Prefab 复核 |
| **P2 攻击计时被推进两次** | 控制器与 `ContactAttack` 状态都 `Tick` 同一个计时器，配置 1 秒实际 0.5 秒 | 计时入口唯一化：控制器不再推进，由接触状态在「仍然接触」时推进 | `TestAttackIntervalIsNotDoubled` |
| **P2 Dash 把快照当终点** | 用「到快照的剩余距离」当行程上界：快照近就提前停（实测 5），远就超过配置（实测 14.4） | 行程改为从冲刺**起点**累计并以此限制 `DashDistance`；快照只用于决定方向 | `TestDashTravelsConfiguredDistance`（实测 12 / 12 / 6） |
| **P2 撞墙每步查询完整距离** | 每步都用完整 `DashDistance` 查询，墙在前方 6 单位时一步未走就结束（实测移动 0） | `IWallCheck` 改为按**单步位移**查询并返回被允许的位置；命中判定也改用本帧实际位移段 | `TestDashStopsAtWallUsingActualStep`（实测停在 6，查询 3 次） |
| **P2 复用只复位具体实现** | `ResetForReuse` 只处理 `SimpleMovementHandler`，换实现后死亡再复用仍不能动 | `ResetMovement` 提升到 `IMovementHandler` 接口，控制器只持有接口并按接口复位 | `TestResetForReuseUsesGenericMovementInterface` |
| **P2 自测断言失败且退出码为 0** | ① 滞回测试的帧数与断言语义不符；② 批处理下失败只 `LogError`，不改退出码 | pending 普通请求改为受滞回门槛约束；失败时在批处理下 `EditorApplication.Exit(1)` | `Assets/KWC/Editor/EnemyFsmChecks.cs` 与 `Tools/EnemyLogicTests/` |
| **P2 旧 HP 同步可杀死复用后的新生命** | HP 回调没有 `LifeId`，Brain 又用零 HP 推断 Dead | HP 同步带 `LifeId` 并由 Controller 校验；HP 只更新事实，死亡只由当前生命的显式事件触发 | `TestControllerHealthLifecycleIsGenerationScoped`（Enemy1 + Boss） |
| **P2 持续接触超时重入造成过密攻击** | `ContactAttack` 5 秒后退出，重入时重置节流并首击 | 移除强制超时退出；持续接触留在当前状态按原节拍攻击 | 30/60 fps 各 12 秒控制器回归 |
| **P2 Boss 冲刺期间反复申请起手** | 冷却在 Recovery 才开始，DashPrepare/Dash 中仍由 Controller 申请 DashPrepare | 只在 Approach / Wander 申请起手；这两态仍保留冲刺优先级 | 24 秒自然冲刺控制器回归并检查 FSM Error |
| **P2 状态机滞回自检与执行顺序不一致** | pending 普通请求在滞回门槛之前执行，导致合法转移不等待最短停留 | 普通请求待滞回门槛通过后执行；事件打断仍优先 | 真实 `StateMachineCore` 自检与纯逻辑回归 |
| **P2 最小接线漏初始化接触事实源** | `PhysicsContactSource` 未设置玩家 Transform 时拒绝物理回调 | 示例在注入 Controller 前调用 `Initialize(playerContext.EntityTransform)`，池化 Owner 管 `ResetContact()` | 需在真实 Unity 场景验证物理回调；本轮纯逻辑测试不覆盖真实 Physics |

### 2.5 dev 占位值（**不是设计值**，全部标 `dev` 前缀，确认后必须替换）

| 字段 | 位置 | 默认 | 收谁确认 |
|---|---|---|---|
| `contactRange` | `Enemy1Controller` | 1.2 | 碰撞尺寸确认后 |
| `decisionInterval` | 两个 Controller | 0.1 | 主程（性能） |
| `contactMaxDwell` | `Enemy1Controller` | 5 | 保险丝，可保留 |
| `stopWhileAttacking` | `Enemy1Controller` | true | 策划 |
| `wanderMaxDwell` | `BossController` | 10 | 由 Wander Interval 推导 |
| `dashHitRadius` | `BossController` | 1.5 | 碰撞尺寸确认后 |

> `WanderEnterRatio = 0.9` 作为**未经设计确认的新数值已撤回**（审核意见），
> 进出门槛现在同为既定的 `PreferredDistance = 8`，门槛重合的抖动风险登记在 2.3。

### 2.6 未实现 / 未接线（不是 bug，是不在范围内）

- Animator 接线：本模块无动画资源，未接。
- 音效、粒子、命中特效：P1，未批准。
- Boss 分阶段（血量阶段切换）：GDD 未要求。
- AI Debug 目前是 IMGUI 开发面板，**不是**正式 HUD（正式 HUD 归 UI Owner）。

---

## 三、开发期工具

- `EnemyAiDebugOverlay`：运行时显示当前状态、停留时长、转移次数、活跃状态数、冷却，以及「撞墙检测仍未接线」这类占位提示。挂在 Prefab 上勾选 `show`；`showTrace` 展开转移日志。
- `KWC.Editor.EnemyFsmChecks`：菜单 `KWC/Enemy/运行状态机逻辑自测`，或
  `-executeMethod KWC.Editor.EnemyFsmChecks.RunSelfTest`。日志出现 `KWC_ENEMY_FSM_SELFTEST_PASS` 即通过；
  **失败时批处理退出码为 1**（不再只打日志返回 0）。
- **纯逻辑回归测试（脱离 Unity）**：可复现工具位于仓库 `Tools/EnemyLogicTests/`，跑法
  `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/EnemyLogicTests/run.ps1`。
  它链接 `Assets` 下的**真实源文件**（不是副本），用最小 UnityEngine 替身编译运行，
  覆盖状态机护栏、终态锁定、死亡闸门、攻击间隔、冲刺起手/行程/撞墙、复用复位，
  以及**控制器级回归**（真实 `Enemy1Controller` / `BossController` 驱动的生命周期、12 秒接触节拍与冲刺流程），当前 96 项，另直接执行 `EnemyFsmChecks.RunSelfTest` 的 52 项断言。
  通过打 `KWC_ENEMY_LOGIC_PASS`，失败返回非零退出码。
  > 测试工程和完整复现脚本均已入库。它是源文件级组合验证，不模拟真实 Unity 物理碰撞，也不代表真实刷怪、Combat 或 Map 链路已接通。
- **重复类型声明检查（面向整个 Assets）**：同目录的 `check-duplicate-types.ps1`。
  扫描所有 `.cs`，报告「同一命名空间内重复声明同名类型」并返回非零退出码。
  > 存在原因：纯逻辑测试只链接部分源文件，结构上抓不到「未被链接的文件里多了一份接口定义」。
  > 这个问题真实发生过一次（`IContactSource` 重复定义导致 CS0101，而当时所有测试都是绿的）。
  > 现在 `PhysicsContactSource.cs` 已纳入测试编译范围，同类问题也会被测试本身抓到。
- Prefab 交付按 Architecture 第 8 节：Enemy1 尺寸 1×1×1、Boss 2×2×2；**Collider 类型、移动平面与 Layer Matrix 未确认前不要设置**。

## 四、最小接线示例

Prefab 上需要的组件与调用顺序（Game System 负责执行；这里给最小可跑版本）：

```csharp
// ---- Enemy1.prefab 上的组件 ----
//   Enemy1Controller      挂载点（必须与脚本同名，已经是独立文件）
//   SimpleMovementHandler 移动执行
//   PhysicsContactSource  接触事实（需与玩家碰撞体接触才会触发）
//   LogOnlyCombatBridge   开发期占位，真实实现由 Combat Owner 提供

var controller = enemyGo.GetComponent<Enemy1Controller>();
var wallCheck  = (IWallCheck)new NoWallCheck();          // 待 Map 提供通行性查询
var bridge     = enemyGo.GetComponent<LogOnlyCombatBridge>();
var contact    = enemyGo.GetComponent<PhysicsContactSource>();
var health     = new FactHealthSource(waveHp);           // HP 事实视图，唯一写入方是 Combat

// 先初始化接触事实源，再注入 Controller；Game System 负责传入玩家实体 Transform。
contact.Initialize(playerContext.EntityTransform);

controller.Initialize(
    playerContext,                                        // KWC.Core.IPlayerContext
    enemyGo.GetComponent<SimpleMovementHandler>(),
    contact,
    bridge,
    enemy1Config,                                         // Data/Enemy1.asset
    health,
    waveHp,                                               // 表 N12 本波 E1 HP
    waveAttack);                                          // 表 N12 本波 E1 Attack

// ---- BossKnightWheelChair.prefab ----
var boss = bossGo.GetComponent<BossController>();
boss.Initialize(
    playerContext,
    bossGo.GetComponent<SimpleMovementHandler>(),
    bossGo.GetComponent<LogOnlyCombatBridge>(),
    new NoWallCheck(),                                    // 待 Map 交付
    new RandomWanderPointSource(seed),
    knightWheelChairConfig,                               // Data/KnightWheelChair.asset
    new FactHealthSource(knightWheelChairConfig.BossHp),
    knightWheelChairConfig.BossHp,
    seed);

// ---- 复用（对象池 / Game System 组合责任）----
contact.ResetContact();       // Game System 在复用边界清除上一轮物理接触事实
controller.ResetForReuse();   // 必须；它恢复移动、重新武装死亡闸门、递增 lifeId

// ---- 由 Combat 驱动死亡（唯一入口）----
controller.ReportEnemyDied(controller.LifeId);   // lifeId 必须原样回传，否则会被拒绝
controller.OnHealthReported(controller.LifeId, currentHp, maxHp); // 同步也必须传入采样时捕获的 lifeId
```

注意：
- `Initialize` 之前不要依赖 `Start`/`OnEnable`；`PrefabPool.Rent` 会先调初始化委托再激活。
- 对象池/组合层负责在新一轮初始化前调用 `PhysicsContactSource.Initialize(playerTransform)`，并在复用边界调用 `ResetContact()`；Controller 不拥有该物理事实。
- 两个 `Controller` 都必须满足「脚本文件名 = 类名」，否则 Prefab 保存重导入后丢脚本。
- `PhysicsContactSource` 目前用「transform 相等」兜底判定接触，会每次使用警告一次；
  Layer Matrix 确认后调用 `SetContactFilter` 接上正式过滤。

