using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 撞墙事实：「从 origin 走这一段位移，会不会撞墙 / 边界」，以及真正能走多远。
    //
    // 表 N04：Dash Collision = Wall -> Stop，「撞击地图墙体或边界时立即结束 Dash」。
    //
    // 待确认（Architecture 第 5 节，通行与碰撞事实归 Map）：Map 还没交付通行性查询，
    // 所以没有真实实现可调。FSM 既不许自己编一个，也不许悄悄忽略这条规则，于是注入成契约。
    //
    // 接口签名按「单步位移」设计，而不是「朝某方向查询 DashDistance」：
    // 之前接口只给方向 + 距离，Dash 每一步都拿完整的 DashDistance 去查，
    // 结果墙在前方 6 单位、Boss 一步还没走就判定撞墙并结束冲刺（实测移动 0 单位）。
    // 现在传入的是本帧真正打算走的位移，返回本帧真正被允许的位移。
    // ============================================================================================
    public interface IWallCheck
    {
        // 查询 origin 沿 displacement 移动是否会撞墙。
        //
        // 返回 false：不会撞墙，allowedPosition = origin + displacement。
        // 返回 true ：会被挡下，allowedPosition 是贴墙的合法位置，
        //             hitPoint 是命中点（用于表现与命中判定）。
        bool TryMove(Vector3 origin, Vector3 displacement, out Vector3 allowedPosition, out Vector3 hitPoint);
    }
}