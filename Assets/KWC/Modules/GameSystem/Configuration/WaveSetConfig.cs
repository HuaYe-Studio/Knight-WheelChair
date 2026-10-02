using System.Collections.Generic;
using UnityEngine;

namespace KWC.Data
{
    [CreateAssetMenu(menuName = "KWC/Config/Wave Set")]
    public sealed class WaveSetConfig : ScriptableObject
    {
        [SerializeField] private float normalWaveDuration = 30f;
        [SerializeField] private float enemySpawnWarningTime = 0.5f;
        [SerializeField] private WaveDefinition[] waves =
        {
            new WaveDefinition(1, 5, 5f, 1, 100, 8),
            new WaveDefinition(2, 10, 3f, 1, 110, 10),
            new WaveDefinition(3, 18, 1.7f, 1, 125, 12),
            new WaveDefinition(4, 14, 2.1f, 1, 165, 16),
            new WaveDefinition(5, 17, 1.8f, 1, 210, 20),
            new WaveDefinition(6, 21, 1.4f, 1, 250, 24),
            new WaveDefinition(7, 44, 1.35f, 2, 270, 24),
            new WaveDefinition(8, 60, 1.5f, 3, 290, 26),
            new WaveDefinition(9, 75, 1.2f, 3, 310, 28),
            new WaveDefinition(10, 0, 3f, 2, 320, 30, true, false,
                "N12: Boss + approximately 20 E1. Exact E1 cap is pending. " +
                "Zero is an unset value, not a decision to spawn no E1. " +
                "Boss spawns in the first batch; this wave has no time limit.")
        };

        public float NormalWaveDuration => normalWaveDuration;
        public float EnemySpawnWarningTime => enemySpawnWarningTime;
        public IReadOnlyList<WaveDefinition> Waves => waves;

        // Minimum spawn distance, first-batch timing and partial-batch policy are pending.
    }
}
