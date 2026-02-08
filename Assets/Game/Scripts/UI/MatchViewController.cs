using UnityEngine;

namespace Game.Scripts.UI
{
    public class MatchViewController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private UnityEngine.UI.Text stadiumNameText; // Optional
        [SerializeField] private UnityEngine.UI.Text scoreText;      // "ST 0 : 0 GK"
        [SerializeField] private UnityEngine.UI.Text timerText;      // "00:00"
        [SerializeField] private UnityEngine.UI.Text actionLogText;  // Central Announcement
        [SerializeField] private UnityEngine.UI.Text skillLogText;   // Skill Activations (Above Action Log)

        [Header("Game Over UI")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private UnityEngine.UI.Text finalScoreText;

        [Header("Settings")]
        [SerializeField] private float logDuration = 2.0f;
        [SerializeField] private float skillLogDuration = 2.0f;

        private float logTimer = 0f;
        private float skillLogTimer = 0f;
        
        private struct SkillLogEntry {
            public string message;
            public string colorHex;
            public float timestamp;
        }
        private System.Collections.Generic.List<SkillLogEntry> _skillLogs = new System.Collections.Generic.List<SkillLogEntry>();

        public static MatchViewController Instance { get; private set; }

        // FAST PLAY MODE: Reset Static Var
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private bool _isPriorityLogActive = false;

        private void Start()
        {
            // Init
            if (actionLogText) 
            {
                actionLogText.text = "";
                
                // FORCE UI POSITION: Height 400 (User Request)
                RectTransform rt = actionLogText.rectTransform;
                if (rt != null)
                {
                    // Bottom Center Anchors (Standard)
                    rt.anchorMin = new Vector2(0.5f, 0.0f);
                    rt.anchorMax = new Vector2(0.5f, 0.0f);
                    rt.pivot = new Vector2(0.5f, 0.0f);
                    
                    // Position: -150px (User Request)
                    // Note: With Bottom Anchor and Bottom Pivot, positive is UP. 
                    // Negative is DOWN (off-screen).
                    // User requested -150 specifically while keeping "Bottom Center" anchor.
                    rt.anchoredPosition = new Vector2(0, -150f);
                    
                    // Height 400 explicitly requested
                    rt.sizeDelta = new Vector2(1000, 400); 
                    
                    // User Request: Scale 0.75
                    rt.localScale = new Vector3(0.75f, 0.75f, 1f);
                    
                    // Reset Offsets (Important when switching from offset-based layout)
                    // (Actually sizeDelta + anchors handles this, but being clean)
                    
                    actionLogText.alignment = TextAnchor.LowerCenter;
                    
                    actionLogText.fontSize = 40; 
                    actionLogText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    actionLogText.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }
            
            // Initialize Skill Log (Above Action Log)
            if (skillLogText)
            {
                skillLogText.text = "";
                
                RectTransform skillRt = skillLogText.rectTransform;
                if (skillRt != null)
                {
                    // Explicitly set anchors to match action log (Bottom-Center)
                    skillRt.anchorMin = new Vector2(0.5f, 0.0f);
                    skillRt.anchorMax = new Vector2(0.5f, 0.0f);
                    
                    // Top Center Pivot for downward growth
                    skillRt.pivot = new Vector2(0.5f, 1.0f);
                    
                    // Height 400 for 3 lines with safe spacing
                    float skillHeight = 400f;
                    skillRt.sizeDelta = new Vector2(1000, skillHeight);
                    
                   // Skill Log: Exactly at transition position (Y=0)
            // Pivot at Top-Center (0.5, 1.0) so it grows DOWNWARDS
            skillLogText.rectTransform.pivot = new Vector2(0.5f, 1.0f);
            skillLogText.rectTransform.anchoredPosition = new Vector2(0, 0f);
            skillLogText.alignment = TextAnchor.UpperCenter;
            skillLogText.fontSize = 30; // Newest line size
            skillLogText.resizeTextForBestFit = false;
                    // Base color white (overridden by rich text)
                    skillLogText.color = Color.white; 
                    
                    skillLogText.supportRichText = true;
                    skillLogText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    skillLogText.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }
            
            if (gameOverPanel) gameOverPanel.SetActive(false);
            
            // FIX: Score Panel width too small for "HOME 0 : 0 AWAY"
            if (scoreText != null)
            {
                RectTransform stRt = scoreText.rectTransform;
                if (stRt != null)
                {
                    // Width 350 (User Request)
                    var currentSize = stRt.sizeDelta;
                    stRt.sizeDelta = new Vector2(350, currentSize.y);
                }
            }
            
            UpdateScore(0, 0);
            UpdateTimer(0);
        }

        public void LogAction(string message, bool isPriority = false)
        {
            if (actionLogText != null)
            {
                // If a priority log is showing, don't overwrite it with normal spam
                if (_isPriorityLogActive && !isPriority) return;
                
                actionLogText.text = message;
                actionLogText.enabled = true;
                logTimer = logDuration;
                
                _isPriorityLogActive = isPriority;
            }
        }
        
        public void LogSkill(string skillMessage, Game.Scripts.Data.Team team)
        {
            if (skillLogText == null) return;

            // Determine Color based on Team
            // Home: Blue (#3399FF), Away: Red (#FF3333)
            string colorHex = (team == Game.Scripts.Data.Team.Home) ? "#3399FF" : "#FF3333";
            
            // Emoji prefix for visual flair
            string emoji = "⚡";
            if (skillMessage.Contains("Tackle")) emoji = "🥋";
            if (skillMessage.Contains("Boost")) emoji = "🔥";
            
            string coloredMessage = $"<color={colorHex}>{emoji} {skillMessage}</color>";
            
            _skillLogs.Insert(0, new SkillLogEntry { message = coloredMessage, timestamp = Time.time });

            if (_skillLogs.Count > 3)
            {
                _skillLogs.RemoveAt(_skillLogs.Count - 1);
            }

            // Reset timer so skill log disappears after duration
            skillLogTimer = skillLogDuration;

            UpdateSkillLogDisplay();
        }

        private void UpdateSkillLogDisplay()
        {
            if (skillLogText == null) return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < _skillLogs.Count; i++)
            {
                // Multi-line Font Scaling (Halved from original req)
                // Line 0 (Newest): 30
                // Line 1: 22
                // Line 2 (Oldest): 15
                int size = 30;
                if (i == 1) size = 22;
                if (i == 2) size = 15;

                sb.AppendLine($"<size={size}>{_skillLogs[i].message}</size>");
            }

            skillLogText.text = sb.ToString();
        }
        
        public void ShowGameOver(int stScore, int gkScore)
        {
            if (gameOverPanel)
            {
                gameOverPanel.SetActive(true);
                if (finalScoreText)
                {
                    string winner = (stScore > gkScore) ? "HOME WINS!" : (gkScore > stScore) ? "AWAY WINS!" : "DRAW";
                    finalScoreText.text = $"GAME OVER\n{winner}\n{stScore} - {gkScore}";
                }
            }
        }

        public void UpdateScore(int st, int gk)
        {
            if (scoreText != null)
            {
                // Note: st = HomeScore, gk = AwayScore (from MatchManager call)
                // Display as: AWAY {AwayScore} : {HomeScore} HOME
                scoreText.text = $"AWAY {gk} : {st} HOME";
            }
        }

        public void UpdateTimer(float timeInSeconds)
        {
            if (timerText != null)
            {
                int minutes = Mathf.FloorToInt(timeInSeconds / 60F);
                int seconds = Mathf.FloorToInt(timeInSeconds - minutes * 60);
                timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }
        }

        private void Update()
        {
            // Handle Action Log cleanup
            if (logTimer > 0)
            {
                logTimer -= Time.deltaTime;
                if (logTimer <= 0)
                {
                    if (actionLogText) actionLogText.text = "";
                    _isPriorityLogActive = false;
                }
            }
            
            // Handle Skill Log cleanup
            if (skillLogTimer > 0)
            {
                skillLogTimer -= Time.deltaTime;
                if (skillLogTimer <= 0)
                {
                    if (skillLogText) skillLogText.text = "";
                    _skillLogs.Clear();
                }
            }
        }
    }
}
