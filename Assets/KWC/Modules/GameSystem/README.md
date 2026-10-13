# Game System · 王若兰

本目录维护 GameManager、Spawn/波次、对象池、配置类型、存档和流程接线。

- `GameManager`：场景级状态及事件；没有单例、波次循环或胜负仲裁实现。
- `DevelopmentLauncher`：临时工程启动面板，正式 UI 接入时移除。
- `PrefabPool`：每 Prefab 一个组件，Rent 初始化后启用；ReturnAll 只清理，不是击杀。
- `Configuration`：SO 类型；实例位于 `Assets/KWC/Data`，GameConfig 组合引用。
- `SpawnWarning`：池化的一次性预警对象（红圈）。初始化口接收"时长 + 到点回调"，可中途取消，复用时归零。
- `SpawnScheduler`：波次刷怪调度：读 Waves配置、发预警、经敌人池生成、计数、死亡/波末回收、普通波全灭提前结束；对外提供只读接口与 WaveChanged/WaveEnded。未接 Map 选点；死亡结果暂由测试驱动模拟。 
- `EnemyWiring`：测试接线件。TestPlayerContext假玩家（实现IPlayerContext）；TestBossInitializer Boss 初始化占位，待 Combat/Map 就位后并入正式接线。

## SpawnWarning 生命周期（初始化 / 完成 / 取消 / 归还）

- 借用：调用方 `Rent(位置, 朝向, 初始化委托)`；池在激活前执行委托 → `InitializeWarning(时长,到点回调)`：全部归零并记账。
- 完成：倒数到时 → 先快照回调、结束本轮状态，最后才调用快照的回调（回调里可安全复用本对象）。
- 取消：`CancelWarning()` → 结束本轮状态、不触发回调。
- 归还：一律由借出方在完成回调或取消后 `Return`；组件自身不认识池。
- 约定：任何时候被合法复用都不产生残留行为；延迟/过期回调不得影响新一轮（由调度器的运行编号守卫兜底）。

## SpawnScheduler 波次循环

- `InitializeWave()`：按 `waveIndex` 读取该波参数；守卫：引用未挂（配置/预警池/敌人池）、找不到行、Boss波占位、数量未确认；开波成功发 WaveChanged）。
- 逐帧推进波次计时与出怪间隔；到量停发；尾批不足暂发剩余数量。
- 生成：预警到点 → enemyPool.Rent(位置, 朝向, InitializeEnemy1)；初始化在激活前执行——contact.Initialize → controller.Initialize（waveHp/waveAttack 取自当前波配置）。
- 回收：统一 EnemyReset（ResetContact → ResetForReuse → Return）。触发点两个：波末/停止清场（非击杀），与死亡入口 RecycleEnemy(enemy)（存活表移除作同命重复通知闸门；正式接线后由流程层在收到 Combat 死亡结果时调用，当前仅测试驱动调用）。
- 提前结束（普通波）：generatedEnemy == spawnCount 且 livedEnemy.Count == 0 时立即 EndWave()，不等 30 秒。
- `EndWave()`（正常波末）：取消在途预警 → 回收场上敌人（非击杀）→ 清账 → 发 WaveEnded → waveIndex 推进"；StopWave 补一句"不发 WaveEnded
- `StopWave()`（中止一局，公开 + 幂等）：同样清理，但**不推进波次**也不发WaveEnded；`OnDisable` 会调用它。调度器不会自行开始下一波。
- 终止统一走 `ClearSpawnState()`，并在此递增运行编号（`runIndex`）：完成回调出生时记录编号，编号过期一律作废。

## 术语

| 名称 | 含义                                                 |                                                                                                       
  |---|------------------------------------------------------|                                                                                                             
| `preparedEnemy` | 累计**安排**发出的预警数量（只增），不是当前在途数量 |                                            
| 预警中 | 当前在途预警数 = `livedSpawnWarnings.Count`          |                                                              
| `generatedEnemy` | 到点已经实际生成的敌人数量                           |                                                                         
| `livedEnemy` |  已生成尚未回收的敌人列表，Count 用于全灭提前结束判定。|

## 对外接口

- 只读：GetWaveIndex()、IsInWave()、IsInBossWave()、GetWaveTime()（未开波 -1、Boss 波 Infinity）、GetLivedEnemyCount()（测试用）。
- 事件：WaveChanged(int) 开普通波成功时发；WaveEnded 仅正常波末发（超时或全灭提前结束），发出时清理已完成、waveIndex 未推进。

## 测试（测试件，正式接线后替换）

- 场景：`Assets/KWC/Prefabs/GameSystem/Scene/TestSpawnWarningScene.unity`；驱动：`TestSpawnSchedulerDriver`（F1 开波 / F2停止 / F3 同步复用复现/F4 杀敌测试）。
- 复现步骤：
    1. 正常：F1 → 每 5 秒一个红圈，0.5 秒后从敌人池借出测试敌人；5 个后停发；30 秒波末清场、`BorrowedCount` 归 0；再 F1 走 Wave 2。
    2. 取消：勾 `useTestWaveTime`、值 0.3 → F1 → 波末在途预警被取消、无迟到回调。
    3. 停止：开波后 F2（或停用组件）→ 在途取消、`waveIndex` 不推进；再 F1 仍是同一波；连按 F2 无副作用。
    4. 同步复用：干净场景按 F3 → 依次出现 Round1 Done、Same:True、Round2 Done，结束后 `BorrowedCount` 为 0。
    5. 帧率回归：`testFrameRate=15` → Wave 3 应满 18、Wave 7 应满 44（出现 "Enemy Totally generated!"）。
    6. 死亡测试：按F4杀一个敌人，勾选wantKillAllEnemies 时循环全杀目前存活敌人，全部生成后全杀则触发提前结束波次。 

## 当前测试策略（正式规则待确认）

- 首批时机：波开始首帧立即发出（无配置字段，行为待确认）。
- 随机出生范围：围绕原点的 ±10 方框（等 Map 接口与最小出生距离）。
- 尾批不足：暂定发剩余数量（待策划确认）。

## 未决项

最小出生距离、首批时间、尾批处理、找不到出生点、Wave 10 约 20 的确切上限；不要绕过 `TryGetActualSpawnCount` 的 false。暂停/升级的子系统许可、同时死亡优先级、最终升级、保存字段/时机、池容量/不足策略和性能指标也尚未决定。
