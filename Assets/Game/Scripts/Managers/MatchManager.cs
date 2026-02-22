using UnityEngine;
using System.Collections;
using Game.Scripts.Visuals;
using Game.Scripts.UI;
using Game.Scripts.AI;

namespace Game.Scripts.Managers
{
    public enum MatchState
    {
        KickOff,
        Playing,
        GoalScored,
        Ended
    }

    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [Header("Settings")]
        public float halfLengthMinutes = 5f;
        
        [Header("Field Dimensions")]
        public float FieldHalfWidth = 32f;
        public float FieldHalfLength = 48f;
        
        [Header("State")]
        public MatchState CurrentState { get; private set; }
        public int HomeScore { get; private set; } 
        public int AwayScore { get; private set; } 
        public float MatchTimer { get; private set; }
        
        public HybridAgentController CurrentBallOwner { get; private set; }
        public Game.Scripts.Data.Team? LastPossessionTeam { get; private set; } = null;

        public Vector3 HomeGoalPosition { get; private set; } = new Vector3(0, 0, -50f);
        public Vector3 AwayGoalPosition { get; private set; } = new Vector3(0, 0, 50f);
        
        public Vector3 GetAttackGoalPosition(Game.Scripts.Data.Team team)
        {
            return (team == Game.Scripts.Data.Team.Home) ? AwayGoalPosition : HomeGoalPosition;
        }
        
        public Vector3 GetDefendGoalPosition(Game.Scripts.Data.Team team)
        {
            return (team == Game.Scripts.Data.Team.Home) ? HomeGoalPosition : AwayGoalPosition;
        }

        // ==================================================================================
        // AGENT CACHING
        // ==================================================================================
        private System.Collections.Generic.List<HybridAgentController> allAgents = new System.Collections.Generic.List<HybridAgentController>();
        private System.Collections.Generic.List<HybridAgentController> homeAgents = new System.Collections.Generic.List<HybridAgentController>();
        private System.Collections.Generic.List<HybridAgentController> awayAgents = new System.Collections.Generic.List<HybridAgentController>();

        public void RegisterAgent(HybridAgentController agent)
        {
            if (!allAgents.Contains(agent)) allAgents.Add(agent);
            
            if (agent.TeamID == Game.Scripts.Data.Team.Home)
            {
                if (!homeAgents.Contains(agent)) homeAgents.Add(agent);
            }
            else
            {
                if (!awayAgents.Contains(agent)) awayAgents.Add(agent);
            }
        }

        public void UnregisterAgent(HybridAgentController agent)
        {
            if (allAgents.Contains(agent)) allAgents.Remove(agent);
            if (homeAgents.Contains(agent)) homeAgents.Remove(agent);
            if (awayAgents.Contains(agent)) awayAgents.Remove(agent);
        }

        public System.Collections.Generic.List<HybridAgentController> GetAllAgents() => allAgents;
        
        public System.Collections.Generic.List<HybridAgentController> GetOpponents(Game.Scripts.Data.Team myTeam)
        {
            return (myTeam == Game.Scripts.Data.Team.Home) ? awayAgents : homeAgents;
        }
        
        public System.Collections.Generic.List<HybridAgentController> GetTeammates(Game.Scripts.Data.Team myTeam)
        {
            return (myTeam == Game.Scripts.Data.Team.Home) ? homeAgents : awayAgents;
        }
        
        // ==================================================================================

        private GameObject ballRef;
        public GameObject Ball => ballRef;

        // [NEW] Global Possession Lockout
        private float _noPossessionTimer = 0f;
        
        public void SetNoPossessionTime(float duration)
        {
            _noPossessionTimer = Mathf.Max(_noPossessionTimer, duration);
        }

        public void SetBallOwner(HybridAgentController agent)
        {
            if (_noPossessionTimer > 0f) return; // [FIX] Lockout active

            if (CurrentBallOwner == agent) return;
            CurrentBallOwner = agent;
            LastPossessionTeam = agent.TeamID; // [FIX] 트랜지션 상태에서 공격 지속 여부를 알기 위함
            agent.NotifyPossessionGained();
            logToUI($"{agent.name} has the ball!");
        }

        public void LosePossession(HybridAgentController agent)
        {
            if (CurrentBallOwner == agent)
            {
                CurrentBallOwner = null;
            }
        }

