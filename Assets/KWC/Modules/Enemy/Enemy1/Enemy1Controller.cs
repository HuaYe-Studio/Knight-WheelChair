using UnityEngine;
using KWC.Core;
using KWC.Data;

namespace KWC.Enemy
{
    // ============================================================================================
    // Enemy1 挂载点。挂在 Enemy1.prefab 上，这是需要拖到 Inspector 的两个组件之一
    // （另一个是 BossController）。
    //
    // 本文件只做三件事：接收接线、按节拍驱动状态机、复用时清状态。
    // 它不持有事实、不持有节流、不决定行为 —— 那些分别在 Enemy1Brain 与各状态里。
    //
    // 接线由 Game System 显式调用 Initialize 注入（Architecture 第 3 节：
    // Unity 不能序列化接口，所以 Owner 用显式 Initialize 接收，不做全局查找服务）。
    // 因此这里刻意没有 Awake、没有 Find、没有 GetComponent、也没有 AddComponent：
    // 控制器一旦能自己造依赖，就没人说得清谁拥有什么了。
    // ============================================================================================
    [DisallowMultipleComponent]
    public sealed class Enemy1Controller : MonoBehaviour, IStateMachineHost<Enemy1StateId>
    {
        [Header("dev：未定的接触几何")]
        [Tooltip("dev：接触范围。表 N03/N04 与 Architecture 都未确定碰撞尺寸与 Collider 类型，" +
                 "此值仅用于「退出接触状态」的滞回判定，不是设计值。")]
        [SerializeField] private float contactRange = 1.2f;

        [Header("dev：决策节流")]
        [Tooltip("dev：决策降频。敌人 AI 不需要每帧想一次，表现层继续使用上一次的决策结果。")]
        [SerializeField] private float decisionInterval = 0.1f;

        [Tooltip("dev：接触状态的兜底最长停留。防止接触永不解触时死锁，属于保险丝而非玩法规则。")]
        [SerializeField] private float contactMaxDwell = 5f;

        [Tooltip("dev：接触攻击时是否停下。GDD 未规定，默认停下以减少穿插。")]
        [SerializeField] private bool stopWhileAttacking = true;

        private ICombatBridge _combatBridge;
        private FactHealthSource _healthSource;

        // 通过接口持有的移动处理器。复位统一走 IMovementHandler.ResetMovement，
        // 而不是缓存具体实现 —— 否则换成别的实现（导航、Rigidbody）复用就失效。
        private IMovementHandler _movement;

        private Enemy1Brain _brain;
        private Enemy1AttackThrottle _attackThrottle;
        private StateMachine<Enemy1StateId> _machine;

        // 死亡闸门属于「这条命」，由控制器在每次复用时 Arm()，
        // 不依赖任何状态的 OnExit 重新武装（那正是重复上报的成因）。
        private readonly DeathReportLatch _deathLatch = new DeathReportLatch();

        private float _decisionAccumulator;
        private bool _isInitialized;
        private float _waveHp;
        private float _waveAttack;

        public bool IsInitialized => _isInitialized;

        // 当前生命编号，严格递增。Combat/流程回调时必须带上它，用于判重与识别迟到回调。
        public int LifeId => _deathLatch.LifeId;

        // 这条命是否还没上报过死亡。供调试面板与测试断言使用。
        public bool DeathReportArmed => _deathLatch.IsArmed;

        // 只读视图，给调试面板和测试用。这里没有任何 setter：
        // 状态只能通过转移改变，事实只能由它的来源改变。
        public StateMachine<Enemy1StateId> Machine => _machine;
        public Enemy1StateId CurrentStateId => _machine != null ? _machine.Current : Enemy1StateId.None;
        public float TimeInState => _machine != null ? _machine.TimeInState : 0f;
        public int TransitionCount => _machine != null ? _machine.TransitionCount : 0;
        public int LiveStateCount => _machine != null ? _machine.LiveStateCount : 0;
        public float DecisionInterval => decisionInterval;
        public Enemy1AttackThrottle AttackThrottle => _attackThrottle;

        // HP 事实视图，交给桥接让 Combat Health 往里报告。
        // 桥接负责写，FSM 只负责读（Architecture 第 4 节：Enemy HP 归 Combat Health）。
        public FactHealthSource HealthFacts => _healthSource;

        // 表 N03：E1 HP 与 E1 Attack 见表 N12，由波次提供。它们在这里成为这条命的数值；
        // Enemy1Config 刻意不重复存这两项。
        public void Initialize(IPlayerContext playerContext, IMovementHandler movement,
            IContactSource contactSource, ICombatBridge combatBridge, Enemy1Config config,
            FactHealthSource healthSource, float waveHp, float waveAttack)
        {
            _combatBridge = combatBridge;
            _healthSource = healthSource;
            _waveHp = waveHp;
            _waveAttack = waveAttack;

            if (movement == null || contactSource == null || combatBridge == null || healthSource == null)
            {
                Debug.LogError("[Enemy] Enemy1Controller.Initialize 收到了 null 依赖" +
                               "（movement / contactSource / combatBridge / healthSource）。" +
                               "缺任何一个状态机都跑不起来。");
                return;
            }

            _movement = movement;

            // 表 N03：E1 Attack Interval = 100 / E1 Attack Speed。
            // 在这里推导一次，并且不再另存一个独立可改的值（Architecture 第 4 节）。
            float attackSpeed = config != null ? config.E1AttackSpeed : 100f;
            float attackIntervalSeconds = attackSpeed > 0f ? 100f / attackSpeed : 0f;
            float moveSpeed = config != null ? config.E1Movement : 0f;

            _brain = new Enemy1Brain(this, playerContext, movement, contactSource, healthSource,
                combatBridge, _deathLatch, contactRange, moveSpeed, _waveAttack, stopWhileAttacking);

            _attackThrottle = new Enemy1AttackThrottle(attackIntervalSeconds);

            _machine = BuildMachine();
            _isInitialized = true;

            ResetForReuse();
        }

