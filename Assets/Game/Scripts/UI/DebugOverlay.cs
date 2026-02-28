using UnityEngine;
using Game.Scripts.AI;
using Game.Scripts.AI.HFSM;
using Game.Scripts.Managers;

namespace Game.Scripts.UI
{
    /// <summary>
    /// 디버그 오버레이 — 지침서 §8.3
    /// 선수 머리 위에 현재 State, Intention, Utility Score를 표시합니다.
    /// PlayerNameTag의 SetStatusText()를 활용하여 OnGUI로 렌더링됩니다.
    /// 
    /// MatchEngineConfig.DebugOverlayEnabled로 전역 On/Off 가능.
    /// </summary>
    [RequireComponent(typeof(HybridAgentController))]
    public class DebugOverlay : MonoBehaviour
    {
        [Header("Display Settings")]
        [Tooltip("오버레이 활성화")]
        public bool Enabled = true;

        [Tooltip("갱신 간격 (초). 매 프레임은 부하가 큼")]
        public float UpdateInterval = 0.2f;

        [Tooltip("상태 텍스트 표시 지속시간")]
        public float DisplayDuration = 0.5f;

        [Header("Display Options")]
        public bool ShowState = true;
        public bool ShowIntention = true;
        public bool ShowUtilityScore = true;
        public bool ShowLOD = false;
        public bool ShowStamina = false;

        // =========================================================
        // 내부
        // =========================================================
        private HybridAgentController _agent;
        private PlayerNameTag _nameTag;
        private float _updateTimer;

        private void Awake()
        {
            _agent = GetComponent<HybridAgentController>();
        }

        private void Start()
        {
            _nameTag = GetComponent<PlayerNameTag>();
            if (_nameTag == null)
                _nameTag = gameObject.AddComponent<PlayerNameTag>();
        }

        private void Update()
        {
            if (!Enabled || _nameTag == null || _agent == null) return;

            _updateTimer += Time.deltaTime;
            if (_updateTimer < UpdateInterval) return;
            _updateTimer = 0f;

            string debugText = BuildDebugText();
            _nameTag.SetStatusText(debugText, DisplayDuration + 0.1f);
        }

        private string BuildDebugText()
        {
            var sb = new System.Text.StringBuilder();

            // 1. 현재 State
            if (ShowState && _agent.StateMachine != null)
            {
                string stateName = GetStateName(_agent.StateMachine.CurrentState);
                sb.Append($"[{stateName}]");
            }

            // 2. Intention (현재 하위 상태 또는 행동)
            if (ShowIntention && _agent.StateMachine != null)
            {
                string intention = GetIntention();
                if (!string.IsNullOrEmpty(intention))
                {
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append(intention);
                }
            }

            // 3. LOD 레벨
            if (ShowLOD)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append($"L{_agent.CurrentLOD}");
            }

            // 4. Stamina
            if (ShowStamina && _agent.Stats != null)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append($"ST:{_agent.Stats.Stamina:F0}%");
            }

            return sb.ToString();
        }

        private string GetStateName(State state)
        {
            if (state == null) return "?";

            // 타입 이름에서 간략한 표시 추출
            string typeName = state.GetType().Name;

            if (typeName.Contains("Attacking")) return "ATK";
            if (typeName.Contains("Defending")) return "DEF";
            if (typeName.Contains("Transition")) return "TRN";
            if (typeName.Contains("Guarding")) return "GK";

            // 접두어 제거
            return typeName
                .Replace("RootState_", "")
                .Replace("State_", "")
                .Replace("GKState_", "GK:");
        }

        private string GetIntention()
        {
            // Ball Owner
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentBallOwner == _agent)
                return "BALL";

            // Receiver
            if (_agent.IsReceiver)
                return "RCV";

            // Busy (Kick pending)
            if (_agent.BallHandler != null && _agent.BallHandler.HasPendingKick)
                return "KICK";

            // Recovering
            if (_agent.Mover != null && _agent.Mover.IsRecoveringBall)
                return "TRAP";

            return "";
        }
    }
}
