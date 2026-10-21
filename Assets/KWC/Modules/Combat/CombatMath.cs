using System;

namespace KWC.Combat
{
    /// <summary>
    /// 战斗数值的纯计算工具，对应数值设计表 N01/N02/N05/N07。
    /// 参数由调用方从现有 ScriptableObject 配置中读取；本类不保存或修改玩家状态。
    /// 配置数值由配置方保证有效，公式只保留攻速、等级等关键边界检查。
    /// </summary>
    public static class CombatMath
    {
        /// <summary>
        /// 攻击间隔（秒）= 100 / 攻速。例如攻速 100 对应每秒一次。
        /// 攻速必须是有限正数，零攻速没有对应的有限攻击间隔。
        /// </summary>
        public static float GetAttackInterval(float attackSpeed)
        {
            if (!IsFinite(attackSpeed) || attackSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(attackSpeed));
            return 100f / attackSpeed;
        }

        /// <summary>
        /// 单次伤害 = 武器伤害 × 玩家攻击 / 100；玩家攻击 100 表示原倍率。
        /// 保留小数结果，伤害取整规则尚未确定。
        /// </summary>
        public static float GetAttackDamage(float weaponDamage, float playerAttack)
        {
            return (float)((double)weaponDamage * playerAttack / 100d);
        }

        /// <summary>
        /// 计算从 X-1 级升到目标等级 X 所需的经验：5 × 向上取整[a+b(X-2)+c(X-2)²]。
        /// targetLevel 是目标等级，最低为 2；返回值不是从 1 级累计到 X 级的总经验。
        /// a、b、c 来自成长配置；此方法只算门槛，不执行升级或扣除经验。
        /// </summary>
        public static int GetRequiredExp(int targetLevel, float a, float b, float c)
        {
            if (targetLevel < 2) throw new ArgumentOutOfRangeException(nameof(targetLevel));
            // 使用 double 进行中间运算，检查结果后才转换为整数，避免越界转换。
            double offset = (double)targetLevel - 2d;
            double result = 5d * Math.Ceiling(a + b * offset + c * offset * offset);
            if (result <= 0d || result > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(targetLevel), "EXP must fit a positive Int32.");
            return (int)result;
        }

        /// <summary>
        /// 属性总值 S(R) = S0 + B×R + G×R×(R-1)/2。
        /// baseValue 为基础值 S0，b 为首次提升量 B，g 为每次提升量的增幅 G。
        /// rank 是该属性已提升的次数 R，与玩家等级不同；R=0 时返回基础值。
        /// </summary>
        public static float GetAttributeValue(float baseValue, float b, float g, int rank)
        {
            if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
            // 这是各次提升量的等差数列求和；此处不设属性 20 级上限。
            double r = rank;
            return (float)(baseValue + b * r + g * r * (r - 1d) / 2d);
        }

        /// <summary>
        /// 第 R 次提升单独增加的数值：B + G×(R-1)。targetRank 从 1 开始。
        /// 例如 B=15、G=5 时，前三次分别增加 15、20、25。
        /// </summary>
        public static float GetAttributeIncrement(float b, float g, int targetRank)
        {
            if (targetRank < 1) throw new ArgumentOutOfRangeException(nameof(targetRank));
            return (float)(b + (double)g * (targetRank - 1d));
        }

        // NaN 表示无效数值，Infinity 表示无穷大；两者都不能进入战斗结算。
        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

    }
}
