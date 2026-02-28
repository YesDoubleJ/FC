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
                 SetSafeDestination(standOffPos, false);
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
                 SetSafeDestination(interceptPos, true); // Sprint to press
             }

             // [NEW] AUTO-TACKLE LOGIC
             // If I am very close, facing ball, and not on cooldown -> TACKLE
             if (distToBall < TackleRange)
             {
                 var owner = matchMgr.CurrentBallOwner;
                 if (owner != null && owner != agent) 
                 {
                     // Angle check: Am I facing the target?
                     Vector3 toOwner = (owner.transform.position - agent.transform.position).normalized;
                     float angle = Vector3.Angle(agent.transform.forward, toOwner);
                     
                     if (angle < 45f)
                     {
                         // COMMIT TO TACKLE
                         agent.SkillSystem.AttemptTackle(owner);
                         
                         // [ACTION LOG]
                         Debug.Log($"[ACTION] {agent.name} is attempting TACKLE on {owner.name} (Dist: {distToBall:F2})");
                         
                         tackleCooldown = 2.0f;
                     }
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
                SetSafeDestination(ballPos + (agent.transform.position - ballPos).normalized * 5f, false);
                return; 
            }

            Vector3 basePos = agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID);

            // 1. ZONAL SHIFT & DEFENSIVE LINE (Phase-based)
            float shiftX = Mathf.Clamp(ballPos.x * 0.4f, -15f, 15f);
            basePos.x += shiftX;
            basePos.z += (ballPos.z - basePos.z) * 0.2f; // Slight vertical compression

            // Apply TacticsConfig Defensive Line Modifier
            if (agent.TacticsConfig != null)
            {
                float defendGoalZ = matchMgr.GetDefendGoalPosition(agent.TeamID).z;
                float attackGoalZ = matchMgr.GetAttackGoalPosition(agent.TeamID).z;
                float fieldLength = Mathf.Abs(attackGoalZ - defendGoalZ);
                
                // Determine Phase based on ball position
                float ballDistFromGoal = Mathf.Abs(ballPos.z - defendGoalZ);
                float phaseRatio = ballDistFromGoal / fieldLength;

                Game.Scripts.Tactics.Data.DefensiveLine currentLineSetting = Game.Scripts.Tactics.Data.DefensiveLine.Normal;
                
                // Deep in our half (< 33%) -> Low Block Phase
                if (phaseRatio < 0.33f) currentLineSetting = agent.TacticsConfig.OutOfPossession.LowBlockLine;
                // Middle third -> Mid Block Phase
                else if (phaseRatio < 0.66f) currentLineSetting = agent.TacticsConfig.OutOfPossession.MidBlockLine;
                // Attack third -> High Block Phase
                else currentLineSetting = agent.TacticsConfig.OutOfPossession.HighBlockLine;

                // Determine Offset
                float zOffset = 0f;
                // Normal is 0.
                if (currentLineSetting == Game.Scripts.Tactics.Data.DefensiveLine.Low) zOffset = -10f; // Pull back 10 meters
                else if (currentLineSetting == Game.Scripts.Tactics.Data.DefensiveLine.High) zOffset = 10f; // Push up 10 meters

                // Apply offset relative to attacking direction
                float directionMultiplier = (defendGoalZ < 0) ? 1f : -1f; // If defend goal is negative Z (Home), positive offset moves forward
                basePos.z += zOffset * directionMultiplier;
            }

            // 2. Identify Threat in My Zone
            HybridAgentController dangerEnemy = FindDangerousEnemy(matchMgr, basePos);

            Vector3 targetPos = basePos;
            bool shouldMark = false;

            if (dangerEnemy != null)
            {
                // Check if in my zone (15m radius from shifted anchor)
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
                    // Require central danger (x within 15m)
                    if (enemyDistToGoal < 25f && Mathf.Abs(dangerEnemy.transform.position.x) < 15f) shouldMark = true;
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

                // Blend Anchor and Mark Pos (Formation Gravity)
                // If enemy is further from my base, pull me back to base
                float distToEnemy = Vector3.Distance(basePos, dangerEnemy.transform.position);
                float anchorWeight = Mathf.Clamp01((distToEnemy - 5f) / 10f); // 5m=0%, 15m=100%
                
                targetPos = Vector3.Lerp(markPos, basePos, anchorWeight);
                agent.Mover.RotateToAction(dangerEnemy.transform.position - agent.transform.position, null);
            }
            else
            {
                agent.Mover.RotateToAction(ballPos - agent.transform.position, null);
            }

            // 3. APPLY REPULSION (Teammate avoidance to prevent clumps)
            Vector3 repulsion = GetRepulsionVector(targetPos, 3.5f);
            targetPos += repulsion;

            // 4. SPRINT LOGIC (Stamina Conservation)
            bool shouldSprint = false;
            float distToTarget = Vector3.Distance(agent.transform.position, targetPos);
            if (distToTarget > 10f)
            {
                float distToBall = Vector3.Distance(agent.transform.position, ballPos);
                // Sprint to recover if ball is somewhat nearby, or if critically out of position
                if (distToBall < 30f || distToTarget > 25f)
                {
                    shouldSprint = true;
                }
            }
            else if (shouldMark && dangerEnemy != null && dangerEnemy.Mover != null && dangerEnemy.Mover.IsSprinting)
            {
                // Track sprinting opponent
                shouldSprint = true; 
            }

            SetSafeDestination(targetPos, shouldSprint);
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

        private HybridAgentController FindDangerousEnemy(MatchManager matchMgr, Vector3 shiftedBasePos)
        {
            // Optimization: Use Cached Opponents List
            var opponents = matchMgr.GetOpponents(agent.TeamID);
            var teammates = agent.GetTeammates();
            HybridAgentController bestCandidate = null;
            float maxThreat = -999f;

            Vector3 myGoal = matchMgr.GetDefendGoalPosition(agent.TeamID);

            foreach(var opp in opponents)
            {
                // Simple Threat Assessment: Proximity to Goal + Proximity to my Zone
                float distToGoal = Vector3.Distance(opp.transform.position, myGoal);
                if (distToGoal > 40f) continue; // Not dangerous

                float distToBase = Vector3.Distance(opp.transform.position, shiftedBasePos);
                
                // Base threat: Closer to goal + Closer to my zone
                float threat = (40f - distToGoal) + (20f - distToBase);

                // ROLE SEPARATION (1대1 마크 중복 방지)
                // Penalty if a teammate is already marking this opponent
                int teammatesMarking = 0;
                foreach(var tm in teammates)
                {
                    if (tm == agent || tm.IsGoalkeeper) continue; // Don't count myself or GK
                    float tmDistToOpp = Vector3.Distance(tm.transform.position, opp.transform.position);
                    if (tmDistToOpp < 3.5f) // Teammate is very close to opponent
                    {
                        teammatesMarking++;
                    }
                }
                
                // Huge penalty if 1 teammate is already on him, even more if 2+
                threat -= (teammatesMarking * 30f);

                if (threat > maxThreat)
                {
                    maxThreat = threat;
                    bestCandidate = opp;
                }
            }
            return bestCandidate;
        }

        private Vector3 GetRepulsionVector(Vector3 myTargetPos, float radius)
        {
            Vector3 repulsion = Vector3.zero;
            int count = 0;
            var teammates = agent.GetTeammates();
            
            foreach(var tm in teammates)
            {
                if (tm == agent || tm.IsGoalkeeper) continue; // Ignore GK in outfield spacing
                
                float dist = Vector3.Distance(myTargetPos, tm.transform.position);
                if (dist < radius && dist > 0.1f)
                {
                    Vector3 away = (myTargetPos - tm.transform.position).normalized;
                    // Stronger repulsion the closer they are
                    repulsion += away * (radius - dist) / radius; 
                    count++;
                }
            }
            if (count > 0) 
            {
                Vector3 finalRepulsion = (repulsion / count).normalized * 2.5f; // Push by max 2.5m
                finalRepulsion.y = 0; // Keep horizontal
                return finalRepulsion;
            }
            return Vector3.zero;
        }

        private void SetSafeDestination(Vector3 targetPos, bool sprint = false)
        {
            // Simple field clamp using Settings
            float w = FieldHalfWidth;
            float l = FieldHalfLength;

            targetPos.x = Mathf.Clamp(targetPos.x, -w, w);
            targetPos.z = Mathf.Clamp(targetPos.z, -l, l);
            
            if (sprint)
                agent.Mover.SprintTo(targetPos);
            else
                agent.Mover.MoveTo(targetPos);
        }

        public override void PhysicsExecute() { }
        public override void Exit() { }
    }
}
