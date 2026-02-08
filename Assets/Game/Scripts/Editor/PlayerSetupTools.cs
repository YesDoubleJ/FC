using UnityEngine;
using UnityEditor;
using Game.Scripts.AI; // For Controller classes
using Game.Scripts.Data; // For Team Enum
using UnityEngine.AI;

namespace Game.Scripts.Editor
{
    public class PlayerSetupTools
    {
        // === HOME TEAM ===
        [MenuItem("Tools/Lotto/Home/Setup GK")]
        public static void SetupHomeGK() => SetupPlayer(Team.Home, Game.Scripts.Tactics.FormationPosition.GK, true);

        [MenuItem("Tools/Lotto/Home/Setup CB Left")]
        public static void SetupHomeCB_Left() => SetupPlayer(Team.Home, Game.Scripts.Tactics.FormationPosition.CB_Left, false);
        
        [MenuItem("Tools/Lotto/Home/Setup CB Right")]
        public static void SetupHomeCB_Right() => SetupPlayer(Team.Home, Game.Scripts.Tactics.FormationPosition.CB_Right, false);

        [MenuItem("Tools/Lotto/Home/Setup CM Left")]
        public static void SetupHomeCM_Left() => SetupPlayer(Team.Home, Game.Scripts.Tactics.FormationPosition.CM_Left, false);

        [MenuItem("Tools/Lotto/Home/Setup CM Right")]
        public static void SetupHomeCM_Right() => SetupPlayer(Team.Home, Game.Scripts.Tactics.FormationPosition.CM_Right, false);

        [MenuItem("Tools/Lotto/Home/Setup ST Center")]
        public static void SetupHomeST_Center() => SetupPlayer(Team.Home, Game.Scripts.Tactics.FormationPosition.ST_Center, false);


        // === AWAY TEAM ===
        [MenuItem("Tools/Lotto/Away/Setup GK")]
        public static void SetupAwayGK() => SetupPlayer(Team.Away, Game.Scripts.Tactics.FormationPosition.GK, true);

        [MenuItem("Tools/Lotto/Away/Setup CB Left")]
        public static void SetupAwayCB_Left() => SetupPlayer(Team.Away, Game.Scripts.Tactics.FormationPosition.CB_Left, false);
        
        [MenuItem("Tools/Lotto/Away/Setup CB Right")]
        public static void SetupAwayCB_Right() => SetupPlayer(Team.Away, Game.Scripts.Tactics.FormationPosition.CB_Right, false);

        [MenuItem("Tools/Lotto/Away/Setup CM Left")]
        public static void SetupAwayCM_Left() => SetupPlayer(Team.Away, Game.Scripts.Tactics.FormationPosition.CM_Left, false);

        [MenuItem("Tools/Lotto/Away/Setup CM Right")]
        public static void SetupAwayCM_Right() => SetupPlayer(Team.Away, Game.Scripts.Tactics.FormationPosition.CM_Right, false);

        [MenuItem("Tools/Lotto/Away/Setup ST Center")]
        public static void SetupAwayST_Center() => SetupPlayer(Team.Away, Game.Scripts.Tactics.FormationPosition.ST_Center, false);


        private static void SetupPlayer(Team team, Game.Scripts.Tactics.FormationPosition position, bool isGK)
        {
            GameObject obj = Selection.activeGameObject;
            if (obj == null)
            {
                Debug.LogError("No object selected! Select a character model first.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(obj, "Setup Player");

            // 1. Add/Get NavMeshAgent
            NavMeshAgent nav = obj.GetComponent<NavMeshAgent>();
            if (nav == null) nav = Undo.AddComponent<NavMeshAgent>(obj);
            nav.speed = (isGK) ? 3.5f : 5.0f; // Slower GK?
            nav.angularSpeed = 360f;
            nav.acceleration = 20f;

            // 2. Add/Get Rigidbody (Required by Controller)
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(obj);
            rb.mass = 70f;
            rb.linearDamping = 1f;
            rb.angularDamping = 0.5f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // 3. Add/Get Collider
            CapsuleCollider cap = obj.GetComponent<CapsuleCollider>();
            if (cap == null) cap = Undo.AddComponent<CapsuleCollider>(obj);
            cap.center = new Vector3(0, 1, 0);
            cap.height = 2f;
            cap.radius = 0.5f;

            // 4. Controller Logic
            HybridAgentController controller = null;

            // Remove existing wrong controller if switching types
            if (isGK)
            {
                // If it has Hybrid but not GK, destroy Hybrid
                HybridAgentController hybrid = obj.GetComponent<HybridAgentController>();
                if (hybrid != null && !(hybrid is GoalkeeperController))
                {
                    Object.DestroyImmediate(hybrid);
                }

                GoalkeeperController gk = obj.GetComponent<GoalkeeperController>();
                if (gk == null) gk = Undo.AddComponent<GoalkeeperController>(obj);
                
                controller = gk;
                // gk.goalLineZ is now handled by GoalkeeperSettings
            }
            else
            {
                // If it has GK, destroy GK
                GoalkeeperController gk = obj.GetComponent<GoalkeeperController>();
                if (gk != null) Object.DestroyImmediate(gk);

                HybridAgentController striker = obj.GetComponent<HybridAgentController>();
                if (striker == null) striker = Undo.AddComponent<HybridAgentController>(obj);

                controller = striker;
            }

            // Common Setup
            if (controller != null)
            {
                controller.TeamID = team;
                controller.assignedPosition = position;
                // Auto-set name for clarity
                obj.name = $"{team}_{position}";
            }

            Debug.Log($"Successfully setup <b>{obj.name}</b> as <b>{team} {position}</b>!");
        }
    }
}