        public void ClearBallOwner()
        {
            if (CurrentBallOwner != null)
            {
                CurrentBallOwner = null;
            }
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
            
            // [NEW] Global Log Timestamp
            Debug.unityLogger.logHandler = new Game.Scripts.Utils.GameLogger();

            Time.fixedDeltaTime = 0.02f; // Standard Physics Step (0.01f is too heavy for mobile)

            // AUTO-ADD UI
            if (GetComponent<Game.Scripts.UI.ActionLogDisplay>() == null)
            {
                // gameObject.AddComponent<Game.Scripts.UI.ActionLogDisplay>(); // User might have it elsewhere
            }
        }

        private float _dynamicGoalHalfWidth = -1f;
        private float _dynamicGoalDepth = -1f;

        private void SetupGoalDimensions()
        {
            _dynamicGoalHalfWidth = 4.5f;
            _dynamicGoalDepth = 4.0f;

            Game.Scripts.Gameplay.GoalTrigger[] triggers = FindObjectsByType<Game.Scripts.Gameplay.GoalTrigger>(FindObjectsSortMode.None);
            if (triggers != null && triggers.Length > 0)
            {
                foreach (var trig in triggers)
                {
                    if (trig.transform.parent != null)
                    {
                        Transform postL = trig.transform.parent.Find("Post_L");
                        Transform postR = trig.transform.parent.Find("Post_R");
                        if (postL != null && postR != null)
                        {
                            float dist = Mathf.Abs(postL.position.x - postR.position.x) / 2f;
                            // 공략의 여유폭을 위해 넉넉히 설정
                            _dynamicGoalHalfWidth = Mathf.Max(_dynamicGoalHalfWidth, dist + 2.0f);
                        }
                        
                        Collider myCol = trig.GetComponent<Collider>();
                        if (myCol != null)
                        {
                            _dynamicGoalDepth = Mathf.Max(_dynamicGoalDepth, myCol.bounds.size.z + 2.0f);
                        }
                    }
                }
            }
        }

        private void Start()
        {
            SetupGoalDimensions();
            RefreshBallReference();
            StartKickOff();
        }
        
        public void RefreshBallReference()
        {
            ballRef = GameObject.Find("Ball");
            if (ballRef == null) ballRef = GameObject.FindGameObjectWithTag("Ball");
            
            if (ballRef == null) 
                Debug.LogError("MatchManager: BALL NOT FOUND! Check Tag 'Ball' or Name 'Ball'");
        }

        private int _lastSeconds = -1;

        private void Update()
        {
            if (CurrentState == MatchState.Playing)
            {
                MatchTimer += Time.deltaTime;
                
                int currentSeconds = Mathf.FloorToInt(MatchTimer);
                if (currentSeconds != _lastSeconds)
                {
                    if (MatchViewController.Instance != null) MatchViewController.Instance.UpdateTimer(MatchTimer);
                    _lastSeconds = currentSeconds;
                }

                // [NEW] Possession Lockout Timer
                if (_noPossessionTimer > 0f)
                {
                    _noPossessionTimer -= Time.deltaTime;
                }

                if (MatchTimer >= halfLengthMinutes * 60f)
                {
                    EndMatch();
                    return;
                }
                
                // Ball Out Check
                if (ballRef != null)
                {
                    Vector3 pos = ballRef.transform.position;
                    // [FIX] 하드코딩된 필드 크기를 변수로 교체 (기즈모 크기 32x48과 일치)
                    if (pos.y < -2.0f || Mathf.Abs(pos.x) > FieldHalfWidth)  
                    {
                         OnAttackFailed("OUT OF BOUNDS (SIDELINE)");
                    }
                    else if (Mathf.Abs(pos.z) > FieldHalfLength)
                    {
                        // 골대(Goal) 영역인 경우, 즉시 아웃 판정을 내리지 않고 GoalTrigger가 작동할 시간을 줍니다.
                        if (Mathf.Abs(pos.x) > _dynamicGoalHalfWidth)
                        {
                            // 골대를 빗나간 완전한 아웃
                            OnAttackFailed("OUT OF BOUNDS (ENDLINE)");
                        }
                        else if (Mathf.Abs(pos.z) > FieldHalfLength + _dynamicGoalDepth)
                        {
                            // 골대 안쪽으로 깊숙이 박혔는데 모종의 이유로 트리거가 안 된 경우 최후의 안전장치
                            OnAttackFailed("OUT OF BOUNDS (DEEP NET)");
                        }
                    }
                }
                else RefreshBallReference();
            }
        }

