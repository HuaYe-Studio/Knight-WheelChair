using System;
using System.Collections.Generic;
using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 状态机核心。整个 Enemy 模块只有这一份状态机实现，Enemy1 与 Boss 共用。
    //
    // 核心思想：把散落在 Update 里的 if-else 换成「当前处于哪个状态」，并把数据分成三层：
    //   事实（Fact）  —— 世界是什么。只读，写入方只能是它的来源（玩家、物理、Map、Combat）。
    //   状态（State） —— 我此刻在做什么。只有状态转移可以改。
    //   节流（Throttle）—— 允不允许现在做。冷却、间隔，单独存、单独时钟。
    // 本文件只负责「状态」这一层：谁当前生效、什么时候允许换、换了要不要收尾。
    // ============================================================================================

    // --------------------------------------------------------------------------------------------
    // 状态机能对控制器做的全部事情，只有这两件。
    // 故意收得很窄：状态如果还需要别的东西，说明它在伸手拿不属于它的事实或节流数据。
    // --------------------------------------------------------------------------------------------
    public interface IStateMachineHost<TStateId> where TStateId : struct
    {
        // 进入当前状态后经过的秒数。状态的兜底出口、前摇、最小停留都用它。
        float TimeInState { get; }

        // 请求在允许的帧转移到 next。返回 false 表示这次转移不合法：
        // 非法边、自我转移、或者本帧已经转移过。调用方必须处理返回值，不允许静默忽略。
        bool TryRequestTransition(TStateId next, string reason);
    }

    // --------------------------------------------------------------------------------------------
    // 单个状态的生命周期。
    //
    // 子类契约（每一条都对应一个真实踩过的坑）：
    //  1. OnEnter 与 OnExit 必须成对。OnEnter 里申请的（动画、协程、Buff、订阅）必须在 OnExit 释放，
    //     不许留给下一个状态打扫，否则就是状态泄漏，表现是人物原地抽搐、特效不消失。
    //  2. OnEnter 只做准备工作，绝不在里面发起转移，否则 Enter/Exit 会递归嵌套
    //     （死亡 → 倒地 → 结算 这种级联要显式分帧）。
    //  3. OnExit 必须能在任何时候被调用（被打断、波末回收、场景卸载），它只收尾，不做新决策。
    //  4. 每个状态只持有属于自己的数据（计时器、阶段、本状态缓存）。跨状态共享的量放到控制器，
    //     否则状态之间会通过共享变量偷偷耦合，改一个崩另一个。
    // --------------------------------------------------------------------------------------------
    public abstract class StateBase<TStateId> : IStateDescriptor where TStateId : struct
    {
        public abstract TStateId Id { get; }

        // 给人看的状态名，用于转移日志和调试面板。与 Id 分开，改日志不会影响行为。
        public abstract string DisplayName { get; }

        // 只有终态（Dead）返回 true。布置期审计用它区分「出不去」和「本来就不该出去」。
        public virtual bool IsTerminal => false;

        public virtual void OnEnter(IStateMachineHost<TStateId> host) { }

        // 返回要转去的状态，返回 null 表示留下。真正的转移由状态机在本方法返回后执行，
        // 所以一帧里不可能出现两次转移。
        public virtual TStateId? OnUpdate(IStateMachineHost<TStateId> host, float deltaTime) { return null; }

        public virtual void OnExit(IStateMachineHost<TStateId> host) { }
    }

    // 编辑期审计需要的结构信息，让审计代码不必知道具体的 TStateId。
    public interface IStateDescriptor
    {
        string DisplayName { get; }
        bool IsTerminal { get; }
    }

    // --------------------------------------------------------------------------------------------
    // 转移表的一条边。运行期它是拦非法转移的保险丝，Review 时它就是状态图本身。
    // --------------------------------------------------------------------------------------------
    public struct TransitionRule<TStateId> where TStateId : struct
    {
        public readonly TStateId From;
        public readonly TStateId To;

        // 优先级是文档用途，不是派发顺序：实际派发按状态里 if / else if 的书写顺序。
        // 审计会拦下「同一个状态出去的两条边优先级相同」，说明图有歧义，胜负只由书写顺序决定。
        public readonly int Priority;

        // 预留给还没接线的边（例如冲刺撞墙）。禁用边会被拒绝并报警，不会静默失效。
        public readonly bool Enabled;

        public TransitionRule(TStateId from, TStateId to, int priority = 0, bool enabled = true)
        {
            From = from;
            To = to;
            Priority = priority;
            Enabled = enabled;
        }
    }

    // --------------------------------------------------------------------------------------------
    // 状态机本体。它只拥有三样东西：当前是哪个状态、当前状态待了多久、上一次转移是什么时候。
    // 它不拥有任何游戏数据。
    //
    // 这里落实的护栏，每一条都对应一条约定：
    //   一帧一次转移、拒绝自我转移、滞回（进入后短时间内拒绝合法转移）、
    //   合法转移表、必有出口（布置期审计）、出入审计计数、转移日志、事件打断通道。
    //
    // 泛型约束 where TStateId : struct 不是装饰，是必需的：没有它，TStateId? 会被解析成
    // 「可空引用类型注解」而不是 Nullable<TStateId>，子类的重写签名就会全部对不上（CS0508）。
    // --------------------------------------------------------------------------------------------
    public sealed class StateMachine<TStateId> : IStateMachineHost<TStateId> where TStateId : struct
    {
        // 滞回：状态自己请求的合法转移，必须等当前状态待够这么久才放行。
        // 事件打断不受它限制，所以同帧到达的死亡信号永远不会被这个门槛吃掉。
        private const float DefaultMinDwell = 0.05f;

        // 状态持续请求一个被拦下的转移超过这么久，说明是请求逻辑有问题而不是时序竞争，
        // 静默告警升级成报错，每个状态只报一次。
        private const float StuckRequestWarnAfter = 0.5f;

        private readonly Dictionary<TStateId, StateBase<TStateId>> _states =
            new Dictionary<TStateId, StateBase<TStateId>>();

        private readonly List<TransitionRule<TStateId>> _rules = new List<TransitionRule<TStateId>>();

        // 打断用队列而不是单个槽位：同一帧到达两次事件不许被悄悄合并，第二个也不能被第一个顶掉。
        private readonly Queue<TStateId> _interrupts = new Queue<TStateId>();

        private readonly Dictionary<TStateId, float> _totalTime = new Dictionary<TStateId, float>();
        private readonly Dictionary<TStateId, int> _enters = new Dictionary<TStateId, int>();
        private readonly Dictionary<TStateId, int> _refusals = new Dictionary<TStateId, int>();

        private TStateId _initial;
        private TStateId _current;
        private TStateId? _next;
        private string _pendingReason = "none";
        private float _timeInState;
        private bool _hasTransited;
        private bool _initialized;
        private bool _loggedStuckRequest;

        // OnEnter / OnExit 必须配平。整个生命周期里它只能在这两者之间来回：
        // 大于 1 是状态泄漏，正在运行时等于 0 是幽灵状态（有东西在状态机外面偷偷跑）。
        private int _liveStateCount;

        private float _lastTransitionTime;
        private string _lastReason = "none";
        private readonly List<string> _log = new List<string>();

        // 转移发生时通知外部（调试面板、埋点）。参数是：从哪来、到哪去、原因。
        public event Action<TStateId, TStateId, string> Transitioned;

        public int MaxTraceEntries { get; set; } = 64;
        public float MinDwell { get; set; } = DefaultMinDwell;
        public bool TraceEnabled { get; set; } = true;

        public TStateId Current => _current;
        public float TimeInState => _timeInState;
        public int LiveStateCount => _liveStateCount;
        public int TransitionCount { get; private set; }
        public string LastTransitionReason => _lastReason;
        public float LastTransitionTime => _lastTransitionTime;

        public IReadOnlyList<TransitionRule<TStateId>> Rules => _rules;
        public IReadOnlyList<string> Trace => _log;

        public void Initialize(TStateId initial, IEnumerable<StateBase<TStateId>> states,
            IEnumerable<TransitionRule<TStateId>> rules)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            _states.Clear();
            _rules.Clear();
            _totalTime.Clear();
            _enters.Clear();
            _refusals.Clear();

            foreach (StateBase<TStateId> state in states)
            {
                if (state == null)
                {
                    throw new ArgumentException("状态列表里有 null。", nameof(states));
                }

                // 两个状态共用一个 id，意味着其中一个永远不可达。
                if (_states.ContainsKey(state.Id))
                {
                    throw new ArgumentException(
                        "状态 id 重复：'" + state.Id + "'。每个状态必须独占一个 id。", nameof(states));
                }

                _states.Add(state.Id, state);
                _totalTime[state.Id] = 0f;
                _enters[state.Id] = 0;
                _refusals[state.Id] = 0;
            }

            if (!_states.ContainsKey(initial))
            {
                throw new ArgumentException("初始状态 '" + initial + "' 没有注册。", nameof(initial));
            }

            if (rules != null)
            {
                foreach (TransitionRule<TStateId> rule in rules)
                {
                    _rules.Add(rule);
                }
            }

            // 布置期就检查转移表，而不是等运行到一半才发现某个状态出不去。
            for (int i = 0; i < _rules.Count; i++)
            {
                TransitionRule<TStateId> rule = _rules[i];
                if (!rule.Enabled)
                {
                    continue;
                }

                if (!_states.ContainsKey(rule.From))
                {
                    throw new ArgumentException("转移表引用了未注册的状态 '" + rule.From + "'。");
                }

                if (!_states.ContainsKey(rule.To))
                {
                    throw new ArgumentException("转移表引用了未注册的状态 '" + rule.To + "'。");
                }
            }

            _initial = initial;
            _initialized = true;

            // 初始进入不算一次转移：不需要为「从无到初始状态」写一条边。
            _liveStateCount = 0;
            Enter(initial, "初始");
        }

        // 唯一的节拍入口。由控制器每帧调用一次（并按决策降频），绝不从物理回调、
        // 动画事件或另一个状态里调用 —— 这样「什么时候允许转移」只有一个答案。
        public void Update(float deltaTime)
        {
            if (!_initialized || deltaTime < 0f)
            {
                return;
            }

            _timeInState += deltaTime;
            _totalTime[_current] += deltaTime;

            // 1. 事件打断优先。死亡这类信号比任何普通边都高，且不受滞回门槛限制。
            if (_interrupts.Count > 0)
            {
                TStateId target = _interrupts.Dequeue();
                SafeTransition(target, "事件打断");
                return;
            }

            // 2. 处理 OnEnter 期间排队的请求（OnEnter 自己不允许转移）。
            if (_next.HasValue)
            {
                TStateId target = _next.Value;
                _next = null;
                SafeTransition(target, _pendingReason);
                return;
            }

            // 3. 滞回：合法转移被延后，但绝不会被丢掉，等门槛开了就执行。
            if (_hasTransited && _timeInState < MinDwell)
            {
                return;
            }

            StateBase<TStateId> state = _states[_current];

            // 4. 状态自己决定。它只返回意图，不自己执行切换。
            TStateId? requested = state.OnUpdate(this, deltaTime);
            if (requested.HasValue && requested.Value.Equals(_current))
            {
                // 自我转移：已经在目标状态里了。直接拒绝，这样「重进一次来重置」就不可能被
                // 当成隐式行为偷偷用上；真需要重置就显式写。
                LogRefusal(requested.Value, "自我转移已忽略");
                return;
            }

            if (requested.HasValue)
            {
                Move(requested.Value, "状态请求");
            }
        }

        // 事件打断通道，给「不允许丢」的事实使用：敌人血量归零。
        // 从 OnEnter、事件回调、物理回调里调用都是安全的。
        public void OnHostileInterrupt(TStateId target, string reason)
        {
            if (!_initialized)
            {
                return;
            }

            // 已经在目标状态里了：这不是失败，所以既不计数也不报错。
            // 迟到的死亡回调重复上报会走到这里。
            if (target.Equals(_current) && _interrupts.Count == 0)
            {
                AddTrace("跳过  事件打断已忽略，当前就在 " + target);
                return;
            }

            _interrupts.Enqueue(target);
            _pendingReason = string.IsNullOrEmpty(reason) ? "事件打断" : reason;
        }

        // 状态请求转移的通道。故意叫 Try：调用方必须看返回值，每次拒绝都会计数并报警。
        public bool TryRequestTransition(TStateId next, string reason)
        {
            if (!_initialized)
            {
                return false;
            }

            if (next.Equals(_current))
            {
                LogRefusal(next, "自我转移已忽略");
                return false;
            }

            return Move(next, reason);
        }

        public float GetStateEnterCount(TStateId id)
        {
            return _enters.TryGetValue(id, out int value) ? value : 0;
        }

        public float GetStateTotalTime(TStateId id)
        {
            return _totalTime.TryGetValue(id, out float value) ? value : 0f;
        }

        public float GetStateRefusalCount(TStateId id)
        {
            return _refusals.TryGetValue(id, out int value) ? value : 0f;
        }

        public IEnumerable<TStateId> RegisteredStates => _states.Keys;

        // 给人看的状态名，供调试面板和转移日志使用。只在显示或报警时调用，不在热路径上。
        public bool TryGetDisplayName(TStateId id, out string displayName)
        {
            if (_states.TryGetValue(id, out StateBase<TStateId> state))
            {
                displayName = state.DisplayName;
                return true;
            }

            displayName = id.ToString();
            return false;
        }

        // 布置期审计用：需要问每个状态它是不是终态。
        public bool TryGetState(TStateId id, out StateBase<TStateId> state)
        {
            return _states.TryGetValue(id, out state);
        }

        // 审计用：这个状态有没有声明出去的边。
        public bool HasOutgoingEnabledRule(TStateId id)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Enabled && _rules[i].From.Equals(id))
                {
                    return true;
                }
            }

            return false;
        }

        // 审计用：有没有能进来这个状态的边。进不来说明是死代码，通常代表某条边写反了。
        public bool HasIncomingEnabledRule(TStateId id)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Enabled && _rules[i].To.Equals(id))
                {
                    return true;
                }
            }

            return false;
        }

        // 强制离开：波末回收、重开、场景卸载、OnDisable 走这条路。
        // 仍然执行 OnExit，保证申请与释放依旧配平，然后把状态机停在初始状态。
        public void ResetToInitial(string reason)
        {
            if (!_initialized)
            {
                return;
            }

            _next = null;
            _interrupts.Clear();

            if (_liveStateCount > 0)
            {
                _states[_current].OnExit(this);
                _liveStateCount--;
            }

            _hasTransited = false;
            _timeInState = 0f;
            Enter(_initial, reason);
        }

        private void Enter(TStateId id, string reason)
        {
            _current = id;
            _timeInState = 0f;
            _hasTransited = false;
            _loggedStuckRequest = false;

            if (_enters.ContainsKey(id))
            {
                _enters[id]++;
            }

            // 出入审计：这里永远不该超过 1。
            _liveStateCount++;
            if (_liveStateCount > 1)
            {
                Debug.LogError("[FSM] 活跃状态数为 " + _liveStateCount + "（进入 '" + id +
                               "' 时）。有状态没执行 OnExit，属于状态泄漏。");
            }

            _lastReason = reason;
            _lastTransitionTime = Time.time;
            AddTrace("进入 " + id + " <- " + reason);

            _states[id].OnEnter(this);
        }

        private void Exit(TStateId id)
        {
            AddTrace("离开 " + id);
            _states[id].OnExit(this);
            _liveStateCount--;

            if (_liveStateCount < 0)
            {
                Debug.LogError("[FSM] 活跃状态数变成负数（离开 '" + id + "' 时）。" +
                               "OnExit 的执行次数多于 OnEnter。");
                _liveStateCount = 0;
            }
        }

        private bool Move(TStateId next, string reason)
        {
            if (!IsLegal(_current, next))
            {
                LogRefusal(next, "非法转移 " + _current + " -> " + next + "（" + reason + "）");
                return false;
            }

            _next = next;
            _pendingReason = string.IsNullOrEmpty(reason) ? "未命名" : reason;

            if (!string.IsNullOrEmpty(reason))
            {
                _lastReason = reason;
            }

            return true;
        }

        // 转移表是保险丝，不是形式。没声明的边就是请求逻辑有 bug，必须吵闹地失败。
        private bool IsLegal(TStateId from, TStateId to)
        {
            if (!_initialized)
            {
                return false;
            }

            if (from.Equals(to))
            {
                return false;
            }

            for (int i = 0; i < _rules.Count; i++)
            {
                TransitionRule<TStateId> rule = _rules[i];
                if (!rule.Enabled)
                {
                    continue;
                }

                if (rule.From.Equals(from) && rule.To.Equals(to))
                {
                    return true;
                }
            }

            // 非终态永远要能出去：这种情况下放行，但必须说出来 ——
            // 出现未声明的边说明转移表已经和状态图不一致了。
            if (!HasOutgoingEnabledRule(from))
            {
                Debug.LogWarning("[FSM] '" + from + "' 没有声明任何出去的边；允许紧急退出到 '" + to +
                                 "'。请把它补进转移表。");
                return true;
            }

            return false;
        }

        private void SafeTransition(TStateId target, string reason)
        {
            if (target.Equals(_current))
            {
                LogRefusal(target, "自我转移已忽略（" + reason + "）");
                return;
            }

            // 打断和排队请求一样要过校验，保证转移表始终是权威。
            TStateId from = _current;
            Exit(from);
            Enter(target, reason);
            _hasTransited = true;
            TransitionCount++;
            AddTrace("转移 " + from + " -> " + target + "  在 " +
                     _totalTime[from].ToString("0.000") + "s 之后（" + reason + "）");
            Transitioned?.Invoke(from, target, reason);
        }

        private void LogRefusal(TStateId target, string message)
        {
            if (_refusals.ContainsKey(target))
            {
                _refusals[target]++;
            }

            if (_timeInState >= StuckRequestWarnAfter)
            {
                if (!_loggedStuckRequest)
                {
                    _loggedStuckRequest = true;

                    // 同一个转移被反复拦下：说明条件和门槛互相矛盾。这是缺陷不是竞争，所以报错。
                    Debug.LogError("[FSM] '" + _current + "' 持续 " + _timeInState.ToString("0.00") +
                                   "s 请求一个被拦下的转移：" + message);
                }
            }
            else
            {
                AddTrace("拒绝 " + _current + " -> " + target + "  " + message);
            }
        }

        private void AddTrace(string line)
        {
            if (!TraceEnabled)
            {
                return;
            }

            _log.Add(line);
            if (_log.Count > MaxTraceEntries)
            {
                _log.RemoveAt(0);
            }
        }
    }
}
