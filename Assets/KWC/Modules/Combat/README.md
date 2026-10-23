# Combat · 葛亮亮

本目录放 Weapon/Projectile、Damage/Health、EXP、成长、掉落/拾取；Prefab 交付到 `Assets/KWC/Prefabs/Combat`。

实现 `KWC.Core.IPlayerStats`，唯一维护玩家/敌人/Boss HP及玩家属性/EXP/等级/成长点/属性等级。通过 IPlayerContext 读位置，通过 IObjectPool 复用对象。UI提交购买请求，Combat校验，不能把购买计算交给UI。

公式/数值以项目根目录《数值设计》为准；属性无20级上限。死亡只通知一次，波末清理不产生击杀收益。经验保留超额部分并允许连续升级。玩家受伤、靠近拾取与HP购买采用下方葛亮亮确认的规则；初始等级与成长点、子弹参数仍需确认。

## 当前实现

### HP属性购买（2026-10-23）

葛亮亮本轮确认采用“最大HP增加多少，当前HP也增加多少”，保留原先缺失的血量。数值仍使用PlayerBaseConfig.PlayerHp与AttributeGrowthConfig中的HP成长参数（当前基础100、B=15、G=5）：60/100购买后依次为75/115、95/135；满血购买仍满血，死亡不购买、不复活。每次消耗现有AttributeUpgradeCost，使用已有SetPurchasingEnabled许可。

- UI沿用`playerStats.TryUpgradeAttribute(AttributeType.Hp)`；GetAttributeRank返回实际HP阶数，初始化清零，无20阶上限。HP基础值与成长参数在PlayerStats初始化时复制，不改写SO。
- Health新增`TryIncreaseMaxHp(newMaxHp, expectedLifeId)`，参数是**新的总上限**。它校验当前存活生命与有效增量，同时写入当前/最大HP，再发送一次HealthChanged。失败不通知。
- HP购买先计算下一阶总值、写好阶数与成长点，再交给Health更新血量，保证HealthChanged监听者能读到完整结果；Health拒绝时恢复本次点数与阶数。成功后同一生命再通知一次StatsChanged。
- HP购买通知期间拒绝再次购买任何属性，防止UI回调重复扣点；try/finally保证处理结束后释放保护。监听者仍不应抛异常或重建玩家；若回调已换生命，则不再向新状态发送旧购买的StatsChanged。
- HP购买不改变LifeId、不重置无敌、不切换阶段。治疗与血包继续读取Health的最新上限，UI/Player仍使用原IPlayerStats，无Core或其他模块代码变化。

复现：用100最大HP、60当前HP、2成长点初始化玩家并打开购买，连续购买两次HP，检查75/115、95/135、阶数2和剩余点数0；第三次失败且状态不变。分别确认满血购买、死亡拒绝，以及HealthChanged内读取成长点/阶数的结果。新一条生命必须重新初始化Health和PlayerStats。

本轮通过Unity 2022.3.62f3隔离真实Play Mode的46项检查（`HP_UPGRADE_PLAY_PASS=46`），覆盖缺血/满血、连续购买与21阶、配置快照、失败不扣点、死亡/旧生命、事件顺序与同步重入/复用、通知异常后的保护释放、升级后血包与治疗、无敌保持、原三项购买及经验连续升级。17份被测源码和meta与交付一致，临时验证工程已清理；不代表团队2022.3.62f3c1整项目或正式UI/阶段接线验收。

### 靠近自动拾取（2026-10-23）

葛亮亮本轮确认：经验球和血包的拾取半径均为1个Unity单位，按玩家位置到物品位置的三维直线距离计算，包含高度差，距离等于半径也可拾取；暂停和Upgrade期间禁止自动拾取，恢复战斗后重新检测距离。此处只实现Combat内部能力，正式场景引用与阶段开关由GameSystem接入；GDD原有待确认文字由设计文档维护方后续同步。

`PlayerPickupCollector`持有同一玩家的IPlayerContext、PlayerStats与Health。两种物品的Update向它请求检测；许可开启、玩家存活且绑定生命有效、物品可消费、距离足够近时，调用原有TryCollect结算。两种Prefab的Pickup Radius均保存为1，可在各自Inspector调整；当前不新增GameSystem配置字段。位置来自IPlayerContext，不使用拾取组件自身的位置，也不决定Player/Map的移动平面。

