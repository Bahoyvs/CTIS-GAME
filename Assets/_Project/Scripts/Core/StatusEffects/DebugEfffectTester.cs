using CBuilding.StatusEffects;
using UnityEngine;

public class DebugEffectTester : MonoBehaviour
{
    [Header("Hedef ve Efekt")]
    public StatusEffectController targetController;
    public EffectDataSO effectToApply;

    [ContextMenu("Apply Effect Now")]
    public void ApplyEffect()
    {
        if (targetController != null && effectToApply != null)
        {

            targetController.ApplyEffect(effectToApply, gameObject);
            Debug.Log($"<color=green>[DEBUG]</color> {effectToApply.name} efekti {targetController.gameObject.name} objesine uygulandı!");
        }
        else
        {
            Debug.LogWarning("Target Controller veya Effect Data SO eksik!");
        }
    }
}