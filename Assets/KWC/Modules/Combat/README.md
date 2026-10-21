# Combat · 葛亮亮

本目录放 Weapon/Projectile、Damage/Health、EXP、成长、掉落/拾取；Prefab 交付到 `Assets/KWC/Prefabs/Combat`。

实现 `KWC.Core.IPlayerStats`，唯一维护玩家/敌人/Boss HP及玩家属性/EXP/等级/成长点/属性等级。通过 IPlayerContext 读位置，通过 IObjectPool 复用对象。UI提交购买请求，Combat校验，不能把购买计算交给UI。

公式/数值以项目根目录《数值设计》为准；属性无20级上限。死亡只通知一次，波末清理不产生击杀收益。经验保留超额部分并允许连续升级。掉血/购买HP变化的细节、初始等级与成长点、拾取距离、子弹参数、同时受击和取整仍需确认。

## 当前实现

### 2026-10-18教学可读性整理

Health使用Mathf.Min/Max表达血量上下限，去掉通知前多余的临时变量；PlayerStats的属性读取展开为分行步骤；ExpOrb把玩家是否能获得经验交给AddExperience统一判断；EnemyDrops用具名初始化方法传给对象池，并用IsCurrentDeath统一重复判断。公共方法、事件、初始化参数及结算规则保持不变。

授课先读公式、扣血、经验while与购买switch，再讲事件，最后讲LifeId/SpawnId与回收。没有通过删除一次性结算保护或引入泛型框架来压缩行数。InitializeExpOrb/InitializeHealthPack作为方法参数传给Rent，由对象池在启用物品前调用；可先讲具名方法，再介绍等价的Lambda写法。

本次整理后，在Unity 2022.3.62f3隔离工程以 `-batchmode -nographics -noUpm` 进入真实Play Mode，51项断言通过（`TEACHING_CHECKS_PASS=51`）。覆盖公式、血量上下限、一次性死亡、回调复用、连续升级、三项购买、重复/无效拾取、掉落与清场。使用当前Combat、Core、三份配置脚本与原有PrefabPool副本；已核对15份被测源码与工作区一致。此结果不代表完整工程、实际场景或物理拾取链路验收；临时验证工程已清理。

提交PR前另以相同Unity版本和隔离方式检查本次交付的两个实际Prefab：14项资源加载、组件/网格/材质引用及保存重导入断言通过（`PREFAB_ASSET_PASS=14`），8项真实Play Mode断言通过（`PREFAB_PLAY_PASS=8`），包括从PrefabPool借出、奖励结算、重复/旧SpawnId拒绝、复用、清场和满血消费。资源保存仅在临时副本中执行，未回写原资产；未进行画面效果或物理拾取验收。此处是新增资源验证，前述51项代码回归沿用本次教学整理后的结果。

### 交付资源与接线边界

本模块保留Health、CombatMath、PlayerStats、EnemyDrops、ExpOrb、HealthPack，以及 `Assets/KWC/Prefabs/Combat` 的经验球/血包Prefab与占位材质。Prefab根节点分别挂ExpOrb、HealthPack，没有Collider、自动拾取或吸附；由GameSystem提供两个独立对象池并调用现有初始化入口。

当前测试场景仍运行GameSystem原有测试逻辑，F4不代表本模块真实Health/掉落链路。波末回血、进入Upgrade、阶段购买开关、敌人/Boss生命初始化和回收编排需由GameSystem/Enemy Owner接入。Combat已提供TryHeal、SetPurchasingEnabled、HealthChanged/Died和掉落/拾取结算入口，不再接管流程。


