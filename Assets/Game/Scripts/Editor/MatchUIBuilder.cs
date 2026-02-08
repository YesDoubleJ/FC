using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using Game.Scripts.UI;

namespace Game.Scripts.Editor
{
    public class MatchUIBuilder
    {
        [MenuItem("Tools/Lotto/Create Match UI")]
        public static void CreateMatchUI()
        {
            // 1. Find or Create Canvas
            GameObject canvasObj = GameObject.Find("MatchCanvas");
            Canvas canvas;
            if (canvasObj == null)
            {
                canvasObj = new GameObject("MatchCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasObj.AddComponent<GraphicRaycaster>();
            }
            else
            {
                canvas = canvasObj.GetComponent<Canvas>();
            }

            // 2. Score Panel (Top Center)
            GameObject scorePanel = CreatePanel(canvas.transform, "ScorePanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(300, 80), new Vector2(0, -50));
            Text scoreText = CreateText(scorePanel.transform, "ScoreText", "ST 0 : 0 GK", 36, FontStyle.Bold, TextAnchor.MiddleCenter);

            // 3. Timer Panel (Top Right or Under Score)
            GameObject timerPanel = CreatePanel(canvas.transform, "TimerPanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(200, 50), new Vector2(0, -110));
            Text timerText = CreateText(timerPanel.transform, "TimerText", "00:00", 24, FontStyle.Normal, TextAnchor.MiddleCenter);

            // 4. Announcement Log (Center Screen)
            GameObject logPanel = CreatePanel(canvas.transform, "ActionLogPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(600, 100), Vector2.zero);
            Text logText = CreateText(logPanel.transform, "ActionLogText", "", 48, FontStyle.Bold, TextAnchor.MiddleCenter);
            logText.color = Color.yellow;
            logText.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2); // Shadow

            // 5. Game Over Panel (Full Screen Overlay)
            GameObject gameOverPanel = CreatePanel(canvas.transform, "GameOverPanel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image bg = gameOverPanel.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.8f); // Dark background
            
            Text gameOverText = CreateText(gameOverPanel.transform, "FinalScoreText", "GAME OVER", 60, FontStyle.Bold, TextAnchor.MiddleCenter);
            gameOverText.color = Color.white;
            gameOverPanel.SetActive(false); // Hide by default

            // 6. Link to MatchViewController
            MatchViewController mvc = Object.FindFirstObjectByType<MatchViewController>();
            if (mvc == null)
            {
                GameObject mvcObj = new GameObject("MatchViewController");
                mvc = mvcObj.AddComponent<MatchViewController>();
            }

            Undo.RecordObject(mvc, "Link Match UI");
            
            // Use SerializedObject to assign private fields
            SerializedObject serializedMvc = new SerializedObject(mvc);
            serializedMvc.FindProperty("scoreText").objectReferenceValue = scoreText;
            serializedMvc.FindProperty("timerText").objectReferenceValue = timerText;
            serializedMvc.FindProperty("actionLogText").objectReferenceValue = logText;
            serializedMvc.FindProperty("gameOverPanel").objectReferenceValue = gameOverPanel;
            serializedMvc.FindProperty("finalScoreText").objectReferenceValue = gameOverText;
            serializedMvc.ApplyModifiedProperties();

            Debug.Log("Match UI Created and Linked Successfully!");
            Selection.activeGameObject = canvasObj;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPosition)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            
            // Optional Background (Transparent for now, or semi-black)
            // Image img = panel.AddComponent<Image>();
            // img.color = new Color(0, 0, 0, 0.5f);

            return panel;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);
            
            // Stretch to fill parent panel
            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text text = textObj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = Color.white;
            
            return text;
        }
    }
}