| 入口 / 状态 | 用法 |
| --- | --- |
| `PlayerPickupCollector.Initialize(context, stats, health)` | Health与PlayerStats初始化后调用，三者必须属于同一玩家；记录当前LifeId，默认关闭拾取 |
| `SetPickupEnabled(bool)` | GameSystem在战斗时开启，暂停、Upgrade或结束本局时关闭 |
| `CanCollect` | 是否已绑定有效且存活的玩家、组件与玩家实体活动、许可开启；尚不表示某个物品在范围内 |
| `TryCollect(ExpOrb)` / `TryCollect(HealthPack)` | 完成距离与许可检查后立即结算；通常由物品Update调用 |
| 两种物品的`Initialize(..., pickupCollector = null)` | 在Rent回调中传入拾取组件；省略时维持原有手动结算用法，复用时不会保留旧绑定 |
| `EnemyDrops.Initialize(..., collector = null)` | 末尾可传同一玩家的拾取组件，EnemyDrops会在自己的Rent回调里继续传给两种物品 |

```csharp
// GameSystem提供这些引用；先完成同一玩家的Health和PlayerStats初始化。
pickupCollector.Initialize(playerContext, playerStats, playerHealth);

// 敌人每次初始化时传入同一个拾取组件，不需要修改对象池实现。
enemyDrops.Initialize(enemyHealth, isBoss, rulesConfig,
    expOrbPool, healthPackPool, pickupCollector);

pickupCollector.SetPickupEnabled(true);  // 进入战斗。
pickupCollector.SetPickupEnabled(false); // 暂停、Upgrade或结束本局。
pickupCollector.SetPickupEnabled(true);  // 恢复后按当前位置重新检测。
```

暂停使用许可开关，不禁用组件。禁用PlayerPickupCollector会结束本次绑定，再启用后须重新Initialize；Health进入新生命后也要重新绑定。物品归还时清除其玩家引用，下次Rent重新传入。Player/Enemy完整Prefab、GameSystem与公共场景均未修改，新组件的正式挂载与引用注入仍待对应Owner接线。

原有`ExpOrb.TryCollect(PlayerStats, spawnId)`和`HealthPack.TryCollect(Health, spawnId)`是底层结算入口，不检查阶段或距离。自动拾取统一通过PlayerPickupCollector，外部不要直接调用底层入口代替检测。满血仍消费血包，死亡不拾取；奖励通知与对象池复用仍沿用原有SpawnId和一次性消费保护。

复现：初始化后先不开许可，物品放在玩家位置也不消费；开启后将物品从1.01单位移到1单位，应结算并归还。高度差1.01单位时不能拾取，高度差1单位时可以。暂停期间将物品移近，经验与HP保持不变；恢复后按新的玩家位置检测。重复拾取、旧SpawnId、玩家死亡或换生命、波末直接ReturnAll均不得重复发奖。

本轮验证：Unity 2022.3.62f3隔离工程通过48项真实Play Mode检查（`PICKUP_PLAY_PASS=48`）及11项Prefab保存/重导入、组件、网格、材质和半径检查（`PICKUP_ASSET_PASS=11`）。覆盖边界/高度/斜向距离、许可关闭与恢复、同帧回调关闭许可、死亡/生命更换、禁用与重新绑定、旧调用兼容、同步重复拾取、对象池回调复用、EnemyDrops传递引用和清场无奖励。17份被测源码及meta、两个实际Prefab与交付版本一致。Prefab由Unity保存，本轮各只增加`pickupRadius: 1`。临时工程已清理；未进行团队2022.3.62f3c1整项目、正式玩家或GameSystem场景接线验收。

### 玩家受伤与无敌（2026-10-21）

本轮由葛亮亮确认两项规则：按请求调用顺序，第一笔实际扣血的伤害立即触发无敌，期间其余请求忽略；暂停和Upgrade期间禁止玩家受伤，并冻结剩余无敌时间，恢复战斗后继续计时。这里只定义玩家受伤行为，不替其他系统决定暂停策略。

新增 `PlayerDamageReceiver`，使用现有 `CombatRulesConfig.PlayerInvulnerabilityTime`（当前0.5秒）与玩家 `Health`。无效伤害、未初始化、死亡、旧生命、未开放及无敌期间的请求返回false；无效伤害不触发无敌。Health仍是HP唯一维护者，治疗照常走Health。

