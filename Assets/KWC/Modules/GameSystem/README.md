# Game System · 王若兰

本目录维护 GameManager、Spawn/波次、对象池、配置类型、存档和流程接线。

- `GameManager`：场景级状态及事件；没有单例、波次循环或胜负仲裁实现。
- `DevelopmentLauncher`：临时工程启动面板，正式 UI 接入时移除。
- `PrefabPool`：每 Prefab 一个组件，Rent 初始化后启用；ReturnAll 只清理，不是击杀；对外提供 `Prefab`、`BorrowedCount`（借出计数）、`Rent`/`Return`/`ReturnAll`。
- `Configuration`：SO 类型；实例位于 `Assets/KWC/Data`，GameConfig 组合引用。
- `SpawnWarning`：池化的一次性预警对象（红圈）。初始化口接收"时长 + 到点回调"，可中途取消，复用时归零。
- `Spawn`：`SpawnScheduler` 只负责波次计时、预警、生成计数与波末判定；敌人池、死亡队列、掉落与回收已拆到 `EnemyLifeCycle`。`TestDriver` 是测试按键驱动。
- `CombatWiring`：`PlayerInitializer`（玩家初始化与承伤入口）负责 Health/PlayerStats 以及正式承伤/拾取组件的初始化；`EnemyCombatBridge`（敌人战斗桥）实现 Enemy 的 `ICombatBridge`（战斗桥接接口）。统一使用 Combat 的 `PlayerDamageReceiver`（玩家受伤入口）与 `PlayerPickupCollector`（玩家拾取入口）。
- `EnemyWiring`：`EnemyLifeCycle` 负责敌人生成、死亡、掉落与回收；`TestPlayerContext` 是假玩家；`TestBossInitializer` 是 Boss 初始化占位。
- `SessionFlow`：场景内本局流程，按“玩家 → 敌人 → 波次”的顺序组织初始化，按 `GameState`（游戏状态）切换承伤/拾取许可，并处理玩家死亡与波末回血。

## SpawnWarning 生命周期（初始化 / 完成 / 取消 / 归还）

- 借用：调用方 `Rent(位置, 朝向, 初始化委托)`；池在激活前执行委托 → `InitializeWarning(时长,到点回调)`：全部归零并记账。
- 完成：倒数到时 → 先快照回调、结束本轮状态，最后才调用快照的回调（回调里可安全复用本对象）。
- 取消：`CancelWarning()` → 结束本轮状态、不触发回调。
- 归还：一律由借出方在完成回调或取消后 `Return`；组件自身不认识池。
- 约定：任何时候被合法复用都不产生残留行为；延迟/过期回调不得影响新一轮（由调度器的运行编号守卫兜底）。

## SpawnScheduler 波次循环

- `Initialize(GameConfig)`：场景启用时由流程层调用一次，缓存配置并校验预警池；失败时不要开波。
- `InitializeWave()`：先确认 `enemyLifeCycle` 已就绪，再按 `waveIndex` 找行；找不到行、Boss 波占位、数量未确认都会拒绝。全部通过才提交状态并发 `WaveChanged`。
- 逐帧推进波次计时与出怪间隔；到量停发；尾批不足暂发剩余数量。
- 生成：预警到点 → `enemyLifeCycle.TryGenerateEnemy1(out enemy, 位置, 朝向, hp, attack)`；成功才计入 `generatedEnemy`。预警的归还与出清在 finally 中执行，租借或初始化失败时不会滞留。
- 死亡与回收不在本组件：死亡上报进入 `EnemyLifeCycle` 的队列，由它在 LateUpdate 统一执行“先掉落、后回收”。
- 提前结束（普通波）：`generatedEnemy == spawnCount` 且 `enemyLifeCycle.GetLivedEnemyCount() == 0` 时立即 EndWave，不等 30 秒。
- `EndWave()`（正常波末）：取消在途预警 → `enemyLifeCycle.ClearEnemySpawnState()` 回收场上敌人与拾取物 → 清账 → 推进 `waveIndex` → 发 WaveEnded。**先提交完整状态、再通知**：回调内可直接调用 InitializeWave 开下一波。
- `StopWave()`（中止一局，公开 + 幂等）：同样清理，但**不推进波次**也不发 WaveEnded；`OnDisable` 会调用它。调度器不会自行开始下一波。
- Boss 占位（`EnterBossWave`）：不改动任何运行状态，只拒绝并记日志，快照保持“未开波”；将来实现时与 isInWave / waveTimer=Infinity 一起提交。
- 终止统一走 `ClearSpawnState()`，并在此递增运行编号（`runIndex`）：预警完成回调出生时记录编号，编号过期一律作废。