- `Health`：当前/最大 HP 的唯一维护者，提供伤害、治疗、生命结束与一次性死亡通知。它是通用生命结算组件，玩家的0.5秒无敌、阵营/有效命中判定由下一轮伤害流程接入。
- `PlayerStats`：实现 `IPlayerStats`，维护玩家运行时属性、经验、等级、成长点和三项属性阶数。HP直接读取对应 `Health`；攻击力、移动速度、攻速从 `PlayerBaseConfig` 初始化，购买后使用成长配置重算，不修改配置资产。经验增加、连续升级和成长点发放也由本组件统一完成。
- `CombatMath`：实现攻击间隔、伤害、目标等级经验门槛、属性增量和最终值公式。属性等级允许超过20；浮点伤害保留公式结果，没有增加取整规则。
- `EnemyDrops`：对普通敌人的一次有效死亡申请经验球和血包，使用外部提供的两个 `IObjectPool`；掉落与回收的顺序由流程层控制。
- `ExpOrb` / `HealthPack`：处理一次拾取的经验或治疗结算，并归还所属对象池。外部确认拾取条件后调用，不内置距离检测。

已支持经验升级、攻击/移动/攻速三项属性购买，以及普通敌人掉落和两种拾取物的内部结算；`GetAttributeRank` 返回实际阶数，HP暂时返回0。武器、子弹、靠近拾取检测和HP购买尚未实现；经验球与血包Prefab已交付；场景和池接线由GameSystem负责。

### 初始化与生命周期

1. 流程层准备好生命编号，调用 `Health.Initialize(maximumHp, initialHp, lifeId)`。初始HP显式传入，新编号必须大于该组件之前的编号；Enemy接线时与它当前的 `LifeId` 对齐。
2. 玩家调用 `PlayerStats.Initialize(config, health, growthConfig, rulesConfig, initialLevel, initialExp, initialGrowthPoints)`。调用方应使用基础配置的HP初始化Health，并传入同一局的成长和战斗规则配置。基础值、成长系数、购买价格、经验系数和每级成长点奖励在初始化时复制，运行中不改写SO。初始进度无默认值，由调用方明确提供；initialExp是当前级内的经验，须低于下一等级门槛。初始化不升级、不发奖励或事件，重置三项阶数并关闭购买。
3. `TryApplyDamage(amount, lifeId)` / `TryHeal(amount, lifeId)` 返回值表示HP是否实际发生变化，拒绝未初始化、过期编号、死亡后的操作和无效数值。治疗不超过最大HP，也不复活。
4. `HealthChanged(lifeId, currentHp, maxHp)` 提供结算后的快照，`Died(lifeId)` 每条生命最多通知一次。初始化与清理不发死亡通知；初始化后使用方先读快照，再按生命周期维护订阅。
5. `EndLife()` 用于非击杀清理；组件 `OnDisable` 也会调用它。重新启用前必须重新初始化，不能靠旧HP继续战斗。Health进入新生命后，旧 `PlayerStats` 视图失效，也须重新初始化。

Health死亡通知后，停行为、掉落与对象回收的顺序仍由后续集成层显式组织。监听器不能提前回收对象或抛出异常来中断该链路。发生回调重入并已开始新生命时，Health会拒绝发送上一生命的迟到死亡通知。

### 三项属性购买

- GameSystem进入Upgrade时调用 `SetPurchasingEnabled(true)`，离开Upgrade时调用 `SetPurchasingEnabled(false)`。默认关闭；当前只是提供接线入口，尚未自动绑定GameManager事件，也不制定暂停策略。
- UI调用 `TryUpgradeAttribute(AttributeType.Attack / Movement / AttackSpeed)`。成功扣除配置中的 `AttributeUpgradeCost`（当前为1），对应阶数加1，并从基础值按总值公式重算。未花点保留，不设20级上限。
- 未开放购买、点数不足、玩家死亡、旧生命视图、HP或未知属性请求均返回false，不修改状态。HP提升的当前血量处理仍待确认。
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
| `EnemyDrops.Initialize(health, boss, rules, orbPool, packPool)` | 每次敌人生成，先初始化Health，再传入配置及两个池；同一Health生命不能重复初始化掉落组件 |
| `EnemyDrops.TryDrop(expectedLifeId)` | 真实死亡链路中，Enemy进入Dead后、回收前调用；接受本次请求返回true，存活、重复、过期或已清理请求返回false；生成回调中若清场或复用敌人，停止剩余掉落 |
| `ExpOrb.Initialize(ownerPool, expAmount)` | 池的Rent初始化委托内调用，设置所属池、经验和新SpawnId |
| `HealthPack.Initialize(ownerPool, healthRatio)` | 同上，设置治疗比例和新SpawnId |
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

