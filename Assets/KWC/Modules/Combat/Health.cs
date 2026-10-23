using System;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 通用生命值组件，负责扣血、治疗与生命事件。
    /// 玩家无敌时间、阵营和攻击是否命中，由后续伤害流程判断后再调用本组件。
    /// </summary>
    public sealed class Health : MonoBehaviour
    {
        private float currentHp;
        private float maxHp;
        // 对象池会复用同一个对象，用递增编号区分它的每一条命，拦截旧生命的迟到请求。
        private int lifeId;
        private bool initialized;

        // 对外只读，所有血量修改都经过本组件。未初始化或结束生命后，应先检查 IsInitialized。
        public float CurrentHp => currentHp;
        public float MaxHp => maxHp;
        public int LifeId => lifeId;
        public bool IsInitialized => initialized;
        // 未初始化、被禁用或被清理的对象不视为击杀死亡。
        public bool IsDead => initialized && currentHp <= 0f;

        /// <summary>
        /// 血量变化通知，参数依次为生命编号、变化后的血量、最大血量。
        /// 传递本次结算的值，方便监听者识别旧事件，避免把它当作复用后新生命的状态。
        /// </summary>
        public event Action<int, float, float> HealthChanged;
        /// <summary>
        /// 有效伤害将血量降为零时发出的死亡通知，参数为生命编号。
        /// 在 HealthChanged 之后触发；初始化和非击杀清理不触发此事件。
        /// </summary>
        public event Action<int> Died;

        /// <summary>
        /// 建立一条新生命，最大血量、初始血量均由调用方明确提供。
        /// newLifeId 必须大于本组件上一次的编号，首次至少为 1。
        /// 初始化不发送事件，监听者接线后应主动读取当前状态。
        /// </summary>
        public void Initialize(float maximumHp, float initialHp, int newLifeId)
        {
            // 生命入口统一保证血量有效，后续结算只需维护上下限。
            if (!CombatMath.IsFinite(maximumHp) || maximumHp <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maximumHp));
            if (!CombatMath.IsFinite(initialHp) || initialHp < 0f || initialHp > maximumHp)
                throw new ArgumentOutOfRangeException(nameof(initialHp));
            if (newLifeId <= lifeId)
                throw new ArgumentOutOfRangeException(nameof(newLifeId), "A new lifetime needs a greater id.");

            maxHp = maximumHp;
            currentHp = initialHp;
            lifeId = newLifeId;
            initialized = true;
        }

        /// <summary>
        /// 尝试对指定生命扣血；只有血量实际变化才返回 true。
        /// expectedLifeId 是请求针对的生命编号，不能在延迟回调中换成新生命编号。
        /// 这里的返回值表示实际扣血，与 Enemy 桥接的“请求已提交”含义不同。
        /// </summary>
        public bool TryApplyDamage(float amount, int expectedLifeId)
        {
            if (!CanChange(expectedLifeId) || !CombatMath.IsFinite(amount) || amount <= 0f)
                return false;

            // 超量伤害最多扣到零；过小的浮点数也可能因精度限制而没有实际改变血量。
            float nextHp = Mathf.Max(0f, currentHp - amount);
            if (nextHp == currentHp) return false;
            // 先写入状态再通知：若监听者再次调用伤害方法，零血量会阻止重复结算死亡。
            currentHp = nextHp;
            // ?.Invoke 表示有订阅者时才通知；参数是本次扣血后的值。
            HealthChanged?.Invoke(expectedLifeId, nextHp, maxHp);

            // 血量监听者可能结束生命或重新初始化对象；再次核对编号，避免误报新生命死亡。
            if (nextHp == 0f && initialized && lifeId == expectedLifeId)
                Died?.Invoke(expectedLifeId);
            return true;
        }

        /// <summary>
        /// 尝试治疗当前仍存活的指定生命，最多恢复到最大血量，不支持复活。
        /// 满血、无效治疗量或过期请求返回 false，且不发送血量变化事件。
        /// </summary>
        public bool TryHeal(float amount, int expectedLifeId)
        {
            if (!CanChange(expectedLifeId) || !CombatMath.IsFinite(amount) || amount <= 0f)
                return false;

            // 治疗后的血量不能超过最大血量。
            float nextHp = Mathf.Min(maxHp, currentHp + amount);
            if (nextHp == currentHp) return false;
            currentHp = nextHp;
            HealthChanged?.Invoke(expectedLifeId, nextHp, maxHp);
            return true;
        }

        /// <summary>
        /// 提高最大HP，并给当前HP增加同样的数值，保留原有缺血量。
        /// newMaxHp是新的总上限；死亡、旧生命或没有提高上限时返回false。
        /// </summary>
        public bool TryIncreaseMaxHp(float newMaxHp, int expectedLifeId)
        {
            if (!CanChange(expectedLifeId) || !CombatMath.IsFinite(newMaxHp) || newMaxHp <= maxHp)
                return false;

            float increase = newMaxHp - maxHp;
            currentHp = Mathf.Min(newMaxHp, currentHp + increase);
            maxHp = newMaxHp;
            HealthChanged?.Invoke(expectedLifeId, currentHp, maxHp);
            return true;
        }

        /// <summary>
        /// 结束本次生命，使后续请求失效。用于波末清理等非击杀路径，不发送死亡通知。
        /// 保留 lifeId，确保下次复用必须使用更大的编号。
        /// </summary>
        public void EndLife()
        {
            initialized = false;
        }

        // 扣血和治疗共用的前置条件：已初始化、生命编号一致、尚未死亡。
        private bool CanChange(int expectedLifeId)
        {
            return initialized && expectedLifeId == lifeId && currentHp > 0f;
        }

        // 对象被禁用时结束本次生命。重新启用前，调用方需要显式初始化新的生命。
        private void OnDisable()
        {
            EndLife();
        }
    }
}
