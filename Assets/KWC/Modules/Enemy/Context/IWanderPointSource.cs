using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 闲逛取点事实：在半径内取一个可走的点。
    //
    // 是否要求可达（绕障）在 GDD 里仍是待确认项，所以实现只在水平面上采点，
    // 并如实报告「没有任何东西校验过它」。在这里猜一个 Layer，正是架构禁止的那种静默默认值。
    //
    // 本文件只放接口；实现见同目录的 RandomWanderPointSource.cs。
    // ============================================================================================
    public interface IWanderPointSource
    {
        bool TryGetPoint(Vector3 center, float radius, out Vector3 point);

        // 抽取下一次重选目标点的间隔。表 N04 给的是 Wander Interval 1~2 s；
        // 范围由配置提供，这里只负责抽，避免任何状态自己造一个分布。
        float RollInterval(float minSeconds, float maxSeconds);
    }
}