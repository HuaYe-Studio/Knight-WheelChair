namespace KWC.Enemy
{
    // ============================================================================================
    // HP 事实与死亡上报闸门。
    //
    // 两个类放在一起，因为它们是同一个关注点：某一条命的血量与「这条命已经上报过死亡了吗」。
    // 而且两者都是纯逻辑（不依赖引擎），可以脱离 Unity 单独测试。
    // ============================================================================================

    // --------------------------------------------------------------------------------------------
    // HP 事实的只读视图。
    //
    // 它自己不拥有 HP：Combat 的 Health 组件是唯一所有者（Architecture 第 4 节
    // 「Enemy HP / Boss HP」）。这里只是一份只读视图，让 FSM 既能读、又不会存出第二份可改的 HP。
    //
    // 事实契约：唯一的写入方是事实来源（Combat Health，经桥接调用 ReportHealth）。
    // 任何状态和控制器都不许直接给 Current 赋值。
    // --------------------------------------------------------------------------------------------
    public interface IFactHealthSource
    {
        float Current { get; }
        float Max { get; }
        bool IsAlive { get; }
    }

    public sealed class FactHealthSource : IFactHealthSource
    {
        private float _current;
        private float _max;

        public float Current => _current;
        public float Max => _max;
        public bool IsAlive => _current > 0f;

        public FactHealthSource(float initialMax)
        {
            _max = initialMax;
            _current = initialMax;
        }

        // 由桥接在 Combat Health 报告变化时调用。
        //
        // 这里刻意不推断「死亡」：本类不知道 Combat 是否已经扣过血，也不知道 max 传得对不对，
        // 所以它只更新事实。死亡是事件，由 Combat 明确通知（ReportEnemyDied），
        // 用「血量 <= 0」去猜会重复触发 —— 零血时每一次重复上报都会命中。
        public void ReportHealth(float current, float max)
        {
            _max = max;
            _current = current;
        }

        // 池化：复用实例时是一条全新的命。
        // 这里不复位任何死亡状态，因为死亡闸门由 DeathReportLatch 单独持有并从外部重新武装。
        public void ResetForReuse(float max)
        {
            _max = max;
            _current = max;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 死亡上报闸门。
    //
    // 为什么单独一个类：这条规则的唯一正确位置是「属于这条命的状态」，而不是「属于某个状态类」。
    // 之前把闩锁放在 Dead 状态里，就只能靠 OnExit 重新武装，而池化回收、场景卸载等路径
    // 并不保证 OnExit 与「下一条命开始」严格配对，于是同一条命被上报两次。
    //
    // 契约：
    //   - 每次复用（新的一条命）由控制器显式 Arm()，不依赖任何 OnExit。
    //   - TryClaimDeathReport() 只在第一次调用时返回 true 并带上生命编号。
    //   - 上报必须带上 LifeId，流程据此判断这是不是当前这条命，迟到的旧上报会被拒绝。
    // --------------------------------------------------------------------------------------------
    public sealed class DeathReportLatch
    {
        private bool _armed;
        private int _lifeId;

        // 当前生命编号。严格递增，复用时改变，用于识别迟到的跨生命回调。
        public int LifeId => _lifeId;

        // 这条命是否还没上报过死亡。
        public bool IsArmed => _armed;

        // 开始新的一条命。只由控制器的复用/初始化路径调用。
        public void Arm()
        {
            _lifeId++;
            _armed = true;
        }

        // 尝试上报。返回 true 表示这次调用完成了上报，调用方必须把 lifeId 一起交出去。
        public bool TryClaimDeathReport(out int lifeId)
        {
            lifeId = _lifeId;

            // 同一条命重复上报：不是错误，静默拒绝（调用方已经打过一次警告/日志）。
            if (!_armed)
            {
                return false;
            }

            _armed = false;
            return true;
        }
    }
}
