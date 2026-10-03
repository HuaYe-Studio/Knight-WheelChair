using System;
using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 事实层的来源接口。事实 = 「世界是什么」，只读，写入方只能是它的来源。
    // 这些接口都只暴露读操作，所以状态和控制器在编译期就无法改写事实。
    // ============================================================================================

    // --------------------------------------------------------------------------------------------
    // HP 事实。
    //
    // 它自己不拥有 HP：Combat 的 Health 组件是唯一所有者（Architecture 第 4 节
    // 「Enemy HP / Boss HP」）。这里只是一份只读视图 + 一次性死亡事件，
    // 让 FSM 既能读、又不用每帧轮询，也不会存出第二份可改的 HP。
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
        private bool _deathReported;

        public event Action EnqueuedDeath;

        public float Current => _current;
        public float Max => _max;
        public bool IsAlive => _current > 0f;

        public FactHealthSource(float initialMax)
        {
            _max = initialMax;
            _current = initialMax;
        }

        // 由桥接在 Combat Health 报告变化时调用。
        public void ReportHealth(float current, float max)
        {
            _max = max;
            _current = current;

            // 死亡只通知一次。零血时的重复上报绝不能再触发一次，
            // 否则每个迟到回调都会重新触发一次 Dead 转移。
            if (_current <= 0f && !_deathReported)
            {
                _deathReported = true;
                EnqueuedDeath?.Invoke();
            }
        }

        // 池化：复用实例时是一条全新的命，死亡闩锁必须清掉，
        // 否则下一个使用者一出生就是死的。
        public void ResetForReuse(float max)
        {
            _max = max;
            _current = max;
            _deathReported = false;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 接触事实：「玩家现在是否碰到我」。由物理写入，状态机只读。
    // 如果状态机可以写它，接触就不再是事实了。
    // --------------------------------------------------------------------------------------------
    public interface IContactSource
    {
        bool IsInContact { get; }
    }

    // 开发期临时实现。
    //
    // 真正的答案需要 ProjectSettings 的 Layer Matrix，而 Architecture 第 9 节把它列为未定
    // （「碰撞层 ... 确认前保持编辑器默认」）。在主程和 Map Owner 定下来之前，
    // 这里只接受 transform 恰好等于注入的玩家 Transform 的碰撞体；
    // 它每个组件只警告一次，让「正在用临时方案」这件事始终可见，并且绝不去猜 Layer。
    //
    // 用 #if 隔开是刻意的：开发捷径不许shipping进 player 构建。
    // 正式实现接上后，运行时代码只依赖 IContactSource。
#if UNITY_EDITOR
    public sealed class PhysicsContactSource : MonoBehaviour, IContactSource
#else
    internal sealed class PhysicsContactSource : MonoBehaviour, IContactSource
#endif
    {
        private Transform _playerTransform;
        private bool _hasExplicitFilter;
        private bool _warnedAboutFallback;
        private bool _inContact;

        public bool IsInContact => _inContact;

        // 调试面板用的审计计数：接触开始 / 结束次数。
        public int ContactBeginCount { get; private set; }
        public int ContactEndCount { get; private set; }
        public bool IsUsingFallbackFilter => !_hasExplicitFilter;

        public void Initialize(Transform playerTransform)
        {
            _playerTransform = playerTransform;
            _inContact = false;
        }

        // 正式的过滤条件确定后由 Game System 调用。接上它之后，下面的兜底判定就可以退休了。
        public void SetContactFilter(Transform playerTransform)
        {
            _playerTransform = playerTransform;
            _hasExplicitFilter = true;
        }

        // 波末回收时接触状态不能留下来，否则这个池化对象的下一个使用者一上线就「已经贴着玩家」。
        public void ResetContact()
        {
            _inContact = false;
            ContactBeginCount = 0;
            ContactEndCount = 0;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other))
            {
                return;
            }

            if (!_inContact)
            {
                ContactBeginCount++;
            }

            _inContact = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other))
            {
                return;
            }

            if (_inContact)
            {
                ContactEndCount++;
            }

            _inContact = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsPlayer(collision.collider))
            {
                return;
            }

            if (!_inContact)
            {
                ContactBeginCount++;
            }

            _inContact = true;
        }

        private void OnCollisionExit(Collision collision)
        {
            if (!IsPlayer(collision.collider))
            {
                return;
            }

            if (_inContact)
            {
                ContactEndCount++;
            }

            _inContact = false;
        }

        private bool IsPlayer(Collider other)
        {
            if (other == null || _playerTransform == null)
            {
                return false;
            }

            if (!_hasExplicitFilter && !_warnedAboutFallback)
            {
                _warnedAboutFallback = true;
                Debug.LogWarning("[Enemy] PhysicsContactSource 正在用「transform 相等」的兜底过滤。" +
                                 "接触层仍未确定（Architecture 第 9 节）；" +
                                 "Layer Matrix 确认后请调用 SetContactFilter。");
            }

            return other.transform == _playerTransform;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 撞墙事实：「我正前方有没有墙 / 边界」。
    //
    // 表 N04：Dash Collision = Wall -> Stop，「撞击地图墙体或边界时立即结束 Dash」。
    //
    // 待确认（Architecture 第 5 节，通行与碰撞事实归 Map）：Map 模块还没交付通行性查询，
    // 所以没有真实实现可调。FSM 既不许自己编一个，也不许悄悄忽略这条规则，
    // 于是把问题注入成契约，并且把每一次「无人应答」都记下来报警。
    // --------------------------------------------------------------------------------------------
    public interface IWallCheck
    {
        // 从 origin 沿 direction 走 distance 会撞墙或撞边界时返回 true，并给出命中点。
        bool TryGetWallHit(Vector3 origin, Vector3 direction, float distance, out Vector3 hitPoint);
    }

    // 开发期临时实现：一直报「没墙」，让 Boss 在 Map 查询存在之前就能跑、能 Review。
    // 它统计查询次数，调试面板会把它显示出来，这样「缺一个能力」这件事不会被忘掉。
    // 它不做 Layer Mask，也不做射线，因为碰撞层仍未确定（Architecture 第 9 节）。
#if UNITY_EDITOR
    public sealed class NoWallCheck : IWallCheck
#else
    internal sealed class NoWallCheck : IWallCheck
#endif
    {
        public int QueryCount { get; private set; }
        public bool IsStub => true;

        public bool TryGetWallHit(Vector3 origin, Vector3 direction, float distance, out Vector3 hitPoint)
        {
            QueryCount++;
            hitPoint = default;
            return false;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 闲逛取点事实：在半径内取一个可走的点。
    //
    // 是否要求可达（绕障）在 GDD 里仍是待确认项，所以这里的实现只在水平面上采点，
    // 并如实报告「没有任何东西校验过它」。在这里猜一个 Layer，正是架构禁止的那种静默默认值。
    // --------------------------------------------------------------------------------------------
    public interface IWanderPointSource
    {
        bool TryGetPoint(Vector3 center, float radius, out Vector3 point);

        // 抽取下一次重选目标点的间隔。表 N04 给的是 Wander Interval 1~2 s；
        // 范围由配置提供，这里只负责抽，避免任何状态自己造一个分布。
        float RollInterval(float minSeconds, float maxSeconds);
    }

    public sealed class RandomWanderPointSource : IWanderPointSource
    {
        private readonly System.Random _random;

        public RandomWanderPointSource(int seed)
        {
            _random = new System.Random(seed);
        }

        public bool TryGetPoint(Vector3 center, float radius, out Vector3 point)
        {
            if (radius <= 0f)
            {
                point = center;
                return false;
            }

            double angle = _random.NextDouble() * System.Math.PI * 2.0;

            // 开根号是为了让采样在圆面上均匀，否则点会堆在圆心附近。
            double r = System.Math.Sqrt(_random.NextDouble()) * radius;

            point = center + new Vector3(
                (float)(System.Math.Cos(angle) * r),
                0f,
                (float)(System.Math.Sin(angle) * r));

            return true;
        }

        public float RollInterval(float minSeconds, float maxSeconds)
        {
            if (maxSeconds <= minSeconds)
            {
                return minSeconds;
            }

            return minSeconds + ((float)_random.NextDouble() * (maxSeconds - minSeconds));
        }
    }
}
