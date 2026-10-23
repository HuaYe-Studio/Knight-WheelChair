using UnityEngine;
using KWC.Core;

namespace KWC.GameSystem
{
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
