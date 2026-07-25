using TMPro;
using UnityEngine;

namespace CBuilding.UI
{
    public class PlayerEconomyUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI pointsText;

        public static PlayerEconomyUI Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (pointsText == null)
            {
                pointsText = GetComponent<TextMeshProUGUI>();
            }

            if (pointsText == null)
            {
                Debug.LogError("PlayerEconomyUI requires a TextMeshProUGUI reference or component.", this);
                enabled = false;
            }
        }

        public void UpdatePointsDisplay(int currentPoints)
        {
            if (pointsText != null)
            {
                pointsText.text = $"PIXEL POINTS: {currentPoints}";
            }
        }
    }
}