## 敌人生命周期（EnemyLifeCycle）

- `TryInitialize(GameConfig, IPlayerContext)`：流程层在玩家就绪后调用；校验 Enemy1/CombatRules、三个对象池（敌人/经验球/血包）、玩家就绪状态（含 `PlayerDamageReceiver` 已初始化），以及敌人 Prefab 上的 Enemy1Controller、SimpleMovementHandler、PhysicsContactSource、EnemyCombatBridge、Health、EnemyDrops；同时校验两个掉落 Prefab 的组件。失败保持未就绪。
- 生成顺序：`TryGenerateEnemy1` → `enemyPool.Rent`（停用状态下执行初始化）→ contact.Initialize → controller.Initialize（推进 LifeId、建立状态机）→ Health.Initialize（用新 LifeId）→ EnemyDrops.Initialize（依赖 Health 已初始化，并传入玩家当前的 `PlayerPickupCollector`）→ bridge.Initialize（最后订阅）→ 激活。
- 死亡入口：`EnqueueDeath(enemy, lifeId)` 只入队；`LateUpdate` 逐条执行 `EnemyDrops.TryDrop(lifeId)`，再 `RecycleEnemy`。掉落失败也会在 finally 中回收。
- `RecycleEnemy`：先校验 lifeId 与控制器当前 LifeId，再移出存活列表并 `EnemyReset`（ResetContact → ResetForReuse → Return）；编号过期一律拒绝。
- `ClearEnemySpawnState()`：波末/中止清理；复位并回收所有在场敌人，归还经验球与血包，清空死亡队列；不触发掉落、经验或击杀。成功波末也走这条路径，未拾取掉落直接 `ReturnAll`，不结算经验（见「已知限制」）。
- `GetLivedEnemyCount()`：存活列表数量，供波次提前结束判定。

## 玩家初始化与承伤（PlayerInitializer / EnemyCombatBridge）

- `PlayerInitializer.TryInitialize(GameConfig, IPlayerContext)`：流程层在 Playing 时调用；幂等，已就绪直接返回 true。顺序固定为 Health → PlayerStats → `PlayerDamageReceiver` → `PlayerPickupCollector`，lifeId 来自 Health 当前编号 + 1；任一初始化失败保持未就绪。
- `IsReady`：已初始化、Health/PlayerStats/`PlayerDamageReceiver` 有效、`PlayerPickupCollector` 已注入且玩家未死亡；`EnemyLifeCycle.TryInitialize` 以它为前置条件。
- `TryReceiveDamage(PlayerDamageRequest)`：同步送达，直接使用调用时的玩家 `Health.LifeId` 调 `PlayerDamageReceiver.TryApplyDamage`；无敌、死亡、未开放或旧生命由 Receiver 拒绝并返回 false。若以后改成排队/延迟请求，必须把请求发生时的 LifeId 随请求带入。
- `EnemyCombatBridge`：每个敌人实例一个，初始化时订阅 Health 的 HealthChanged/Died，OnDisable 解绑。HealthChanged 只同步 HP 事实；Died 经控制器进入 Dead。`TrySubmitPlayerDamage` 的返回值只表示请求已送达玩家承伤入口，**不表示实际扣血**：无敌/未开放时仍返回 true，攻击节流照常计一次，FSM 不再逐决策帧重试；`NotifyEnemyDied` 带上 lifeId 交给 `EnemyLifeCycle`。

## 本局流程（SessionFlow）