| 入口 / 状态 | 用法 |
| --- | --- |
| `Initialize(Health, CombatRulesConfig)` | 在Health初始化后，每条生命调用一次；复制无敌时长、清空计时、默认关闭受伤许可 |
| `SetDamageEnabled(bool)` | 流程层在战斗可受伤时开启，在暂停/Upgrade/结束本局时关闭；重复设置不重置计时 |
| `TryApplyDamage(float, int)` | 传伤害与请求产生时捕获的玩家LifeId；返回值表示实际扣血，不等于Enemy桥接的“请求已送达” |
| `IsInitialized` | 组件与所绑定的Health仍属于同一条有效生命 |
| `CanReceiveDamage` | 当前处于活动状态、许可开启、存活且无敌已结束 |
| `InvulnerabilityRemaining` | 战斗时读倒计时，许可关闭时读冻结值；生命失效或死亡返回0 |

接线示例（依赖由GameSystem准备；运行许可在Play Mode且Awake完成后开启）：

```csharp
health.Initialize(maxHp, initialHp, newLifeId);
receiver.Initialize(health, combatRules);
receiver.SetDamageEnabled(true);

int requestLifeId = health.LifeId; // 在生成这次请求时记录，延迟送达时不要改读新编号。
bool applied = receiver.TryApplyDamage(damage, requestLifeId);

receiver.SetDamageEnabled(false); // 暂停或Upgrade：停止受伤，保存剩余无敌时间。
receiver.SetDamageEnabled(true);  // 恢复战斗：继续剩余计时。
```

玩家因攻击受伤应统一经过此入口；直接调用玩家Health.TryApplyDamage会绕过无敌。敌人和Boss的Health仍可由相应伤害流程直接调用。新组件不读取Enemy类型或GameState，不实现桥接、胜负或场景接线；现有Enemy的PlayerDamageRequest没有玩家LifeId字段，正式适配时必须明确请求产生和目标编号捕获的位置，不能在迟到请求到达时临时读取新生命编号冒充原编号。

计时采用“结束时刻减当前时刻”，不用协程或每帧手动扣减；无敌在Health发事件之前预先锁定，防止同步回调再次扣血。暂停时保存剩余量，恢复时重算结束时刻。使用[Unity Time.time](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Time-time.html)的游戏时间，受外部TimeScale影响；组件不修改TimeScale。暂停应使用SetDamageEnabled，不能以禁用组件代替：OnDisable会使本次初始化失效，再启用时须配合Health的新LifeId重新初始化。

复现要点：100HP连续提交10和80伤害，只扣10；等待有效游戏时间0.5秒后再提交才扣血。受伤后等待约0.1秒再关闭许可，等待超过0.5秒仍保留关闭时的无敌余量，恢复后继续等待该余量。分别验证零伤害不占用无敌、HealthChanged内再次调用被拒绝、旧LifeId不影响新生命、停用后重新启用不会自动恢复受伤。可在模块临时空场景使用上述入口验证；不需要修改Game/MainMenu或正式角色Prefab。

本轮验证：Unity 2022.3.62f3隔离工程，以 `-batchmode -nographics -noUpm` 运行真实Play Mode，49项断言通过（`PLAYER_DAMAGE_PASS=49`）；另有1项新组件Prefab保存/重导入检查通过（`DAMAGE_PREFAB_PASS=1`），无Missing Script。覆盖无效/同帧/无敌期伤害、暂停超过原无敌时限后的恢复、同步回调重入与复用、生命周期及配置快照；运行检查无错误，编辑器正常退出。16份被测源码与工作区一致，临时工程已清理。未进行团队2022.3.62f3c1整项目、Enemy正式桥接或公共场景联调。

### 2026-10-18教学可读性整理

Health使用Mathf.Min/Max表达血量上下限，去掉通知前多余的临时变量；PlayerStats的属性读取展开为分行步骤；ExpOrb把玩家是否能获得经验交给AddExperience统一判断；EnemyDrops用具名初始化方法传给对象池，并用IsCurrentDeath统一重复判断。公共方法、事件、初始化参数及结算规则保持不变。

授课先读公式、扣血、经验while与购买switch，再讲事件，最后讲LifeId/SpawnId与回收。没有通过删除一次性结算保护或引入泛型框架来压缩行数。InitializeExpOrb/InitializeHealthPack作为方法参数传给Rent，由对象池在启用物品前调用；可先讲具名方法，再介绍等价的Lambda写法。

