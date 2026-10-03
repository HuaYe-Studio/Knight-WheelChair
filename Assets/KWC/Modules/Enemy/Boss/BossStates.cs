using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // Boss 轮椅骑士的六个状态（表 N04）。
    //
    // Approach    玩家超出 Preferred Distance 时主动靠近
    // Wander      进入范围后在其半径内随机移动
    // DashPrepare 前摇 0.7 s，结束时锁定玩家位置快照
    // Dash        以 Dash Speed 冲出，最多 Dash Distance，途中不追踪；这是唯一能造成伤害的状态
    // DashRecovery恢复 0.6 s，Dash Cooldown 从这里开始计时
    // Dead        终态
    //
    // Dash 被拆成三个阶段，是因为每个阶段拥有不同的数据（快照锁、命中记录、冷却起点）
    // 和不同的出口条件；合成一个状态就会退回到用内部标志位区分阶段，那正是我们要避免的。
    // ============================================================================================

    public enum BossStateId
    {
        None = 0,
        Approach = 1,
        Wander = 2,
        DashPrepare = 3,
        Dash = 4,
        DashRecovery = 5,
        Dead = 6
    }

    // --------------------------------------------------------------------------------------------
    // 状态：Approach。
    // 只拥有移动决策，不拥有冲刺决策 —— 冲刺由 Wander 发起，
    // 这样同一个决策帧里不可能出现「继续靠近」和「起手冲刺」互相竞争。
    // --------------------------------------------------------------------------------------------
    public sealed class BossApproachState : StateBase<BossStateId>
    {
        private readonly BossBrain _brain;

        public BossApproachState(BossBrain brain)
        {
            _brain = brain;
        }

        public override BossStateId Id => BossStateId.Approach;
        public override string DisplayName => "Approach 靠近";

        public override BossStateId? OnUpdate(IStateMachineHost<BossStateId> host, float deltaTime)
        {
            // 顺序即显式优先级：死亡 → 玩家丢失 → 进入范围。
            if (_brain.IsDead)
            {
                return BossStateId.Dead;
            }

            if (!_brain.HasPlayer)
            {
                _brain.StopMoving();
                return null;
            }

            // 出口用的是和进入不同的带（滞回），所以卡在 Preferred Distance 上也不会抖动。
            if (_brain.ShouldStopApproaching)
            {
                return BossStateId.Wander;
            }

            _brain.ApproachPlayer(deltaTime);
            return null;
        }

        public override void OnExit(IStateMachineHost<BossStateId> host)
        {
            _brain.StopMoving();
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：Wander。
    // 只拥有自己的目标点、自己的重选计时器、自己的兜底时长。三者都在 OnEnter 复位，
    // 所以池化 Boss 重新进入本状态时，不会走向上一个使用者留下的点。
    // --------------------------------------------------------------------------------------------
    public sealed class BossWanderState : StateBase<BossStateId>
    {
        private readonly BossBrain _brain;
        private readonly IWanderPointSource _pointSource;
        private readonly BossDashThrottle _dashThrottle;
        private readonly float _maxStaySeconds;

        private Vector3 _targetPoint;
        private bool _hasTargetPoint;
        private float _repickElapsed;
        private float _currentInterval;

        public BossWanderState(BossBrain brain, IWanderPointSource pointSource, BossDashThrottle dashThrottle,
            float maxStaySeconds)
        {
            _brain = brain;
            _pointSource = pointSource;
            _dashThrottle = dashThrottle;
            _maxStaySeconds = maxStaySeconds;
        }

        public override BossStateId Id => BossStateId.Wander;
        public override string DisplayName => "Wander 闲逛";

        public override void OnEnter(IStateMachineHost<BossStateId> host)
        {
            _hasTargetPoint = false;
            _repickElapsed = 0f;
            _currentInterval = 0f;
            PickNewPoint();
        }

        public override BossStateId? OnUpdate(IStateMachineHost<BossStateId> host, float deltaTime)
        {
            if (_brain.IsDead)
            {
                return BossStateId.Dead;
            }

            if (!_brain.HasPlayer)
            {
                _brain.StopMoving();
                return null;
            }

            // 玩家离开范围就重新追。这一条排在冲刺之前，
            // 保证「该追了」不会被一个刚好变得合法的冲刺抢走。
            if (_brain.ShouldApproach)
            {
                return BossStateId.Approach;
            }

            if (_brain.CanStartDash(_dashThrottle))
            {
                return BossStateId.DashPrepare;
            }

            _repickElapsed += deltaTime;
            if (!_hasTargetPoint || _repickElapsed >= _currentInterval)
            {
                PickNewPoint();
            }

            if (_hasTargetPoint)
            {
                _brain.MoveToward(_targetPoint, _brain.Config.WanderSpeed, deltaTime);
            }
            else
            {
                _brain.StopMoving();
            }

            // 必有出口：保险丝。Wander 也被上面两个条件带出去，
            // 但一个无法证明会结束的状态，就是在等一个丢失的信号造成死锁。
            // 上限由表 N04 给出的重选节奏（Wander Interval 1~2 s）推导，不是编的。
            if (_maxStaySeconds > 0f && host.TimeInState >= _maxStaySeconds)
            {
                return BossStateId.Approach;
            }

            return null;
        }

        public override void OnExit(IStateMachineHost<BossStateId> host)
        {
            _brain.StopMoving();

            // 释放闲逛目标点，状态结束后不再有人用它。
            _hasTargetPoint = false;
        }

        public bool HasTargetPoint => _hasTargetPoint;
        public Vector3 TargetPoint => _targetPoint;

        private void PickNewPoint()
        {
            _repickElapsed = 0f;

            BossConfigValues config = _brain.Config;
            _currentInterval = _pointSource.RollInterval(config.WanderIntervalMin, config.WanderIntervalMax);
            _hasTargetPoint = _pointSource.TryGetPoint(_brain.SelfPosition, config.WanderRadius, out _targetPoint);
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：DashPrepare。前摇 0.7 s（表 N04），此状态不移动，也不锁快照。
    //
    // 快照在 OnExit 里取，也就是「DashPrepare 结束时」。放进 OnEnter 会锁得太早，
    // 放进 OnUpdate 会反复锁，而表 N04 说得很具体：前摇结束锁一次，途中不继续追踪。
    //
    // OnEnter 不记录任何东西、也不发起转移：这是一个纯等待态，
    // 所以它有一个明确的倒计时而不是等某个事件。
    // --------------------------------------------------------------------------------------------
    public sealed class BossDashPrepareState : StateBase<BossStateId>
    {
        private readonly BossBrain _brain;
        private readonly BossDashContext _dashContext;

        public BossDashPrepareState(BossBrain brain, BossDashContext dashContext)
        {
            _brain = brain;
            _dashContext = dashContext;
        }

        public override BossStateId Id => BossStateId.DashPrepare;
        public override string DisplayName => "DashPrepare 冲刺前摇";

        public override void OnEnter(IStateMachineHost<BossStateId> host)
        {
            // 冲刺从静止开始：前摇不许朝玩家飘，也不许继承上一个状态的移动。
            _brain.StopMoving();

            // 清掉可能残留的快照，避免上一次冲刺的快照被误用。
            _dashContext.Reset();
        }

        public override BossStateId? OnUpdate(IStateMachineHost<BossStateId> host, float deltaTime)
        {
            if (_brain.IsDead)
            {
                return BossStateId.Dead;
            }

            // 前摇中途玩家引用没了，快照就没意义，别朝一个过期位置起冲。
            // 这也是本状态的一个保底出口：倒计时本身总会结束。
            if (!_brain.HasPlayer)
            {
                return BossStateId.Approach;
            }

            if (host.TimeInState >= _brain.Config.DashPrepareTime)
            {
                return BossStateId.Dash;
            }

            return null;
        }

        public override void OnExit(IStateMachineHost<BossStateId> host)
        {
            // 前摇结束时锁定玩家位置快照，只做这一次。
            if (_brain.HasPlayer)
            {
                _dashContext.CaptureSnapshot(_brain.PlayerPosition);
            }
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：Dash。沿快照方向以 Dash Speed 冲刺，最多 Dash Distance。
    //
    // 这是 Boss 唯一允许提交伤害的状态，因为 Normal Contact Damage = 0：非 Dash 普通接触不造成伤害。
    //
    // 数据归属：快照属于 BossDashContext（DashPrepare 写），冷却属于 BossDashThrottle
    // （DashRecovery 启动），命中计数属于本状态，因为它只在这一段冲刺里有意义。
    // --------------------------------------------------------------------------------------------
    public sealed class BossDashState : StateBase<BossStateId>
    {
        private readonly BossBrain _brain;
        private readonly BossDashContext _dashContext;
        private readonly float _hitRadius;

        private float _elapsed;
        private Vector3 _dashDirection;
        private bool _started;
        private int _strikeCount;

        public BossDashState(BossBrain brain, BossDashContext dashContext, float hitRadius)
        {
            _brain = brain;
            _dashContext = dashContext;
            _hitRadius = hitRadius;
        }

        public override BossStateId Id => BossStateId.Dash;
        public override string DisplayName => "Dash 冲刺";

        // dev：调试面板用的审计计数。「提交了几次请求」和「打中了几次」是两个不同的数，
        // 占位桥接的存在正是它们必须分开的原因。
        public int SubmittedStrikeCount => _strikeCount;
        public int AcceptedStrikeCount { get; private set; }

        public override void OnEnter(IStateMachineHost<BossStateId> host)
        {
            // 自己的状态数据在这里复位，绝不在构造函数里：
            // 池化实例会重新进入本状态，不能继承上一次冲刺的进度。
            _elapsed = 0f;
            _started = false;
            _dashDirection = Vector3.zero;
            _strikeCount = 0;
            AcceptedStrikeCount = 0;

            // 一次冲刺最多命中一次（表 N04 Dash Hit Count = 1）。
            _dashContext.ResetHitLatch();

            if (!_dashContext.HasSnapshot)
            {
                // 没有快照说明前摇没走完或玩家引用丢失。朝任意方向冲就是静默默认值，
                // 所以这里退化成一趟零长度冲刺，交给本状态自己的保险丝带出去。
                _brain.StopMoving();
                return;
            }

            Vector3 toTarget = _dashContext.SnapshotPosition - _brain.SelfPosition;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= 1e-8f)
            {
                _brain.StopMoving();
                return;
            }

            _dashDirection = toTarget.normalized;
            _started = true;
        }

        public override BossStateId? OnUpdate(IStateMachineHost<BossStateId> host, float deltaTime)
        {
            if (_brain.IsDead)
            {
                return BossStateId.Dead;
            }

            if (!_started)
            {
                return BossStateId.DashRecovery;
            }

            _elapsed += deltaTime;

            // 表 N04：撞击地图墙体或边界时立即结束 Dash。
            if (_brain.IsWallAhead(_dashDirection, _brain.Config.DashDistance))
            {
                return BossStateId.DashRecovery;
            }

            // 夹住步长，让冲刺精确停在快照位置，绝不冲过头。
            // 少了这句，决策帧的最后一步会越过 Dash Distance。
            float remainingDistance = Vector3.Distance(_brain.SelfPosition, _dashContext.SnapshotPosition);
            if (remainingDistance <= 1e-4f)
            {
                return BossStateId.DashRecovery;
            }

            float step = Mathf.Min(_brain.Config.DashSpeed * deltaTime, remainingDistance);
            Vector3 from = _brain.SelfPosition;
            Vector3 to = from + (_dashDirection * step);

            _brain.MoveToward(to, _brain.Config.DashSpeed, deltaTime);

            // 命中判定针对刚刚走过的这一段，而不是当前位置：
            // Dash Speed 是 24，只用当前位置判定会让冲刺从玩家身上穿过去。
            TryStrikeAlong(from, to);

            // 已经吃满了配置的 Dash Distance。
            if (remainingDistance <= step)
            {
                return BossStateId.DashRecovery;
            }

            // 必有出口：本状态自己的倒计时，独立于几何形状和撞墙检测。
            float maxDashSeconds = _brain.Config.DashTravelDuration;
            if (maxDashSeconds > 0f && _elapsed >= maxDashSeconds)
            {
                return BossStateId.DashRecovery;
            }

            return null;
        }

        public override void OnExit(IStateMachineHost<BossStateId> host)
        {
            _brain.StopMoving();
        }

        private void TryStrikeAlong(Vector3 from, Vector3 to)
        {
            // 表 N04 Dash Hit Count = 1：单次 Dash 最多对玩家造成一次伤害。
            // 从配置读而不是写死 1，这样以后调平衡时这里会自动跟上。
            if (_strikeCount >= _brain.Config.DashHitCountClamped)
            {
                return;
            }

            // 玩家是移动目标，所以按「刚走过的那一段」算接近程度，而不是只算当前点。
            float distanceToSegment = DistancePointToSegment(_brain.PlayerPosition, from, to);
            if (distanceToSegment > _hitRadius)
            {
                return;
            }

            if (!_brain.SubmitPlayerDamage(_brain.Config.DashDamage, AttackKind.Dash))
            {
                // 没送达：不要把允许的命中次数浪费在一个没交出去的请求上。
                return;
            }

            _strikeCount++;
            AcceptedStrikeCount++;
            _dashContext.MarkHitPlayer();
        }

        // 水平面上的点到线段最短距离。
        private static float DistancePointToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            Vector3 ap = point - a;
            ab.y = 0f;
            ap.y = 0f;

            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 1e-8f)
            {
                return ap.magnitude;
            }

            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / lengthSquared);
            Vector3 closest = a + (ab * t);
            Vector3 delta = point - closest;
            delta.y = 0f;
            return delta.magnitude;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：DashRecovery。冲刺结束后的恢复 0.6 s（表 N04）。
    //
    // Dash Cooldown 是「每次冲刺结束后的冷却时间」，所以冷却时钟在这里、在恢复开始的这一刻启动，
    // 不是在冲刺开始时，也不是在命中时。
    //
    // 恢复期不造成伤害：表 N04 规定 Normal Contact Damage = 0，而恢复不是 Dash。
    // --------------------------------------------------------------------------------------------
    public sealed class BossDashRecoveryState : StateBase<BossStateId>
    {
        private readonly BossBrain _brain;
        private readonly BossDashThrottle _dashThrottle;

        public BossDashRecoveryState(BossBrain brain, BossDashThrottle dashThrottle)
        {
            _brain = brain;
            _dashThrottle = dashThrottle;
        }

        public override BossStateId Id => BossStateId.DashRecovery;
        public override string DisplayName => "DashRecovery 冲刺恢复";

        public override void OnEnter(IStateMachineHost<BossStateId> host)
        {
            _brain.StopMoving();

            // 节流只在这件事真正发生的唯一一处被写。
            _dashThrottle.StartCooldown();
        }

        public override BossStateId? OnUpdate(IStateMachineHost<BossStateId> host, float deltaTime)
        {
            if (_brain.IsDead)
            {
                return BossStateId.Dead;
            }

            if (host.TimeInState >= _brain.Config.DashRecoveryTime)
            {
                // 恢复结束回到范围判定。选哪个由事实（ShouldApproach）决定，
                // 而不是靠「记住冲刺是从哪里开始的」。
                return _brain.ShouldApproach ? BossStateId.Approach : BossStateId.Wander;
            }

            return null;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：Dead。设计上就是终态 —— 唯一没有出边的 Boss 状态。
    //
    // 表 N04 与 Architecture 第 5 节：Boss 死亡进入结算，所以行为在这里停止，不再做任何决策。
    // 「Boss 死亡与结算先后」的排序是流程的事，不是本状态的事，因此它只停止并上报一次。
    //
    // OnExit 依然完整实现：池化实例迟早会被强制离开，收尾不能依赖走到某条转移。
    // --------------------------------------------------------------------------------------------
    public sealed class BossDeadState : StateBase<BossStateId>
    {
        private readonly BossBrain _brain;
        private readonly BossDashThrottle _dashThrottle;
        private readonly BossDashContext _dashContext;

        private bool _deathReported;

        public BossDeadState(BossBrain brain, BossDashThrottle dashThrottle, BossDashContext dashContext)
        {
            _brain = brain;
            _dashThrottle = dashThrottle;
            _dashContext = dashContext;
        }

        public override BossStateId Id => BossStateId.Dead;
        public override string DisplayName => "Dead 死亡";

        // 终态：布置期审计不该向它要出口。
        public override bool IsTerminal => true;

        public override void OnEnter(IStateMachineHost<BossStateId> host)
        {
            // OnEnter 只做拆解、绝不发起转移，否则 Enter/Exit 会递归嵌套。
            _brain.SetMovementEnabled(false);

            // 死掉的 Boss 不许把半格冷却和过期快照带进下一条命。
            _dashThrottle.Reset();
            _dashContext.Reset();

            ReportDeathOnce();
        }

        public override BossStateId? OnUpdate(IStateMachineHost<BossStateId> host, float deltaTime)
        {
            ReportDeathOnce();
            return null;
        }

        public override void OnExit(IStateMachineHost<BossStateId> host)
        {
            // 为下一条命重新武装。移动能力由 Initialize 重新打开。
            _deathReported = false;
        }

        public bool HasReportedDeath => _deathReported;

        private void ReportDeathOnce()
        {
            // 一次性死亡通知：每条命只上报一次，避免进入两次结算。
            if (_deathReported)
            {
                return;
            }

            _deathReported = true;
            _brain.ReportDeath();
        }
    }
}