- 场景启用：校验 GameManager 配置、SpawnScheduler、TestPlayerContext 与 PlayerInitializer（含 `PlayerDamageReceiver` / `PlayerPickupCollector` 引用）；订阅 WaveEnded、玩家 Health.Died、GameStateChanged。`enemyLifeCycle` 在进入 Playing 时补查，缺失会报错并中止。
- 进入 Playing：`hasStarted` 保证只执行一次；顺序为 `PlayerInitializer.TryInitialize` → 同步开启承伤/拾取许可 → `EnemyLifeCycle.TryInitialize` → `SpawnScheduler.InitializeWave`。`Start` 还会补一次当前状态快照，防止 BeginSession 在 Awake 阶段被调用时漏掉事件。
- 阶段许可：每次 `GameStateChanged` 按 `gameState == Playing` 调 `SetDamageEnabled` / `SetPickupEnabled`；Upgrade、Paused、Victory、GameOver 关闭许可。`PlayerDamageReceiver` 会冻结/恢复无敌剩余时间，`PlayerPickupCollector` 只切许可。
- 波末：`WaveEnded` 后回满玩家 HP。
- 玩家死亡：`Died` 只置标记；`LateUpdate` 先 `StopWave`（取消预警、回收敌人与拾取物），再 `CompleteSession(GameOver)`。
- `OnDisable` 解绑事件并复位 `isInitialized/hasStarted`。

## 术语

| 名称 | 含义 |
|---|---|
| `preparedEnemy` | 累计**安排**发出的预警数量（只增），不是当前在途数量 |
| 预警中 | 当前在途预警数 = `livedSpawnWarnings.Count` |
| `generatedEnemy` | 到点已经实际生成的敌人数量 |
| `livedEnemy` | `EnemyLifeCycle` 的存活列表：已生成尚未回收的敌人；Count 用于全灭提前结束判定 |

## 对外接口

- `SpawnScheduler`：`Initialize(GameConfig)`、`InitializeWave()`、`StopWave()`（幂等）；只读 `GetWaveIndex()`、`IsInWave()`、`IsInBossWave()`（仅在 Boss 波**运行中**为 true；占位实现下恒为 false）、`GetWaveTime()`（未开波 -1、Boss 波 Infinity）；事件 `WaveChanged(int)`、`WaveEnded`（仅正常波末，发出时清理已完成且 `waveIndex` 已指向下一波）。
- `EnemyLifeCycle`：`TryInitialize(GameConfig, IPlayerContext)`、`TryGenerateEnemy1(...)`、`EnqueueDeath(enemy, lifeId)`、`ClearEnemySpawnState()`、`RecycleEnemy(enemy, lifeId)`（死亡回收；携带发起时捕获的 LifeId，编号过期即拒绝）、`GetLivedEnemyCount()`；`isInitialized` 供流程层检查就绪。
- `PlayerInitializer`：`TryInitialize(GameConfig, IPlayerContext)`、`TryReceiveDamage(PlayerDamageRequest)`、`IsReady`、`PlayerHealth`、`PlayerStats`、`PlayerDamageReceiver`、`PlayerPickupCollector`。
- Combat 提供的正式组件：`PlayerDamageReceiver`（承伤/无敌；`SetDamageEnabled`、`TryApplyDamage`、`CanReceiveDamage`、`InvulnerabilityRemaining`）、`PlayerPickupCollector`（靠近拾取；`SetPickupEnabled`、`CanCollect`、`TryCollect`，由物品自身每帧请求）。
- `EnemyCombatBridge`：Enemy 的 `ICombatBridge` 实现，挂在 Enemy1 Prefab 上；每次租出时由 `EnemyLifeCycle` 调用 `Initialize`。
- `PrefabPool`：`Prefab`、`BorrowedCount`、`Rent`、`Return`、`ReturnAll`。
- `SessionFlow`：没有对外方法；场景内的流程入口，订阅 GameStateChanged 后组织初始化、阶段许可与结束。

## 测试（测试件，正式接线后替换）

