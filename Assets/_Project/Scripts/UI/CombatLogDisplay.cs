using System.Collections.Generic;
using CBuilding.Core;
using TMPro;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// On-screen debug console that displays combat logs in real-time.
    /// Subscribes to CombatLogManager.OnEntryLogged to build a scrolling history.
    /// 
    /// SETUP: place on a Canvas with a TextMeshPro component (or child).
    /// Optional: RectTransform for sizing/positioning.
    /// </summary>
    public class CombatLogDisplay : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI logText;

        [Header("Display")]
        [SerializeField] private int maxLines = 20;
        [Tooltip("Time in seconds before a line fades out. Set to 0 to disable auto-fade.")]
        [SerializeField] private float lineFadeTime = 10f;
        [SerializeField] private bool showTimestamp = true;

        private Queue<LogEntry> _logHistory = new();

        private struct LogEntry
        {
            public string text;
            public float timestamp;
        }

        private void OnEnable()
        {
            CombatLogManager.OnEntryLogged += AddLogEntry;
        }

        private void OnDisable()
        {
            CombatLogManager.OnEntryLogged -= AddLogEntry;
        }

        private void Start()
        {
            if (logText == null)
            {
                logText = GetComponent<TextMeshProUGUI>();
            }

            if (logText == null)
            {
                Debug.LogError("CombatLogDisplay requires a TextMeshProUGUI component or reference.");
                enabled = false;
                return;
            }
        }

        private void AddLogEntry(string message)
        {
            string displayText = message;
            if (showTimestamp)
            {
                displayText = $"<size=80%>[{GetFormattedTime()}]</size> {message}";
            }

            _logHistory.Enqueue(new LogEntry
            {
                text = displayText,
                timestamp = Time.time
            });

            // Remove oldest entries if we exceed max lines
            while (_logHistory.Count > maxLines)
            {
                _logHistory.Dequeue();
            }

            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            logText.text = string.Join("\n", BuildDisplayLines());
        }

        private IEnumerable<string> BuildDisplayLines()
        {
            foreach (var entry in _logHistory)
            {
                // If fade is enabled, calculate alpha based on age
                if (lineFadeTime > 0f)
                {
                    float age = Time.time - entry.timestamp;
                    if (age > lineFadeTime)
                        continue; // Skip lines that are too old

                    float alpha = Mathf.Clamp01(1f - (age / lineFadeTime));
                    // TextMeshPro color tag with alpha (0-FF)
                    int alphaInt = Mathf.RoundToInt(alpha * 255);
                    yield return $"<alpha=#{alphaInt:X2}>{entry.text}</alpha>";
                }
                else
                {
                    yield return entry.text;
                }
            }
        }

        private void Update()
        {
            // Refresh display each frame to handle fade-out
            if (lineFadeTime > 0f)
            {
                RefreshDisplay();
            }
        }

        private string GetFormattedTime()
        {
            int minutes = (int)(Time.time / 60f);
            int seconds = (int)(Time.time % 60f);
            return $"{minutes:D2}:{seconds:D2}";
        }

        /// <summary>Clears all log entries.</summary>
        public void ClearLogs()
        {
            _logHistory.Clear();
            logText.text = "";
        }
    }
}
