using CBuilding.Abilities;
using CBuilding.Data;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — bottom-center ability bar. Left to right: RMB (Feature), Q (Skill1),
    /// E (Skill2), F (Ultimate). Passive/FinalPassive have no HUD circle.
    ///
    /// Subscribes once to the local AbilityController's owner-side mirror events
    /// and routes them to the matching widget. Purely event-driven.
    /// </summary>
    public class AbilityBarController : MonoBehaviour
    {
        [Header("Order: RMB (Feature), Q (Skill1), E (Skill2), F (Ultimate)")]
        [SerializeField] private AbilitySlotWidget rmbSlot;
        [SerializeField] private AbilitySlotWidget qSlot;
        [SerializeField] private AbilitySlotWidget eSlot;
        [SerializeField] private AbilitySlotWidget fSlot;

        private AbilityController bound;

        public void Bind(AbilityController abilities, HeroRole role)
        {
            Unbind();
            if (abilities == null) return;

            bound = abilities;
            Color classColor = UIPalette.GetClassColor(role);

            rmbSlot.Setup(bound.GetAssignedData(AbilitySlot.Feature), classColor);
            qSlot.Setup(bound.GetAssignedData(AbilitySlot.Skill1), classColor);
            eSlot.Setup(bound.GetAssignedData(AbilitySlot.Skill2), classColor);
            fSlot.Setup(bound.GetAssignedData(AbilitySlot.Ultimate), classColor);

            bound.OnCooldownUpdated += HandleCooldownUpdated;
            bound.OnChargesUpdated += HandleChargesUpdated;
        }

        public void Unbind()
        {
            if (bound != null)
            {
                bound.OnCooldownUpdated -= HandleCooldownUpdated;
                bound.OnChargesUpdated -= HandleChargesUpdated;
                bound = null;
            }
            rmbSlot.Clear();
            qSlot.Clear();
            eSlot.Clear();
            fSlot.Clear();
        }

        private void HandleCooldownUpdated(AbilitySlot slot, float remaining, float duration)
        {
            var widget = GetWidget(slot);
            if (widget != null) widget.HandleCooldownUpdated(remaining, duration);
        }

        private void HandleChargesUpdated(AbilitySlot slot, int charges)
        {
            var widget = GetWidget(slot);
            if (widget != null) widget.HandleChargesUpdated(charges);
        }

        private AbilitySlotWidget GetWidget(AbilitySlot slot) => slot switch
        {
            AbilitySlot.Feature  => rmbSlot,
            AbilitySlot.Skill1   => qSlot,
            AbilitySlot.Skill2   => eSlot,
            AbilitySlot.Ultimate => fSlot,
            _ => null // Passive / FinalPassive: no HUD circle
        };

        private void OnDestroy() => Unbind();
    }
}
