using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Enemy1")]
    public sealed class Enemy1Config : ScriptableObject
    {
        [SerializeField, Min(0)] private float e1Movement = 10f;
        [SerializeField, Min(0.01f)] private float e1AttackSpeed = 100f;
        public float E1Movement => e1Movement;
        public float E1AttackSpeed => e1AttackSpeed;
        // E1 HP and E1 Attack come from WaveDefinition, never duplicated here.
    }
}
