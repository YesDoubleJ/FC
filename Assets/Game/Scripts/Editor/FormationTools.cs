using UnityEngine;
using UnityEditor;
using Game.Scripts.Tactics;
using Game.Scripts.AI;
using System.Collections.Generic;

namespace Game.Scripts.Editor
{
    public class FormationTools
    {
        [MenuItem("Tools/Lotto/Capture Home Formation (From Scene)")]
        public static void CaptureHomeFormation()
        {
            CaptureFormation(Game.Scripts.Data.Team.Home);
        }

        [MenuItem("Tools/Lotto/Capture Away Formation (From Scene)")]
        public static void CaptureAwayFormation()
        {
            CaptureFormation(Game.Scripts.Data.Team.Away);
        }

        private static void CaptureFormation(Game.Scripts.Data.Team team)
        {
            // 1. Find the Manager
            TeamFormationManager manager = null;
            if (team == Game.Scripts.Data.Team.Home)
                manager = Object.FindFirstObjectByType<HomeFormationManager>();
            else
                manager = Object.FindFirstObjectByType<AwayFormationManager>();

            if (manager == null)
            {
                Debug.LogError($"Could not find {(team == Game.Scripts.Data.Team.Home ? "Home" : "Away")}FormationManager in the scene!");
                return;
            }

            // 2. Find All Agents of this Team
            var agents = Object.FindObjectsByType<HybridAgentController>(FindObjectsSortMode.None);
            List<HybridAgentController> teamAgents = new List<HybridAgentController>();
            
            foreach (var agent in agents)
            {
                if (agent.TeamID == team)
                    teamAgents.Add(agent);
            }

            if (teamAgents.Count == 0)
            {
                Debug.LogWarning($"No agents found for team {team}. Make sure HybridAgentControllers are active in the scene.");
                return;
            }

            Undo.RecordObject(manager, $"Capture {team} Formation");

            // 3. Update Offsets
            // Assumption: Scene Center (0,0,0) represents the "Anchor" point.
            // Players should be placed relative to (0,0,0) in the scene.
            
            // Rebuild the list or Update existing entries?
            // Safer to Rebuild/Update. We will create a map first.
            Dictionary<FormationPosition, Vector3> capturedOffsets = new Dictionary<FormationPosition, Vector3>();
            
            foreach (var agent in teamAgents)
            {
                Vector3 pos = agent.transform.position;
                // Flatten Y (Offset is usually 2D relative)
                // Actually, FormationEntry.offset is Vector3. But we usually ignore Y.
                // Let's keep X and Z.
                Vector3 offset = new Vector3(pos.x, 0f, pos.z);
                
                if (capturedOffsets.ContainsKey(agent.assignedPosition))
                {
                    Debug.LogWarning($"Duplicate Position found in Scene: {agent.assignedPosition}. Using {agent.name}.");
                    capturedOffsets[agent.assignedPosition] = offset;
                }
                else
                {
                    capturedOffsets.Add(agent.assignedPosition, offset);
                }
            }

            // 4. Apply to Manager List
            List<FormationEntry> newList = new List<FormationEntry>();
            
            foreach (var kvp in capturedOffsets)
            {
                newList.Add(new FormationEntry { position = kvp.Key, offset = kvp.Value });
            }
            
            manager.formationOffsets = newList;
            
            // Force Dictionary Refresh if necessary (Runtime)
            // But this is Editor time.
            EditorUtility.SetDirty(manager);

            Debug.Log($"<color=green>Successfully Captured {newList.Count} positions for {team} Team!</color>");
        }
    }
}