本次整理后，在Unity 2022.3.62f3隔离工程以 `-batchmode -nographics -noUpm` 进入真实Play Mode，51项断言通过（`TEACHING_CHECKS_PASS=51`）。覆盖公式、血量上下限、一次性死亡、回调复用、连续升级、三项购买、重复/无效拾取、掉落与清场。使用当前Combat、Core、三份配置脚本与原有PrefabPool副本；已核对15份被测源码与工作区一致。此结果不代表完整工程、实际场景或物理拾取链路验收；临时验证工程已清理。

提交PR前另以相同Unity版本和隔离方式检查本次交付的两个实际Prefab：14项资源加载、组件/网格/材质引用及保存重导入断言通过（`PREFAB_ASSET_PASS=14`），8项真实Play Mode断言通过（`PREFAB_PLAY_PASS=8`），包括从PrefabPool借出、奖励结算、重复/旧SpawnId拒绝、复用、清场和满血消费。资源保存仅在临时副本中执行，未回写原资产；未进行画面效果或物理拾取验收。此处是新增资源验证，前述51项代码回归沿用本次教学整理后的结果。

### 交付资源与接线边界

本模块保留Health、CombatMath、PlayerStats、EnemyDrops、ExpOrb、HealthPack，并新增PlayerDamageReceiver和PlayerPickupCollector，以及 `Assets/KWC/Prefabs/Combat` 的经验球/血包Prefab与占位材质。Prefab根节点分别挂ExpOrb、HealthPack，已支持绑定后的距离拾取，没有Collider或吸附；由GameSystem提供两个独立对象池并注入玩家拾取组件。

当前测试场景仍运行GameSystem原有测试逻辑，F4不代表本模块真实Health/掉落链路。波末回血、进入Upgrade、阶段购买开关、敌人/Boss生命初始化和回收编排需由GameSystem/Enemy Owner接入。Combat已提供TryHeal、SetPurchasingEnabled、HealthChanged/Died和掉落/拾取结算入口，不再接管流程。


- `Health`：当前/最大 HP 的唯一维护者，提供伤害、治疗、生命结束与一次性死亡通知。
- `PlayerDamageReceiver`：玩家受伤入口，处理首笔有效伤害后的0.5秒无敌、受伤许可与冻结/恢复。阵营/有效命中判定及Enemy请求适配仍待联合接线。
- `PlayerStats`：实现 `IPlayerStats`，维护玩家运行时属性、经验、等级、成长点和四项属性阶数。HP直接读取对应 `Health`；攻击力、移动速度、攻速从 `PlayerBaseConfig` 初始化，购买后使用成长配置重算，不修改配置资产。经验增加、连续升级和成长点发放也由本组件统一完成。
- `CombatMath`：实现攻击间隔、伤害、目标等级经验门槛、属性增量和最终值公式。属性等级允许超过20；浮点伤害保留公式结果，没有增加取整规则。
- `EnemyDrops`：对普通敌人的一次有效死亡申请经验球和血包，使用外部提供的两个 `IObjectPool`；掉落与回收的顺序由流程层控制。
- `PlayerPickupCollector`：读取玩家位置，检查拾取许可、玩家生命与物品距离；不接管玩家移动或阶段切换。
- `ExpOrb` / `HealthPack`：每帧向绑定的PlayerPickupCollector请求距离检测，处理一次拾取的经验或治疗结算，并归还所属对象池。

已支持经验升级、HP/攻击/移动/攻速四项属性购买，以及普通敌人掉落、靠近拾取和奖励结算；`GetAttributeRank` 返回四项实际阶数。武器、子弹尚未实现；经验球与血包Prefab已交付；场景、玩家引用、阶段许可和池接线由GameSystem负责。

### 初始化与生命周期

1. 流程层准备好生命编号，调用 `Health.Initialize(maximumHp, initialHp, lifeId)`。初始HP显式传入，新编号必须大于该组件之前的编号；Enemy接线时与它当前的 `LifeId` 对齐。
2. 玩家调用 `PlayerStats.Initialize(config, health, growthConfig, rulesConfig, initialLevel, initialExp, initialGrowthPoints)`。调用方应使用基础配置的HP初始化Health，并传入同一局的成长和战斗规则配置。基础值、成长系数、购买价格、经验系数和每级成长点奖励在初始化时复制，运行中不改写SO。初始进度无默认值，由调用方明确提供；initialExp是当前级内的经验，须低于下一等级门槛。初始化不升级、不发奖励或事件，重置四项阶数并关闭购买。
3. `TryApplyDamage(amount, lifeId)` / `TryHeal(amount, lifeId)` 返回值表示HP是否实际发生变化，拒绝未初始化、过期编号、死亡后的操作和无效数值。治疗不超过最大HP，也不复活。
4. `HealthChanged(lifeId, currentHp, maxHp)` 提供结算后的快照，`Died(lifeId)` 每条生命最多通知一次。初始化与清理不发死亡通知；初始化后使用方先读快照，再按生命周期维护订阅。
5. `EndLife()` 用于非击杀清理；组件 `OnDisable` 也会调用它。重新启用前必须重新初始化，不能靠旧HP继续战斗。Health进入新生命后，旧 `PlayerStats` 视图失效，也须重新初始化。

