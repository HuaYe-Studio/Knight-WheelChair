using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 接触事实：「玩家现在是否碰到我」。由物理写入，状态机只读。
    // 如果状态机可以写它，接触就不再是事实了。
    // ============================================================================================
    public interface IContactSource
    {
        bool IsInContact { get; }
    }

    // --------------------------------------------------------------------------------------------
    // 开发期临时实现。
    //
    // 文件名必须与类名一致，否则 Prefab 保存后重导入会显示 Missing Script。
    //
    // 真正的答案需要 ProjectSettings 的 Layer Matrix，而 Architecture 第 9 节把它列为未定
    // （「碰撞层 ... 确认前保持编辑器默认」）。在主程和 Map Owner 定下来之前，
    // 这里只接受 transform 恰好等于注入的玩家 Transform 的碰撞体；
    // 它每个组件只警告一次，让「正在用临时方案」这件事始终可见，并且绝不去猜 Layer。
    //
    // 用 #if 隔开是刻意的：开发捷径不许进 player 构建。
    // 正式实现接上后，运行时代码只依赖 IContactSource。
    // --------------------------------------------------------------------------------------------
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
}
