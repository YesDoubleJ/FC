using UnityEngine;
using Game.Scripts.AI.HFSM;
using Game.Scripts.AI.DecisionMaking;
using Game.Scripts.AI.Tactics;
using Game.Scripts.Data;
using Game.Scripts.Managers;

namespace Game.Scripts.AI.States
{
    /// <summary>
    /// Refactored Attacking State - Delegates logic to helper classes.
    /// Pure Field Player Logic (GK Logic moved to GoalkeeperController).
    /// </summary>
    public class RootState_Attacking : State
    {
        // Helper Classes (Strategy Pattern)
        private AttackingTactics _tactics;
        private DecisionEvaluator _evaluator;
        
        // Legacy Components (Preserved for compatibility)
        private UtilityScorer _scorer;
        private PlayerStats _stats;
        
        // Timing & State
        private float _decisionTimer = 0f;
        private float _lastActionTime = -999f;
        private float _aimingTimer = 0f;
        private float _lastDebugLogTime = -999f;
        
        // Goal Position
        private Vector3 _goalPosition = new Vector3(0, 0, 25f);

        // Settings Shortcuts
        private float MaxPassAngle => agent.config ? agent.config.MaxPassAngle : 45f;
        private float MaxDribbleAngle => agent.config ? agent.config.MaxDribbleAngle : 45f;

        public RootState_Attacking(HybridAgentController agent, StateMachine stateMachine) : base(agent, stateMachine) 
        {
            MatchEngineConfig config = agent.config; // Can be null here if Awake hasn't run fully or if assigned in Start
             // Note: DecisionEvaluator is created in Enter(), so we pass config there.
             // UtilityScorer might need refactoring or we just pass config to it too.
            _scorer = new UtilityScorer(config); 
            _stats = agent.GetComponent<PlayerStats>();
        }

        public override void Enter()
        {
            // Get Goal Position from MatchManager
            if (MatchManager.Instance != null)
            {
                _goalPosition = MatchManager.Instance.GetAttackGoalPosition(agent.TeamID);
            }
            else
            {
                float goalZ = (agent.TeamID == Team.Home) ? 50f : -50f;
                _goalPosition = new Vector3(0, 0, goalZ);
            }

            // Initialize Helper Classes
            _tactics = new AttackingTactics(_goalPosition);
            _evaluator = new DecisionEvaluator(_goalPosition, agent.config);

            // Desync decision timer
            float interval = agent.config ? agent.config.DecisionInterval : 0.1f;
            _decisionTimer = Random.Range(0f, interval);
        }

        private float _execLogTimer = 0f;
        public override void Execute()
        {
            if (Time.time > _execLogTimer + 2.0f)
            {
                _execLogTimer = Time.time;
                // Debug.Log($"[RootState] {agent.name} Executing. AgentActive={agent.enabled}");
            }

            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentState != MatchState.Playing) return;

            Transform ball = matchMgr.Ball?.transform;
            if (ball == null) return;

            // FIELD PLAYER LOGIC ONLY
            RunFieldPlayerLogic(ball, matchMgr);
        }

        public override void PhysicsExecute()
        {
            // Physics-based logic can go here if needed
        }

        public override void Exit()
        {
            // Cleanup if needed
        }

        // =========================================================
        // FIELD PLAYER LOGIC
        // =========================================================
        private void RunFieldPlayerLogic(Transform ball, MatchManager matchMgr)
        {
            // SKILL OVERRIDE: If doing a breakthrough, don't interrupt!
            if (agent.SkillSystem != null && agent.SkillSystem.IsBreakthroughActive) return;

            Vector3 ballPos = ball.position;
            
            // Check if we have possession
            bool hasPossession = (matchMgr.CurrentBallOwner == agent);

            if (hasPossession)
            {
                // ON-BALL: Decision Making
                _decisionTimer += Time.fixedDeltaTime;
                
                // ON-BALL: Decision Making
                _decisionTimer += Time.fixedDeltaTime;
                
                float interval = agent.config ? agent.config.DecisionInterval : 0.1f;
                if (_decisionTimer >= interval)
                {
                    _decisionTimer = 0f;
                    MakeDecision(ballPos);
                }
            }
            else
            {
                // OFF-BALL: Positioning
                MoveToSupportPosition(ballPos);
            }
        }

