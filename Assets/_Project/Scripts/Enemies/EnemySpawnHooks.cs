using System;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Static, server-only spawn notification. Lets bespoke ability runtimes react to new
    /// enemies appearing without a real "spawn zone" system existing yet — Bahadır's
    /// Ultimate (GS-9) subscribes while its "pre-infected zone" window is open and applies
    /// SpywareMark directly to anything that spawns in that window.
    /// BaseEnemy.OnNetworkSpawn raises this on the server once its data/health are ready.
    /// </summary>
    public static class EnemySpawnHooks
    {
        public static event Action<BaseEnemy> OnEnemySpawned;

        public static void RaiseSpawned(BaseEnemy enemy)
        {
            if (enemy == null) return;
            OnEnemySpawned?.Invoke(enemy);
        }
    }
}
