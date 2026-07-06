using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Data
{
    /// <summary>
    /// One node in a hero's skill tree. Nodes reference their prerequisites directly
    /// (asset references = foreign keys), so the tree topology lives entirely in data.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkillNode", menuName = "C-Building/Data/Skill Node")]
    public class SkillNodeData : ScriptableObject
    {
        [Header("Identity")]
        public string NodeName = "Unnamed Skill";
        [TextArea] public string Description;
        public Sprite Icon;

        [Header("Tree Topology")]
        [Tooltip("All of these must be unlocked before this node can be taken.")]
        public List<SkillNodeData> Prerequisites = new List<SkillNodeData>();
        [Min(0)] public int Cost = 1;

        [Header("Effects")]
        public List<StatModifierDefinition> Modifiers = new List<StatModifierDefinition>();
    }
}
