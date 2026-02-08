using UnityEngine;
using Game.Scripts.Data;
// using Game.Scripts.AI.Settings;
using Game.Scripts.Managers;
using System.Collections.Generic;

namespace Game.Scripts.AI.DecisionMaking
{
    public class UtilityScorer
    {
        private readonly MatchEngineConfig _config;

        // Fallbacks if settings are missing
        private float MaxShootRange => 35f; // Hardcoded fallback or from config derived
        private float SweetSpotRange => _config ? _config.SweetSpotRange : 22f;
        // private float GoalWidth => _settings ? _settings.goalWidth : 7.32f; // Assuming standard goal width
        // private float PostMargin => _settings ? _settings.postMargin : 0.5f;
        
        private float MinPassDist => _config ? _config.MinPassDist : 5f;
        private float MaxPassDist => _config ? _config.MaxPassRange : 40f;
        
        private float DangerRadius => _config ? _config.DangerRadius : 4f;
        // private float EnemyDensityCheckDist => _settings ? _settings.enemyDensityCheckDist : 5f;
        
        private float DistanceWeight => _config ? _config.DistanceWeight : 0.85f;
        private float AngleWeight => _config ? _config.AngleWeight : 0.15f;
        private float StatWeight => _config ? _config.StatWeight : 0.8f;

