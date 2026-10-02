using System;
using System.Collections.Generic;
using KWC.Core;
using UnityEngine;

namespace KWC.GameSystem
{
    // One pool per prefab. Owners reset their components in Rent's initialize callback.
    public sealed class PrefabPool : MonoBehaviour, IObjectPool
    {
        [SerializeField] private GameObject prefab;

        private readonly Queue<GameObject> available = new Queue<GameObject>();
        private readonly HashSet<GameObject> borrowed = new HashSet<GameObject>();
        private Transform inactiveRoot;

        public int BorrowedCount => borrowed.Count;

        public GameObject Rent(Vector3 position, Quaternion rotation, Action<GameObject> initialize)
        {
            if (prefab == null)
            {
                throw new InvalidOperationException("Assign a prefab to this pool before renting.");
            }

            EnsureStorage();
            GameObject instance = null;
            while (available.Count > 0 && instance == null)
            {
                instance = available.Dequeue();
            }

            if (instance == null)
            {
                // An inactive parent prevents OnEnable before the first initialization.
                instance = Instantiate(prefab, inactiveRoot);
            }

            instance.SetActive(false);
            instance.transform.SetParent(transform, false);
            instance.transform.SetPositionAndRotation(position, rotation);

            try
            {
                initialize?.Invoke(instance);
            }
            catch
            {
                instance.SetActive(false);
                instance.transform.SetParent(inactiveRoot, false);
                available.Enqueue(instance);
                throw;
            }

            borrowed.Add(instance);
            instance.SetActive(true);
            return instance;
        }

        public bool Return(GameObject instance)
        {
            if (instance == null || !borrowed.Remove(instance))
            {
                return false;
            }

            instance.SetActive(false);
            instance.transform.SetParent(inactiveRoot, false);
            available.Enqueue(instance);
            return true;
        }

        // Cleanup, not death: this must not award drops, EXP or kills.
        public void ReturnAll()
        {
            foreach (GameObject instance in new List<GameObject>(borrowed))
            {
                Return(instance);
            }
        }

        private void EnsureStorage()
        {
            if (inactiveRoot != null)
            {
                return;
            }

            var storage = new GameObject("Inactive");
            storage.SetActive(false);
            storage.transform.SetParent(transform, false);
            inactiveRoot = storage.transform;
        }
    }
}
