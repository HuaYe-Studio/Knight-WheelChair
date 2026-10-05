using UnityEngine;
using KWC.Core;

namespace KWC.Enemy
{
    // ============================================================================================
    // Enemy1 的大脑：本文件把「事实读取 + 节流 + 伤害提交」集中收成一个窄接口，
    // 供 Enemy1States.cs 里的三个状态使用。
    //
    // 为什么这样分：状态需要用到控制器和事实层，如果每个状态各自持有 5 个依赖，
    // 构造函数会长得难读、增删依赖要改所有状态。收成一个 Brain 之后，状态只看这一份门面，
    // 状态与外部世界的耦合面只剩「读事实 / 发请求」这一组方法。
    //
    // Brain 本身不持有状态，也不决定转移：转移由状态返回、由状态机执行。
    // ============================================================================================
    public sealed class Enemy1Brain
    {
        private readonly Enemy1Controller _owner;
        private readonly IPlayerContext _player;
        private readonly IMovementHandler _movement;
        private readonly IContactSource _contact;
        private readonly IFactHealthSource _health;
        private readonly ICombatBridge _bridge;
        private readonly float _contactRange;

        // 死亡闸门属于「这条命」，所以放在 Brain（每条命一个），而不是放在 Dead 状态里。
        // 放在状态里就只能靠 OnExit 重新武装，而 OnExit 与「下一条命开始」并不总是严格配对。
        private readonly DeathReportLatch _deathLatch;

        public Enemy1Brain(Enemy1Controller owner, IPlayerContext player, IMovementHandler movement,
            IContactSource contact, IFactHealthSource health, ICombatBridge bridge,
            DeathReportLatch deathLatch,
            float contactRange, float moveSpeed, float contactAttackDamage, bool stopWhileAttacking)
        {
            _owner = owner;
            _player = player;
            _movement = movement;
            _contact = contact;
            _health = health;
            _bridge = bridge;
            _deathLatch = deathLatch;
            _contactRange = contactRange;
            MoveSpeed = moveSpeed;
            ContactAttackDamage = contactAttackDamage;
            StopWhileAttacking = stopWhileAttacking;
        }

        // 表 N03 的 E1 Movement = 10，由配置注入，本文件不写死数字。
        public float MoveSpeed { get; }

        // 表 N03 的 E1 Attack，逐波由表 N12 提供，所以是运行时注入的值。
        public float ContactAttackDamage { get; }

        // dev：接触攻击时是否停下。GDD 未规定，默认停下以减少穿插。
        public bool StopWhileAttacking { get; }

        // ---- 事实（只读）------------------------------------------------------------------

        public bool HasPlayer => _player != null && _player.EntityTransform != null;

        public Vector3 PlayerPosition => _player.Position;

        public float DistanceToPlayer
        {
            get
            {
                Vector3 delta = PlayerPosition - _movement.Position;
                delta.y = 0f;
                return delta.magnitude;
            }
        }

        // 环境事实，归物理所有：玩家现在是否碰到我。
        public bool IsTouchingPlayer => _contact != null && _contact.IsInContact;

        // 进度事实，归 Combat Health 所有：我是不是已经死了。
        public bool IsDead => _health != null && !_health.IsAlive;

        // 窗口判定（不是纯事实）：实体此刻与玩家的交互关系。它是这一对阈值的唯一定义处，
        // 所以状态里不会出现裸写「距离 < x」的比较，滞回也只有一个家。
        public bool IsStillInContactRange => IsTouchingPlayer || DistanceToPlayer <= _contactRange;

        // 命名判定：把复合条件收成一句可读、可单独测试的话。
        // 散在 if 里的 a && !b && c 漏一个取反，就是一次死锁。
        public bool CanAttack => !IsDead && HasPlayer && DistanceToPlayer <= _contactRange;

        // ---- 执行（状态唯一能做的两件事）--------------------------------------------------

        public void MoveTowardPlayer(float deltaTime)
        {
            _movement.MoveToward(PlayerPosition, MoveSpeed, deltaTime);
        }

        public void StopMoving()
        {
            _movement.Stop();
        }

        public void SetMovementEnabled(bool enabled)
        {
            _movement.SetMovementEnabled(enabled);
        }

        // 提交伤害请求。返回 true 只表示请求已交出，不代表玩家掉血（结果归 Combat）。
        public bool SubmitPlayerDamage(float damage, AttackKind kind)
        {
            PlayerDamageRequest request = new PlayerDamageRequest(_owner, damage, PlayerPosition, kind);
            return _bridge.TrySubmitPlayerDamage(request);
        }

        // 这条命是否还没上报过死亡。只读，供调试面板与测试断言使用。
        public bool DeathReportArmed => _deathLatch == null || _deathLatch.IsArmed;

        // 上报死亡。由 Dead 状态调用，但真正的去重在这里：
        // 每条命只有第一次调用会真的送出，并且带上生命编号。
        // 这样即使某个回收路径没走 OnExit、或者同一个状态被重进，也不会重复通知。
        public bool TryClaimDeathReport(out int lifeId)
        {
            if (!_deathLatch.TryClaimDeathReport(out lifeId))
            {
                return false;
            }

            _bridge.NotifyEnemyDied(_owner, false, lifeId);
            return true;
        }
    }

    // ============================================================================================
    // 节流层：接触攻击的「重复间隔计时器」。
    //
    // 它既不是「世界是什么」，也不是「我在干什么」，它只回答「允不允许现在再打一次」。
    // 所以它单独存字段、单独用时钟，与状态无关。
    //
    // E1 Attack Interval = 100 / E1 Attack Speed（表 N03）。秒数由控制器推导好传进来，
    // 本类不读配置、也不自己造一个速率。
    //
    // 计时入口只有一个：Enemy1ContactAttackState 的 OnUpdate 负责 Tick。
    // 控制器**不**再对这个计时器推进 —— 之前两边都 Tick，
    // 配置 1 秒的间隔实际按 0.5 秒走，攻击频率翻倍。
    // ============================================================================================
    public sealed class Enemy1AttackThrottle
    {
        private readonly float _intervalSeconds;
        private float _elapsed;
        private bool _hasAttacked;

        public Enemy1AttackThrottle(float intervalSeconds)
        {
            _intervalSeconds = intervalSeconds < 0f ? 0f : intervalSeconds;
            _elapsed = _intervalSeconds;
        }

        public float IntervalSeconds => _intervalSeconds;

        public bool IsReady => _elapsed >= _intervalSeconds;

        public float TimeUntilReady
        {
            get
            {
                float remaining = _intervalSeconds - _elapsed;
                return remaining > 0f ? remaining : 0f;
            }
        }

        public bool HasAttacked => _hasAttacked;

        // 只由接触状态在「仍然接触」时调用。
        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                _elapsed += deltaTime;
            }
        }

        public void MarkAttacked()
        {
            _elapsed = 0f;
            _hasAttacked = true;
        }

        // 回到「可以立刻再打一次」的状态。
        //
        // 语义是「重新开始接触」：E1 首次有效接触立即攻击，所以每次进入接触状态都要回到就绪。
        // 复用（池化）时也用它，避免下一个使用者一上线就处在冷却中间。
        public void Reset()
        {
            _elapsed = _intervalSeconds;
            _hasAttacked = false;
        }
    }
}