        private void EndMatch()
        {
            CurrentState = MatchState.Ended;
            if (MatchViewController.Instance != null) MatchViewController.Instance.ShowGameOver(HomeScore, AwayScore);
            // Optional: Restart logic
        }

        private Game.Scripts.Data.Team nextKickOffTeam = Game.Scripts.Data.Team.Home;

        public bool IsKickOffFirstPass { get; set; } = false;

        public void StartKickOff()
        {
            CurrentState = MatchState.KickOff;
            string teamName = (nextKickOffTeam == Game.Scripts.Data.Team.Home) ? "HOME" : "AWAY";
            logToUI($"{teamName} KICK OFF!");
            
            CurrentBallOwner = null;
            IsKickOffFirstPass = true; // [FIX] First pass must be perfect
            ResetBall();
            ResetPlayerPositions();
            StartCoroutine(KickOffRoutine());
        }

        private void ResetPlayerPositions()
        {
            var agents = allAgents;
            if (agents == null || agents.Count == 0) 
            {
                 agents = new System.Collections.Generic.List<HybridAgentController>(FindObjectsByType<HybridAgentController>(FindObjectsSortMode.None));
            }

            // 1. 일단 모두 끕니다. (여기서 isKinematic = true가 됨)
            SetPlayersActiveState(false); 

            foreach (var agent in agents)
            {
                UnityEngine.AI.NavMeshAgent nav = agent.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (nav == null) continue;
                
                Vector3 targetPos = Vector3.zero;
                Vector3 lookDir = Vector3.forward;

                // (위치 계산 로직은 그대로 유지...)
                if (agent.IsGoalkeeper || agent.assignedPosition == Game.Scripts.Tactics.FormationPosition.GK)
                {
                     if (agent.formationManager != null)
                        targetPos = agent.formationManager.GetAnchorPosition(Game.Scripts.Tactics.FormationPosition.GK, agent.TeamID);
                     else
                        targetPos = new Vector3(0, 0, (agent.TeamID == Game.Scripts.Data.Team.Home) ? -48 : 48);
                     lookDir = (Vector3.zero - targetPos).normalized;
                }
                else
                {
                    bool isKickOffTeam = (agent.TeamID == nextKickOffTeam);
                    if (agent.formationManager != null)
                        targetPos = agent.formationManager.GetKickoffPosition(agent.assignedPosition, agent.TeamID, isKickOffTeam);
                    else
                        targetPos = new Vector3((agent.GetInstanceID() % 5) * 5, 0, (agent.TeamID == Game.Scripts.Data.Team.Home) ? -10 : 10); 
                    lookDir = (Vector3.zero - targetPos).normalized;
                }

                // Warp & Rotate
                nav.Warp(targetPos);
                agent.transform.rotation = Quaternion.LookRotation(new Vector3(lookDir.x, 0, lookDir.z));
                
                // [수정된 부분] 안전하게 멈추는 3단계
                if (agent.TryGetComponent<Rigidbody>(out var rb))
                {
                    // 1. 잠금 해제 (수정하려면 켜야 함)
                    rb.isKinematic = false; 

                    // 2. 위치 이동 및 정지
                    rb.position = targetPos;
                    rb.rotation = agent.transform.rotation;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;

                    // 3. 다시 잠금 (대기 상태)
                    rb.isKinematic = true;
                }
            }
        }

        private IEnumerator KickOffRoutine()
        {
            logToUI("Ready...");
            yield return new WaitForSeconds(2.0f);
            
            SetPlayersActiveState(true);
            CurrentState = MatchState.Playing;
            logToUI("PLAY!");
        }

