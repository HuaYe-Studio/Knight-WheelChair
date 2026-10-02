using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Boss")]
    public sealed class BossConfig : ScriptableObject
    {
        [SerializeField] private float bossHp = 7500f;
        [SerializeField] private float bossMovement = 6f;
        [SerializeField] private float preferredDistance = 8f;
        [SerializeField] private float wanderRadius = 2.5f;
        [SerializeField] private float wanderSpeed = 4f;
        [SerializeField] private Vector2 wanderInterval = new Vector2(1f, 2f);
        [SerializeField] private float dashDistance = 12f;
        [SerializeField] private float dashSpeed = 24f;
        [SerializeField] private float dashDamage = 80f;
        [SerializeField] private float dashCooldown = 3.5f;
        [SerializeField] private float dashPrepareTime = 0.7f;
        [SerializeField] private float dashRecoveryTime = 0.6f;
        [SerializeField] private int dashHitCount = 1;
        [SerializeField] private float normalContactDamage;

        public float BossHp => bossHp;
        public float BossMovement => bossMovement;
        public float PreferredDistance => preferredDistance;
        public float WanderRadius => wanderRadius;
        public float WanderSpeed => wanderSpeed;
        public Vector2 WanderInterval => wanderInterval;
        public float DashDistance => dashDistance;
        public float DashSpeed => dashSpeed;
        public float DashDamage => dashDamage;
        public float DashCooldown => dashCooldown;
        public float DashPrepareTime => dashPrepareTime;
        public float DashRecoveryTime => dashRecoveryTime;
        public int DashHitCount => dashHitCount;
        public float NormalContactDamage => normalContactDamage;
    }
}
