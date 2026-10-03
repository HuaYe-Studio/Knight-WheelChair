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
    // 表 N04 的纯值快照。决策层依赖它而不是 ScriptableObject，这样决策层不加载资源也能测试，
    // 而且没有任何状态能反手改配置。
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

        // dev：Approach 与 Wander 之间的滞回带宽。
        //
        // 表 N04 只给了一个阈值（「玩家超出该距离则主动靠近；进入范围后转为 Wander」）。
        // 进入条件和退出条件用同一个数，正是抖动的根因，所以这里用比例推出退出带，
        // 而不是再编一个距离。标为 dev 并登记在 Modules/Enemy/README.md 待策划确认；
        // 正式规则出来之后这个比例就删掉，换成设计值。
        public float WanderEnterRatio = 0.9f;

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
        private readonly IFactHealthSource _health;
        private readonly ICombatBridge _bridge;
        private readonly IWallCheck _wallCheck;

        public BossBrain(BossController owner, KWC.Core.IPlayerContext player, IMovementHandler movement,
            IFactHealthSource health, ICombatBridge bridge, IWallCheck wallCheck, BossConfigValues config)
        {
            _owner = owner;
            _player = player;
            _movement = movement;
            _health = health;
            _bridge = bridge;
            _wallCheck = wallCheck;
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
        public bool IsDead => _health != null && !_health.IsAlive;

        // 表 N04：「玩家超出该距离则主动靠近；进入范围后转为 Wander」。
        // 滞回是必需的：规范只给了一个阈值，进入和退出用同一个数就是抖动，
        // 所以两个方向用不同的带，退出带只在 Config.WanderEnterRatio 一处定义。
        public bool ShouldApproach => DistanceToPlayer > Config.PreferredDistance;

        public bool ShouldStopApproaching => DistanceToPlayer <= Config.PreferredDistance * Config.WanderEnterRatio;

        // 命名判定：值得起手冲刺，只有玩家已经跑出 Preferred Distance 时才成立。
        // PreferredDistance 是唯一有设计来源的距离量。
        public bool CanStartDash(BossDashThrottle throttle)
        {
            return !IsDead && HasPlayer && throttle != null && throttle.IsReady && ShouldApproach;
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
        // 待确认：Map 还没交付通行性查询，所以注入的检查目前是「一直报没墙」的占位实现。
        // 调用点是真的，以后把 Map 的查询接进来，这里一行都不用改。
        public bool IsWallAhead(Vector3 direction, float distance)
        {
            return _wallCheck != null &&
                   _wallCheck.TryGetWallHit(_movement.Position, direction, distance, out _);
        }

        // 提交伤害请求。返回 true 只表示请求已交出，不代表玩家掉血。
        public bool SubmitPlayerDamage(float damage, AttackKind kind)
        {
            PlayerDamageRequest request = new PlayerDamageRequest(_owner, damage, PlayerPosition, kind);
            return _bridge.TrySubmitPlayerDamage(request);
        }

        public void ReportDeath()
        {
            _bridge.NotifyEnemyDied(_owner, true);
        }
    }
}