        private void SetPlayersActiveState(bool isActive)
        {
            var agents = allAgents;
            if (agents == null) return; 

            foreach (var agent in agents)
            {
                Rigidbody rb = agent.GetComponent<Rigidbody>();
                UnityEngine.AI.NavMeshAgent nav = agent.GetComponent<UnityEngine.AI.NavMeshAgent>();

                agent.enabled = isActive; // Brain On/Off

                if (isActive)
                {
                    // [IMPORTANT FIX] Playing State
                    if (nav != null)
                    {
                        nav.enabled = true;
                        nav.isStopped = false;
                        nav.updatePosition = true; // [FIX] TRUE로 변경하여 NavMesh가 몸을 직접 움직이게 함
                        nav.updateRotation = true;
                    }

                    if (rb != null)
                    {
                        // NavMesh와 싸우지 않게 Kinematic으로 두거나, 축을 잠급니다.
                        // 드리블 등 물리 충돌이 필요하면 isKinematic = false가 맞지만, 
                        // 이동이 튀는 걸 막기 위해 일단은 Kinematic = true로 테스트해보는 것이 좋습니다.
                        // 하지만 "축구"니까 충돌을 위해 false로 두되, NavAgent가 이기도록 합니다.
                        rb.isKinematic = false; 
                        rb.constraints = RigidbodyConstraints.FreezeRotation; // 넘어지지 않게
                    }
                }
                else
                {
                    // [IMPORTANT FIX] Ready State
                    if (nav != null)
                    {
                        nav.isStopped = true;
                        nav.velocity = Vector3.zero;
                        // nav.enabled = false; // 끄지 말고 멈추기만 함 (Warp를 위해)
                    }

                    if (rb != null)
                    {
                        // [핵심 수정] 순서 변경: 멈춤 -> 얼림
                        // Kinematic 상태에서는 velocity 수정이 불가능하므로, 켜져있다면 끄고 수정해야 함
                        if (rb.isKinematic) rb.isKinematic = false;
                        
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true; // 마지막에 잠금
                    }
                }
            }
        }

        // ... [Goal, AttackFailed Logic은 기존과 동일] ...
        private bool isProcessingGoal = false;

        public void OnGoalScored(bool isHomeTeam)
        {
            if (CurrentState != MatchState.Playing || isProcessingGoal) return;
            isProcessingGoal = true;
            
            if (isHomeTeam) { HomeScore++; logToUI("GOAL! HOME SCORES!"); nextKickOffTeam = Game.Scripts.Data.Team.Away; }
            else { AwayScore++; logToUI("GOAL! AWAY SCORES!"); nextKickOffTeam = Game.Scripts.Data.Team.Home; }

            StartCoroutine(GoalResetRoutine());
        }
        
        public void OnAttackFailed(string reason)
        {
             if (CurrentState != MatchState.Playing || isProcessingGoal) return;
            isProcessingGoal = true;
            logToUI($"{reason}");
            nextKickOffTeam = (nextKickOffTeam == Game.Scripts.Data.Team.Home) ? Game.Scripts.Data.Team.Away : Game.Scripts.Data.Team.Home;
            StartCoroutine(GoalResetRoutine()); 
        }

        private IEnumerator GoalResetRoutine()
        {
            yield return new WaitForSeconds(3.0f);
            StartKickOff();
            isProcessingGoal = false;
        }

        private void ResetBall()
        {
            if (ballRef == null) RefreshBallReference();
            if (ballRef != null)
            {
                Rigidbody rb = ballRef.GetComponent<Rigidbody>();
                if (rb != null) 
                { 
                    // [핵심 수정] 순서 변경: 멈춤 -> 얼림
                    if (rb.isKinematic) rb.isKinematic = false;
                    
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true; // 이제 얼림
                }
                
                ballRef.transform.position = new Vector3(0, 0.5f, 0); 
                ballRef.transform.rotation = Quaternion.identity;
                
                // 다시 켤 때의 안전장치
                if (rb != null) 
                { 
                    rb.position = new Vector3(0, 0.5f, 0);
                    UnityEngine.Physics.SyncTransforms(); 
                    
                    // 경기 시작 직전이므로 풀기
                    rb.isKinematic = false;
                    rb.WakeUp(); 
                }
            }
        }
        
        public void OnSave()
        {
            // GK가 막았을 때 로직
             if (ballRef == null) RefreshBallReference();
             // (필요 시 구현)
        }

        private void logToUI(string msg)
        {
            Debug.Log($"[MatchManager] {msg}");
            if (MatchViewController.Instance != null) { MatchViewController.Instance.LogAction(msg); MatchViewController.Instance.UpdateScore(HomeScore, AwayScore); }
        }
    }
}