using UnityEngine;
using System;

namespace KWC.GameSystem
{
    public class SpawnWarning:MonoBehaviour
    {
        private float spawnWarningTime;
        private float passedWarningTime = 0f;
        private bool isStart = false;
        private bool isDone = false;

        private Action OnWarningDone;

        public void InitializeWarning(float duration, Action onWarningDone)
        {
            passedWarningTime = 0f;
            OnWarningDone = onWarningDone;
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
                    OnWarningDone?.Invoke();
                    EndWarning();
                }
            }
        }

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
            OnWarningDone = null;
            isDone = true;
            isStart = false;
        }
    }
}