        // 池化契约（Architecture 第 10 节）：属于「这一条命」的状态全部在这里清。
        // PrefabPool 会先调用 Initialize 再激活实例，所以 OnEnable 读到的一定是本轮数据。
        public void ResetForReuse()
        {
            if (!_isInitialized)
            {
                return;
            }

            // 先清事实：复用实例不能一出生就是死的，否则 FSM 会立刻进 Dead。
            _healthSource.ResetForReuse(_waveHp);
            _attackThrottle.Reset();

            // 重新武装死亡闸门并推进生命编号。放在这里而不是 Dead 状态的 OnExit，
            // 因为「下一条命开始」的正确标志是复用/初始化，不是某次状态离开。
            _deathLatch.Arm();

            if (_movement != null)
            {
                // 走接口而不是具体类型：任何 IMovementHandler 实现都必须能正确复位。
                // 之前只复位 SimpleMovementHandler，换成别的实现后死亡再复用仍然不能动。
                _movement.ResetMovement(Vector3.zero);
            }

            _decisionAccumulator = 0f;
            _machine.ResetToInitial("复用时重置");
        }

        // 决策节拍。Enemy1 用自己的 Tick，而不是把逻辑塞进 MonoBehaviour 的 Update。
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        // 累加真实时间，够一个决策间隔才醒一次。省性能只是顺带，
        // 真正的好处是行为一致：每帧决策更容易被单帧噪声带偏，出现「刚决定追击又立刻改主意」。
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

            // 注意：这里**不**推进 _attackThrottle。
            // 攻击间隔的唯一计时入口是 Enemy1ContactAttackState.OnUpdate —— 让节流只在
            // 「仍然接触」时走，语义才正确（离开接触就不该继续充能）。
            // 之前这里也 Tick 一次，等于把配置 1 秒的间隔变成 0.5 秒。
            _machine.Update(step);
        }

        // 状态请求转移的通道，如实上报失败原因。
        // 它只做转发不做判断：合法性、滞回、一帧一次全部归状态机所有，
        // 控制器如果复制一份，就会出现两个互相矛盾的答案。
        public bool TryRequestTransition(Enemy1StateId next, string reason)
        {
            return _isInitialized && _machine.TryRequestTransition(next, reason);
        }

        // 由 Combat Health 经桥接调用，用于同步 HP 事实（只更新血量，不推断死亡）。
        //
        // 这里刻意**不再**用「血量 <= 0」去触发 Dead 转移：推断会重复触发，
        // 因为零血时的每一次重复上报都会命中这个条件。死亡改由 ReportEnemyDied 显式通知。
        public void OnHealthReported(float current, float max)
        {
            if (!_isInitialized)
            {
                return;
            }

            _healthSource.ReportHealth(current, max);
        }

        // 由 Combat Health 经桥接调用：这个敌人死了。lifeId 用来识别迟到的跨生命回调。
        //
        // 返回 true 表示本次通知被接受并已转发给状态机（走事件打断通道，不受节流门槛限制）。
        // 返回 false 表示 lifeId 不是当前这条命 —— 池化对象已经被复用，
        // 这条通知属于上一条命的旧回调，必须拒绝，否则新一轮实例会被立刻判死。
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

            _machine.OnHostileInterrupt(Enemy1StateId.Dead, "Combat 通知血量归零");
            return true;
        }

        private void OnDisable()
        {
            // 池化：关掉实例时把状态对收平，让活跃状态计数回到零，
            // 并丢掉半格决策时间，避免被回收的实例把这段时间带进下一条命。
            if (!_isInitialized || _machine == null)
            {
                return;
            }

            _machine.ResetToInitial("已禁用");
            _decisionAccumulator = 0f;
        }

        private StateMachine<Enemy1StateId> BuildMachine()
        {
            // 转移表就是状态图的数据形态：运行期是拦非法跳转的保险丝，Review 时它就是那张图。
            TransitionRule<Enemy1StateId>[] rules =
            {
                new TransitionRule<Enemy1StateId>(Enemy1StateId.Chase, Enemy1StateId.ContactAttack, 1),
                new TransitionRule<Enemy1StateId>(Enemy1StateId.ContactAttack, Enemy1StateId.Chase, 1),
                new TransitionRule<Enemy1StateId>(Enemy1StateId.Chase, Enemy1StateId.Dead, 2),
                new TransitionRule<Enemy1StateId>(Enemy1StateId.ContactAttack, Enemy1StateId.Dead, 2)
            };

            StateMachine<Enemy1StateId> machine = new StateMachine<Enemy1StateId>();
            machine.Initialize(Enemy1StateId.Chase,
                new StateBase<Enemy1StateId>[]
                {
                    new Enemy1ChaseState(_brain),
                    new Enemy1ContactAttackState(_brain, _attackThrottle, contactMaxDwell),
                    new Enemy1DeadState(_brain, _attackThrottle)
                },
                rules);

            // 必有出口审计：Dead 是唯一终态，其他状态都必须能出去。
            foreach (Enemy1StateId stateId in machine.RegisteredStates)
            {
                if (stateId == Enemy1StateId.Dead)
                {
                    continue;
                }

                if (!machine.HasOutgoingEnabledRule(stateId))
                {
                    Debug.LogError("[Enemy] 状态 '" + stateId + "' 没有声明任何出去的转移。" +
                                   "每个非终态都必须有出口。");
                }
            }

            return machine;
        }
    }
}
