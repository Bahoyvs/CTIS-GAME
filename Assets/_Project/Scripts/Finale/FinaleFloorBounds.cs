using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Spirit free-cam'inin kat başına serbest hareket sınırı (doküman §3.4: serbestlik
    /// binanın geneline değil, Runner'ların bulunduğu KATA sınırlı). Her kata bir tane
    /// yerleştirilir; SpiritVisionController aktif katın bounds'una clamp'ler.
    /// </summary>
    public class FinaleFloorBounds : MonoBehaviour
    {
        private static readonly List<FinaleFloorBounds> All = new();

        [Tooltip("0 = Bodrum, 4 = Çatı.")]
        [SerializeField, Range(0, 4)] private int floorIndex;

        [Tooltip("Katın world-space serbest kamera hacmi (merkez = bu transform).")]
        [SerializeField] private Vector3 size = new(40f, 8f, 40f);

        public int FloorIndex => floorIndex;
        public Bounds Bounds => new(transform.position, size);

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public static bool TryGetBounds(int floorIndex, out Bounds bounds)
        {
            foreach (FinaleFloorBounds b in All)
            {
                if (b.floorIndex == floorIndex) { bounds = b.Bounds; return true; }
            }
            bounds = default;
            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.7f, 0.4f, 1f, 0.8f);
            Gizmos.DrawWireCube(transform.position, size);
        }
#endif
    }
}
