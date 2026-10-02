using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Weapon")]
    public sealed class WeaponConfig : ScriptableObject
    {
        [SerializeField, Min(0)] private float weaponDamage = 75f;
        public float WeaponDamage => weaponDamage;

        // Projectile speed, search range and tracking are pending Owner confirmation.
        // Do not treat an absent value as zero, infinite range, or a homing rule.
    }
}
