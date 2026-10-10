using UnityEngine;

namespace KWC.Enemy
{
    // --------------------------------------------------------------------------------------------
    // 闲逛取点的默认实现：在圆面上均匀采点，并抽重选间隔。
    //
    // 用 System.Random 而不是 UnityEngine.Random：决策层不依赖引擎随机源，
    // 同一颗种子可以得到可复现的行为轨迹（便于测试）。
    // --------------------------------------------------------------------------------------------
    public sealed class RandomWanderPointSource : IWanderPointSource
    {
        private readonly System.Random _random;

        public RandomWanderPointSource(int seed)
        {
            _random = new System.Random(seed);
        }

        public bool TryGetPoint(Vector3 center, float radius, out Vector3 point)
        {
            if (radius <= 0f)
            {
                point = center;
                return false;
            }

            double angle = _random.NextDouble() * System.Math.PI * 2.0;

            // 开根号是为了让采样在圆面上均匀，否则点会堆在圆心附近。
            double r = System.Math.Sqrt(_random.NextDouble()) * radius;

            point = center + new Vector3(
                (float)(System.Math.Cos(angle) * r),
                0f,
                (float)(System.Math.Sin(angle) * r));

            return true;
        }

        public float RollInterval(float minSeconds, float maxSeconds)
        {
            if (maxSeconds <= minSeconds)
            {
                return minSeconds;
            }

            return minSeconds + ((float)_random.NextDouble() * (maxSeconds - minSeconds));
        }
    }
}
