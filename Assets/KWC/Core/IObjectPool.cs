using System;
using UnityEngine;

namespace KWC.Core
{
    public interface IObjectPool
    {
        // Initialize/reset each instance before its OnEnable runs.
        GameObject Rent(Vector3 position, Quaternion rotation, Action<GameObject> initialize);
        bool Return(GameObject instance);
    }
}
