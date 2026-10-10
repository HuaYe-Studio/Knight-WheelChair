using UnityEngine;
using KWC.Core;
using KWC.Data;

namespace KWC.Enemy
{
    // ============================================================================================
    // Boss 轮椅骑士挂载点。挂在 BossKnightWheelChair.prefab 上，
    // 这是需要拖到 Inspector 的两个组件之一（另一个是 Enemy1Controller）。
    //
    // 与 Enemy1Controller 同样的契约：只做接线、驱动节拍、复用时清状态；
    // 不持有事实、不持有节流、不决定行为。
    //
    // 全部数值来自 BossConfig（表 N04）。下面带 dev 的字段是设计源没有定义的值，
    // 每一个都登记在 Modules/Enemy/README.md 的待确认清单里。
    // ============================================================================================
    [DisallowMultipleComponent]
    public sealed class BossController : MonoBehaviour, IStateMachineHost<BossStateId>
    {
        [Header("dev：决策节流")]
        [Tooltip("dev：决策降频。Boss 不需要每帧想一次，表现层继续使用上一次的决策结果。")]
        [SerializeField] private float decisionInterval = 0.1f;

        [Header("dev：未定的几何与兜底")]
        [Tooltip("dev：Wander 状态的兜底最长停留。由表 N04 的 Wander Interval 1~2 s 推导，不是设计值。")]
        [SerializeField] private float wanderMaxDwell = 10f;

        [Tooltip("dev：Dash 命中玩家的判定半径。接触几何未确认，此值不是设计值。")]
        [SerializeField] private float dashHitRadius = 1.5f;

        [Tooltip("dev：IWallCheck 仍为占位实现时是否报警。撞墙规则由 Map 提供，接口未定。")]
        [SerializeField] private bool warnOnStubWallCheck = true;

        private FactHealthSource _healthSource;

        // 撞墙查询是注入进来的事实来源，控制器只保留引用用于诊断显示。
        // 真正的读操作由 BossBrain 转发给状态，控制器不参与决策。
        private IWallCheck _wallCheck;

        // 通过接口持有的移动处理器。复位统一走 IMovementHandler.ResetMovement，
        // 而不是缓存具体实现 —— 否则换成别的实现（导航、Rigidbody）复用就失效。
        private IMovementHandler _movement;

        private BossBrain _brain;
        private BossDashThrottle _dashThrottle;
        private BossDashContext _dashContext;

        // 闲逛取点源：状态需要它，但它既不是事实也不是节流，所以由控制器在 BuildMachine 之前
        // 备好并传给 BossWanderState。
        private IWanderPointSource _wanderPointSource;

        private StateMachine<BossStateId> _machine;

        // 死亡闸门属于「这条命」，由控制器在每次复用时 Arm()。
        private readonly DeathReportLatch _deathLatch = new DeathReportLatch();

        private float _decisionAccumulator;
        private bool _isInitialized;
        private float _waveHp;

        public bool IsInitialized => _isInitialized;

        // 当前生命编号，严格递增。Combat/流程回调时必须带上它，用于判重与识别迟到回调。
        public int LifeId => _deathLatch.LifeId;

        // 这条命是否还没上报过死亡。供调试面板与测试断言使用。
        public bool DeathReportArmed => _deathLatch.IsArmed;

        public StateMachine<BossStateId> Machine => _machine;
        public BossStateId CurrentStateId => _machine != null ? _machine.Current : BossStateId.None;
        public float TimeInState => _machine != null ? _machine.TimeInState : 0f;
        public int TransitionCount => _machine != null ? _machine.TransitionCount : 0;
        public int LiveStateCount => _machine != null ? _machine.LiveStateCount : 0;
        public float DecisionInterval => decisionInterval;
        public BossDashThrottle DashThrottle => _dashThrottle;
        public BossDashContext DashContext => _dashContext;

        // 调试面板用它显示「撞墙检测还没接线」这类未完成能力。
        // 判据放在这里而不是 Brain 上：这是接线状况的诊断，不是 Boss 的行为事实。
        public bool IsWallCheckStub => _wallCheck is NoWallCheck;

        public FactHealthSource HealthFacts => _healthSource;