Health死亡通知后，停行为、掉落与对象回收的顺序仍由后续集成层显式组织。监听器不能提前回收对象或抛出异常来中断该链路。发生回调重入并已开始新生命时，Health会拒绝发送上一生命的迟到死亡通知。

### 四项属性购买

- GameSystem进入Upgrade时调用 `SetPurchasingEnabled(true)`，离开Upgrade时调用 `SetPurchasingEnabled(false)`。默认关闭；当前只是提供接线入口，尚未自动绑定GameManager事件，也不制定暂停策略。
- UI调用 `TryUpgradeAttribute(AttributeType.Hp / Attack / Movement / AttackSpeed)`。成功扣除配置中的 `AttributeUpgradeCost`（当前为1），对应阶数加1，并从基础值按总值公式重算。未花点保留，不设20级上限。
- 未开放购买、点数不足、玩家死亡、旧生命视图或未知属性请求均返回false，不修改状态。HP购买时同时提高当前/最大HP，保留缺血量，详见本页HP购买说明。
- 每次成功购买只发送一次 `StatsChanged`，此时属性、阶数和剩余点数已一起更新。初始化和失败请求不通知。UI接线时先读取状态，并成对订阅/取消订阅；事件同步执行，监听者负责显示，避免在通知内再次购买、重建状态或抛出异常。
- 保持Core的 `IPlayerStats` 为只读接口。UI由集成层拿到具体PlayerStats的购买入口与事件；Player仍只读取 `IPlayerStats.PlayerMovement`。

以基础配置和显式传入的3点成长点为例，开放购买后依次买一次攻击、移动、攻速，应得到攻击108、移动10.4、攻速106，三项阶数各1，剩余点数0；再次购买失败。它是接线检查示例，不表示游戏初始成长点已定为3。

接线调用示例（变量由集成层提供，UI不负责初始化或阶段许可）：

```csharp
// GameSystem：先初始化Health，再初始化玩家属性；初始进度显式传入。
playerHealth.Initialize(playerConfig.PlayerHp, initialHp, lifeId);
playerStats.Initialize(playerConfig, playerHealth, growthConfig, rulesConfig,
    initialLevel, initialExp, initialGrowthPoints);

// GameSystem进入Upgrade后开放购买，离开时传false关闭。
playerStats.SetPurchasingEnabled(true);

// UI按钮：发出请求，成功后StatsChanged会通知界面刷新。
bool purchased = playerStats.TryUpgradeAttribute(AttributeType.Attack);
```

### 经验与升级

主策划已确认保留超额经验、允许一次获得经验连续升级（2026-10-15，葛亮亮转达），现有代码与该规则一致，已同步GDD；初始等级和成长点继续由流程层明确传入。

| 接口 | 调用方 | 含义 |
|---|---|---|
| `bool AddExperience(int amount)` | Combat拾取逻辑 | 正数经验成功结算返回true；未初始化、旧生命视图、死亡或非正数请求返回false，不修改状态 |
| `IPlayerStats.Exp` | UI | 当前级内已积累的经验，不是历史总经验 |
| `IPlayerStats.ExpToNextLevel` | UI | 当前级升到下一级的完整门槛；还差多少可用它减去Exp |
| `ExperienceChanged` | UI订阅 | 每次成功增加经验后通知一次，读取新的经验及门槛 |
| `LevelUp(int finalLevel)` | UI或升级表现订阅 | 本次结算有升级时通知一次，传入最终等级；连升多级不逐级重放事件 |
| `StatsChanged` | 状态界面订阅 | 成功购买或成功增加经验后通知一次，可读取已更新的属性、等级与成长点 |

