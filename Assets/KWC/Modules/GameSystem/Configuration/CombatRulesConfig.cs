using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Combat Rules")]
    public sealed class CombatRulesConfig : ScriptableObject
    {
        [Header("N05 - EXP and normal enemy drops")]
        [SerializeField] private int expPerOrb = 5;
        [SerializeField] private int dropCountMin = 1;
        [SerializeField] private int dropCountMax = 3;
        [SerializeField] private float expA = 6f;
        [SerializeField] private float expB = 1.5f;
        [SerializeField] private float expC = 0.25f;

        [Header("N06 - Health packs (non-Boss only)")]
        [SerializeField, Range(0, 1)] private float healthPackDropRate = 0.1f;
        [SerializeField, Range(0, 1)] private float healthPackHealRatio = 0.1f;

        [Header("N07 - Growth")]
        [SerializeField] private int growthPointsPerLevel = 2;
        [SerializeField] private int attributeUpgradeCost = 1;

        [Header("GDD - Player damage immunity")]
        [SerializeField] private float playerInvulnerabilityTime = 0.5f;

        public int ExpPerOrb => expPerOrb;
        public int DropCountMin => dropCountMin;
        public int DropCountMax => dropCountMax;
        public float ExpA => expA;
        public float ExpB => expB;
        public float ExpC => expC;
        public float HealthPackDropRate => healthPackDropRate;
        public float HealthPackHealRatio => healthPackHealRatio;
        public int GrowthPointsPerLevel => growthPointsPerLevel;
        public int AttributeUpgradeCost => attributeUpgradeCost;
        public float PlayerInvulnerabilityTime => playerInvulnerabilityTime;

        // Initial level/points, EXP carryover and pickup distances remain undecided.
    }
}