        // =========================================================
        // DECISION MAKING (Delegated to DecisionEvaluator)
        // =========================================================
        private void MakeDecision(Vector3 ballPos)
        {
            var decision = _evaluator.EvaluateBestAction(agent, ref _lastActionTime, ref _aimingTimer);

            switch (decision.Action)
            {
                case "Shoot":
                    ExecuteShoot(decision.Position);
                    break;
                    
                case "Pass":
                    ExecutePass(decision.Target);
                    break;
                    
                case "Dribble":
                    ExecuteDribble(decision.Position);
                    break;
                    
                case "Breakthrough":
                    agent.SkillSystem.ActivateBreakthrough();
                    _lastActionTime = Time.time;
                    break;
                    
                case "Hold":
                default:
                    // Do nothing, keep possession
                    break;
            }
        }

        // =========================================================
        // ACTION EXECUTION
        // =========================================================
        private void ExecuteShoot(Vector3 targetPos)
        {
            // 1. Try Curved Shot if blocked
            if (TryCurvedShotLogic(targetPos))
            {
                _lastActionTime = Time.time;
                return;
            }

            // 2. Normal shot
            agent.Shoot(targetPos);
            _lastActionTime = Time.time;
            
            LogAction($"SHOOT at {targetPos}");
        }

        private void ExecutePass(GameObject target)
        {
            if (target == null)
            {
                // Fallback: Emergency clearance (Smart Clearance)
                // Find Safest Direction (Cluster of Teammates)
                var teammates = agent.GetTeammates();
                Vector3 avgPos = Vector3.zero;
                int count = 0;
            
                foreach(var tm in teammates)
                {
                    avgPos += tm.transform.position;
                    count++;
                }
            
                if (count > 0)
                {
                    avgPos /= count; // Center of Teammates
                    
                    // Direction to Center
                    Vector3 clearDir = (avgPos - agent.transform.position).normalized;
                    
                    // Add some forward bias (don't kick back to own goal if possible)
                    Vector3 goalDir = (_goalPosition - agent.transform.position).normalized;
                    clearDir = (clearDir + goalDir * 0.5f).normalized;
                    
                    agent.BallHandler.HighClearanceKick(clearDir);
                    LogAction("EMERGENCY CLEARANCE (To Teammates)");
                }
                else
                {
                    // No teammates? Just kick forward towards goal (Long Ball)
                    Vector3 forward = (_goalPosition - agent.transform.position).normalized;
                    agent.BallHandler.HighClearanceKick(forward);
                    LogAction("EMERGENCY CLEARANCE (Blind)");
                }

                _lastActionTime = Time.time;
                return;
            }

            // ANGLE CHECK: Restrict Passing using Settings
            Vector3 toTarget = target.transform.position - agent.transform.position;
            if (Vector3.Angle(agent.transform.forward, toTarget) > MaxPassAngle)
            {
                 // REORIENT
                 if (Time.time - _lastActionTime > 1.0f) // Throttle log
                    Game.Scripts.UI.MatchViewController.Instance?.LogAction($"{agent.name}: Turning to Pass");

                 agent.Mover.RotateToAction(toTarget, null);
                 _lastActionTime = Time.time; // Commit to Turn Action (Lockout)
                 return;
            }

            agent.Pass(target);
            _lastActionTime = Time.time;
            
            LogAction($"PASS to {target.name}");
        }

