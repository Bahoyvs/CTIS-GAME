using System.Collections.Generic;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// SpawnDirector hedefleme modu. Eski tasarımdaki DefenderCoreOnly KALDIRILDI —
    /// Final Phase'de düşmanlar yalnızca Runner'ları hedefler (EscapeCorridorOnly).
    /// </summary>
    public enum SpawnDirectorMode : byte
    {
        Normal,             // Section 1-3: tüm bağlı oyuncular hedef havuzudur.
        EscapeCorridorOnly  // Section 4: sadece RegisterTargetPool ile verilen Runner'lar.
    }

    /// <summary>
    /// FinaleManager'ın SpawnDirector ile konuştuğu daraltılmış arayüz (mimari doküman §4).
    /// Server-only; tüm çağrılar server dışında no-op olmalıdır.
    /// </summary>
    public interface ISpawnDirectorRouting
    {
        /// <summary>Mod değişimi. Normal'e dönüş target pool + kat filtresi + encounter override'ı sıfırlar.</summary>
        void SetMode(SpawnDirectorMode mode);

        /// <summary>Düşman hedefleme / spawn mesafe bandı bu clientId'lere göre hesaplanır (hayattaki Runner'lar).</summary>
        void RegisterTargetPool(IReadOnlyList<ulong> clientIds);

        /// <summary>
        /// Kat bazlı spawn/despawn: önceki katın kayıtlı düşmanları despawn edilir,
        /// yalnızca FloorSpawnNodeTag'i eşleşen node'lardan spawn yapılır. -1 = filtre yok.
        /// </summary>
        void SetActiveFloor(int floorIndex);
    }
}
