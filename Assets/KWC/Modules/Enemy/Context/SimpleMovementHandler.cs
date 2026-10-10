using UnityEngine;

namespace KWC.Enemy
{
    // --------------------------------------------------------------------------------------------
    // Enemy1 用的执行层实现。它完全不知道「状态」是什么。
    //
    // 文件名必须与类名一致，否则 Prefab 保存后重导入会显示 Missing Script。
    //
    // Collider 类型 / 移动平面 / Layer Matrix 都还没确认，所以这里只驱动 Transform，
    // 不假设 CharacterController，也不假设 Rigidbody 速度。等这些确认后，
    // 需要改的只有 MoveToward 的方法体。
    // --------------------------------------------------------------------------------------------
    [DisallowMultipleComponent]
    public sealed class SimpleMovementHandler : MonoBehaviour, IMovementHandler
    {
        [SerializeField] private bool updateFacing = true;

        private Vector3 _facing = Vector3.forward;
        private bool _enabled = true;

        public Vector3 Position => transform.position;
        public Vector3 Facing => _facing;

        public void MoveToward(Vector3 target, float speed, float deltaTime)
        {
            if (!_enabled)
            {
                return;
            }

            Vector3 current = transform.position;
            Vector3 toTarget = target - current;
            toTarget.y = 0f;

            // 已经到位：不要再转向，否则零长度向量会让朝向抖动。
            if (toTarget.sqrMagnitude <= 1e-8f)
            {
                return;
            }

            Vector3 direction = toTarget.normalized;
            float step = Mathf.Max(0f, speed) * deltaTime;

            // 不允许冲过头：少了这一句，实体就会绕着目标点来回振荡。
            if (step >= toTarget.magnitude)
            {
                transform.position = target;
            }
            else
            {
                transform.position = current + (direction * step);
            }

            if (updateFacing)
            {
                _facing = direction;
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }

        public void Stop()
        {
            // 驱动 Transform 的情况下，「停」就等于「这一帧不动」，所以这里是空实现。
            // 保留成显式方法，是因为换成 Rigidbody 或 CharacterController 时这里要写真的东西。
        }

        public void SetMovementEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled)
            {
                Stop();
            }
        }

        // 复用时调用：池里的实例不能再留着上一个使用者的朝向，也不能还处在「被禁用」状态。
        public void ResetMovement(Vector3 facing)
        {
            _enabled = true;
            _facing = facing.sqrMagnitude > 1e-8f ? facing.normalized : Vector3.forward;
        }
    }
}
