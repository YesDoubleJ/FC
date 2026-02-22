using UnityEngine;
using Game.Scripts.AI.HFSM;
using Game.Scripts.Tactics;
using Game.Scripts.Managers;
using Game.Scripts.AI.Tactics;

namespace Game.Scripts.AI.States
{
    public class RootState_Transition : State
    {
        // Optimization
        private Collider[] _nearbyBuffer = new Collider[10];
        private float _lastPressureCheckTime;

        public RootState_Transition(HybridAgentController agent, StateMachine stateMachine) : base(agent, stateMachine) { }

        public override void Enter()
        {
            if (agent.IsGoalkeeper)
                Debug.Log($"<color=yellow><b>{agent.name}</b> entered TRANSITION state</color>");
        }

        public override void Execute()
        {
            // TRANSITION STATE (Neutral Ball)
            // Goal: Race to the ball to claim Possession
            
            // Optimize: Use Centralized Ball Reference
            Transform ball = Game.Scripts.Managers.MatchManager.Instance?.Ball?.transform;
            if (ball == null) ball = GameObject.Find("Ball")?.transform; // Fallback
            if (ball != null)
            {
                // GK SWEEPER LOGIC (User Req: React to loose balls)
                if (agent.IsGoalkeeper)
                {
                    Vector3 myGoal = Game.Scripts.Managers.MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
                    float distToGoal = Vector3.Distance(ball.position, myGoal);
                    
                    // If ball is in danger zone (Penalty Box ~20m + slack), GK chases it.
                    // Expanded to 150m to cover full defensive half awareness
                    // Improve: Use Field Settings if available, else fallback
                    float maxChaseDist = 150.0f;
                    if (agent.BallHandler && agent.BallHandler.settings)
                    {
                        maxChaseDist = agent.BallHandler.settings.fieldHalfLength * 3.0f; // Cover wide area
                    }

                    if (distToGoal < maxChaseDist) 
                    {
                        agent.Mover.SprintTo(ball.position); // [FIX] Run fast to clear!
                        return; // Priority handling
                    }
                    else
                    {
                        // Wait / Idle
                        if (agent.formationManager != null)
                        {
                             agent.Mover.MoveTo(agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID));
                        }
                    }
                }

                // GET BALL VELOCITY FOR PREDICTION
                Vector3 ballVel = Vector3.zero;
                if (agent.BallRb != null) ballVel = agent.BallRb.linearVelocity;
                
                // NEW: PASS RECEIVER PRIORITY (Refined)
                // If I am the designated receiver, behave smartly based on pressure.
                if (agent.IsReceiver)
                {
                    // Pressure Check: Throttled & NonAlloc (Every 0.2s)
                    if (Time.time > _lastPressureCheckTime + 0.2f)
                    {
                        _lastPressureCheckTime = Time.time;
                        
                        // Use NonAlloc to avoid GC
                        int hitCount = UnityEngine.Physics.OverlapSphereNonAlloc(agent.transform.position, 5.0f, _nearbyBuffer);
                        for (int i = 0; i < hitCount; i++)
                        {
                            var col = _nearbyBuffer[i];
                            var opp = col.GetComponent<HybridAgentController>();
                            if (opp != null && opp.TeamID != agent.TeamID)
                            {
                                break;
                            }
                        }
                    }

                    // ACTIVE RECEPTION FIX (User Req: Don't stop!)
                    // Always move to meet the ball. Prediction helps align with moving pass.
                    // If comfortable (no pressure), move to intercept. 
                    // If pressured, sprint to intercept.
                    
                    Vector3 interceptPos = GetInterceptPosition(agent.transform.position, agent.Mover.Speed * 1.4f, ball.position, ballVel);
                    
                    // Don't just stop!
                    agent.Mover.SprintTo(interceptPos); // [FIX] Sprint to receive pass
                    
                    /* REMOVED PASSIVE LOGIC
                    if (_isPressuredCached) { ... } else { agent.ResetPath(); ... } 
                    */
                    
                    return;
                }

                // Improved Logic: Only CLOSEST chases, others maintain formation (to be open for pass)
                if (IsClosestToBall(ball.position))
                {
                    // PREDICTIVE CHASING (User Req: Don't chase tail)
                    Vector3 interceptPos = GetInterceptPosition(agent.transform.position, agent.Mover.Speed * 1.4f, ball.position, ballVel);
                    agent.Mover.SprintTo(interceptPos); // [FIX] Sprint when chasing free ball
                }
                else
                {
                    // If kickoff logic is needed
                    // For now, check if our team was just in possession (e.g., passing). 
                    // If so, maintain support positions instead of dropping back.
                    if (MatchManager.Instance.LastPossessionTeam == agent.TeamID)
                    {
                        Vector3 goalPos = MatchManager.Instance.GetAttackGoalPosition(agent.TeamID);
                        AttackingTactics tactics = new AttackingTactics(goalPos);
                        Vector3 supportPos = tactics.GetSupportPosition(agent, ball.position);
                        tactics.MoveToSafePosition(agent, supportPos);
                    }
                    else if (agent.formationManager != null)
                    {
                        // FIXED: Returning to formation when opposing team passes or neutral ball
                        agent.Mover.MoveTo(agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID));
                    }
                }
                
