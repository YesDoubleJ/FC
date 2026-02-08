using UnityEngine;
using UnityEditor;

namespace Game.Editor.Tools
{
    public class GoalCreator
    {
        [MenuItem("Tools/LottoSoccer/Create Goal")]
        public static void CreateGoal()
        {
            GameObject goalRoot = new GameObject("SoccerGoal");
            
            // 1. Posts (Height 2.44m, Width 7.32m standard, simplified here)
            // Left Post
            CreateBox(goalRoot, "Post_L", new Vector3(-3.66f, 1.22f, 0), new Vector3(0.1f, 2.44f, 0.1f));
            // Right Post
            CreateBox(goalRoot, "Post_R", new Vector3(3.66f, 1.22f, 0), new Vector3(0.1f, 2.44f, 0.1f));
            // Crossbar
            CreateBox(goalRoot, "Crossbar", new Vector3(0, 2.44f, 0), new Vector3(7.42f, 0.1f, 0.1f));

            // 2. Goal Center (For AI)
            GameObject center = new GameObject("GoalCenter");
            center.transform.SetParent(goalRoot.transform);
            center.transform.localPosition = new Vector3(0, 0, 0); // On the line
            
            // 3. Net (Visual/Trigger) - Optional simplified box behind
            GameObject net = GameObject.CreatePrimitive(PrimitiveType.Cube);
            net.name = "NetTrigger";
            net.transform.SetParent(goalRoot.transform);
            net.transform.localPosition = new Vector3(0, 1.22f, 1f);
            net.transform.localScale = new Vector3(7.32f, 2.44f, 2f);
            net.GetComponent<Collider>().isTrigger = true;
            Renderer renderer = net.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Standard")); // Improve later
                Color c = Color.white; c.a = 0.3f;
                renderer.material.color = c;
                // renderer.material.SetOverrideTag("RenderType", "Transparent");
                // Transparency setup requires more shader work, keeping simple for now.
            }

            // Position Goal in World
            goalRoot.transform.position = new Vector3(0, 0, 25f); // End of field
            goalRoot.transform.rotation = Quaternion.Euler(0, 180, 0); // Facing center

            Selection.activeGameObject = goalRoot;
            Undo.RegisterCreatedObjectUndo(goalRoot, "Create Goal");
            
            Debug.Log("Goal Created! Assigned 'GoalCenter' to GK.");
        }

        private static void CreateBox(GameObject parent, string name, Vector3 localPos, Vector3 scale)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent.transform);
            obj.transform.localPosition = localPos;
            obj.transform.localScale = scale;
        }
    }
}