        public UtilityScorer(MatchEngineConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Calculates the "Expected Goal" (xG) score from a specific position.
        /// 0.0 (Impossible) -> 1.0 (Guaranteed Goal)
        /// </summary>
        public float CalculateShootScore(Vector3 fromPos, PlayerStats stats, Vector3 goalPosition)
        {
            // 1. Distance Factor
            float distance = Vector3.Distance(fromPos, goalPosition);
            
            float maxShootRange = 35f;

            float distanceScore = 0f;
            if (distance < SweetSpotRange) 
            {
                distanceScore = 1.0f; // Sweet Spot
            }
            else if (distance > maxShootRange) 
            {
                distanceScore = 0.05f; // Impossible
            }
            else
            {
                // Sharp Drop-off
                float t = (distance - SweetSpotRange) / (maxShootRange - SweetSpotRange); // 0 to 1
                distanceScore = Mathf.Lerp(1.0f, 0.05f, t);
            }
            
            // 2. Angle Factor (Centrality)
            float absX = Mathf.Abs(fromPos.x);
            float angleScore = Mathf.Lerp(1.0f, 0.4f, absX / 30f);
            
            // 3. Stat Factor
            float statScore = (stats != null) ? (stats.GetStat(StatType.Shooting) / 100.0f) : 0.5f;
            
            // Weighting
            float baseScore = (distanceScore * DistanceWeight) + (angleScore * AngleWeight);
            
            // Apply Stat Multiplier
            float statMult = 0.8f + (statScore * 0.4f);
            
            return Mathf.Clamp01(baseScore * statMult);
        }

        public float CalculatePassScore(HybridAgentController agent, PlayerStats stats, GameObject teammate)
        {
            if (teammate == null) return 0f;

            // 1. Distance Factor
            float distance = Vector3.Distance(agent.transform.position, teammate.transform.position);
            float distanceScore = 1f;
            if (distance < MinPassDist) distanceScore = 0.5f; // Too close
            else if (distance > MaxPassDist) distanceScore = 0.2f; // Too far

            // 2. Stat Factor
            float statScore = stats.GetStat(StatType.Passing) / 100.0f;

            return (distanceScore * 0.5f) + (statScore * 0.5f);
        }

        public float CalculateDribbleScore(HybridAgentController agent, PlayerStats stats, Vector3 targetDirection)
        {
            float baseScore = 0.3f;
            
            // Check for enemies
            Vector3 checkPos = agent.transform.position + targetDirection.normalized * 5f;
            float enemyDensity = CalculateEnemyDensityAt(agent, checkPos);
            
            // Less enemies = better
            float spaceBonus = Mathf.Lerp(0.4f, -0.2f, Mathf.Clamp01(enemyDensity / 3f));
            
            // Stat bonus
            float statBonus = (stats != null) ? (stats.GetStat(StatType.Dribbling) / 100f) * 0.2f : 0f;
            
            return Mathf.Clamp01(baseScore + spaceBonus + statBonus);
        }

        /// <summary>
        /// Calculates pressure level based on nearby opponents.
        /// Optimized to use MatchManager cached lists.
        /// </summary>
        public float CalculatePressureScore(HybridAgentController agent)
        {
            if (MatchManager.Instance == null) return 0f;

            float pressure = 0f;
            float radius = DangerRadius;
            
            // Use cached opponents list
            var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
            if (opponents == null) return 0f;

            foreach (var other in opponents)
            {
                if (other == null) continue;
                
                float dist = Vector3.Distance(agent.transform.position, other.transform.position);
                
                if (dist < radius)
                {
                    // Closer = more pressure
                    float contribution = Mathf.Pow(1f - (dist / radius), 2f) * 0.5f;
                    pressure += contribution;
                }
            }
            
            return Mathf.Clamp01(pressure);
        }

        private float CalculateEnemyDensityAt(HybridAgentController agent, Vector3 pos)
        {
            if (MatchManager.Instance == null) return 0f;

            float density = 0f;
            var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
            if (opponents == null) return 0f;

            foreach (var other in opponents)
            {
                if (other == null) continue;
                
                float dist = Vector3.Distance(pos, other.transform.position);
                if (dist < DangerRadius)
                {
                    density += (DangerRadius - dist) / DangerRadius;
                }
            }
            
            return density;
        }

        /// <summary>
        /// SMART FINISHING: Returns the best point in the goal to aim at.
        /// Uses Dynamic Goal Width from settings.
        /// </summary>
        public Vector3 GetBestShootingTarget(HybridAgentController shooter, Vector3 goalCenter, float goalLineZ)
        {
            // Define Targets (Local relative to Goal Center)
            float goalWidth = 7.32f;
            float postMargin = 0.5f;
            float halfWidth = (goalWidth * 0.5f) - postMargin; // Reduce by margin to avoid post
            
            // Expanded Candidate Points
            Vector3[] targets = new Vector3[5];
            targets[0] = goalCenter; // Center
            targets[1] = goalCenter + new Vector3(-halfWidth * 0.6f, 0, 0); // Left Inner
            targets[2] = goalCenter + new Vector3(halfWidth * 0.6f, 0, 0);  // Right Inner
            targets[3] = goalCenter + new Vector3(-halfWidth, 0, 0); // Left Corner
            targets[4] = goalCenter + new Vector3(halfWidth, 0, 0);  // Right Corner
            
            float bestScore = -1f;
            Vector3 bestTarget = goalCenter;
            
            // Find GK (Optimization: Use MatchManager)
            HybridAgentController gk = null;
            if (MatchManager.Instance != null)
            {
                var opponents = MatchManager.Instance.GetOpponents(shooter.TeamID);
                if (opponents != null)
                {
                    foreach(var a in opponents)
                    {
                        if (a != null && a.IsGoalkeeper) 
                        {
                            gk = a;
                            break;
                        }
                    }
                }
            }
            
            float safeRadius = 2.0f; // Tunable?
            
            foreach (Vector3 t in targets)
            {
                float score = 1.0f;
                
                // 1. Difficulty / Reward
                float distFromCenter = Mathf.Abs(t.x - goalCenter.x);
                
                // Bonus for aiming at corners
                if (distFromCenter > (7.32f * 0.3f)) score += 0.15f; 
                
                // 2. GK Avoidance
                if (gk != null)
                {
                    Vector3 shootDir = (t - shooter.transform.position).normalized;
                    Vector3 gkVec = gk.transform.position - shooter.transform.position;
                    
                    float projection = Vector3.Dot(gkVec, shootDir);
                    float shootDist = Vector3.Distance(shooter.transform.position, t);
                    
                    if (projection > 0 && projection < shootDist)
                    {
                        Vector3 closestPointOnLine = shooter.transform.position + shootDir * projection;
                        float distFromLine = Vector3.Distance(gk.transform.position, closestPointOnLine);
                        
                        if (distFromLine < safeRadius)
                        {
                            float avoidance = Mathf.Clamp01(distFromLine / safeRadius);
                            if (distFromLine < 1.0f) score *= 0.05f;
                            else score *= (0.2f + 0.8f * avoidance);
                        }
                        else
                        {
                             score += 0.2f; // Good shot
                        }
                    }
                    else
                    {
                         score += 0.5f; // Safe
                    }
                }
                
                // 3. Block Check (Raycast)
                Vector3 finalShootDir = (t - shooter.transform.position).normalized;
                float finalDist = Vector3.Distance(shooter.transform.position, t);
                
                if (UnityEngine.Physics.Raycast(shooter.transform.position + Vector3.up * 0.5f, finalShootDir, out RaycastHit hit, finalDist, LayerMask.GetMask("Player")))
                {
                     if (hit.collider.gameObject != shooter.gameObject)
                     {
                         score *= 0.05f; // HARD BLOCK
                     }
                }
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = t;
                }
            }
            
            return bestTarget;
        }

