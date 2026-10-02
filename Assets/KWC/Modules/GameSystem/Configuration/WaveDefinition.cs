using System;
using UnityEngine;

namespace KWC.Data
{
    [Serializable]
    public sealed class WaveDefinition
    {
        [SerializeField] private int waveIndex;
        [SerializeField] private bool isBossWave;
        [SerializeField] private bool actualSpawnCountConfirmed;
        [SerializeField] private int actualSpawnCount;
        [SerializeField] private float spawnInterval;
        [SerializeField] private int spawnBatchCount;
        [SerializeField] private float e1Hp;
        [SerializeField] private float e1Attack;
        [SerializeField, TextArea] private string designNote;

        public int WaveIndex => waveIndex;
        public bool IsBossWave => isBossWave;
        public bool ActualSpawnCountConfirmed => actualSpawnCountConfirmed;
        public float SpawnInterval => spawnInterval;
        public int SpawnBatchCount => spawnBatchCount;
        public float E1Hp => e1Hp;
        public float E1Attack => e1Attack;
        public string DesignNote => designNote;

        public bool TryGetActualSpawnCount(out int count)
        {
            count = actualSpawnCountConfirmed ? actualSpawnCount : 0;
            return actualSpawnCountConfirmed;
        }

        public WaveDefinition(int index, int count, float interval, int batch,
            float hp, float attack, bool bossWave = false, bool countConfirmed = true,
            string note = "")
        {
            waveIndex = index;
            actualSpawnCount = count;
            spawnInterval = interval;
            spawnBatchCount = batch;
            e1Hp = hp;
            e1Attack = attack;
            isBossWave = bossWave;
            actualSpawnCountConfirmed = countConfirmed;
            designNote = note;
        }
    }
}