经验结算先扣除每一级门槛并保留余量，再按升级次数发放配置中的成长点（当前每级2点），全部写入后依次通知ExperienceChanged、LevelUp（若升级）、StatsChanged。监听者负责显示结果，不在同步通知中再次加经验、购买、回收或重新初始化玩家，也不要抛异常中断后续通知。

升级只改变经验、玩家等级和成长点，不自动提升属性、回血、切换GameState或开启购买。属性购买仍由GameSystem在波末Upgrade阶段开放。

```csharp
// 已由外部确认玩家满足拾取条件；ExpOrb内部调用AddExperience并回收。
int spawnId = expOrb.SpawnId;
bool collected = expOrb.TryCollect(playerStats, spawnId);

// UI读取：例如5/40表示当前级已积累5点，升级门槛为40点。
int currentExp = playerStats.Exp;
int requiredExp = playerStats.ExpToNextLevel;
```

示例：以显式传入的1级、0经验、0成长点开始，一次获得75经验，依次扣除30和40，结果为3级、5经验、4成长点，下一门槛为50。LevelUp通知一次，参数为3。这是示例进度，不是新增初始数值规定。

新增的Core只读属性需让其他IPlayerStats实现同步提供；当前仓库内唯一实现为PlayerStats。UI事件接线应在OnEnable订阅、OnDisable退订，首次订阅后读取快照；GameSystem确保引用与初始化已完成。此轮没有修改UI、Player、Enemy实现或接通真实场景。

### 普通敌人掉落与拾取结算

`EnemyDrops` 从 `CombatRulesConfig` 复制数值：普通敌人掉落1～3颗经验球，每颗5经验；独立进行一次10%的血包判定。血包按拾取时玩家最大HP的10%治疗，不超过上限，满血也消费，死亡玩家不能拾取。血包没有自然消失计时，波末由流程层清理。

| 接口 | 调用时机与结果 |
|---|---|
| `EnemyDrops.Initialize(health, boss, rules, orbPool, packPool, collector = null)` | 每次敌人生成，先初始化Health，再传入配置、两个池及可选拾取组件；同一Health生命不能重复初始化掉落组件 |
| `EnemyDrops.TryDrop(expectedLifeId)` | 真实死亡链路中，Enemy进入Dead后、回收前调用；接受本次请求返回true，存活、重复、过期或已清理请求返回false；生成回调中若清场或复用敌人，停止剩余掉落 |
| `ExpOrb.Initialize(ownerPool, expAmount, pickupCollector = null)` | 池的Rent初始化委托内调用，设置所属池、经验、新SpawnId及本次拾取绑定 |
| `HealthPack.Initialize(ownerPool, healthRatio, pickupCollector = null)` | 同上，设置治疗比例、新SpawnId及本次拾取绑定 |
| `TryCollect(playerStats / playerHealth, expectedSpawnId)` | 外部确认有效拾取后调用；true表示已消费并结算，false表示未消费；调用方不用再次发奖励或Return |
| `SpawnId` / `CanCollect` | 当前生成编号与是否尚可消费；CanCollect不代表已经满足距离、阶段等拾取条件 |

两个池的Prefab根节点分别挂 `ExpOrb`、`HealthPack`，不要共用一个池。EnemyDrops挂到随敌人一起启停的对象，使用该敌人的Health；HealthPack只传入玩家Health。初始化不依赖Start。当前掉落物生成在死亡位置，没有追加散落半径、吸附、碰撞层等规则。`boss=true`时跳过这套普通敌人奖励；Boss的具体掉落与胜利结算顺序仍待确认。

```csharp
// 敌人每次生成时：Health和Enemy使用同一生命编号。
enemyHealth.Initialize(maxHp, initialHp, lifeId);
enemyDrops.Initialize(enemyHealth, isBoss, rulesConfig, expOrbPool, healthPackPool);

// 以下位于集成层的真实死亡处理内：先使Enemy进入Dead，再掉落，最后回收。
// deadLifeId来自本次死亡通知，不在延迟回调中改读复用后的LifeId。
enemyDrops.TryDrop(deadLifeId);
// 之后由GameSystem回收敌人并更新存活统计；不能依赖订阅顺序。

// 确认拾取条件时记录编号；若延迟执行，要保留当时的编号。
int orbId = expOrb.SpawnId;
bool gotExperience = expOrb.TryCollect(playerStats, orbId);
int packId = healthPack.SpawnId;
bool usedPack = healthPack.TryCollect(playerHealth, packId);
```

