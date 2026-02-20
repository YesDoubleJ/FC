using UnityEngine;

namespace Game.Scripts.UI
{
    /// <summary>
    /// Displays the player's GameObject name above their head, always facing the camera.
    /// Attach this to each player prefab or add via HybridAgentController.
    /// </summary>
    public class PlayerNameTag : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private Vector3 offset = new Vector3(0, 4.5f, 0);
        [SerializeField] private int fontSize = 14;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color shadowColor = Color.black;
        
        private Camera mainCamera;
        private GUIStyle style;
        private GUIStyle shadowStyle;
        
        private void Start()
        {
            mainCamera = Camera.main;
            
            // Setup main text style
            style = new GUIStyle();
            style.fontSize = fontSize;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = textColor;
            style.alignment = TextAnchor.MiddleCenter;
            
            // Setup shadow style
            shadowStyle = new GUIStyle(style);
            shadowStyle.normal.textColor = shadowColor;
        }
        
        // Status Text State
        private string _statusText = "";
        private float _statusEndTime = 0f;
        private GUIStyle _statusStyle;

        public void SetStatusText(string text, float duration)
        {
            _statusText = text;
            _statusEndTime = Time.time + duration;
        }

        private void OnGUI()
        {
            if (mainCamera == null) 
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }
            
            // Init Styles if needed
            if (style == null)
            {
                style = new GUIStyle();
                style.fontSize = fontSize;
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = textColor;
                style.alignment = TextAnchor.MiddleCenter;
                
                shadowStyle = new GUIStyle(style);
                shadowStyle.normal.textColor = shadowColor;
            }

            if (_statusStyle == null)
            {
                // Status Style (Yellow)
                _statusStyle = new GUIStyle(style);
                _statusStyle.fontSize = Mathf.Max(10, fontSize - 2);
                _statusStyle.normal.textColor = Color.yellow;
            }

            // Calculate screen position
            Vector3 worldPos = transform.position + offset;
            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos);
            
            // Only render if in front of camera
            if (screenPos.z > 0)
            {
                // Convert to GUI coordinates (Y is flipped)
                float x = screenPos.x;
                float y = Screen.height - screenPos.y;
                
                // 1. Draw Name
                string playerName = gameObject.name;
                GUIContent content = new GUIContent(playerName);
                Vector2 size = style.CalcSize(content);
                Rect rect = new Rect(x - size.x / 2, y - size.y / 2, size.x, size.y);
                
                // Shadow
                Rect shadowRect = new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height);
                GUI.Label(shadowRect, content, shadowStyle);
                GUI.Label(rect, content, style);

                // 2. Draw Status (if active)
                if (!string.IsNullOrEmpty(_statusText) && Time.time < _statusEndTime)
                {
                    GUIContent statusContent = new GUIContent(_statusText);
                    Vector2 statusSize = _statusStyle.CalcSize(statusContent);
                    
                    // Position below name
                    Rect statusRect = new Rect(x - statusSize.x / 2, rect.yMax + 2, statusSize.x, statusSize.y);
                    Rect statusShadowRect = new Rect(statusRect.x + 1, statusRect.y + 1, statusRect.width, statusRect.height);

                    GUI.Label(statusShadowRect, statusContent, shadowStyle);
                    GUI.Label(statusRect, statusContent, _statusStyle);
                }
            }
        }
    }
}
