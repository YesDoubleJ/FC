using UnityEngine;
using Game.Scripts.AI.HFSM;
using Game.Scripts.AI.States;
using Game.Scripts.Data;
using Game.Scripts.Tactics;
using Game.Scripts.Managers;

namespace Game.Scripts.AI
{
    /// <summary>
    /// The Brain of the Agent.
    /// Coordinators specialized components: AgentMover, AgentBallHandler, AgentSkillSystem.
    /// Handles High-Level Decision Making (HFSM).
    /// </summary>
    [RequireComponent(typeof(AgentMover))]
    [RequireComponent(typeof(AgentBallHandler))]
    [RequireComponent(typeof(AgentSkillSystem))]
    [RequireComponent(typeof(PlayerStats))]
    public class HybridAgentController : MonoBehaviour
    {
        // Components
        public AgentMover Mover { get; private set; }
        public AgentBallHandler BallHandler { get; private set; }
        public AgentSkillSystem SkillSystem { get; private set; }
        public PlayerStats Stats { get; private set; }
        public UnityEngine.AI.NavMeshAgent NavAgent => Mover.GetComponent<UnityEngine.AI.NavMeshAgent>();
        public Rigidbody VelocityRb => Mover.GetComponent<Rigidbody>();
        
        // HFSM
        public StateMachine StateMachine { get; private set; }
        public RootState_Attacking AttackingState { get; private set; }
        public RootState_Defending DefendingState { get; private set; }
        public RootState_Transition TransitionState { get; private set; }

        // Tactics
        [Header("Tactics")]
        public Team TeamID;
        public FormationManager formationManager;
        public FormationPosition assignedPosition;
        public TacticalRole tacticalRole;
        public bool IsGoalkeeper { get; private set; }

        // Tuning Config
        [Header("Tuning")]
        public MatchEngineConfig config;

        // Visuals
        private Renderer _cachedRenderer;
        private Color _originalColor;
        private Game.Scripts.Visuals.AgentAnimationController _animController;
        public Game.Scripts.Visuals.AgentAnimationController AnimController => _animController;

        // Shared Ball Accessor (Bridge)
        public Rigidbody BallRb => BallHandler.BallRb;

        // Lockout Logic
        private float _lastActionGlobalTime = -999f;
        public bool IsBusy => Mover.IsBusy || BallHandler.HasPendingKick || (Time.time < _lastActionGlobalTime + (config ? config.ActionLockoutTime : 0.5f));

        // Teammate Cache
        public System.Collections.Generic.List<HybridAgentController> Teammates { get; private set; } = new System.Collections.Generic.List<HybridAgentController>();

        // Pass Reception
        public bool IsReceiver { get; private set; }
        public Vector3 IncomingBallPos { get; private set; }
        private Coroutine _receiverTimeoutCoroutine;
        
        // Pass History
        public HybridAgentController receivedBallFrom;
        public float ballReceivedTime;

        private void Awake()
        {
            // Initialize Components
            Mover = GetComponent<AgentMover>();
            BallHandler = GetComponent<AgentBallHandler>();
            SkillSystem = GetComponent<AgentSkillSystem>();
            Stats = GetComponent<PlayerStats>();
            
            // Manual Init
            Mover.Initialize(this);
            BallHandler.Initialize(this);
            SkillSystem.Initialize(this);

            if (Stats == null) Stats = gameObject.AddComponent<PlayerStats>();
            
            IsGoalkeeper = GetComponent<GoalkeeperController>() != null;
            _animController = GetComponentInChildren<Game.Scripts.Visuals.AgentAnimationController>();
            
            if (formationManager == null) formationManager = FindFirstObjectByType<FormationManager>();

            // Initialize HFSM
            StateMachine = new StateMachine();
            AttackingState = new RootState_Attacking(this, StateMachine);
            DefendingState = new RootState_Defending(this, StateMachine);
            TransitionState = new RootState_Transition(this, StateMachine);

            // Visual Debug Init
            _cachedRenderer = GetComponentInChildren<SkinnedMeshRenderer>(); // Priority
            if (_cachedRenderer == null) _cachedRenderer = GetComponentInChildren<Renderer>();
            if (_cachedRenderer != null) _originalColor = _cachedRenderer.material.color;
        }