波末调用各PrefabPool的 `ReturnAll()`，不要调用TryDrop或TryCollect模拟清理；OnDisable只使入口失效，不增加经验、不治疗、不产生掉落。两个掉落Prefab已交付；拾取距离检测、玩家引用与阶段许可、池引用、死亡及波末流程接线仍待对应Owner联调。

### 校验范围与接线状态

配置方负责提供有效的武器伤害、基础属性和成长系数。公式不逐项重复检查配置，也不再使用通用浮点转换校验包装；保留正攻速、有效等级/属性阶数与整数经验门槛的范围检查。

运行时保留Health血量边界、生命编号、一次性死亡与过期回调防护，以及PlayerStats的必要接线和初始化状态检查。独立Combat测试工具已移除。验证记录按轮次列在下方；模块验证不等同于真实物理或整局Gameplay验收。

本轮属性购买通过51项临时纯逻辑检查，覆盖三项数值、扣点与失败不变更、通知时机、21阶购买、配置快照、死亡及复用。另使用Unity 2022.3.62f3的真实程序集编译通过；独立编译提示现有AttributeGrowthConfig.playerBase序列化字段未在C#中赋值。临时检查文件已清理，以上不代表场景联调完成。

2026-10-14经验升级通过43项临时纯逻辑检查，覆盖门槛、经验结转、连续升级、每级奖励、通知次数与顺序、升级后购买、初始进度、配置快照、整数溢出不写入以及死亡/复用后的请求拒绝。使用真实Unity程序集编译通过，仍只有上述配置字段警告；临时检查代码与产物已清理，未进行Play Mode联调。

2026-10-14掉落与拾取结算通过50项临时逻辑检查，使用替代Unity对象与对象池，覆盖数量上下界、独立血包概率及0/1边界、配置快照、重复与过期请求、池化复用、连续升级、满血消费、治疗上限、死亡拒绝及清理无收益。新增三个组件连同既有Combat代码通过真实Unity程序集编译，仍只有上述配置字段警告。临时文件已清理；替代对象检查不能证明Unity启停、真实Prefab或场景联调通过。

提交PR前，在隔离临时工程中用真实Unity 2022.3.62f3执行Play Mode：51项检查通过，其中5项检查Health、PlayerStats、EnemyDrops、ExpOrb、HealthPack保存为Prefab并重导入后无Missing Script。其余覆盖生命结束、经验升级与购买、真实PrefabPool启停/复用、重复拾取、满血消费及清场。检查复现并修复了“掉落生成回调中清场并复用敌人，旧循环仍继续掉落”的问题。下方README示例也单独打印COMBAT_EXAMPLE_PASS。

此次隔离工程只载入Combat、必要Core/配置及现有PrefabPool；本机包管理器启动失败后使用 `-noUpm` 完成检查。它不等于整个KWC编译、公共场景、物理交互或Windows构建验收。团队基线2022.3.62f3c1与本机2022.3.62f3不同，PR未修改项目版本或包配置；基线版本整项目复核仍待Reviewer执行。临时工程与验证产物不入库，完成后清理。

Enemy的 `ICombatBridge` 仍为待Review提案，本轮没有修改其接口或接通真实战斗。后续先确认玩家无敌计时、多来源受伤、购买HP变化及拾取距离，再接入相应玩法。

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
