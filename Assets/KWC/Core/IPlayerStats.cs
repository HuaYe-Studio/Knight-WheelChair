namespace KWC.Core
{
    // Combat owns these values. Player/UI only read this view, never a second copy.
    public interface IPlayerStats
    {
        float CurrentHp { get; }
        float MaxHp { get; }
        float PlayerAttack { get; }
        float PlayerMovement { get; }
        float AttackSpeed { get; }
        int Exp { get; }
        int PlayerLevel { get; }
        int GrowthPoints { get; }
        int GetAttributeRank(AttributeType attribute);
    }
}
