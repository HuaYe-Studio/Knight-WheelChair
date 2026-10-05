namespace KWC.Enemy
{
    // ============================================================================================
    // Enemy1 的状态定义与三个状态。
    //
    // 表 N03：E1 HP / E1 Attack 来自波次表 N12，E1 Movement = 10，E1 Attack Speed = 100。
    // 行为：追击玩家 → 有效接触立即提交首击 → 持续接触按攻击间隔重复 → 死亡停在原地。
    // ============================================================================================

    public enum Enemy1StateId
    {
        None = 0,

        // 正在接近玩家。
        Chase = 1,

        // 已与玩家接触，提交接触伤害。
        ContactAttack = 2,

        // 终态：死亡。唯一没有出口的状态。
        Dead = 3
    }

    // --------------------------------------------------------------------------------------------
    // 状态：Chase。移动到接触为止，自己不做任何伤害决定。
    // 只持有自己的数据（这里什么都没有）。滞回交给状态机，接触判定交给事实层，伤害交给 ContactAttack。
    // --------------------------------------------------------------------------------------------
    public sealed class Enemy1ChaseState : StateBase<Enemy1StateId>
    {
        private readonly Enemy1Brain _brain;

        public Enemy1ChaseState(Enemy1Brain brain)
        {
            _brain = brain;
        }

        public override Enemy1StateId Id => Enemy1StateId.Chase;
        public override string DisplayName => "Chase 追击";

        public override Enemy1StateId? OnUpdate(IStateMachineHost<Enemy1StateId> host, float deltaTime)
        {
            // 顺序就是显式优先级：先看不可逆的死亡，再看接触，最后才是移动。
            // 绝不依赖「谁恰好写在前面」碰巧正确。
            if (_brain.IsDead)
            {
                return Enemy1StateId.Dead;
            }

            if (_brain.IsTouchingPlayer)
            {
                return Enemy1StateId.ContactAttack;
            }

            if (!_brain.HasPlayer)
            {
                // 还没拿到玩家引用：原地待命，而不是猜一个方向。
                _brain.StopMoving();
                return null;
            }

            _brain.MoveTowardPlayer(deltaTime);
            return null;
        }

        public override void OnExit(IStateMachineHost<Enemy1StateId> host)
        {
            _brain.StopMoving();
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：ContactAttack。Enemy1 有效接触立即提交首击（Architecture 第 5 节）。
    //
    // 表 N03 给出 E1 Attack Interval = 100 / E1 Attack Speed 作为「持续接触」的节奏，
    // 所以首击立即、后续按该间隔。
    //
    // 待确认（Architecture 第 5 节「持续接触节奏/重接触重置待确认」）：离开接触时是停止计时还是清零、
    // 重新贴上是否再给一次首击。当前取保守读法：离开接触只停止计时、不清零。它既不白送一次重置，
    // 也不会让计时器在暗处继续跑。规则定下来后，改 OnEnter / OnExit 各一行即可。
    // --------------------------------------------------------------------------------------------
    public sealed class Enemy1ContactAttackState : StateBase<Enemy1StateId>
    {
        private readonly Enemy1Brain _brain;
        private readonly Enemy1AttackThrottle _throttle;
        private readonly float _maxDwellSeconds;

        private bool _struckThisEntry;

        public Enemy1ContactAttackState(Enemy1Brain brain, Enemy1AttackThrottle throttle, float maxDwellSeconds)
        {
            _brain = brain;
            _throttle = throttle;
            _maxDwellSeconds = maxDwellSeconds;
        }

        public override Enemy1StateId Id => Enemy1StateId.ContactAttack;
        public override string DisplayName => "ContactAttack 接触攻击";

        public override void OnEnter(IStateMachineHost<Enemy1StateId> host)
        {
            // 自己的状态数据在自己这里复位，绝不在构造函数里：
            // 池化实例会重新进入本状态，不能继承上一个使用者的阶段。
            _struckThisEntry = false;
            _throttle.Reset();

            if (_brain.StopWhileAttacking)
            {
                _brain.StopMoving();
            }

            // 首击立即提交。放在 OnEnter 而不是塞进 OnUpdate 的某个分支，
            // 这样「首次接触就攻击」不会因为分支书写顺序而被推迟一帧。
            TryStrike();
        }

        public override Enemy1StateId? OnUpdate(IStateMachineHost<Enemy1StateId> host, float deltaTime)
        {
            if (_brain.IsDead)
            {
                return Enemy1StateId.Dead;
            }

            // 退出阈值只在这里定义一次，而且刻意不等于进入条件
            // （进入靠物理接触事件，退出靠 contactRange）。两个不同的值就是滞回，
            // 它阻止实体在 Chase 与 ContactAttack 之间抖动。
            if (!_brain.IsStillInContactRange)
            {
                return Enemy1StateId.Chase;
            }

            _throttle.Tick(deltaTime);
            TryStrike();

            // 必有出口：即使接触永远结束不了（比如碰撞体卡在一起），本状态也不许无限期停留。
            // 这是保险丝，不是玩法规则。
            if (host.TimeInState >= _maxDwellSeconds)
            {
                return Enemy1StateId.Chase;
            }

            return null;
        }

        public override void OnExit(IStateMachineHost<Enemy1StateId> host)
        {
            // 刻意不在这里 _throttle.Reset()：见类注释。
            // 只有进入本状态才重置，所以重新进入拿不到一次免费的攻击间隔。
        }

        public bool HasStruckThisEntry => _struckThisEntry;

        private void TryStrike()
        {
            if (!_throttle.IsReady || !_brain.CanAttack)
            {
                return;
            }

            if (_brain.SubmitPlayerDamage(_brain.ContactAttackDamage, AttackKind.Contact))
            {
                // 只有请求真的交出去了才记一次。桥接拒绝的话，下一个决策帧会重试，
                // 而不是悄悄漏掉一次攻击。
                _throttle.MarkAttacked();
                _struckThisEntry = true;
            }
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态：Dead。设计上就是终态 —— 唯一没有出边的状态。
    //
    // OnExit 依然完整实现：池化对象迟早会被强制离开（波末回收、重开、场景卸载），
    // 收尾不能依赖状态机走到某条转移。
    // --------------------------------------------------------------------------------------------
    public sealed class Enemy1DeadState : StateBase<Enemy1StateId>
    {
        private readonly Enemy1Brain _brain;
        private readonly Enemy1AttackThrottle _throttle;

        public Enemy1DeadState(Enemy1Brain brain, Enemy1AttackThrottle throttle)
        {
            _brain = brain;
            _throttle = throttle;
        }

        public override Enemy1StateId Id => Enemy1StateId.Dead;
        public override string DisplayName => "Dead 死亡";

        // 终态：布置期审计不该向它要出口。
        public override bool IsTerminal => true;

        public override void OnEnter(IStateMachineHost<Enemy1StateId> host)
        {
            // OnEnter 只做拆解，绝不发起转移，否则 Enter/Exit 会递归嵌套
            // （死亡 → 倒地 → 结算 这种级联要避免）。
            _brain.SetMovementEnabled(false);

            // 死掉的实体不许把半格攻击间隔带进下一条命。
            _throttle.Reset();

            ReportDeathOnce();
        }

        public override Enemy1StateId? OnUpdate(IStateMachineHost<Enemy1StateId> host, float deltaTime)
        {
            ReportDeathOnce();
            return null;
        }

        public override void OnExit(IStateMachineHost<Enemy1StateId> host)
        {
            // 这里不再重新武装死亡闸门：闸门属于「这条命」，由控制器在复用/初始化时 Arm()。
            // 之前在这里清标志，等于把「下一条命开始」绑在 OnExit 上，
            // 而池化回收、场景卸载等路径并不保证 OnExit 与下一次 Initialize 严格配对，
            // 结果就是同一条命被上报两次。
        }

        public bool HasReportedDeath => !_brain.DeathReportArmed;

        private void ReportDeathOnce()
        {
            // 一次性死亡通知：每条命只上报一次。去重由 Brain 里的 DeathReportLatch 负责，
            // 本状态只负责「到了 Dead 就该上报」这一件事。
            _brain.TryClaimDeathReport(out _);
        }
    }
}
