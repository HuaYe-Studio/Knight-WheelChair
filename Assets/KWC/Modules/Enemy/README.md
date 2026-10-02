# Enemy / AI · 王勤伟

本目录放 Enemy1、Boss FSM/移动/命中机会/动画接线/AI Debug；Prefab 交付到 `Assets/KWC/Prefabs/Enemy`。

读取 `IPlayerContext`、Map 碰撞事实和配置，向 Combat 提交伤害。Combat Health 拥有 HP；Enemy 处理 Dead 行为，Game System 负责生成和回收。Enemy1 首次接触立即攻击。Boss 仅 Dash 造成伤害，单次最多一次，蓄力结束才锁位置快照，撞墙进入 Recovery。

复用时清掉目标、Dead、冷却、Dash快照/命中记录。非击杀的波末回收不能请求掉落。首次冲刺时机、持续接触节奏、绕障、Wander选点等仍依 GDD 的待确认项处理。不要添加敌人种类或招式。
