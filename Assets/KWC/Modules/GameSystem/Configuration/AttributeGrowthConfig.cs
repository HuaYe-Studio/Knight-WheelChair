using System;
using KWC.Core;
using UnityEngine;

namespace KWC.Data
{
    [Serializable]
    public struct AttributeGrowth
    {
        [SerializeField] private float b;
        [SerializeField] private float g;
        public float B => b;
        public float G => g;

        public AttributeGrowth(float b, float g)
        {
            this.b = b;
            this.g = g;
        }
    }

    [CreateAssetMenu(menuName = "KWC/Config/Attribute Growth")]
    public sealed class AttributeGrowthConfig : ScriptableObject
    {
        [SerializeField] private PlayerBaseConfig playerBase;
        [SerializeField] private AttributeGrowth hp = new AttributeGrowth(15f, 5f);
        [SerializeField] private AttributeGrowth attack = new AttributeGrowth(8f, 2f);
        [SerializeField] private AttributeGrowth movement = new AttributeGrowth(0.4f, 0.05f);
        [SerializeField] private AttributeGrowth attackSpeed = new AttributeGrowth(6f, 1.5f);

        public PlayerBaseConfig PlayerBase => playerBase;

        public AttributeGrowth GetGrowth(AttributeType attribute)
        {
            switch (attribute)
            {
                case AttributeType.Hp: return hp;
                case AttributeType.Attack: return attack;
                case AttributeType.Movement: return movement;
                case AttributeType.AttackSpeed: return attackSpeed;
                default: throw new ArgumentOutOfRangeException(nameof(attribute));
            }
        }
        // S0 comes from PlayerBase. This asset never stores runtime attribute ranks.
    }
}