        private void ExecuteDribble(Vector3 targetPos)
        {
            // ANGLE CHECK: Force Frontal Dribble Conformance
            Vector3 toBall = MatchManager.Instance.Ball.transform.position - agent.transform.position;
            toBall.y = 0; 
            Vector3 agentForward = agent.transform.forward;
            agentForward.y = 0;

            if (Vector3.Angle(agentForward, toBall) > MaxDribbleAngle)
            {
                 if (Time.time - _lastActionTime > 1.0f) 
                    Game.Scripts.UI.MatchViewController.Instance?.LogAction($"{agent.name}: Reorienting to Ball");
                 
                 _lastActionTime = Time.time; // Commit to this action
                 return;
            }

            // SMART DRIBBLE LOGIC
            // PRIORITY: Catch Up to Ball
            if (!agent.BallHandler.IsInPocket)
            {
                 Vector3 ballPos = MatchManager.Instance.Ball.transform.position;
                 agent.Mover.SprintTo(ballPos);
                 _lastActionTime = Time.time;
                 return;
            }

            // 1. Check if Blocked (Enemy in front)
            bool isBlocked = agent.SkillSystem.IsFrontalBlocked(); 

            if (isBlocked)
            {
                 // BLOCKED: Attempt Breakthrough
                 if (agent.SkillSystem.CanUseBreakthrough)
                 {
                     agent.SkillSystem.ActivateBreakthrough();
                     Game.Scripts.UI.MatchViewController.Instance?.LogAction($"{agent.name} BREAKTHROUGH!");
                     _lastActionTime = Time.time;
                     return; 
                 }
                 // If skill on cooldown, standard MoveTo
                 agent.Mover.MoveTo(targetPos);
            }
            else
            {
                // OPEN SPACE: SPRINT
                agent.Mover.SprintTo(targetPos);
            }
            
            _lastActionTime = Time.time;
            LogAction($"DRIBBLE to {targetPos}");
        }

        // =========================================================
        // OFF-BALL POSITIONING
        // =========================================================
        private void MoveToSupportPosition(Vector3 ballPos)
        {
            // [FIX] WAIT FOR PASS LOGIC
            if (IsPassIncoming())
            {
                agent.Mover.Stop();
                agent.Mover.RotateToAction((ballPos - agent.transform.position).normalized, null);
                LogAction("Waiting for Incoming Pass...");
                return;
            }

            // [FIX] KICK OFF EXCLUSION ZONE
            // If it is KickOff first pass, and I am NOT the receiver/kicker, Stay AWAY from ball.
            if (MatchManager.Instance.IsKickOffFirstPass)
            {
                float dist = Vector3.Distance(agent.transform.position, ballPos);
                if (dist < 3.0f)
                {
                    // Backpedal
                    Vector3 away = (agent.transform.position - ballPos).normalized;
                    if (away == Vector3.zero) away = -agent.transform.forward;
                    
                    Vector3 safeSpot = ballPos + away * 3.5f;
                    _tactics.MoveToSafePosition(agent, safeSpot);
                    return;
                }
            }

            // [FIX] Don't chase ball if Teammate has it!
            bool teammateHasBall = (MatchManager.Instance.CurrentBallOwner != null && 
                                    MatchManager.Instance.CurrentBallOwner.TeamID == agent.TeamID);

            if (!teammateHasBall && IsClosestToBall(ballPos))
            {
                agent.Mover.SprintTo(ballPos);
                return;
            }

            Vector3 supportPos = _tactics.GetSupportPosition(agent, ballPos);
            _tactics.MoveToSafePosition(agent, supportPos);
        }

