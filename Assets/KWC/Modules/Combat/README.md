# Combat · 葛亮亮

本目录放 Weapon/Projectile、Damage/Health、EXP、成长、掉落/拾取；Prefab 交付到 `Assets/KWC/Prefabs/Combat`。

实现 `KWC.Core.IPlayerStats`，唯一维护玩家/敌人/Boss HP及玩家属性/EXP/等级/成长点/属性等级。通过 IPlayerContext 读位置，通过 IObjectPool 复用对象。UI提交购买请求，Combat校验，不能把购买计算交给UI。

公式/数值以项目根目录《数值设计》为准；属性无20级上限。死亡只通知一次，波末清理不产生击杀收益。掉血/购买HP变化的细节、初始等级、EXP结转、拾取距离、子弹参数、同时受击和取整仍需确认。

## 第一轮：基础代码

- `Health`：当前/最大 HP 的唯一维护者，提供伤害、治疗、生命结束与一次性死亡通知。它是通用生命结算组件，玩家的0.5秒无敌、阵营/有效命中判定由下一轮伤害流程接入。
- `PlayerStats`：实现 `IPlayerStats`，提供本局初始属性、经验、等级和成长点的只读快照。HP直接读取对应 `Health`；攻击力、移动速度、攻速从 `PlayerBaseConfig` 初始化，不修改配置资产。
- `CombatMath`：实现攻击间隔、伤害、目标等级经验门槛、属性增量和最终值公式。属性等级允许超过20；浮点伤害保留公式结果，没有增加取整规则。

本轮没有武器、子弹、掉落、拾取、经验升级结算或属性购买。`GetAttributeRank` 当前只反映四项属性的基础等级0，尚无购买操作。

### 初始化与生命周期

1. 流程层准备好生命编号，调用 `Health.Initialize(maximumHp, initialHp, lifeId)`。初始HP显式传入，新编号必须大于该组件之前的编号；Enemy接线时与它当前的 `LifeId` 对齐。
2. 玩家调用 `PlayerStats.Initialize(config, health, initialLevel, initialExp, initialGrowthPoints)`。本轮从基础属性开始，调用方应使用配置的基础HP初始化Health；PlayerStats直接读取Health，不再重复校验HP与配置相等。等级、经验和成长点无默认值，由调用方明确决定。
3. `TryApplyDamage(amount, lifeId)` / `TryHeal(amount, lifeId)` 返回值表示HP是否实际发生变化，拒绝未初始化、过期编号、死亡后的操作和无效数值。治疗不超过最大HP，也不复活。
4. `HealthChanged(lifeId, currentHp, maxHp)` 提供结算后的快照，`Died(lifeId)` 每条生命最多通知一次。初始化与清理不发死亡通知；初始化后使用方先读快照，再按生命周期维护订阅。
5. `EndLife()` 用于非击杀清理；组件 `OnDisable` 也会调用它。重新启用前必须重新初始化，不能靠旧HP继续战斗。Health进入新生命后，旧 `PlayerStats` 视图失效，也须重新初始化。

Health死亡通知后，停行为、掉落与对象回收的顺序仍由后续集成层显式组织。监听器不能提前回收对象或抛出异常来中断该链路。发生回调重入并已开始新生命时，Health会拒绝发送上一生命的迟到死亡通知。

### 校验范围与接线状态

配置方负责提供有效的武器伤害、基础属性和成长系数。公式不逐项重复检查配置，也不再使用通用浮点转换校验包装；保留正攻速、有效等级/属性阶数与整数经验门槛的范围检查。

运行时保留Health血量边界、生命编号、一次性死亡与过期回调防护，以及PlayerStats的必要接线和初始化状态检查。独立Combat测试工具已移除。当前尚未验证Unity Play Mode、真实物理或整局Gameplay。

Enemy的 `ICombatBridge` 仍为待Review提案，本轮没有修改其接口或接通真实战斗。后续先确认玩家无敌、多来源受伤、购买HP变化与经验策略，再接入相应玩法。
