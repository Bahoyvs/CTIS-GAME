using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Data
{
    /// <summary>
    /// A pickup/consumable item. Pure data — the effect is just a list of stat modifiers
    /// applied by HeroStatController. Adding a new item = creating a new asset, zero code.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "C-Building/Data/Item")]
    public class ItemData : ScriptableObject
    {
        [Header("Identity")]
        public string ItemName = "Unnamed Item";
        [TextArea] public string Description;
        public Sprite Icon;

        [Header("Behaviour")]
        [Tooltip("Consumables apply their modifiers permanently for the run (e.g. +Max HP potion). " +
                 "Non-consumables are equipment-style: their modifiers can be removed again by source.")]
        public bool IsConsumable = true;

        [Header("Effects")]
        public List<StatModifierDefinition> Modifiers = new List<StatModifierDefinition>();
    }
}
