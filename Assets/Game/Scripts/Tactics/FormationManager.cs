using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Managers;

namespace Game.Scripts.Tactics
{
    public class FormationManager : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private HomeFormationManager homeManager;
        [SerializeField] private AwayFormationManager awayManager;

        private Transform ballTransform;

        private void Awake()
        {
            var ballObj = GameObject.Find("Ball"); 
            if (ballObj != null) ballTransform = ballObj.transform;

            // Auto-find managers if not assigned
            if (homeManager == null) homeManager = FindFirstObjectByType<HomeFormationManager>();
            if (awayManager == null) awayManager = FindFirstObjectByType<AwayFormationManager>();
            
            // Auto-create if missing (Safety fallback)
            if (homeManager == null) 
            {
                var go = new GameObject("HomeFormationManager");
                homeManager = go.AddComponent<HomeFormationManager>();
            }
            if (awayManager == null) 
            {
                var go = new GameObject("AwayFormationManager");
                awayManager = go.AddComponent<AwayFormationManager>();
            }
        }

        private void ForceRepositionPlayer(string name, Vector3 pos)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                var agent = obj.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) agent.Warp(pos);
                else obj.transform.position = pos;
                
                float lookZ = (pos.z < 0) ? 1 : -1;
                obj.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, lookZ));
            }
        }

        // Default for backward compatibility (Home Team)
        public Vector3 GetAnchorPosition(FormationPosition pos)
        {
            return GetAnchorPosition(pos, Game.Scripts.Data.Team.Home);
        }

        public Vector3 GetAnchorPosition(FormationPosition pos, Game.Scripts.Data.Team team)
        {
            float ballZ = 0f;
            if (MatchManager.Instance != null && MatchManager.Instance.Ball != null)
            {
                ballZ = MatchManager.Instance.Ball.transform.position.z;
            }
            else if (ballTransform != null)
            {
                ballZ = ballTransform.position.z;
            }
            
            // Get Offset from Managers
            Vector3 offset = Vector3.zero;
            if (team == Game.Scripts.Data.Team.Home && homeManager != null) offset = homeManager.GetOffset(pos);
            else if (team == Game.Scripts.Data.Team.Away && awayManager != null) offset = awayManager.GetOffset(pos);
            
            // Get Field Dimensions
            float fieldLimit = 45f;
            float gkLimit = 49.5f;
            if (FieldManager.Instance != null)
            {
                fieldLimit = (FieldManager.Instance.Length / 2f) - 5f; // 50 - 5 = 45
                gkLimit = (FieldManager.Instance.Length / 2f) - 0.5f; // 49.5
            }
            
            // Anchor Logic:
            float teamCenterZ = Mathf.Clamp(ballZ, -30f, 30f);
            
            // Since Away offsets are already mirrored (e.g. +20 for CB), we just ADD.
            float finalZ = teamCenterZ + offset.z;
            
            // Clamp
            float minZ = -fieldLimit;
            float maxZ = fieldLimit;
            
            if (pos == FormationPosition.GK)
            {
                // GK Clamping depends on team?
                if (team == Game.Scripts.Data.Team.Home) { minZ = -gkLimit; maxZ = 40f; }
                else { minZ = -40f; maxZ = gkLimit; }
            }
            else
            {
                // Field Player Clamping
                if (team == Game.Scripts.Data.Team.Home) { minZ = -fieldLimit; maxZ = 45f; }
                else { minZ = -45f; maxZ = fieldLimit; }
            }

            finalZ = Mathf.Clamp(finalZ, minZ, maxZ);
            
            return new Vector3(offset.x, 0.05f, finalZ);
        }

        // kick-off specific position
        public Vector3 GetKickoffPosition(FormationPosition pos, Game.Scripts.Data.Team team, bool isKickOffTeam)
        {
            Vector3 offset = Vector3.zero;
            if (team == Game.Scripts.Data.Team.Home && homeManager != null) offset = homeManager.GetOffset(pos);
            else if (team == Game.Scripts.Data.Team.Away && awayManager != null) offset = awayManager.GetOffset(pos);

            // KICKOFF START POSITION LOGIC
            // User Request: Don't mess up the formation settings! Just enforce Rules.

            // 1. Base Position directly from Offset (Captured from Scene)
            // Assumes offsets are defined in Field Coordinates (Home < 0, Away > 0)
            float finalX = offset.x;
            float finalZ = offset.z;

            // 2. KICKOFF TAKER EXCEPTION (Rules: 1 or 2 players at center)
            bool isStriker = (pos == FormationPosition.ST_Center || pos == FormationPosition.ST_Left || pos == FormationPosition.ST_Right);
            
            // If it is MY Kickoff, put me on the ball (Center Spot)
            if (isKickOffTeam && isStriker)
            {
                 // Central Striker takes priority for center spot
                 if (pos == FormationPosition.ST_Center)
                 {
                     float strikerZ = (team == Game.Scripts.Data.Team.Home) ? -0.9f : 0.9f; // 0.9m (Safe but close)
                     return new Vector3(0, 0.05f, strikerZ); 
                 }
                 // Fallback for side strikers if Center is missing (rare in 6v6 but safe)
                 float finalKickerZ = (team == Game.Scripts.Data.Team.Home) ? -0.9f : 0.9f;
                 // Actually, let's put ST_Left/Right just slightly back.
                 float finalKickerX = (pos == FormationPosition.ST_Left) ? -0.8f : 0.8f;
                 if (team == Game.Scripts.Data.Team.Away) finalKickerX *= -1; 
                 
                 return new Vector3(finalKickerX, 0.05f, finalKickerZ);
            }

            // 3. ENFORCE RULES (Stay in Own Half)
            if (team == Game.Scripts.Data.Team.Home)
            {
                // Home must be Z < 0
                if (finalZ > -0.5f) finalZ = -0.5f; 
            }
            else
            {
                // Away must be Z > 0
                if (finalZ < 0.5f) finalZ = 0.5f;
            }

            // 4. DEFENDING TEAM RULE: Outside Center Circle (9.15m)
            if (!isKickOffTeam)
            {
                float distToCenter = Mathf.Sqrt(finalX*finalX + finalZ*finalZ);
                if (distToCenter < 9.15f)
                {
                    // Push out radially
                    Vector3 posVec = new Vector3(finalX, 0f, finalZ);
                    posVec = posVec.normalized * 9.5f; // Push to 9.5m
                    finalX = posVec.x;
                    finalZ = posVec.z;
                }
            }
            
            return new Vector3(finalX, 0.05f, finalZ);
        }

        // Helper: Get Offside Line (Z position of the last defender of the OPPOSING team)
        public float GetOffsideLine(Game.Scripts.Data.Team attackingTeam)
        {
            float extremeZ = (attackingTeam == Game.Scripts.Data.Team.Home) ? 50f : -50f;
            
            // Find all defenders of OPPOSING team
            var agents = FindObjectsByType<Game.Scripts.AI.HybridAgentController>(FindObjectsSortMode.None);
            
            if (attackingTeam == Game.Scripts.Data.Team.Home)
            {
                // Home attacking. Creating line based on AWAY defenders (Lowest Z)
                // Goal is at +50. Defenders are trying to stop us.
                // We want the Defender with the LOWEST Z (closest to our side... wait)
                // Offside line is the defender closest to THEIR goal.
                // i.e. The defender with the LARGEST Z is closest to +50? No.
                // The "Second Last Defender" rule.
                // Usually GK is near +50.
                // We want the field player who is closest to +50 (Largest Z).
                
                List<float> defenderZs = new List<float>();
                
                foreach (var agent in agents)
                {
                    if (agent.TeamID == Game.Scripts.Data.Team.Away)
                    {
                        defenderZs.Add(agent.transform.position.z);
                    }
                }
                
                defenderZs.Sort(); // Ascending (-40 ... +40)
                // Closest to Goal (+50) are at the END of the list.
                // GK is usually last (e.g. 48).
                // Last Defender is second to last.
                
                if (defenderZs.Count >= 2)
                {
                    // GK is Max. Defender is Max-1.
                    // Return the Z of the second deepest player
                    return defenderZs[defenderZs.Count - 2]; 
                }
                return 50f;
            }
            else
            {
                // Away attacking. Creating line based on HOME defenders.
                // Goal is at -50.
                // We want defender closest to -50. (Smallest Z).
                
                List<float> defenderZs = new List<float>();
                foreach (var agent in agents)
                {
                    if (agent.TeamID == Game.Scripts.Data.Team.Home)
                    {
                        defenderZs.Add(agent.transform.position.z);
                    }
                }
                
                defenderZs.Sort(); // Ascending (-50 ... 50)
                // Closest to Goal (-50) are at START of list.
                // GK is usually first (e.g. -48).
                // Last defender is second.
                
                if (defenderZs.Count >= 2)
                {
                    return defenderZs[1];
                }
                return -50f;
            }
        }
    }
}
