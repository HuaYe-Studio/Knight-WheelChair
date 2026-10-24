using UnityEngine;
using KWC.Core;

namespace KWC.GameSystem
{
    /// <summary>
    /// 开发期 IPlayerContext（玩家上下文接口）实现：直接以自身 Transform 作为玩家位置/朝向。
    /// SetControlEnabled 为空实现；正式 Player 模块接入后替换。
    /// </summary>
    public class TestPlayerContext:MonoBehaviour,IPlayerContext
    {
        public Transform EntityTransform => transform;
        public Vector3 Facing => EntityTransform.forward;
        public Vector3 Position => EntityTransform.position;
        public void SetControlEnabled(bool isEnabled)
        {

        }
    }
}
