using System.Collections.Generic;
using Unity.Netcode;
using CBuilding.Heroes;

namespace CBuilding.Core
{
    /// <summary>
    /// Server-only helper for "everyone on the team" effects that skip the Delivery layer
    /// entirely (spatial deliveries can't express "every hero regardless of distance").
    /// Bahadır's Final Passive is the first consumer (GS-9 §2 architecture note).
    /// </summary>
    public static class TeamRoster
    {
        private static readonly List<BaseHero> Buffer = new();

        /// <summary>All connected, currently-alive heroes. Server-only (reads NetworkManager state).</summary>
        public static List<BaseHero> GetAllHeroes(bool aliveOnly = true)
        {
            Buffer.Clear();
            if (NetworkManager.Singleton == null) return Buffer;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                if (!client.PlayerObject.TryGetComponent(out BaseHero hero)) continue;
                if (aliveOnly && !hero.IsAlive) continue;
                Buffer.Add(hero);
            }
            return Buffer;
        }
    }
}
