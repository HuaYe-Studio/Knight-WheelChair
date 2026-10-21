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
    // 已选定的语义（与实现一致，勿再按旧注释理解）：
    //   **每次重新接触都立即打一次。** OnEnter 里会 _throttle.Reset()（回到「就绪」），
    //   紧接着 TryStrike() 提交首击。因此「贴上 → 离开 → 再贴上」会给一次新的首击，
    //   而不是沿用上一次的冷却残留。
    //   这是 Architecture 第 5 节「首次有效接触立即提交首击」的直接延伸：
    //   「重新接触」被当作一次新的有效接触。
    //
    // 仍待确认（Architecture 第 5 节「持续接触节奏/重接触重置待确认」）：
    //   若策划希望「离开接触不清零、重新贴上不白送一次攻击」，则删除 OnEnter 里的
    //   _throttle.Reset() 即可（一行），此时节流器会保留上次的冷却进度。
    // --------------------------------------------------------------------------------------------
    public sealed class Enemy1ContactAttackState : StateBase<Enemy1StateId>
    {
        private readonly Enemy1Brain _brain;
        private readonly Enemy1AttackThrottle _throttle;

        private bool _struckThisEntry;

        public Enemy1ContactAttackState(Enemy1Brain brain, Enemy1AttackThrottle throttle)
        {
            _brain = brain;
            _throttle = throttle;
        }

        public override Enemy1StateId Id => Enemy1StateId.ContactAttack;
        public override string DisplayName => "ContactAttack 接触攻击";

        public override void OnEnter(IStateMachineHost<Enemy1StateId> host)
        {
            // 自己的状态数据在自己这里复位，绝不在构造函数里：
            // 池化实例会重新进入本状态，不能继承上一个使用者的阶段。
            _struckThisEntry = false;

            // 回到「就绪」：重新接触视为一次新的有效接触，因此紧接着的首击立即生效。
            // 这条是已选定的语义（见类注释）；若要改成「不白送重置」，删掉这一行即可。
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

            return null;
        }

        public override void OnExit(IStateMachineHost<Enemy1StateId> host)
        {
            // 这里不碰节流器：离开接触时只停止计时（Tick 只在 OnUpdate 里被调用），
            // 但不清零也不重置 —— 重置只发生在 OnEnter，语义是「新的接触开始了」。
            // 两个位置各管一件事，改语义时只需动 OnEnter 那一行（见类注释）。
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
