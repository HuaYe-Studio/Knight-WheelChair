using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Player Base")]
    public sealed class PlayerBaseConfig : ScriptableObject
    {
        [SerializeField, Min(0)] private float playerHp = 100f;
        [SerializeField, Min(0)] private float playerAttack = 100f;
        [SerializeField, Min(0)] private float playerMovement = 10f;
        [SerializeField, Min(0.01f)] private float attackSpeed = 100f;

        public float PlayerHp => playerHp;
        public float PlayerAttack => playerAttack;
        public float PlayerMovement => playerMovement;
        public float AttackSpeed => attackSpeed;
    }
}
