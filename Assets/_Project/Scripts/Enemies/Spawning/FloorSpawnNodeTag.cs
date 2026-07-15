using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// Final Phase binasındaki SpawnNode'ları katlarına etiketler. SpawnDirector,
    /// SetActiveFloor(n) aktifken yalnızca FloorIndex == n olan node'ları kullanır.
    /// Section 1-3 sahnelerindeki node'lara eklenmesine gerek yoktur (filtre -1 iken pasif).
    /// SpawnNode ile aynı GameObject'e eklenir.
    /// </summary>
    [RequireComponent(typeof(SpawnNode))]
    public class FloorSpawnNodeTag : MonoBehaviour
    {
        [Tooltip("0 = Bodrum, 4 = Çatı.")]
        [SerializeField, Range(0, 4)] private int floorIndex;

        public int FloorIndex => floorIndex;
    }
}
