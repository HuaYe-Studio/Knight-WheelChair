using System;
using KWC.Core;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 经验球的消费与回收。外部确认玩家满足拾取条件后调用TryCollect。
    /// 本组件不决定拾取距离、碰撞类型或吸附方式。
    /// </summary>
    public sealed class ExpOrb : MonoBehaviour
    {
        private IObjectPool pool;
        private int experience;
        private bool available;
        private int spawnId;

        // 每次从池中取出都会产生新编号，延迟拾取请求必须携带当时记录的编号。
        public int SpawnId => spawnId;
        public bool CanCollect => available;

        public void Initialize(IObjectPool ownerPool, int expAmount)
        {
            if (ownerPool == null) throw new ArgumentNullException(nameof(ownerPool));
            if (expAmount <= 0) throw new ArgumentOutOfRangeException(nameof(expAmount));
            pool = ownerPool;
            experience = expAmount;
            spawnId = checked(spawnId + 1);
            available = true;
        }

        /// <summary>
        /// 给玩家增加经验并将本次经验球归还对象池。true表示拾取已结算。
        /// 无效玩家、死亡玩家、重复或过期请求返回false，不消耗经验球。
        /// </summary>
        public bool TryCollect(PlayerStats player, int expectedSpawnId)
        {
            if (!available || spawnId != expectedSpawnId || player == null ||
                !player.IsInitialized || player.CurrentHp <= 0f)
                return false;

            // AddExperience会同步通知UI；提前标记，避免回调再次拾取同一颗经验球。
            available = false;
            bool accepted = player.AddExperience(experience);
            if (!accepted)
            {
                if (spawnId == expectedSpawnId) available = true;
                return false;
            }

            // 若回调已经回收并重新借出该对象，就不能把新一轮经验球也回收。
            if (spawnId == expectedSpawnId)
                pool.Return(gameObject);
            return true;
        }

        // 波末由GameSystem统一归还剩余拾取物；停用不增加经验。
        private void OnDisable()
        {
            available = false;
        }
    }
}
