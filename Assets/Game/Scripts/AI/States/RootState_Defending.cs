using UnityEngine;
using Game.Scripts.AI.HFSM;
using Game.Scripts.Tactics;
using Game.Scripts.Managers;
using Game.Scripts.Data;
// using Game.Scripts.AI.Settings;

namespace Game.Scripts.AI.States
{
    /// <summary>
    /// Refactored Defending State.
    /// Optimized for performance (No FindObjectsByType) and uses DecisionSettings.
    /// Strategy: Pressing (Closest) vs Zonal Marking (Rest).
    /// </summary>
    public class RootState_Defending : State
    {
        private Game.Scripts.AI.DecisionMaking.UtilityScorer scorer;
        private float tackleCooldown = 0f;

        // Settings Shortcuts
        private MatchEngineConfig Config => agent.config;
        
        // Defaults (Fallback if settings null)
        private float RedZoneDist => Config ? Config.RedZoneDistance : 25f;
        private float DistNormal => Config ? Config.ContainmentDistNormal : 2.5f;
        private float DistClose => Config ? Config.ContainmentDistClose : 1.2f;
        private float DistTight => Config ? Config.ContainmentDistTight : 0.4f;
        private float TackleRange => Config ? Config.TackleRange : 2.0f;
        private float BodyCheckRange => Config ? Config.BodyCheckRange : 1.5f;
        private float RecoveryDist => Config ? Config.RecoverySprintDist : 8.0f;

        // Field Dimensions (From BallHandler Settings)
        private float FieldHalfWidth => (agent.BallHandler && agent.BallHandler.settings) ? agent.BallHandler.settings.fieldHalfWidth : 32f;
        private float FieldHalfLength => (agent.BallHandler && agent.BallHandler.settings) ? agent.BallHandler.settings.fieldHalfLength : 48f;
        
        public RootState_Defending(HybridAgentController agent, StateMachine stateMachine) : base(agent, stateMachine) 
        {
            scorer = new Game.Scripts.AI.DecisionMaking.UtilityScorer(Config);
        }

        public override void Enter() { /* Optional Initialization */ }

        public override void Execute()
        {
            tackleCooldown -= Time.deltaTime;

            MatchManager matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentState != MatchState.Playing) return;

            Transform ball = matchMgr.Ball?.transform;
            if (ball == null) return;
            Vector3 ballPos = ball.position;

            // GK Handling
            if (agent.IsGoalkeeper) return; // Logic handled in GoalkeeperController

            // [FIX] GUARD CLAUSE: If teammate has ball, I should be ATTACKING, not Defending/Chasing.
            var owner = matchMgr.CurrentBallOwner;
            if (owner != null && owner.TeamID == agent.TeamID)
            {
                stateMachine.ChangeState(agent.AttackingState);
                return;
            }

            // 1. RECOVERY LOGIC (Beaten?)
            if (ShouldRecover(matchMgr, ballPos))
            {
                RunRecoveryLogic(matchMgr, ballPos);
                return;
            }

            // 2. MAIN LOGIC: Pressing vs Zonal
            // Only the Closest Defender Presses. Others hold position.
            if (IsClosestToBall(ballPos))
            {
                RunPressingLogic(ballPos, matchMgr);
            }
            else
            {
                RunZonalMarkingLogic(ballPos, matchMgr);
            }
        }

        // =========================================================
        // RECOVERY LOGIC
        // =========================================================
        private bool ShouldRecover(MatchManager matchMgr, Vector3 ballPos)
        {
            Vector3 myGoal = matchMgr.GetDefendGoalPosition(agent.TeamID);
            float myDistToGoal = Vector3.Distance(agent.transform.position, myGoal);
            float ballDistToGoal = Vector3.Distance(ballPos, myGoal);

            // If ball is significantly closer to goal than me, I am beaten.
            return ballDistToGoal < myDistToGoal - 5.0f;
        }

        private void RunRecoveryLogic(MatchManager matchMgr, Vector3 ballPos)
        {
            Vector3 myGoal = matchMgr.GetDefendGoalPosition(agent.TeamID);
            
            // Recover point: Goal-side of ball, slightly deep
            Vector3 recoverPoint = ballPos + (myGoal - ballPos).normalized * RecoveryDist;
            
            // Clamp to field bounds (Using Settings)
            // Add slight buffer (e.g. -2m) to avoid hugging the wall perfectly
            float w = FieldHalfWidth - 2f; 
            float l = FieldHalfLength + 2f; // Length can go slightly beyond goal line for recovery

            recoverPoint.x = Mathf.Clamp(recoverPoint.x, -w, w);
            recoverPoint.z = Mathf.Clamp(recoverPoint.z, -l, l);

            agent.Mover.SprintTo(recoverPoint);

            // Skill Use
            if (agent.SkillSystem.CanUseDefenseBurst)
            {
                agent.SkillSystem.ActivateDefenseBurst();
            }
        }