        public float CalculateSupportScore(Vector3 candidatePos, Vector3 ballPos, Vector3 goalPos, 
            IEnumerable<HybridAgentController> enemies, float offsideLineZ, Team myTeam)
        {
             // 1. Offside Check
             bool isOffside = false;
             if (myTeam == Team.Home) { if (candidatePos.z >= offsideLineZ) isOffside = true; }
             else { if (candidatePos.z <= offsideLineZ) isOffside = true; }
             
             if (isOffside) return 0.0f;

             // 2. Space Score
             float closestEnemyDist = 100f;
             if (enemies != null)
             {
                 foreach (var enemy in enemies)
                 {
                     if (enemy == null) continue;
                     float d = Vector3.Distance(candidatePos, enemy.transform.position);
                     if (d < closestEnemyDist) closestEnemyDist = d;
                 }
             }
             
             float spaceScore = Mathf.Clamp01(closestEnemyDist / 15f);
             
             // 3. Threat Score
             float distToGoal = Vector3.Distance(candidatePos, goalPos);
             float threatScore = Mathf.Clamp01(1.0f - ((distToGoal - 10f) / 50f));

             // 4. Pass Lane Safety
             float laneSafety = 1.0f;
             Vector3 passDir = (candidatePos - ballPos).normalized;
             float passDist = Vector3.Distance(candidatePos, ballPos);
             
             if (enemies != null)
             {
                 foreach (var enemy in enemies)
                 {
                     if (enemy == null) continue;
                     Vector3 enemyVec = enemy.transform.position - ballPos;
                     float projection = Vector3.Dot(enemyVec, passDir);
                     
                     if (projection > 0 && projection < passDist)
                     {
                         Vector3 closestPointOnLine = ballPos + passDir * projection;
                         float distFromLine = Vector3.Distance(enemy.transform.position, closestPointOnLine);
                         
                         if (distFromLine < 2.0f)
                         {
                             laneSafety *= 0.2f;
                         }
                     }
                 }
             }

             // 5. Distance from Ball
             float distScore = 0f;
             if (passDist < MinPassDist) distScore = 0.2f;
             else if (passDist < (MaxPassDist - 5f)) distScore = 1.0f;
             else distScore = Mathf.Clamp01(1.0f - ((passDist - (MaxPassDist - 5f)) / 5f));

             float total = (spaceScore * 0.4f + threatScore * 0.3f + laneSafety * 0.3f) * distScore;
             
             return total;
        }

        public float CalculateInterceptionRisk(Vector3 startPos, Vector3 endPos, float ballSpeed, IEnumerable<HybridAgentController> opponents)
        {
            float maxRisk = 0f;
            Vector3 passDir = (endPos - startPos).normalized;
            float passDist = Vector3.Distance(startPos, endPos);
            
            if (opponents != null)
            {
                foreach (var opp in opponents)
                {
                    if (opp == null) continue;
                    
                    if (Vector3.Distance(startPos, opp.transform.position) > passDist + 5f) continue;

                    Vector3 toOpp = opp.transform.position - startPos;
                    float projection = Vector3.Dot(toOpp, passDir);
                    
                    if (projection > 1.0f && projection < passDist - 1.0f)
                    {
                        Vector3 pointOnLine = startPos + passDir * projection;
                        float distFromLine = Vector3.Distance(opp.transform.position, pointOnLine);
                        
                        float timeBall = projection / ballSpeed;
                        
                        float oppSpeed = (opp.Velocity.magnitude > 2f) ? opp.Velocity.magnitude : 6.0f; 
                        float timeOpp = distFromLine / oppSpeed;
                        
                        if (timeOpp < timeBall + 0.2f)
                        {
                            float interceptRisk = Mathf.Clamp01(1.0f - (distFromLine / 2.5f));
                            if (interceptRisk > maxRisk) maxRisk = interceptRisk;
                        }
                    }
                }
            }
            return maxRisk;
        }
    }
}
