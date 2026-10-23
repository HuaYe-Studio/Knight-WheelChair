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
        // 当前级升到下一级所需的完整经验值，UI使用Exp / ExpToNextLevel显示进度。
        int ExpToNextLevel { get; }
        int PlayerLevel { get; }
        int GrowthPoints { get; }
        int GetAttributeRank(AttributeType attribute);
    }
}