        // =========================================================
        // PRESSING LOGIC (Closest Defender)
        // =========================================================
        private void RunPressingLogic(Vector3 ballPos, MatchManager matchMgr)
        {
             // Failsafe: Reset Tackle Commitment
             if (tackleCooldown > 0f)
             {
                 // Back off slightly to reset after failed tackle
                 Vector3 dirFromBall = (agent.transform.position - ballPos).normalized;
                 Vector3 standOffPos = ballPos + dirFromBall * 3.5f;
                 SetSafeDestination(standOffPos);
                 agent.Mover.RotateToAction(ballPos - agent.transform.position, null);
                 return;
             }

             Vector3 defendGoalPos = matchMgr.GetDefendGoalPosition(agent.TeamID);
             float distGoal = Vector3.Distance(ballPos, defendGoalPos);
             
             // 1. Determine Pressing Intensity
             float targetDist = DistNormal;
             bool isRedZone = (distGoal < RedZoneDist);

             if (isRedZone) targetDist = DistTight; // 0.4m (Body Check Range)
             else if (distGoal < 40f) targetDist = DistClose; // 1.2m

             // 2. Physicality (Body Check)
             float distToBall = Vector3.Distance(agent.transform.position, ballPos);
             if (isRedZone && distToBall < BodyCheckRange)
             {
                 var target = matchMgr.CurrentBallOwner;
                 if (target != null && target.TeamID != agent.TeamID)
                 {
                     agent.SkillSystem.AttemptBodyCheck(target);
                 }
             }

             // 3. Positioning (Intercept Line)
             Vector3 dirBallToGoal = (defendGoalPos - ballPos).normalized;
             Vector3 interceptPos = ballPos + (dirBallToGoal * targetDist);

             // 4. Desperation Block (Slide Tackle Logic)
             // If Ball is fast + moving to goal => Intercept Line!
             Rigidbody ballRb = matchMgr.Ball?.GetComponent<Rigidbody>(); // Cached? matchMgr.Ball is GO.
             if (ballRb != null && ballRb.linearVelocity.magnitude > 10f)
             {
                 Vector3 ballVel = ballRb.linearVelocity.normalized;
                 Vector3 toGoal = (defendGoalPos - ballPos).normalized;
                 
                 if (Vector3.Dot(ballVel, toGoal) > 0.8f) // Shot detected
                 {
                     // Project position onto line
                     Vector3 ballToMe = agent.transform.position - ballPos;
                     float projection = Vector3.Dot(ballToMe, toGoal);
                     if (projection > 0 && projection < distGoal)
                     {
                         Vector3 pointOnLine = ballPos + toGoal * projection;
                         if (Vector3.Distance(agent.transform.position, pointOnLine) < 4.0f)
                         {
                             interceptPos = pointOnLine; // Rush to line
                             agent.Mover.SprintTo(interceptPos);
                             return;
                         }
                     }
                 }
             }

             // 5. Engagement Logic (Stand Ground vs Move)
             // Am I blocking the angle?
             bool isBlocking = Vector3.Dot(dirBallToGoal, (agent.transform.position - ballPos).normalized) > 0.9f;
             float distToIntercept = Vector3.Distance(agent.transform.position, interceptPos);

             if (isBlocking && distToIntercept < 2.0f)
             {
                 // HOLD GROUND (Don't retreat forever)
                 agent.Mover.Stop();
                 agent.Mover.RotateToAction(ballPos - agent.transform.position, null);

                 // Tackle if close
                 if (distToBall < TackleRange)
                 {
                     var owner = matchMgr.CurrentBallOwner;
                     if (owner != null && owner != agent) agent.SkillSystem.AttemptTackle(owner);
                     tackleCooldown = 2.0f;
                 }
             }
             else
             {
                 // Move to Position
                 SetSafeDestination(interceptPos);
             }

             // Opportunistic Tackle (Even if moving)
             if (distToBall < TackleRange)
             {
                 var owner = matchMgr.CurrentBallOwner;
                 if (owner != null && owner != agent) 
                 {
                     agent.SkillSystem.AttemptTackle(owner);
                     tackleCooldown = 2.0f;
                 }
             }
        }

        // =========================================================
        // ZONAL MARKING LOGIC (Off-Ball Defenders)
        // =========================================================
        private void RunZonalMarkingLogic(Vector3 ballPos, MatchManager matchMgr)
        {
            if (agent.formationManager == null) 
            {
                // Fallback if no formation
                SetSafeDestination(ballPos + (agent.transform.position - ballPos).normalized * 5f);
                return; 
            }

            // 1. Identify Threat in My Zone
            HybridAgentController dangerEnemy = FindDangerousEnemy(matchMgr);
            Vector3 basePos = agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID);