- 场景：`Assets/KWC/Prefabs/GameSystem/Scene/TestSpawnWarningScene.unity`；驱动：`TestDriver`（F1 开波 / F2 停止 / F3 同步复用复现 / F4 杀敌测试 / F5 迟到回收测试）。
- 接触攻击依赖物理接触：仓库中的 `TestEnemyCube.prefab` 和测试场景当前没有 Collider/Rigidbody。本地验证 `ContactAttack` 时需临时给敌人加  Rigidbody +  Collider、给 `TestPlayerContext` 所在 Transform 加 Collider；正式碰撞层待确认。
- 复现步骤：
    1. 正常：F1 → 每 5 秒一个红圈，0.5 秒后从敌人池借出测试敌人；5 个后停发；30 秒波末清场、`BorrowedCount` 归 0；再 F1 走 Wave 2。
    2. 取消：勾 `useTestWaveTime`、值 0.3 → F1 → 波末在途预警被取消、无迟到回调。
    3. 停止：开波后 F2（或停用组件）→ 在途取消、`waveIndex` 不推进；再 F1 仍是同一波；连按 F2 无副作用。
    4. 同步复用：干净场景按 F3 → 依次出现 Round1 Done、Same:True、Round2 Done，结束后 `BorrowedCount` 为 0。
    5. 帧率回归：`testFrameRate=15` → Wave 3 应满 18、Wave 7 应满 44（出现 "Enemy Totally generated!"）。
    6. 死亡测试：按F4杀一个敌人，勾选wantKillAllEnemies 时循环全杀目前存活敌人，全部生成后全杀则触发提前结束波次。
    7. 迟到回收（F5）：F1 后等第一只敌人出现 → F5 对在册敌人用过期编号（当前编号 - 1）发回收 → 被拒绝、无副作用；日志出现 `…accepted=False（应为 false）；Success` 及警告"回收时出现life id错误"。

### 当前接线验收点（Review 反馈）

- 回收辨识：同一 lifeId 重复通知、对象再次借出后的迟到通知，均由 `EnemyLifeCycle.RecycleEnemy` 拒绝；新命的存活数、池 BorrowedCount 与活动状态不变（F5 可复现，见步骤 7）。
- 事件时序：在 WaveEnded 回调中调用 InitializeWave 开下一波，GetWaveIndex()、实际配置与 WaveChanged 参数三者一致。
- 占位一致：选中 Boss 波（Wave 10）不进入运行态；快照为 inWave=false、isBoss=false、time=-1，Stop 无副作用。
- 开波校验：`EnemyLifeCycle.TryInitialize` 拒绝缺少玩家、配置、池或 Prefab 组件的接线；`SpawnScheduler.InitializeWave` 拒绝未就绪的生命周期。两者都不产生 NRE，失败时 BorrowedCount 保持 0。
- 初始化顺序：`SessionFlow.hasStarted` 保证只走一次“玩家 → 敌人 → 波次”；`PrefabPool.Rent` 在激活前完成初始化，敌人不会用旧生命数据 tick。
- 无敌时序：首笔有效伤害先占用 `PlayerDamageReceiver` 的无敌，再由 `Health` 同步通知 `HealthChanged`；回调内再提交的伤害被拒，100 HP 受到 10 伤害后保持 90。
- 桥接送达：`EnemyCombatBridge.TrySubmitPlayerDamage` 在有效请求交给玩家承伤入口后返回 true；无敌期间攻击节流照常计一次，不逐决策帧重试。
- 拾取接线：`PlayerPickupCollector` 由物品自身每帧请求，消费时读取当前 `SpawnId`；不再使用跨帧快照，重借后的新物品不会被旧引用误消费。
- 阶段许可：仅 Playing 开放承伤/拾取；Upgrade、Paused、Victory、GameOver 关闭，无敌剩余时间冻结。

## 当前测试策略（正式规则待确认）

- 首批时机：波开始首帧立即发出（无配置字段，行为待确认）。
- 随机出生范围：围绕原点的 ±10 方框（等 Map 接口与最小出生距离）。
- 尾批不足：暂定发剩余数量（待策划确认）。

## 已知限制（本次接线）

- 成功波末是否应清掉未拾取掉落尚未确认：`EndWave`（正常波末）与 `StopWave`（中止）共用清场路径，未拾取物品直接 `ReturnAll` 且不结算经验；最后一批敌人的掉落可能来不及拾取，同一波经验总量会波动。
- 仓库中的 `TestEnemyCube.prefab` 与测试场景没有 Collider/Rigidbody，接触攻击只能在临时加物理的环境验证；正式碰撞层与 Layer Matrix 待确认。
- 未拾取掉落的最终归属（保留 / 自动结算 / 丢弃）待设计确认。

## 未决项

最小出生距离、首批时间、尾批处理、找不到出生点、Wave 10 约 20 的确切上限；不要绕过 `TryGetActualSpawnCount` 的 false。暂停/升级的子系统许可、同时死亡优先级、最终升级、保存字段/时机、池容量/不足策略和性能指标也尚未决定。玩家 0.5 秒无敌已由 Combat 的 `PlayerDamageReceiver` 管理；正式 Player/UI 接入后替换 TestPlayerContext 与 TestDriver。
