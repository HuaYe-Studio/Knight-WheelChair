using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Game")]
    public sealed class GameConfig : ScriptableObject
    {
        [SerializeField] private PlayerBaseConfig playerBase;
        [SerializeField] private WeaponConfig weapon;
        [SerializeField] private Enemy1Config enemy1;
        [SerializeField] private BossConfig boss;
        [SerializeField] private CombatRulesConfig combatRules;
        [SerializeField] private AttributeGrowthConfig attributeGrowth;
        [SerializeField] private WaveSetConfig waves;

        public PlayerBaseConfig PlayerBase => playerBase;
        public WeaponConfig Weapon => weapon;
        public Enemy1Config Enemy1 => enemy1;
        public BossConfig Boss => boss;
        public CombatRulesConfig CombatRules => combatRules;
        public AttributeGrowthConfig AttributeGrowth => attributeGrowth;
        public WaveSetConfig Waves => waves;
        public bool HasAllReferences => playerBase != null && weapon != null &&
            enemy1 != null && boss != null && combatRules != null &&
            attributeGrowth != null && waves != null;
        // Reference completeness is not gameplay readiness; pending design rules still apply.
    }
}
