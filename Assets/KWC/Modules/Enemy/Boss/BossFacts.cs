using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // Boss 的事实层与节流层。
    //
    // 「事实」= 世界是什么，只读，写入方只能是它的来源。
    // 「节流」= 允不允许现在做，单独存字段、单独用时钟。
    // 这里把两者放在一起，是因为它们都不含行为：行为在 BossStates.cs 里。
    // ============================================================================================

    // --------------------------------------------------------------------------------------------
    // 表 N04 的值快照。决策层依赖它而不是 ScriptableObject，这样决策层不加载资源也能测试。
    // 字段目前可写；Brain/状态约定只读取它，不能把它当成编译器强制只读的对象。
    //
    // 每一项都来自表 N04，这里不引入新数字（除了明确标注 dev 的滞回带宽与推导值）。
    // --------------------------------------------------------------------------------------------
    public sealed class BossConfigValues
    {
        public float BossHp;
        public float BossMovement;
        public float PreferredDistance;
        public float WanderRadius;
        public float WanderSpeed;
        public float WanderIntervalMin;
        public float WanderIntervalMax;
        public float DashDistance;
        public float DashSpeed;
        public float DashDamage;
        public float DashCooldown;
        public float DashPrepareTime;
        public float DashRecoveryTime;
        public int DashHitCount;
        public float NormalContactDamage;

        // 表 N04：单次 Dash 最多造成一次伤害。
        public int DashHitCountClamped => DashHitCount < 1 ? 1 : DashHitCount;

        // 表 N04 没有列 Dash 持续时间，它由距离和速度推导，保证 Boss 冲完 Dash Distance 就停。
        // 写在这里而不是散在某个状态里。
        public float DashTravelDuration => DashSpeed > 0f ? DashDistance / DashSpeed : 0f;
    }

    // dev：从 Game System 维护的资产类型转成值快照。放在 Enemy 模块里，
    // 这样 Enemy 模块不需要去改配置类。
    public static class BossConfigExtensions
    {
        public static BossConfigValues ToValues(this KWC.Data.BossConfig config)
        {
            if (config == null)
            {
                return null;
            }

            Vector2 interval = config.WanderInterval;

            return new BossConfigValues
            {
                BossHp = config.BossHp,
                BossMovement = config.BossMovement,
                PreferredDistance = config.PreferredDistance,
                WanderRadius = config.WanderRadius,
                WanderSpeed = config.WanderSpeed,
                WanderIntervalMin = interval.x,
                WanderIntervalMax = interval.y,
                DashDistance = config.DashDistance,
                DashSpeed = config.DashSpeed,
                DashDamage = config.DashDamage,
                DashCooldown = config.DashCooldown,
                DashPrepareTime = config.DashPrepareTime,
                DashRecoveryTime = config.DashRecoveryTime,
                DashHitCount = config.DashHitCount,
                NormalContactDamage = config.NormalContactDamage
            };
        }
    }

    // --------------------------------------------------------------------------------------------
    // 节流层：Dash 冷却，回答「现在能不能冲」。
    //
    // 表 N04：Dash Cooldown 3.5 s，是「每次冲刺结束后的冷却时间」，
    // 所以时钟从 DashRecovery 开始走，不是从 DashPrepare。
    // 它单独存字段、单独时钟，绝不混进任何一个事实里。
    // --------------------------------------------------------------------------------------------
    public sealed class BossDashThrottle
    {
        private readonly float _cooldownSeconds;
        private float _elapsed;

        public BossDashThrottle(float cooldownSeconds)
        {
            _cooldownSeconds = cooldownSeconds < 0f ? 0f : cooldownSeconds;
            _elapsed = _cooldownSeconds;
        }

        public float CooldownSeconds => _cooldownSeconds;

        public bool IsReady => _elapsed >= _cooldownSeconds;

        public float TimeUntilReady
        {
            get
            {
                float remaining = _cooldownSeconds - _elapsed;
                return remaining > 0f ? remaining : 0f;
            }
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                _elapsed += deltaTime;
            }
        }

        // 只在这件事真的发生的那一刻写，绝不提前写。
        public void StartCooldown()
        {
            _elapsed = 0f;
        }

        public void Reset()
        {
            _elapsed = _cooldownSeconds;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 一次 Dash 的共享数据：锁定的目标位置 + 「本次已命中」闩锁。
    //
    // 它刻意不是状态基类的字段，也不是单个状态的字段：它必须跨越两个状态存活
    // （DashPrepare 结束时写、Dash 期间读），所以它属于控制器，只交给需要它的两个状态。
    // 放进共享基类才是问题所在 —— 那样所有状态都会通过它偷偷耦合。
    // --------------------------------------------------------------------------------------------
    public sealed class BossDashContext
    {
        public Vector3 SnapshotPosition { get; private set; }
        public bool HasSnapshot { get; private set; }
        public bool HasHitPlayer { get; private set; }

        // 只在一次：前摇结束时调用（表 N04「DashPrepare 结束时锁定玩家位置」）。
        // 命中闩锁也在这里清，保证每次冲刺都是从零开始计数。
        public void CaptureSnapshot(Vector3 playerPosition)
        {
            SnapshotPosition = playerPosition;
            HasSnapshot = true;
            HasHitPlayer = false;
        }

        // 冲刺真正开始时清闩锁。与 CaptureSnapshot 分开，是为了让这两个时刻在转移日志里可分辨。
        public void ResetHitLatch()
        {
            HasHitPlayer = false;
        }

        // 一次冲刺命中过就不能再命中（表 N04 Dash Hit Count = 1）。
        public void MarkHitPlayer()
        {
            HasHitPlayer = true;
        }

        // 新冲刺开始时清，复用时也清。冲刺途中绝不清。
        public void Reset()
        {
            SnapshotPosition = default;
            HasSnapshot = false;
            HasHitPlayer = false;
        }
    }

    // ============================================================================================
    // Boss 的大脑：把「事实读取 + 节流 + 伤害提交」收成一个窄接口，供 BossStates.cs 使用。
    //
    // 与 Enemy1Brain 同样的理由：六个状态如果各自持有 6 个依赖，构造函数会长得没法读。
    // 收成门面之后，状态对外界的耦合面只剩「读事实 / 发请求」这一组方法。
    //
    // Brain 不持有状态、不决定转移：转移由状态返回、由状态机执行。
    // ============================================================================================
    public sealed class BossBrain
    {
        private readonly BossController _owner;
        private readonly KWC.Core.IPlayerContext _player;
        private readonly IMovementHandler _movement;
        private readonly ICombatBridge _bridge;
        private readonly IWallCheck _wallCheck;

        // 死亡闸门属于「这条命」，所以放在 Brain（每条命一个），而不是放在 Dead 状态里。
        private readonly DeathReportLatch _deathLatch;

        public BossBrain(BossController owner, KWC.Core.IPlayerContext player, IMovementHandler movement,
            ICombatBridge bridge, IWallCheck wallCheck, DeathReportLatch deathLatch,
            BossConfigValues config)
        {
            _owner = owner;
            _player = player;
            _movement = movement;
            _bridge = bridge;
            _wallCheck = wallCheck;
            _deathLatch = deathLatch;
            Config = config;
        }

        // 表 N04 的全部数值。决策层只读它，任何状态都改不了。
        public BossConfigValues Config { get; }

        // ---- 事实（只读）------------------------------------------------------------------

        public bool HasPlayer => _player != null && _player.EntityTransform != null;

        public Vector3 PlayerPosition => _player.Position;

        public Vector3 SelfPosition => _movement.Position;

        public float DistanceToPlayer
        {
            get
            {
                Vector3 delta = PlayerPosition - SelfPosition;
                delta.y = 0f;
                return delta.magnitude;
            }
        }

        // 进度事实，归 Combat Health 所有。
        public bool IsDead => _deathLatch != null && !_deathLatch.IsArmed;

        // 表 N04：「玩家超出该距离则主动靠近；进入范围后转为 Wander」。
        //
        // 进出使用**同一个既定阈值 PreferredDistance（8）**，不再乘以任何比例 ——
        // 之前引入的 0.9 带宽属未经设计确认的新数值，已按要求撤回。
        //
        // 已知风险（登记为待确认项，不用默认值掩盖）：进出门槛重合时，玩家停在 8 附近
        // 可能造成 Approach 与 Wander 之间反复切换。表 N04 没有规定第二个阈值，
        // 所以这里不自行发明带宽，改为在 Modules/Enemy/README.md 登记待策划确认。
        public bool ShouldApproach => DistanceToPlayer > Config.PreferredDistance;

        public bool ShouldStopApproaching => DistanceToPlayer <= Config.PreferredDistance;

        // 命名判定：现在能不能起手冲刺。
        //
        // 判据只有「活着 + 有玩家 + 冷却就绪」，**刻意不含距离门槛**。
        //
        // 为什么去掉距离门槛（审核意见）：原来要求 ShouldApproach（距离 > PreferredDistance），
        // 而这个条件同时又是 Wander → Approach 的出口条件，于是冲刺成了不可达分支 ——
        // 玩家一超出阈值就转去走路，走路速度 6 追不上玩家 10，Boss 永远不冲刺。
        // 现在按 GDD 的意图「冲刺是接近玩家的手段」：冷却好了就冲，Approach 与 Wander 都能起手。
        // 起手判断统一放在 BossController.Tick，位于状态机之前，与所处状态无关。
        public bool CanStartDash(BossDashThrottle throttle)
        {
            return !IsDead && HasPlayer && throttle != null && throttle.IsReady;
        }

        // ---- 执行（状态唯一能做的几件事）--------------------------------------------------

        public void ApproachPlayer(float deltaTime)
        {
            _movement.MoveToward(PlayerPosition, Config.BossMovement, deltaTime);
        }

        public void MoveToward(Vector3 point, float speed, float deltaTime)
        {
            _movement.MoveToward(point, speed, deltaTime);
        }

        public void StopMoving()
        {
            _movement.Stop();
        }

        public void SetMovementEnabled(bool enabled)
        {
            _movement.SetMovementEnabled(enabled);
        }

        // 表 N04：撞击地图墙体或边界时立即结束 Dash。
        //
        // 传入的是**本帧实际打算走的位移**，返回的是**本帧实际被允许到达的位置**。
        // 之前接口是「朝某方向查询 DashDistance」，Dash 每一步都拿完整距离去查，
        // 于是墙在前方 6 单位时一步未走就被判定撞墙（实测移动 0 单位）。
        //
        // 返回 true 表示这一步撞墙被截断，调用方应结束冲刺。
        public bool TryDashStep(Vector3 from, Vector3 intendedPosition, out Vector3 allowedPosition)
        {
            Vector3 displacement = intendedPosition - from;

            if (_wallCheck == null)
            {
                // 没有撞墙检测实现时不做静默兜底判断：直接放行，并在控制器的启动警告里
                // 已经说明过「Wall -> Stop 无法触发」。
                allowedPosition = intendedPosition;
                return false;
            }

            return _wallCheck.TryMove(from, displacement, out allowedPosition, out _);
        }

        // 提交伤害请求。返回 true 只表示请求已交出，不代表玩家掉血。
        public bool SubmitPlayerDamage(float damage, AttackKind kind)
        {
            PlayerDamageRequest request = new PlayerDamageRequest(_owner, damage, PlayerPosition, kind);
            return _bridge.TrySubmitPlayerDamage(request);
        }

        // 这条命是否还没上报过死亡。只读，供调试面板与测试断言使用。
        public bool DeathReportArmed => _deathLatch == null || _deathLatch.IsArmed;

        // 上报死亡。由 Dead 状态调用，去重在这里：每条命只有第一次调用会真的送出，并带生命编号。
        public bool TryClaimDeathReport(out int lifeId)
        {
            if (!_deathLatch.TryClaimDeathReport(out lifeId))
            {
                return false;
            }

            _bridge.NotifyEnemyDied(_owner, true, lifeId);
            return true;
        }
    }
}
