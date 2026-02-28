using UnityEngine;
using Game.Scripts.Managers;
using Game.Scripts.Data;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// Lightweight Pitch Control Evaluator for Off-ball AI.
    /// Evaluates the tactical value of a specific point on the pitch based on proximity to friendly and enemy players, 
    /// as well as the passing lane to the ball carrier.
    /// </summary>
    public static class PitchControlEvaluator
    {
        // Settings for Gaussian influence
        private const float MAX_EVAL_RADIUS = 20f; // Ignore players further than 20m from the point
        private const float SPREAD = 5.0f;         // Gaussian spread (controls how fast influence decays)
        
        /// <summary>
        /// Evaluates a candidate point. A higher score means it is safer and better controlled by the agent's team.
        /// Returns a value typically between -1.0 (enemy heavily controls) to 1.0 (friendly heavily controls), 
        /// though values can exceed these bounds depending on player density.
        /// </summary>
        public static float GetControlScore(Vector3 point, Team myTeam)
        {
            float score = 0f;
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null) return 0f;

            // Define teams
            var friendlies = matchMgr.GetTeammates(myTeam);
            var enemies = matchMgr.GetOpponents(myTeam);

            // 1. Calculate Enemy Pressure (Negative Score)
            foreach (var enemy in enemies)
            {
                if (enemy.IsGoalkeeper) continue;

                float dist = Vector3.Distance(point, enemy.transform.position);
                if (dist > MAX_EVAL_RADIUS) continue;

                // Influence decays exponentially based on distance
                float influence = Mathf.Exp(-(dist * dist) / (2f * SPREAD * SPREAD));
                
                // If enemy is sprinting, their area of influence stretches (simplified: base influence increases slightly)
                float speedMod = (enemy.Mover != null && enemy.Mover.IsSprinting) ? 1.2f : 1.0f;
                
                score -= (influence * speedMod);
            }

            // 2. Calculate Friendly Support (Positive Score)
            // (Optional: Weigh friendlies less than enemies to encourage finding truly empty space, 
            // rather than just standing exactly next to a teammate)
            foreach (var friendly in friendlies)
            {
                if (friendly.IsGoalkeeper) continue;

                float dist = Vector3.Distance(point, friendly.transform.position);
                if (dist > MAX_EVAL_RADIUS) continue;
                if (dist < 1.0f) 
                {
                    // Penalty for standing too close to another teammate (prevents clumping)
                    score -= 1.0f;
                    continue;
                }

                float influence = Mathf.Exp(-(dist * dist) / (2f * SPREAD * SPREAD));
                score += (influence * 0.5f); // Friendlies contribute half as much as enemies subtract
            }

            return score;
        }

        /// <summary>
        /// Evaluates the Pass Lane from the given point to the ball carrier.
        /// Returns 1.0f if the lane is completely clear, down to 0.0f (or negative) if heavily blocked.
        /// </summary>
        public static float GetPassLaneScore(Vector3 point, Team myTeam)
        {
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentBallOwner == null) return 0f;

            // If the ball owner is an enemy, pass lane is irrelevant (we are defending or waiting for turnover)
            if (matchMgr.CurrentBallOwner.TeamID != myTeam) return 0f;

            Vector3 ballPos = matchMgr.Ball.transform.position;
            Vector3 passDir = (point - ballPos).normalized;
            float passDist = Vector3.Distance(ballPos, point);

            var enemies = matchMgr.GetOpponents(myTeam);

            float laneScore = 1.0f; // Start perfect

            foreach (var enemy in enemies)
            {
                if (enemy.IsGoalkeeper) continue;

                Vector3 enemyVec = enemy.transform.position - ballPos;
                float dot = Vector3.Dot(passDir, enemyVec);

                // If enemy is behind the ball or behind the target point, they don't block
                if (dot < 0 || dot > passDist) continue;

                // Calculate orthogonal distance from the pass line
                Vector3 projection = passDir * dot;
                float distToLine = Vector3.Distance(enemyVec, projection);

                // If enemy is very close to the line, reduce score drastically
                if (distToLine < 1.5f) 
                {
                    laneScore -= 1.0f; // Strongly blocked
                }
                else if (distToLine < 3.0f)
                {
                    laneScore -= 0.4f; // Partially blocked
                }
            }

            return Mathf.Clamp(laneScore, -1f, 1f);
        }
    }
}
