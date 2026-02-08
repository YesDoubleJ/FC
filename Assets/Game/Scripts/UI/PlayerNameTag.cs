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
        
        private void OnGUI()
        {
            if (mainCamera == null) 
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
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
                
                // Get player name
                string playerName = gameObject.name;
                
                // Calculate text size
                Vector2 size = style.CalcSize(new GUIContent(playerName));
                Rect rect = new Rect(x - size.x / 2, y - size.y / 2, size.x, size.y);
                Rect shadowRect = new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height);
                
                // Draw shadow first, then text
                GUI.Label(shadowRect, playerName, shadowStyle);
                GUI.Label(rect, playerName, style);
            }
        }
    }
}
