using System.Collections.Generic;
using KWC.Combat;
using KWC.Core;
using UnityEngine;

namespace KWC.GameSystem
{
    /// <summary>
    /// 开发期拾取接线：监听经验球/血包对象池的借用与归还，按测试半径检测并调用 Combat 的 TryCollect。
    /// 拾取半径是测试参数，正式规则（碰撞/吸附/范围）确认后由对应 Owner 替换。
    /// </summary>
    public class TestPickupCollector:MonoBehaviour
    {
        [SerializeField] private PlayerInitializer player;
        private IPlayerContext playerContext;
        [SerializeField] private PrefabPool expOrbPool;
        [SerializeField] private PrefabPool healthPackPool;
        //测试值
        [SerializeField] private float collectRadius;

        private List<ExpOrb> activeExpOrbs = new List<ExpOrb>();
        private List<HealthPack> activeHealthPacks = new List<HealthPack>();
        private List<GameObject> snapShots = new List<GameObject>();

        private readonly List<ExpOrb> expBuffer = new List<ExpOrb>();
        private readonly List<HealthPack> healthPackBuffer = new List<HealthPack>();

        private void Update()
        {
            if (player==null || !player.IsReady || playerContext == null)
                return;

            Vector3 playerPosition = playerContext.Position;
            float radiusSqr = collectRadius * collectRadius;

            expBuffer.Clear();
            expBuffer.AddRange(activeExpOrbs);
            for (int i = expBuffer.Count - 1; i >= 0; i--)
            {
                var expOrb = expBuffer[i];
                if (expOrb == null || !expOrb.CanCollect)
                    continue;

                Vector3 expOrbOffset = expOrb.transform.position - playerPosition;
                if (expOrbOffset.sqrMagnitude > radiusSqr)
                    continue;

                expOrb.TryCollect(player.PlayerStats,expOrb.SpawnId);
            }

            healthPackBuffer.Clear();
            healthPackBuffer.AddRange(activeHealthPacks);
            for (int i = healthPackBuffer.Count - 1; i >= 0; i--)
            {
                var healthPack = healthPackBuffer[i];
                if (healthPack == null || !healthPack.CanCollect)
                    continue;

                Vector3 healthPackOffset = healthPack.transform.position - playerPosition;
                if (healthPackOffset.sqrMagnitude > radiusSqr)
                    continue;

                healthPack.TryCollect(player.PlayerHealth,healthPack.SpawnId);
            }

        }
        private void OnEnable()
        {
            if (player == null)
            {
                Debug.LogError("PickupCollector requires playerInitializer");
                return;
            }

            if (expOrbPool == null)
            {
                Debug.LogError("PickupCollector requires expOrbPool");
                return;
            }

            if (healthPackPool == null)
            {
                Debug.LogError("PickupCollector requires healthPackPool");
                return;
            }

            if (collectRadius <= 0)
            {
                Debug.LogError("collectRadius must be greater than 0");
                return;
            }

            expOrbPool.Rented -= HandleRented;
            expOrbPool.Rented += HandleRented;
            expOrbPool.Returned -= HandleReturned;
            expOrbPool.Returned += HandleReturned;

            healthPackPool.Rented -= HandleRented;
            healthPackPool.Rented += HandleRented;
            healthPackPool.Returned -= HandleReturned;
            healthPackPool.Returned += HandleReturned;

            expOrbPool.CopyBorrowedTo(snapShots);
            foreach (var expOrb in snapShots)
            {
                HandleRented(expOrb);
            }

            healthPackPool.CopyBorrowedTo(snapShots);
            foreach (var healthPack in snapShots)
            {
                HandleRented(healthPack);
            }
        }

        private void OnDisable()
        {
            if (expOrbPool != null)
            {
                expOrbPool.Rented -= HandleRented;
                expOrbPool.Returned -= HandleReturned;
            }

            if (healthPackPool != null)
            {
                healthPackPool.Rented -= HandleRented;
                healthPackPool.Returned -= HandleReturned;
            }

            snapShots.Clear();
            activeExpOrbs.Clear();
            activeHealthPacks.Clear();
        }

        /// <summary>
        /// 由 SessionFlow 注入玩家位置来源；只缓存引用，不触发拾取。
        /// 未注入时 Update 会跳过检测。
        /// </summary>
        public bool Initialize(IPlayerContext context)
        {
            if(context == null)
                return false;

            playerContext = context;

            return true;
        }

        private void HandleRented(GameObject rented)
        {
            if(rented == null)
                return;

            if (rented.TryGetComponent(out ExpOrb expOrb))
            {
                if(!activeExpOrbs.Contains(expOrb))
                    activeExpOrbs.Add(expOrb);
            }

            if (rented.TryGetComponent(out HealthPack healthPack))
            {
                if (!activeHealthPacks.Contains(healthPack))
                    activeHealthPacks.Add(healthPack);
            }
        }

        private void HandleReturned(GameObject returned)
        {
            if (returned == null)
                return;

            if (returned.TryGetComponent(out ExpOrb expOrb))
            {
                if (activeExpOrbs.Contains(expOrb))
                    activeExpOrbs.Remove(expOrb);
            }

            if (returned.TryGetComponent(out HealthPack healthPack))
            {
                if (activeHealthPacks.Contains(healthPack))
                    activeHealthPacks.Remove(healthPack);
            }
        }
    }
}