        protected virtual void Start()
        {
            // Settings Fallback
            if (config == null)
            {
                 config = Resources.Load<MatchEngineConfig>("DefaultMatchEngineConfig");
                 // If still null, we might need a distinct default asset, but let's assume one exists or is assigned.
                 if (config == null) Debug.LogWarning($"[HybridAgentController] {name}: MatchEngineConfig not assigned and DefaultMatchEngineConfig not found!");
            }

            // Initialize State
            StateMachine.Initialize(AttackingState);
            
            CacheTeammates();
            
            // Register
            MatchManager.Instance?.RegisterAgent(this);
            if (GetComponent<Game.Scripts.UI.PlayerNameTag>() == null) gameObject.AddComponent<Game.Scripts.UI.PlayerNameTag>();
        }

        private void OnDestroy()
        {
            MatchManager.Instance?.UnregisterAgent(this);
        }

        protected virtual void Update()
        {
            // Propagate Ticks
            Mover.Tick();
            BallHandler.Tick();
            SkillSystem.Tick();
            
            // [FIX] Update State Transition BEFORE State Execution to prevent 1-frame lag
            UpdateStateBasedOnPossession();

            // [FIX] SAFETY LOCK: Prevent collision with teammate ball owner
            // If I am too close to a teammate with the ball, STOP immediately.
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentBallOwner != null)
            {
                var owner = MatchManager.Instance.CurrentBallOwner;
                if (owner != this && owner.TeamID == this.TeamID)
                {
                    float dist = Vector3.Distance(transform.position, owner.transform.position);
                    float safeDist = MatchManager.Instance.IsKickOffFirstPass ? 3.0f : 2.0f; // Stricter on KickOff
                    
                    if (dist < safeDist)
                    {
                        // FORCE STOP / SEPARATION
                        Mover.Stop();
                        // Optional: Add small pushback? 
                        // For now, stopping is enough to kill the "Chase" momentum.
                    }
                }
            }
            
            // HFSM
            StateMachine.Update();
            
            UpdateDebugVisuals();
        }

        private void FixedUpdate()
        {
            Mover.FixedTick();       // Physics Movement
            BallHandler.FixedTick(); // Physics Kicks & Dribble
            StateMachine.FixedUpdate();
        }

        public void NotifyActionExecution()
        {
            _lastActionGlobalTime = Time.time;
        }

        // =========================================================
        // STATE MANAGEMENT
        // =========================================================
        protected virtual void UpdateStateBasedOnPossession()
        {
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentState != MatchState.Playing) return;

