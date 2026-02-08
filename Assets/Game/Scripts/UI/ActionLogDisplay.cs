using UnityEngine;
using System.Collections.Generic;

namespace Game.Scripts.UI
{
    /// <summary>
    /// Simple On-Screen Log for AI Actions (Shoot, Pass, Dribble).
    /// Displays last 5 messages in bottom-left corner.
    /// </summary>
    public class ActionLogDisplay : MonoBehaviour
    {
        public static ActionLogDisplay Instance { get; private set; }

        private Queue<string> _logQueue = new Queue<string>();
        private const int MaxLogs = 15; // Increased from 5 to 15 to prevent flushing

        private GUIStyle _style;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public static void AddLog(string message)
        {
            if (Instance != null)
            {
                Instance.InternalAddLog(message);
            }
        }

        private string _lastLogMessage = "";
        private float _lastLogTime = 0f;

        private void InternalAddLog(string message)
        {
            // Debounce: Ignore exact duplicates within 2.0 seconds
            if (message == _lastLogMessage && Time.time - _lastLogTime < 2.0f)
            {
                return;
            }

            _lastLogMessage = message;
            _lastLogTime = Time.time;

            // Format: [Time] Message
            string time = System.DateTime.Now.ToString("HH:mm:ss");
            string entry = $"[{time}] {message}";

            _logQueue.Enqueue(entry);

            if (_logQueue.Count > MaxLogs)
            {
                _logQueue.Dequeue();
            }
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle();
                _style.normal.textColor = Color.white;
                _style.fontSize = 20; // Readable size
                _style.fontStyle = FontStyle.Bold;
            }

            // Bottom Left Corner
            // Start Y at Screen.height - (MaxLogs * LineHeight) - Padding
            float lineHeight = 25f;
            float startY = Screen.height - (MaxLogs * lineHeight) - 50f;
            float startX = 20f;

            // Draw Background Box (Optional, for readability)
            // GUI.Box(new Rect(startX - 5, startY - 5, 400, MaxLogs * lineHeight + 10), "Action Log");

            int i = 0;
            foreach (var log in _logQueue)
            {
                GUI.Label(new Rect(startX, startY + (i * lineHeight), 500, lineHeight), log, _style);
                i++;
            }
        }
    }
}