        private bool IsPassIncoming()
        {
            // 1. Check if I was the designated target recently
            if (agent.receivedBallFrom != null && Time.time - agent.ballReceivedTime < 2.0f)
            {
                // Double check: Is the ball actually moving towards me?
                if (MatchManager.Instance.Ball != null)
                {
                    Rigidbody ballRb = MatchManager.Instance.Ball.GetComponent<Rigidbody>();
                    if (ballRb != null && ballRb.linearVelocity.sqrMagnitude > 2.0f) // Moving fast enough
                    {
                        Vector3 toMe = agent.transform.position - MatchManager.Instance.Ball.transform.position;
                        float angle = Vector3.Angle(ballRb.linearVelocity, toMe);
                        if (angle < 45f) return true; // Yes, coming at me
                    }
                }
            }
            return false;
        }

        private bool IsClosestToBall(Vector3 ballPos)
        {
            var teammates = agent.GetTeammates();
            float myDist = Vector3.Distance(agent.transform.position, ballPos);
            
            foreach (var tm in teammates)
            {
                if (tm == agent) continue;
                float tmDist = Vector3.Distance(tm.transform.position, ballPos);
                if (tmDist < myDist - 2f) return false;
            }
            return true;
        }
        
        // =========================================================
        // PRIVATE HELPERS (Inlined to remove Physics Utilities dependency)
        // =========================================================
        
        private bool TryCurvedShotLogic(Vector3 targetGoalPos)
        {
             // 1. Check if direct path is blocked by GK
             Vector3 dirToGoal = (targetGoalPos - agent.transform.position).normalized;
             float dist = Vector3.Distance(agent.transform.position, targetGoalPos);
             
             if (UnityEngine.Physics.Raycast(agent.transform.position + Vector3.up, dirToGoal, out RaycastHit hit, dist, LayerMask.GetMask("Player")))
             {
                 var hitAgent = hit.collider.GetComponent<HybridAgentController>();
                 bool isGK = hitAgent != null && hitAgent.TeamID != agent.TeamID && hitAgent.IsGoalkeeper;
                 
                 if (isGK)
                 {
                     // BLOCKED BY GK -> Try Curve
                     
                     // Right/Left vectors relative to Goal Direction
                     Vector3 goalRight = Vector3.Cross(Vector3.up, dirToGoal).normalized;
                     Vector3 goalLeft = -goalRight;

                     // Target Side (Check Left/Right of Goal) - 5.0m wide targets
                     Vector3 leftTarget = targetGoalPos + goalLeft * 5.0f; // Aim Wide Left
                     Vector3 rightTarget = targetGoalPos + goalRight * 5.0f; // Aim Wide Right
                     
                     // Raycast to Left Target
                     Vector3 dirLeft = (leftTarget - agent.transform.position).normalized;
                     if (!UnityEngine.Physics.Raycast(agent.transform.position + Vector3.up, dirLeft, dist, LayerMask.GetMask("Player")))
                     {
                         // Curve Right -> Torque UP
                         Vector3 curveTorque = Vector3.up * 180f; 
                         _aimingTimer = 0f;
                         
                         // Replaced pure physics call with BallHandler kick
                         agent.BallHandler.CurvedKick(dirLeft * 28f, curveTorque); 
                         LogAction("CURVED SHOT (Left->Right)");
                         return true;
                     }
                     
                     // Raycast to Right Target
                     Vector3 dirRight = (rightTarget - agent.transform.position).normalized;
                     if (!UnityEngine.Physics.Raycast(agent.transform.position + Vector3.up, dirRight, dist, LayerMask.GetMask("Player")))
                     {
                         // Curve Left -> Torque DOWN
                         Vector3 curveTorque = Vector3.down * 180f;
                         _aimingTimer = 0f;
                         
                         agent.BallHandler.CurvedKick(dirRight * 28f, curveTorque);
                         LogAction("CURVED SHOT (Right->Left)");
                         return true;
                     }
                 }
             }
             return false;
        }

        private void LogAction(string msg)
        {
            if (Time.time > _lastDebugLogTime + 1.0f)
            {
                _lastDebugLogTime = Time.time;
                Game.Scripts.UI.MatchViewController.Instance?.LogAction($"{agent.name}: {msg}");
            }
        }
    }
}
