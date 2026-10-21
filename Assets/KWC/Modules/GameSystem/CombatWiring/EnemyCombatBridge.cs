using System;
using KWC.Enemy;
using KWC.Combat;
using UnityEngine;

namespace KWC.GameSystem
{
    public class EnemyCombatBridge : MonoBehaviour,ICombatBridge
    {
        private Enemy1Controller E1Controller;
        private Health E1Health;
        private PlayerInitializer playerInitializer;
        private SpawnScheduler spawnScheduler;

        public void Initialize(Enemy1Controller  controller,Health health,PlayerInitializer player,SpawnScheduler scheduler)
        {
            E1Controller = controller;
            E1Health = health;
            playerInitializer = player;
            spawnScheduler = scheduler;

            if (E1Health == null)
            {
                Debug.LogError("assign the E1Health before starting initialization.", this);
                return;
            }

            if (playerInitializer == null)
            {
                Debug.LogError("assign the playerInitializer before starting initialization.", this);
                return;
            }

            if (spawnScheduler == null)
            {
                Debug.LogError("assign the spawnScheduler before starting initialization.", this);
                return;
            }

            if (E1Controller == null)
            {
                Debug.LogError("assign the E1Controller before starting initialization.", this);
                return;
            }

            E1Health.HealthChanged -= HandleHealthChanged;
            E1Health.HealthChanged += HandleHealthChanged;

            E1Health.Died -= HandleDied;
            E1Health.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (E1Health == null)
                return;

            E1Health.HealthChanged -= HandleHealthChanged;
            E1Health.Died -= HandleDied;
        }

        private void HandleHealthChanged(int lifeId, float currentHP, float maxHP)
        {
            ReportEnemyHealth(E1Controller,lifeId, currentHP, maxHP);
        }

        private void HandleDied(int lifeId)
        {
            ReportEnemyDied(E1Controller,lifeId);
        }

        public void ReportEnemyHealth(MonoBehaviour enemy, int lifeId, float currentHealth, float maxHealth)
        {
            if(E1Controller == enemy)
                E1Controller.OnHealthReported(lifeId, currentHealth, maxHealth);
        }

        public void ReportEnemyDied(MonoBehaviour enemy,int lifeId)
        {
            if(E1Controller == enemy && E1Controller != null)
                E1Controller.ReportEnemyDied(lifeId);
        }

        public bool TrySubmitPlayerDamage(PlayerDamageRequest request)
        {
            if(request.Attacker !=E1Controller)
                return false;

            playerInitializer.TryReceiveDamage(request);

            return true;
        }

        public void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss, int lifeId)
        {
            if(E1Controller != enemy)
                return;

            if (isBoss)
            {
                Debug.Log("Boss未实现");
            }

            spawnScheduler.EnqueueDeath(E1Controller.gameObject, lifeId);
        }
    }
}