            bool shouldMark = false;
            if (dangerEnemy != null)
            {
                // Check if in my zone (15m radius from anchor)
                if (Vector3.Distance(dangerEnemy.transform.position, basePos) < 15f)
                {
                    shouldMark = true;
                }
                
                // CB Override: Mark central threats near goal
                bool isCB = (agent.assignedPosition == Game.Scripts.Tactics.FormationPosition.CB_Left || 
                             agent.assignedPosition == Game.Scripts.Tactics.FormationPosition.CB_Right);
                if (isCB)
                {
                    float enemyDistToGoal = Vector3.Distance(dangerEnemy.transform.position, matchMgr.GetDefendGoalPosition(agent.TeamID));
                    if (enemyDistToGoal < 25f) shouldMark = true;
                }
            }

            if (shouldMark && dangerEnemy != null)
            {
                // MARKING: Stand Goal-Side of Enemy
                Vector3 defendGoal = matchMgr.GetDefendGoalPosition(agent.TeamID);
                Vector3 goalDir = (defendGoal - dangerEnemy.transform.position).normalized;
                Vector3 ballDir = (ballPos - dangerEnemy.transform.position).normalized;

                // Position: 70% Goal Side, 30% Ball Intercept
                Vector3 guardDir = (goalDir * 0.7f + ballDir * 0.3f).normalized;
                Vector3 markPos = dangerEnemy.transform.position + (guardDir * 1.5f);

                SetSafeDestination(markPos);
                agent.Mover.RotateToAction(dangerEnemy.transform.position - agent.transform.position, null);
            }
            else
            {
                // ZONAL SHIFT (Sliding)
                // Shift base position slightly towards ball to compress space
                float shiftX = Mathf.Clamp(ballPos.x * 0.35f, -10f, 10f);
                basePos.x += shiftX;
                basePos.z += (ballPos.z - basePos.z) * 0.1f; // Slight vertical compression

                SetSafeDestination(basePos);
                agent.Mover.RotateToAction(ballPos - agent.transform.position, null);
            }
        }

        // =========================================================
        // UTILITIES
        // =========================================================
        private bool IsClosestToBall(Vector3 ballPos)
        {
            float myDist = Vector3.Distance(agent.transform.position, ballPos);
            Vector3 defendGoal = MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
            float ballDistToGoal = Vector3.Distance(ballPos, defendGoal);

            var teammates = agent.GetTeammates();
            foreach (var tm in teammates)
            {
                // Ignore teammates who are "Beaten" (Behind the ball)
                // UNLESS I am also beaten.
                float tmDistToGoal = Vector3.Distance(tm.transform.position, defendGoal);
                bool tmIsBeaten = (tmDistToGoal > ballDistToGoal + 1.0f);
                
                if (tmIsBeaten)
                {
                     // If I am NOT beaten, I ignore the beaten teammate.
                     // (I am in front, he is behind. I should defend.)
                     bool iAmBeaten = (Vector3.Distance(agent.transform.position, defendGoal) > ballDistToGoal + 1.0f);
                     if (!iAmBeaten) continue;
                }

                float tmDist = Vector3.Distance(tm.transform.position, ballPos);
                if (tmDist < myDist - 0.5f) return false; // Simple Check
            }
            return true;
        }

        private HybridAgentController FindDangerousEnemy(MatchManager matchMgr)
        {
            // Optimization: Use Cached Opponents List
            var opponents = matchMgr.GetOpponents(agent.TeamID);
            HybridAgentController bestCandidate = null;
            float maxThreat = -1f;

            Vector3 myGoal = matchMgr.GetDefendGoalPosition(agent.TeamID);

            foreach(var opp in opponents)
            {
                // Simple Threat Assessment: Proximity to Goal + Openness
                float distToGoal = Vector3.Distance(opp.transform.position, myGoal);
                if (distToGoal > 35f) continue; // Not dangerous

                float threat = (40f - distToGoal); // Closer = Higher Threat
                if (threat > maxThreat)
                {
                    maxThreat = threat;
                    bestCandidate = opp;
                }
            }
            return bestCandidate;
        }

        private void SetSafeDestination(Vector3 targetPos)
        {
            // Simple field clamp using Settings
            float w = FieldHalfWidth;
            float l = FieldHalfLength;

            targetPos.x = Mathf.Clamp(targetPos.x, -w, w);
            targetPos.z = Mathf.Clamp(targetPos.z, -l, l);
            
            agent.Mover.MoveTo(targetPos);
        }

        public override void PhysicsExecute() { }
        public override void Exit() { }
    }
}