                // Note: The actual "Claiming" happens in HybridAgentController.UpdatePossessionLogic()
            }
            // State: END
            else if (MatchManager.Instance.CurrentState == MatchState.Ended)
            {
                 // Celebrate or Idle
                 if (agent.formationManager != null)
                 {
                     agent.Mover.MoveTo(agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID));
                 }
            }    
            else
            {
                // No ball? Return to formation as fallback
                if (agent.formationManager != null)
                     agent.Mover.MoveTo(agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID));
            }
        }

        private bool IsClosestToBall(Vector3 ballPos)
        {
            float myDist = Vector3.Distance(agent.transform.position, ballPos);
            var teammates = agent.GetTeammates();
            
            foreach (var tm in teammates)
            {
                float tmDist = Vector3.Distance(tm.transform.position, ballPos);
                
                // Tie-breaker: If distances are very similar, use InstanceID to pick ONE deterministic winner.
                if (Mathf.Abs(myDist - tmDist) < 0.2f)
                {
                    if (tm.GetInstanceID() < agent.GetInstanceID()) return false; 
                }
                else if (tmDist < myDist)
                {
                    return false;
                }
            }
            return true;
        }

        public override void Exit()
        {
            if (agent.IsGoalkeeper)
            {
               // Debug.Log($"{agent.name} exited TRANSITION state");
            }
        }

        // Simple First-Order Intercept Prediction
        private Vector3 GetInterceptPosition(Vector3 runnerPos, float runnerSpeed, Vector3 targetPos, Vector3 targetVel)
        {
            Vector3 toTarget = targetPos - runnerPos;
            float dist = toTarget.magnitude;
            
            // If very close, just go to target
            if (dist < 1.0f) return targetPos;

            // Guess time to intercept
            // t = dist / (runnerSpeed + targetClosureSpeed)
            // Approx: t = dist / runnerSpeed
            float t = dist / Mathf.Max(runnerSpeed, 1.0f);
            
            // Limit prediction time to prevent overshooting (User Req: Max 1.5s)
            t = Mathf.Min(t, 1.5f);
            
            // Predicted Pos = Target + Vel * t
            Vector3 predictedPos = targetPos + targetVel * t;
            
            // Limit prediction (don't run off map for a fast ball)
            // Clamp t? Or just Clamp Position.
            // If prediction is TOO far (> 10m), clamp it.
            if (Vector3.Distance(targetPos, predictedPos) > 10f)
            {
                predictedPos = targetPos + targetVel.normalized * 10f;
            }
            
            return predictedPos;
        }
    }
}