拾取组件先锁定本次消费，再同步修改玩家状态并通知监听者，最后归还池；重复请求不会再次发奖。每次Initialize递增SpawnId，旧请求不能消费复用后的对象。监听者仍遵守前述通知约定，不能抛异常中断结算链路。池/Prefab配置错误会直接报错，应修正配置，不要在同一死亡中反复重试掉落。

波末调用各PrefabPool的 `ReturnAll()`，不要调用TryDrop或TryCollect模拟清理；OnDisable只使入口失效，不增加经验、不治疗、不产生掉落。两个掉落Prefab和拾取距离检测已交付；玩家引用与阶段许可、池引用、死亡及波末流程接线仍待对应Owner联调。

### 校验范围与接线状态

配置方负责提供有效的武器伤害、基础属性和成长系数。公式不逐项重复检查配置，也不再使用通用浮点转换校验包装；保留正攻速、有效等级/属性阶数与整数经验门槛的范围检查。

运行时保留Health血量边界、生命编号、一次性死亡与过期回调防护，以及PlayerStats的必要接线和初始化状态检查。独立Combat测试工具已移除。验证记录按轮次列在下方；模块验证不等同于真实物理或整局Gameplay验收。

本轮属性购买通过51项临时纯逻辑检查，覆盖三项数值、扣点与失败不变更、通知时机、21阶购买、配置快照、死亡及复用。另使用Unity 2022.3.62f3的真实程序集编译通过；独立编译提示现有AttributeGrowthConfig.playerBase序列化字段未在C#中赋值。临时检查文件已清理，以上不代表场景联调完成。

2026-10-14经验升级通过43项临时纯逻辑检查，覆盖门槛、经验结转、连续升级、每级奖励、通知次数与顺序、升级后购买、初始进度、配置快照、整数溢出不写入以及死亡/复用后的请求拒绝。使用真实Unity程序集编译通过，仍只有上述配置字段警告；临时检查代码与产物已清理，未进行Play Mode联调。

2026-10-14掉落与拾取结算通过50项临时逻辑检查，使用替代Unity对象与对象池，覆盖数量上下界、独立血包概率及0/1边界、配置快照、重复与过期请求、池化复用、连续升级、满血消费、治疗上限、死亡拒绝及清理无收益。新增三个组件连同既有Combat代码通过真实Unity程序集编译，仍只有上述配置字段警告。临时文件已清理；替代对象检查不能证明Unity启停、真实Prefab或场景联调通过。

提交PR前，在隔离临时工程中用真实Unity 2022.3.62f3执行Play Mode：51项检查通过，其中5项检查Health、PlayerStats、EnemyDrops、ExpOrb、HealthPack保存为Prefab并重导入后无Missing Script。其余覆盖生命结束、经验升级与购买、真实PrefabPool启停/复用、重复拾取、满血消费及清场。检查复现并修复了“掉落生成回调中清场并复用敌人，旧循环仍继续掉落”的问题。下方README示例也单独打印COMBAT_EXAMPLE_PASS。

此次隔离工程只载入Combat、必要Core/配置及现有PrefabPool；本机包管理器启动失败后使用 `-noUpm` 完成检查。它不等于整个KWC编译、公共场景、物理交互或Windows构建验收。团队基线2022.3.62f3c1与本机2022.3.62f3不同，PR未修改项目版本或包配置；基线版本整项目复核仍待Reviewer执行。临时工程与验证产物不入库，完成后清理。

Enemy的 `ICombatBridge` 仍为待Review提案，本模块没有修改其接口或接通真实战斗。玩家无敌计时和多来源受伤已按本页2026-10-21记录实现组件，靠近拾取已按2026-10-23记录实现，正式接线仍待协作；HP购买已按本页规则实现，UI购买入口仍待接线。

### Reviewer最小复现步骤

先检查 `Assets/KWC/Prefabs/Combat/ExpOrb.prefab` 与 `HealthPack.prefab`：根节点各有对应脚本、MeshFilter和MeshRenderer，网格与材质引用有效，没有Collider；在临时副本上保存并Reimport后应无Missing Script。测试池的Prefab字段分别指定这两个资源，Rent时先Initialize，再调用TryCollect；经验球增加配置中的经验后回收，血包按最大HP比例治疗，满血也消费，ReturnAll不产生奖励。可按下方示例配置PrefabPool，把示例的临时template替换为实际Prefab引用，并删去对该资源的SetActive、AddComponent和Destroy操作。