        // 表 N04：Boss HP 7500 是基础值，实际总量仍可由波次提供，
        // 与 E1 HP 走表 N12 的方式一致。两者都注入进来，让事实来源留在 FSM 之外。
        public void Initialize(IPlayerContext playerContext, IMovementHandler movement, ICombatBridge combatBridge,
            IWallCheck wallCheck, IWanderPointSource wanderPointSource, BossConfig config,
            FactHealthSource healthSource, float waveHp, int wanderRandomSeed)
        {
            _healthSource = healthSource;
            _waveHp = waveHp;

            if (movement == null || combatBridge == null || config == null || healthSource == null)
            {
                Debug.LogError("[Enemy] BossController.Initialize 收到了 null 依赖" +
                               "（movement / combatBridge / config / healthSource）。状态机跑不起来。");
                return;
            }

            if (wallCheck == null)
            {
                // 不静默兜底：缺失的能力被显式创建并公告，
                // 这样「撞墙结束 Dash」不会看起来已经生效其实什么都没做。
                wallCheck = new NoWallCheck();
            }

            if (wanderPointSource == null)
            {
                wanderPointSource = new RandomWanderPointSource(wanderRandomSeed);
            }

            BossConfigValues values = config.ToValues();
            if (values == null)
            {
                Debug.LogError("[Enemy] BossConfig.ToValues() 返回 null；Boss 状态机需要表 N04 的数值。");
                return;
            }

            _movement = movement;
            _wanderPointSource = wanderPointSource;
            _wallCheck = wallCheck;
            _brain = new BossBrain(this, playerContext, movement, combatBridge, wallCheck,
                _deathLatch, values);
            _dashThrottle = new BossDashThrottle(values.DashCooldown);
            _dashContext = new BossDashContext();

            _machine = BuildMachine();
            _isInitialized = true;

            if (warnOnStubWallCheck && wallCheck is NoWallCheck)
            {
                Debug.LogWarning("[Enemy] Boss 撞墙检测尚未接线：IWallCheck 是 NoWallCheck 占位实现，" +
                                 "表 N04 的 'Wall -> Stop' 无法触发。等 Map 的通行性查询。");
            }

            ResetForReuse();
        }

