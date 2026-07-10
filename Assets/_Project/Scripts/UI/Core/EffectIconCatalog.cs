using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — resolves the EffectHash values replicated by StatusEffectController
    /// back to their EffectDataSO assets (icon, duration) on clients.
    /// Register every EffectDataSO the game can apply; one asset shared by the
    /// player HUD status row and every enemy billboard.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/UI/Effect Icon Catalog", fileName = "EffectIconCatalog")]
    public class EffectIconCatalog : ScriptableObject
    {
        [SerializeField] private EffectDataSO[] effects;

        public EffectDataSO GetByHash(int effectHash)
        {
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null && effects[i].EffectHash == effectHash)
                    return effects[i];
            return null;
        }
    }
}