            var owner = matchMgr.CurrentBallOwner;
            if (owner == null)
            {
                if (StateMachine.CurrentState != TransitionState) StateMachine.ChangeState(TransitionState);
            }
            else
            {
                if (owner.TeamID == this.TeamID)
                {
                    if (StateMachine.CurrentState != AttackingState) StateMachine.ChangeState(AttackingState);
                }
                else
                {
                    if (StateMachine.CurrentState != DefendingState) StateMachine.ChangeState(DefendingState);
                }
            }
        }
        
        public void CacheTeammates()
        {
            Teammates.Clear();
            
            // Optimization: Use MatchManager cache if available
            var matchMgr = MatchManager.Instance;
            if (matchMgr != null)
            {
                var allAgents = matchMgr.GetAllAgents();
                foreach (var agent in allAgents)
                {
                    if (agent != this && agent.TeamID == this.TeamID)
                    {
                        Teammates.Add(agent);
                    }
                }
            }
            else
            {
                // Fallback
                Debug.LogWarning($"[HybridAgentController] {name}: MatchManager not found! Using slow FindObjectsByType.");
                var allAgents = FindObjectsByType<HybridAgentController>(FindObjectsSortMode.None);
                foreach (var agent in allAgents)
                {
                    if (agent != this && agent.TeamID == this.TeamID)
                    {
                        Teammates.Add(agent);
                    }
                }
            }
        }
        
        public System.Collections.Generic.List<HybridAgentController> GetTeammates() => Teammates;

        // =========================================================
        // MOVEMENT FACADE
        // =========================================================
        public virtual void SetDestination(Vector3 target)
        {
            Mover.MoveTo(target);
        }

        public void ResetPath()
        {
            Mover.Stop();
        }

        public Vector3 Velocity => VelocityRb != null ? VelocityRb.linearVelocity : Vector3.zero;

        // =========================================================
        // BALL HANDLING FACADE
        // =========================================================
        public void ExecutePendingKick() => BallHandler.ExecutePendingKick();
        public void Pass(GameObject target) => BallHandler.Pass(target);
        public void PassToPosition(Vector3 pos, GameObject recipient = null) => BallHandler.PassToPosition(pos, recipient);
        public void HighClearanceKick(Vector3 dir) => BallHandler.HighClearanceKick(dir);
        public void CurvedKick(Vector3 force, Vector3 torque) => BallHandler.CurvedKick(force, torque);
        
        // Shoot method (was missing from BallHandler, adding wrapper)
        public void Shoot(Vector3 targetPos)
        {
            // Now using dedicated Shoot logic
            BallHandler.Shoot(targetPos);
        }

        // =========================================================
        // SKILL FACADE
        // =========================================================
        public bool CanUseDribbleBurst => SkillSystem.CanUseBreakthrough; // Alias
        public void ActivateDribbleBurst() => SkillSystem.ActivateBreakthrough(); // Alias



        // =========================================================
        // EXTERNAL NOTIFICATIONS
        // =========================================================
        public void NotifyPossessionGained()
        {
             // Force Immediate State Update
             if (StateMachine.CurrentState != AttackingState) StateMachine.ChangeState(AttackingState);
             
             // Reset Roles
             IsReceiver = false;
             
             // Reset Low-Level States
             Mover.EnterNormalMode(); // Exit Trap Mode if active
             BallHandler.ResetState(); // Clear broken dribble states
        }

        public void ResetDribbleState()
        {
            BallHandler.ResetState();
        }

        // =========================================================
        // PASS RECEPTION
        // =========================================================
        public void NotifyIncomingPass(Vector3 estimatedPos)
        {
             IsReceiver = true;
             IncomingBallPos = estimatedPos;
             
             if (_receiverTimeoutCoroutine != null) StopCoroutine(_receiverTimeoutCoroutine);
             _receiverTimeoutCoroutine = StartCoroutine(ReceiverTimeoutRoutine());
             
             Debug.Log($"<color=cyan>{name} is expecting a PASS at {estimatedPos}!</color>");
        }

        private System.Collections.IEnumerator ReceiverTimeoutRoutine()
        {
            yield return new WaitForSeconds(config ? config.ReceiverTimeout : 3.0f);
            IsReceiver = false;
        }

        // =========================================================
        // DEBUG VISUALS
        // =========================================================
        private void UpdateDebugVisuals()
        {
            if (_cachedRenderer == null) return;
            
            Color targetColor = _originalColor;
            
            if (Mover.IsRecoveringBall) targetColor = Color.yellow; // TRAP
            else if (SkillSystem.IsBreakthroughActive) targetColor = Color.red; // SKILL
            else if (BallHandler.HasPendingKick) targetColor = Color.green; // KICK
            else if (Mover.IsBusy) targetColor = Color.cyan; // AIMING
            else if (MatchManager.Instance != null && MatchManager.Instance.CurrentBallOwner == this) targetColor = Color.white; // DRIBBLE

            float lerpSpeed = config ? config.DebugColorLerpSpeed : 10f;
            _cachedRenderer.material.color = Color.Lerp(_cachedRenderer.material.color, targetColor, Time.deltaTime * lerpSpeed);
        }

        private void OnDrawGizmos()
        {
            // Visualize 45-degree cone (Frontal Dribble/Pass Zone)
            Gizmos.color = Color.yellow;
            Vector3 forward = transform.forward;
            Vector3 start = transform.position + Vector3.up * 0.1f;
            
            // Draw Center Line (2m)
            Gizmos.DrawLine(start, start + forward * 2.0f);
            
            // Draw +/- 45 degrees
            Vector3 leftLimit = Quaternion.Euler(0, -45, 0) * forward;
            Vector3 rightLimit = Quaternion.Euler(0, 45, 0) * forward;
            
            Gizmos.color = new Color(1, 1, 0, 0.5f); // Transparent Yellow
            Gizmos.DrawLine(start, start + leftLimit * 2.0f);
            Gizmos.DrawLine(start, start + rightLimit * 2.0f);
            
            // Draw Arc
            Vector3 prev = start + leftLimit * 2.0f;
            for (int i = -40; i <= 45; i += 5)
            {
                 Vector3 nextDir = Quaternion.Euler(0, i, 0) * forward;
                 Vector3 nextPos = start + nextDir * 2.0f;
                 Gizmos.DrawLine(prev, nextPos);
                 prev = nextPos;
            }
        }
    }
}
