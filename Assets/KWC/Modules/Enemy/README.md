# Enemy / AI · 王勤伟

本目录放 Enemy1、Boss FSM/移动/命中机会/动画接线/AI Debug；Prefab 交付到 `Assets/KWC/Prefabs/Enemy`。

读取 `IPlayerContext`、Map 碰撞事实和配置，向 Combat 提交伤害。Combat Health 拥有 HP；Enemy 处理 Dead 行为，Game System 负责生成和回收。Enemy1 首次接触立即攻击。Boss 仅 Dash 造成伤害，单次最多一次，蓄力结束才锁位置快照，撞墙进入 Recovery。

复用时清掉目标、Dead、冷却、Dash快照/命中记录。非击杀的波末回收不能请求掉落。首次冲刺时机、持续接触节奏、绕障、Wander选点等仍依 GDD 的待确认项处理。不要添加敌人种类或招式。

> 本 README 只维护**对外接口**与**待确认项**。内部实现（三层分离、约定落实、目录组织）见 Architecture 与代码注释，不在这里重复。

---

## 一、对外接口

命名空间 `KWC.Enemy`，不使用 `.asmdef`。所有依赖由 Game System 在场景接线时**显式调用 `Initialize` 注入**（Architecture 第 3 节）；不做全局查找、不用 Inspector 序列化接口、不设静态 Instance。

### 1.1 我方需要别人提供的

| 需求 | 由谁提供 | 类型 | 现状 |
|---|---|---|---|
| 玩家位置 | Player 实现 `KWC.Core.IPlayerContext` | Core 已有 | 可用 |
| 移动执行 | Game System 接线注入 `IMovementHandler` | 已提供 `SimpleMovementHandler` | 可用，仅驱动 Transform |
| 接触事实 | 物理 + Layer Matrix | 接口 `IContactSource` | **占位实现**：`PhysicsContactSource` 用「transform 相等」判定 |
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
| `OnHealthReported(current, max)` | Combat Health 经桥接 | 死亡走事件打断通道，同帧两次也不丢 |
| `CurrentStateId` / `TimeInState` / `TransitionCount` / `LiveStateCount` / `Machine` | UI、Debug、测试 | 全部只读，无 setter |
| `PlayerDamageRequest`（定义在 `Bridge/CombatBridge.cs`） | Combat 实现 `ICombatBridge` | 伤害请求与死亡通知，见 1.3 |

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

Enemy 只发请求，不写 HP、不读 Combat 内部。全部集中在 `Bridge/CombatBridge.cs`：

```csharp
bool TrySubmitPlayerDamage(PlayerDamageRequest request);
void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss);
```

- `PlayerDamageRequest` 携带：`Attacker` / `Damage` / `HitPoint` / `Kind`（`Contact` 或 `Dash`）。
- 返回 `true` 只表示**请求已交出**，不代表玩家掉血；结果归 Combat。
- 当前实现是 `LogOnlyCombatBridge` 占位：只记日志、**故意不伪造伤害**。契约谈定后**只需替换这一个文件**，State 与 Brain 不用改。

### 1.4 生命周期约定（Game System ）

- `Initialize(...)` 结束后对象**立刻可用**，不需要再等 `Start`。
- `PrefabPool.Rent` 会先调用初始化委托再激活，因此 `OnEnable` 能读到本轮数据。
- 池化复用**必须调用 `ResetForReuse()`**：它清 HP、死亡闩锁、攻击间隔、Dash 冷却、快照、命中记录、决策计时。
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
| Dead 通知签名 | 同上 | 掉落与回收接不上 |
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
| 持续接触节奏 / 重接触是否重置 | 离开接触只**停止计时、不清零** | 既不白送一次重置，也不让计时器暗跑。改 `OnExit`／`OnEnter` 一行 |
| 首次冲刺时机 | 要求 `距离 > PreferredDistance` | 可辩护读法，非定论 |
| 绕障 | `IWanderPointSource` **不做可达性校验** | 待 Map 接口 + 策划规则 |
| Approach↔Wander 滞回带宽 | 用 `WanderEnterRatio = 0.9f` 比例推导 | 因表 N04 只给一个阈值；正式规则出来后换成设计值 |
| 死亡与结算先后、同时死亡优先级 | 未处理 | Architecture 第 108 行 |
| 敌人互相阻挡 | 未实现 | 未知是否需要 |

### 2.4 dev 占位值（**不是设计值**，全部标 `dev` 前缀，确认后必须替换）

| 字段 | 位置 | 默认 | 收谁确认 |
|---|---|---|---|
| `contactRange` | `Enemy1Controller` | 1.2 | 碰撞尺寸确认后 |
| `decisionInterval` | 两个 Controller | 0.1 | 主程（性能） |
| `contactMaxDwell` | `Enemy1Controller` | 5 | 保险丝，可保留 |
| `stopWhileAttacking` | `Enemy1Controller` | true | 策划 |
| `wanderMaxDwell` | `BossController` | 10 | 由 Wander Interval 推导 |
| `dashHitRadius` | `BossController` | 1.5 | 碰撞尺寸确认后 |
| `WanderEnterRatio` | `BossConfigValues` | 0.9 | 策划 |

### 2.5 未实现 / 未接线（不是 bug，是不在范围内）

- Animator 接线：本模块无动画资源，未接。
- 音效、粒子、命中特效：P1，未批准。
- Boss 分阶段（血量阶段切换）：GDD 未要求。
- AI Debug 目前是 IMGUI 开发面板，**不是**正式 HUD（正式 HUD 归 UI Owner）。

---

## 三、开发期工具

- `EnemyAiDebugOverlay`：运行时显示当前状态、停留时长、转移次数、活跃状态数、冷却，以及「撞墙检测仍未接线」这类占位提示。挂在 Prefab 上勾选 `show`；`showTrace` 展开转移日志。
- `KWC.Editor.EnemyFsmChecks`：菜单 `KWC/Enemy/运行状态机逻辑自测`，或 `-executeMethod KWC.Editor.EnemyFsmChecks.RunSelfTest`。日志出现 `KWC_ENEMY_FSM_SELFTEST_PASS` 即通过。
- Prefab 交付按 Architecture 第 8 节：Enemy1 尺寸 1×1×1、Boss 2×2×2；**Collider 类型、移动平面与 Layer Matrix 未确认前不要设置**。
