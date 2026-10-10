using UnityEngine;

namespace KWC.Enemy
{
    // --------------------------------------------------------------------------------------------
    // 开发期临时实现：一直报「不会撞墙」，让 Boss 在 Map 查询存在之前就能跑、能 Review。
    //
    // 文件名必须与类名一致，否则 Prefab 保存后重导入会显示 Missing Script。
    //
    // 它统计查询次数，调试面板会把它显示出来，这样「缺一个能力」这件事不会被忘掉。
    // 它不做 Layer Mask，也不做射线，因为碰撞层仍未确定（Architecture 第 9 节）。
    //
    // 用 #if 隔开：开发捷径不许进 player 构建。
    // --------------------------------------------------------------------------------------------
#if UNITY_EDITOR
    public sealed class NoWallCheck : IWallCheck
#else
    internal sealed class NoWallCheck : IWallCheck
#endif
    {
        public int QueryCount { get; private set; }
        public bool IsStub => true;

        public bool TryMove(Vector3 origin, Vector3 displacement, out Vector3 allowedPosition,
            out Vector3 hitPoint)
        {
            QueryCount++;
            allowedPosition = origin + displacement;
            hitPoint = default;
            return false;
        }
    }
}
