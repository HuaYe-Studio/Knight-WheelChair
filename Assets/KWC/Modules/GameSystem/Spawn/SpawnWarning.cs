using UnityEngine;
using System;

namespace KWC.GameSystem
{
    /// <summary>
    /// 生成预警：按配置时长倒计时，完成时回调一次；取消时不回调。
    /// 由 PrefabPool 复用时必须重新 InitializeWarning，计时和完成标记都会重置。
    /// </summary>
    public class SpawnWarning:MonoBehaviour
    {
        private float spawnWarningTime;
        private float passedWarningTime = 0f;
        private bool isStart = false;
        private bool isDone = false;

        private Action onWarningDone;

        /// <summary>
        /// 由 SpawnScheduler 在租用预警时调用；重置计时并登记完成回调。
        /// 时长来自 WaveSetConfig 的 EnemySpawnWarningTime。
        /// </summary>
        public void InitializeWarning(float duration, Action onWarningDone)
        {
            passedWarningTime = 0f;
            this.onWarningDone = onWarningDone;
            spawnWarningTime = duration;
            isStart = true;
            isDone = false;
        }

        private void Update()
        {
            PlayWarning();
        }

        private void PlayWarning()
        {
            if (isStart && !isDone)
            {
                if (passedWarningTime < spawnWarningTime)
                {
                    passedWarningTime += Time.deltaTime;
                }
                else
                {
                    Action onFinished = onWarningDone;
                    EndWarning();
                    onFinished?.Invoke();
                }
            }
        }

        /// <summary>
        /// 取消尚未完成的预警并阻止完成回调；返回是否真的取消。
        /// 已完成或未开始的本轮返回 false。
        /// </summary>
        public bool CancelWarning()
        {
            if (isStart && !isDone)
            {
                EndWarning();
                return true;
            }
            else
            {
                return false;
            }
        }

        private void EndWarning()
        {
            onWarningDone = null;
            isDone = true;
            isStart = false;
        }
    }
}
