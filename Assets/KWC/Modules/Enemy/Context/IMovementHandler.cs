using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 执行层：实体怎么动。与状态机分开，状态只表达意图（「朝这个点走」），不直接碰 Transform。
    //
    // 单独一个文件，因为「Unity 要求脚本文件名与类名一致」——
    // 之前接口和实现挤在 MovementHandler.cs 里，Prefab 保存/重导入就变成 Missing Script。
    // ============================================================================================
    public interface IMovementHandler
    {
        Vector3 Position { get; }

        // 朝向归移动处理器管：它是「上一次移动的表现」，不是一个决策。
        Vector3 Facing { get; }

        void MoveToward(Vector3 target, float speed, float deltaTime);

        // 立即停下。会被 OnExit 收尾路径调用，所以必须任何时候都安全。
        void Stop();

        void SetMovementEnabled(bool enabled);

        // 复位到「刚出生」的状态。由控制器在池化复用时调用。
        //
        // 放在接口上而不是只在具体实现上提供：控制器只持有 IMovementHandler，
        // 如果复位只能通过具体类型做，那么换一个实现（导航、Rigidbody、CharacterController）
        // 时复用就会失效——之前就出现过「死亡后再复用仍然不能动」。
        void ResetMovement(Vector3 facing);
    }
}
