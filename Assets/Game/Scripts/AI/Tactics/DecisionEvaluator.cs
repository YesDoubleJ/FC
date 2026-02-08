using UnityEngine;
using Game.Scripts.AI.DecisionMaking;
using Game.Scripts.Managers;
using Game.Scripts.Data;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// Evaluates and scores different actions (Pass, Shoot, Dribble) for attacking players.
    /// Extracted from RootState_Attacking to separate decision-making logic.
    /// </summary>
    public class DecisionEvaluator
    {
        private UtilityScorer _scorer;
        private Vector3 _goalPosition;
        private MatchEngineConfig _config;

        // Tuning Values (From MatchEngineConfig)
        private float ShootThreshold => _config != null ? _config.ShootThreshold : 0.5f;
        private float PassThreshold => _config != null ? _config.PassThreshold : 0.6f;
        private float DribbleThreshold => _config != null ? _config.DribbleThreshold : 0.3f;
        private float BreakawayDistance => _config != null ? _config.BreakawayDistance : 14f;
        private float BaseDribbleScore => _config != null ? _config.BaseDribbleScore : 0.4f;
        
        private float ActionLockoutTime => _config ? _config.ActionLockoutTime : 0.5f;

        public DecisionEvaluator(Vector3 goalPosition, MatchEngineConfig config)
        {
            _config = config;
            // UtilityScorer needs to be updated or we pass config if it accepts it
            _scorer = new UtilityScorer(config);
            _goalPosition = goalPosition;
        }

        // =========================================================
        // DECISION RESULT
        // =========================================================
        public struct DecisionResult
        {
            public string Action; // "Pass", "Shoot", "Dribble", "Hold", "Breakthrough"
            public GameObject Target; // Pass/Dribble target
            public Vector3 Position; // Shoot/Dribble position
            public float Confidence; // Score
        }

        // =========================================================
        // MAIN DECISION LOGIC
        // =========================================================
        public DecisionResult EvaluateBestAction(HybridAgentController agent, ref float lastActionTime, ref float aimingTimer)
        {
            // Default: Hold
            DecisionResult result = new DecisionResult { Action = "Hold", Confidence = 0f };

            // STRICT POSSESSION CHECK
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentBallOwner != agent)
                return result;

            // CRITICAL: If we are already kicking (or rotating), DO NOT DECIDE AGAIN
            if (agent.IsBusy || (agent.BallHandler && agent.BallHandler.HasPendingKick))
                return result;

            // CRITICAL: Strict Lockout
            if (Time.time < lastActionTime + ActionLockoutTime)
                return result;

            float distToGoal = Vector3.Distance(agent.transform.position, _goalPosition);
            float pressure = _scorer.CalculatePressureScore(agent);

            // 1. SKILL CHECK: Breakthrough
            // Replace PhysicsUtils.IsFrontalBlocked -> SkillSystem.IsFrontalBlocked
            if (agent.SkillSystem != null && agent.SkillSystem.IsFrontalBlocked())
            {
                if (agent.SkillSystem.CanUseBreakthrough)
                {
                    result.Action = "Breakthrough";
                    result.Confidence = 1.0f;
                    return result;
                }
                pressure = 1.0f; // Force Evasion/BackPass
            }

            // 2. BREAKAWAY LOGIC
            bool isBreakaway = CheckBreakaway(agent, distToGoal);
            if (isBreakaway && distToGoal > BreakawayDistance)
            {
                result.Action = "Dribble";
                // Pass true for forceEvade to encourage keeping safe distance while advancing
                result.Position = FindBestDribbleTarget(agent, agent.transform.position, false);
                result.Confidence = 0.9f;
                return result;
            }

            // 3. SCORE CALCULATION
            float shootScore = CalculateShootScore(agent, distToGoal, pressure);
            float passScore = CalculatePassScore(agent, pressure);
            float dribbleScore = CalculateDribbleScore(agent, pressure);

            // 4. DETERMINE BEST ACTION
            if (shootScore > ShootThreshold && shootScore >= passScore && shootScore >= dribbleScore)
            {
                result.Action = "Shoot";
                // Modernize: Use UtilityScorer for target
                result.Position = _scorer.GetBestShootingTarget(agent, _goalPosition, _goalPosition.z);
                result.Confidence = shootScore;
            }
            else if (passScore > PassThreshold && passScore >= dribbleScore)
            {
                result.Action = "Pass";
                result.Target = FindBestPassTarget(agent);
                result.Confidence = passScore;
            }
            else if (dribbleScore > DribbleThreshold)
            {
                result.Action = "Dribble";
                result.Position = FindBestDribbleTarget(agent, agent.transform.position, pressure > 0.7f);
                result.Confidence = dribbleScore;
            }

            return result;
        }

        // =========================================================
        // SCORING CALCULATIONS
        // =========================================================
        private float CalculateShootScore(HybridAgentController agent, float distToGoal, float pressure)
        {
            // Use UtilityScorer with correct signature: (fromPos, stats, goalPosition)
            // Note: agent.Stats might be null if not initialized, but HybridAgentController usually has it.
            float xG = _scorer.CalculateShootScore(agent.transform.position, agent.Stats, _goalPosition);
            
            // Reduce xG if under pressure
            xG *= (1.0f - pressure * 0.5f);
            
            // Bonus for 1v1 situations
            if (Is1v1Situation(agent, _goalPosition))
            {
                xG += 0.2f;
            }

            return xG;
        }

        private float CalculatePassScore(HybridAgentController agent, float pressure)
        {
            var bestTarget = FindBestPassTarget(agent);
            if (bestTarget == null) return 0f;

            // Simple pass score based on distance and pressure
            float maxRange = _config ? _config.MaxPassRange : 40f; 
            float dist = Vector3.Distance(agent.transform.position, bestTarget.transform.position);
            float passScore = Mathf.Clamp01(1.0f - (dist / maxRange)); // Closer = better
            
            // Increase pass score under high pressure
            passScore += pressure * 0.3f;

            return passScore;
        }

        private float CalculateDribbleScore(HybridAgentController agent, float pressure)
        {
            // Base dribble score
            float dribbleScore = BaseDribbleScore;

            // Reduce if under pressure
            dribbleScore -= pressure * 0.3f;

            // Increase if space ahead
            if (agent.SkillSystem != null && !agent.SkillSystem.IsFrontalBlocked())
            {
                dribbleScore += 0.2f;
            }

            return Mathf.Clamp01(dribbleScore);
        }

        // =========================================================
        // HELPER METHODS
        // =========================================================
        private GameObject FindBestPassTarget(HybridAgentController agent)
        {
            var teammates = agent.GetTeammates();
            GameObject bestTarget = null;
            float bestScore = 0f;

            foreach (var tm in teammates)
            {
                if (tm == agent) continue;
                
                // Simple scoring: prefer teammates closer to goal
                float distToGoal = Vector3.Distance(tm.transform.position, _goalPosition);
                float score = 100f - distToGoal; // Closer to goal = higher score
                
                // Penalize if teammate is marked (Simple distance check)
                float enemyDensity = CalculateEnemyDensity(tm.transform.position, tm);
                score -= enemyDensity * 10f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = tm.gameObject;
                }
            }

            return bestTarget;
        }

        public Vector3 FindBestDribbleTarget(HybridAgentController agent, Vector3 ballPos, bool forceEvade = false)
        {
            Vector3 dirToGoal = (_goalPosition - ballPos).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dirToGoal);
            
            // Generate 8 candidate directions
            Vector3[] candidates = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(i * 45f, Vector3.up) * dirToGoal;
                candidates[i] = ballPos + dir.normalized * 10f;
            }
            
            float bestScore = -999f;
            Vector3 bestPos = _goalPosition;
            
            // Field Bounds from Settings
            float w = 32f;
            float l = 48f;
            if (agent.BallHandler && agent.BallHandler.settings)
            {
                w = agent.BallHandler.settings.fieldHalfWidth;
                l = agent.BallHandler.settings.fieldHalfLength;
            }
            
            foreach (var target in candidates)
            {
                float score = 0f;
                
                // 1. Goal Progress
                float distToGoal = Vector3.Distance(target, _goalPosition);
                float progressScore = (100f - distToGoal) * 0.05f;
                
                // 2. Goal Alignment
                Vector3 moveDir = (target - ballPos).normalized;
                float alignmentToGoal = Vector3.Dot(moveDir, dirToGoal);
                float alignmentBonus = alignmentToGoal * 15f;
                
                // 3. Enemy Density
                float enemyDensity = CalculateEnemyDensity(target, agent);
                float enemyPenalty = enemyDensity * 10f;
                
                // 4. Evasion Bonus
                float evasionBonus = 0f;
                if (forceEvade)
                {
                    float perpendicularity = Mathf.Abs(Vector3.Dot(moveDir, right));
                    evasionBonus = perpendicularity * 8f;
                    progressScore *= 0.5f;
                    alignmentBonus *= 0.3f;
                }
                
                score = progressScore + alignmentBonus - enemyPenalty + evasionBonus;
                
                // Dynamic Field Bounds Check
                // Reduce slightly (e.g. -1 for dribble buffer)
                if (Mathf.Abs(target.x) > (w - 1f) || Mathf.Abs(target.z) > (l - 1f))
                {
                    score -= 500f;
                }
                
                // Near bounds: prioritize turning inward
                bool nearSide = Mathf.Abs(ballPos.x) > (w - 7f);
                bool nearEnd = Mathf.Abs(ballPos.z) > (l - 8f);
                
                if (nearSide || nearEnd)
                {
                    Vector3 toCenter = -ballPos.normalized;
                    if (Vector3.Dot(moveDir, toCenter) > 0.3f)
                    {
                        score += 30f;
                    }
                }

                // Strategic retreat bonus when evading
                if (forceEvade && alignmentToGoal < -0.1f)
                {
                    score += 15f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = target;
                }
            }
            
            return bestPos;
        }

        // PERFORMANCE OPTIMIZATION: Removed FindObjectsByType
        public float CalculateEnemyDensity(Vector3 pos, HybridAgentController agent)
        {
            float density = 0f;
            if (MatchManager.Instance == null) return 0f;

            // Use Cached Opponents List
            var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
            
            foreach (var opp in opponents)
            {
                if (opp == null || opp == agent) continue;

                float dist = Vector3.Distance(opp.transform.position, pos);
                float checkDist = _config ? _config.DangerRadius : 5.0f;

                if (dist < checkDist)
                {
                    density += (checkDist - dist);
                }
            }
            return density;
        }

        public bool Is1v1Situation(HybridAgentController agent, Vector3 goalPos)
        {
             Vector3 toGoal = (goalPos - agent.transform.position);
             float distToGoal = toGoal.magnitude;
             Vector3 dirToGoal = toGoal.normalized;

             if (MatchManager.Instance != null)
             {
                 var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
                 foreach (var opp in opponents)
                 {
                     if (!opp.IsGoalkeeper)
                     {
                         Vector3 toOpp = opp.transform.position - agent.transform.position;
                         float dist = toOpp.magnitude;
                         
                         if (dist > distToGoal) continue;
                         if (Vector3.Dot(dirToGoal, toOpp.normalized) < 0) continue;

                         Vector3 oppProj = Vector3.Project(toOpp, dirToGoal);
                         Vector3 rejection = toOpp - oppProj;
                         
                         if (rejection.magnitude < 2.5f) return false;
                     }
                 }
                 return true; 
             }
             return false;
        }

        private bool CheckBreakaway(HybridAgentController agent, float distToGoal)
        {
            // Use Threshold from Settings
            if (distToGoal <= BreakawayDistance) return false;

            float nearestDefenderDist = 999f;
            if (MatchManager.Instance != null)
            {
                var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
                foreach (var opp in opponents)
                {
                    // Ignore GK in breakaway logic (Breakaway means getting past defenders)
                    if (!opp.IsGoalkeeper)
                    {
                        float oppDist = Vector3.Distance(opp.transform.position, _goalPosition);
                        if (oppDist < nearestDefenderDist)
                        {
                            nearestDefenderDist = oppDist;
                        }
                    }
                }
            }

            // Breakaway if I am significantly closer to goal than nearest defender
            return (distToGoal < nearestDefenderDist - 3f);
        }
    }
}