        // 池化契约（Architecture 第 10 节）：目标、冷却、Dash 快照与命中记录在交给下一个使用者前清掉。
        public void ResetForReuse()
        {
            if (!_isInitialized)
            {
                return;
            }

            _healthSource.ResetForReuse(_waveHp);
            _dashThrottle.Reset();

            // 快照与命中记录属于上一次冲刺的两个不同事实，两个都要清。
            _dashContext.Reset();

            // 重新武装死亡闸门并推进生命编号。放在这里而不是 Dead 状态的 OnExit，
            // 因为「下一条命开始」的正确标志是复用/初始化，不是某次状态离开。
            _deathLatch.Arm();

            if (_movement != null)
            {
                // 走接口而不是具体类型：任何 IMovementHandler 实现都必须能正确复位。
                _movement.ResetMovement(Vector3.zero);
            }

            _decisionAccumulator = 0f;
            _machine.ResetToInitial("复用时重置");
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        // 与 Enemy1 同样的决策降频。Boss 的行为不应该被单帧噪声带偏。
        // 公开是为了让测试可以不依赖 Unity 直接喂 deltaTime。
        public void Tick(float deltaTime)
        {
            if (!_isInitialized)
            {
                return;
            }

            float interval = Mathf.Max(0.01f, decisionInterval);
            _decisionAccumulator += deltaTime;

            if (_decisionAccumulator < interval)
            {
                return;
            }

            float step = _decisionAccumulator;
            _decisionAccumulator = 0f;

            // 冷却在决策时钟上走，暂停或 timeScale 变化都不会让它和状态不同步。
            _dashThrottle.Tick(step);

            // ---- 冲刺起手判断：统一放在状态机之前 ------------------------------------------
            //
            // 不放在某个状态里，是因为「冲刺该不该起手」与「我现在是 Approach 还是 Wander」
            // 无关，只与冷却和距离有关。放进某个状态就会出现这样的死角：
            // Wander 里 ShouldApproach 优先于冲刺判断，玩家一超出 PreferredDistance
            // 就转回 Approach，导致冲刺分支永远不可达（实测 Boss 完全不冲刺）。
            //
            // 放在这里之后，Approach 与 Wander 都能起手冲刺，不存在顺序问题。
            // 转移申请表里必须同时声明 Approach → DashPrepare 与 Wander → DashPrepare 两条边，
            // 否则状态机仍会把这次转移判为非法。
            if (_brain != null && _brain.CanStartDash(_dashThrottle) &&
                (_machine.Current == BossStateId.Approach || _machine.Current == BossStateId.Wander))
            {
                _machine.TryRequestTransition(BossStateId.DashPrepare, "冲刺冷却就绪");
            }

            _machine.Update(step);
        }

        // 状态请求转移的通道。只转发不做判断：合法性、滞回、一帧一次都归状态机所有。
        public bool TryRequestTransition(BossStateId next, string reason)
        {
            return _isInitialized && _machine.TryRequestTransition(next, reason);
        }

        // 由 Combat Health 经桥接调用，用于同步 HP 事实（只更新血量，不推断死亡）。
        // 死亡改由 ReportEnemyDied 显式通知，理由见 Enemy1Controller 的同类注释。
        public bool OnHealthReported(int lifeId, float current, float max)
        {
            if (!_isInitialized || lifeId != _deathLatch.LifeId)
            {
                return false;
            }

            _healthSource.ReportHealth(current, max);
            return true;
        }

        // 由 Combat Health 经桥接调用：Boss 死了。lifeId 用来识别迟到的跨生命回调。
        // 返回 false 表示 lifeId 不是当前这条命，调用方应忽略这次通知。
        public bool ReportEnemyDied(int lifeId)
        {
            if (!_isInitialized)
            {
                return false;
            }

            if (lifeId != _deathLatch.LifeId)
            {
                Debug.LogWarning("[Enemy] 拒绝了过期的死亡通知：收到的 lifeId=" + lifeId +
                                 "，当前 lifeId=" + _deathLatch.LifeId +
                                 "。这属于上一条命的迟到回调。");
                return false;
            }

            _machine.OnHostileInterrupt(BossStateId.Dead, "Combat 通知血量归零");
            return true;
        }

        private void OnDisable()
        {
            if (!_isInitialized || _machine == null)
            {
                return;
            }

            _machine.ResetToInitial("已禁用");
            _decisionAccumulator = 0f;
        }

        private StateMachine<BossStateId> BuildMachine()
        {
            // 表 N04 的状态图，写进数据里。
            //
            // 注意这里没有的东西：没有从 Wander 直接到 Dash 的边。
            // 冲刺永远走 Prepare -> Dash -> Recovery，所以快照总在一个确定的时刻锁定、
            // 冷却总在一个确定的时刻启动。
            TransitionRule<BossStateId>[] rules =
            {
                new TransitionRule<BossStateId>(BossStateId.Approach, BossStateId.Wander, 1),

                // 冲刺可以从 Approach 直接起手。缺少这条边时状态机会拒绝该转移并报「非法转移」，
                // 这是「Boss 永远不冲刺」的第二个原因 —— 即使状态请求了，转移表也会拦下。
                new TransitionRule<BossStateId>(BossStateId.Approach, BossStateId.DashPrepare, 2),
                new TransitionRule<BossStateId>(BossStateId.Wander, BossStateId.Approach, 1),
                new TransitionRule<BossStateId>(BossStateId.Wander, BossStateId.DashPrepare, 2),
                new TransitionRule<BossStateId>(BossStateId.DashPrepare, BossStateId.Dash, 1),
                new TransitionRule<BossStateId>(BossStateId.DashPrepare, BossStateId.Approach, 2),
                new TransitionRule<BossStateId>(BossStateId.Dash, BossStateId.DashRecovery, 1),
                new TransitionRule<BossStateId>(BossStateId.DashRecovery, BossStateId.Approach, 1),
                new TransitionRule<BossStateId>(BossStateId.DashRecovery, BossStateId.Wander, 2),

                // 血量归零 → 停行为 → 进入 Dead，从每个非终态都合法。
                new TransitionRule<BossStateId>(BossStateId.Approach, BossStateId.Dead, 3),
                new TransitionRule<BossStateId>(BossStateId.Wander, BossStateId.Dead, 3),
                new TransitionRule<BossStateId>(BossStateId.DashPrepare, BossStateId.Dead, 3),
                new TransitionRule<BossStateId>(BossStateId.Dash, BossStateId.Dead, 3),
                new TransitionRule<BossStateId>(BossStateId.DashRecovery, BossStateId.Dead, 3)
            };

            StateMachine<BossStateId> machine = new StateMachine<BossStateId>();
            machine.Initialize(BossStateId.Approach,
                new StateBase<BossStateId>[]
                {
                    new BossApproachState(_brain),
                    new BossWanderState(_brain, _wanderPointSource, wanderMaxDwell),
                    new BossDashPrepareState(_brain, _dashContext),
                    new BossDashState(_brain, _dashContext, dashHitRadius),
                    new BossDashRecoveryState(_brain, _dashThrottle),
                    new BossDeadState(_brain, _dashThrottle, _dashContext)
                },
                rules);

            // 必有出口审计：Dead 是唯一终态。
            foreach (BossStateId stateId in machine.RegisteredStates)
            {
                if (stateId == BossStateId.Dead)
                {
                    continue;
                }

                if (!machine.HasOutgoingEnabledRule(stateId))
                {
                    Debug.LogError("[Enemy] Boss 状态 '" + stateId + "' 没有声明任何出去的转移。" +
                                   "每个非终态都必须有出口。");
                }
            }

            // 可达性审计：进不去的状态是死代码，通常代表某条边写反了。
            foreach (BossStateId stateId in machine.RegisteredStates)
            {
                if (stateId == BossStateId.Approach)
                {
                    continue;
                }

                if (!machine.HasIncomingEnabledRule(stateId))
                {
                    Debug.LogError("[Enemy] Boss 状态 '" + stateId + "' 不可达：没有任何转移能进入它。");
                }
            }

            return machine;
        }
    }
}
