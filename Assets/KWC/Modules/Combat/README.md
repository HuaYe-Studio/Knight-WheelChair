# Combat · 葛亮亮

本目录放 Weapon/Projectile、Damage/Health、EXP、成长、掉落/拾取；Prefab 交付到 `Assets/KWC/Prefabs/Combat`。

实现 `KWC.Core.IPlayerStats`，唯一维护玩家/敌人/Boss HP及玩家属性/EXP/等级/成长点/属性等级。通过 IPlayerContext 读位置，通过 IObjectPool 复用对象。UI提交购买请求，Combat校验，不能把购买计算交给UI。

本阶段只有配置和只读契约，没有替你实现 Gameplay。公式/数值以根目录《数值设计》为准；属性无20级上限。死亡只通知一次，波末清理不产生击杀收益。掉血/购买HP变化的细节、初始等级、EXP结转、拾取距离、子弹参数、同时受击和取整仍需确认。