以下示例只在空的临时场景验证模块与真实PrefabPool，不接管Game/MainMenu。把代码临时保存为 `Assets/Editor/CombatReviewExample.cs`（已有Editor目录则直接使用），进入空场景的Play Mode，再执行菜单 `KWC/Combat/Review Example`。成功打印 `COMBAT_EXAMPLE_PASS`；失败直接抛异常。运行后删除临时脚本及其meta，不提交测试产物。

```csharp
using System;
using KWC.Combat;
using KWC.Core;
using KWC.Data;
using KWC.GameSystem;
using UnityEditor;
using UnityEngine;

public static class CombatReviewExample
{
    [MenuItem("KWC/Combat/Review Example")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play Mode first.");
        var player = new GameObject("Review Player");
        var template = new GameObject("Review ExpOrb");
        var poolObject = new GameObject("Review Pool");
        var config = ScriptableObject.CreateInstance<PlayerBaseConfig>();
        var growth = ScriptableObject.CreateInstance<AttributeGrowthConfig>();
        var rules = ScriptableObject.CreateInstance<CombatRulesConfig>();
        try
        {
            var health = player.AddComponent<Health>();
            health.Initialize(100, 100, 1);
            var stats = player.AddComponent<PlayerStats>();
            stats.Initialize(config, health, growth, rules, 1, 0, 0);
            stats.AddExperience(75);
            Require(stats.PlayerLevel == 3 && stats.Exp == 5 && stats.GrowthPoints == 4);
            stats.SetPurchasingEnabled(true);
            Require(stats.TryUpgradeAttribute(AttributeType.Attack));
            Require(stats.PlayerAttack == 108 && stats.GrowthPoints == 3);

            template.SetActive(false);
            template.AddComponent<ExpOrb>();
            var pool = poolObject.AddComponent<PrefabPool>();
            var serializedPool = new SerializedObject(pool);
            serializedPool.FindProperty("prefab").objectReferenceValue = template;
            serializedPool.ApplyModifiedPropertiesWithoutUndo();
            var instance = pool.Rent(Vector3.zero, Quaternion.identity,
                go => go.GetComponent<ExpOrb>().Initialize(pool, rules.ExpPerOrb));
            var orb = instance.GetComponent<ExpOrb>();
            int oldId = orb.SpawnId;
            Require(orb.TryCollect(stats, oldId));
            Require(stats.Exp == 10 && pool.BorrowedCount == 0);
            Require(!orb.TryCollect(stats, oldId));
            var reused = pool.Rent(Vector3.zero, Quaternion.identity,
                go => go.GetComponent<ExpOrb>().Initialize(pool, rules.ExpPerOrb));
            Require(reused == instance && orb.SpawnId > oldId);
            Require(!orb.TryCollect(stats, oldId));
            pool.ReturnAll();
            Require(stats.Exp == 10 && !orb.CanCollect);
            Debug.Log("COMBAT_EXAMPLE_PASS");
        }
        finally
        {
            UnityEngine.Object.Destroy(player);
            UnityEngine.Object.Destroy(template);
            UnityEngine.Object.Destroy(poolObject);
            UnityEngine.Object.Destroy(config);
            UnityEngine.Object.Destroy(growth);
            UnityEngine.Object.Destroy(rules);
        }
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new Exception("Combat example assertion failed.");
    }
}
```

其余关键复现输入如下，调用初始化和结算入口的方式见上文；使用临时配置副本，不修改正式SO资产：

| 检查 | 输入/步骤 | 预期 |
|---|---|---|
| 一次性死亡 | Health初始化100HP，连续两次提交100伤害，编号相同 | Died一次，HP为0；TryHeal拒绝复活 |
| 旧生命请求 | Health开始下一生命，再提交旧编号的伤害/治疗/掉落 | 返回false，新生命不变 |
| 普通敌人掉落 | 配置副本固定DropCountMin/Max为3，血包概率为1；初始化后致死，再调用TryDrop两次 | 两个池分别借出3颗球、1个血包；第二次返回false |
| 血包 | 100MaxHP、50HP拾取，再以满血状态拾取另一血包 | 首次60HP；满血仍消费，两次都回收 |
| 波末清理 | 各池ReturnAll，敌人对象停用 | 无经验、治疗、死亡或掉落收益 |
| 同步复用回归 | 用IObjectPool包装现有池，在首次Rent返回前清场、归还拾取物并初始化敌人下一生命 | 旧TryDrop不再生成剩余物品，新生命以后仍能正常掉落；该包装只用于复现 |
