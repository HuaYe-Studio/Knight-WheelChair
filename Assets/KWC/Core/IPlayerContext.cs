using UnityEngine;

namespace KWC.Core
{
    // Implemented by Player; injected into consumers by the scene's composition code.
    public interface IPlayerContext
    {
        Transform EntityTransform { get; }
        Vector3 Position { get; }
        Vector3 Facing { get; }
        void SetControlEnabled(bool enabled);
    }
}
