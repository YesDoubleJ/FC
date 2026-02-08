using UnityEngine;
using UnityEditor;
using Game.Scripts.Managers;

namespace Game.Scripts.Editor
{
    public class WallGeneratorEditor : MonoBehaviour
    {
        [MenuItem("Tools/Lotto/Create Field Walls")]
        public static void CreateFieldWalls()
        {
            // 1. Get Dimensions
            float width = 68f; // Standard Pitch width (or read from FieldManager)
            float length = 105f; // Standard Pitch length

            // Try to read from Scene logic if available, otherwise defaults
            var data = GameObject.FindFirstObjectByType<Game.Scripts.Managers.FieldManager>();
            if (data != null)
            {
                width = data.Width;
                length = data.Length;
                Debug.Log($"Failed to find FieldManager, using found instance: {width}x{length}");
            }
            else
            {
                 // Default to the values I saw in file: 100x60
                 width = 60f;
                 length = 100f;
                 Debug.Log($"Using Default Dimensions: {width}x{length}");
            }

            // Margin (Walls slightly outside lines)
            float margin = 1.0f;
            float height = 10.0f; // Wall Height (User: Doubled to prevent aerial escape)

            // 2. Create Parent
            string parentName = "FieldWalls";
            GameObject parent = GameObject.Find(parentName);
            if (parent != null)
            {
                Undo.DestroyObjectImmediate(parent); // Replace old
            }
            parent = new GameObject(parentName);
            Undo.RegisterCreatedObjectUndo(parent, "Create Field Walls");

            // 3. Create Walls
            
            // Wall Material (Invisible or Debug?)
            // We'll create invisible colliders.

            // North (Z+)
            // Offset logic: Length/2 (Line) + Margin (1m) + Thickness/2 (2.5m)
            CreateWall("Wall_North", parent, 
                new Vector3(0, height/2, length/2 + margin + 2.5f), 
                new Vector3(width + 2*margin, height, 5f)); // Thickness 5f

            // South (Z-)
            CreateWall("Wall_South", parent, 
                new Vector3(0, height/2, -(length/2 + margin + 2.5f)), 
                new Vector3(width + 2*margin, height, 5f));

            // East (X+)
            CreateWall("Wall_East", parent, 
                new Vector3(width/2 + margin + 2.5f, height/2, 0), 
                new Vector3(5f, height, length + 2*margin));

            // West (X-)
            CreateWall("Wall_West", parent, 
                new Vector3(-(width/2 + margin + 2.5f), height/2, 0), 
                new Vector3(5f, height, length + 2*margin));

            Debug.Log("<color=green>Field Walls Created!</color>");
        }

        private static void CreateWall(string name, GameObject parent, Vector3 localPos, Vector3 size)
        {
            // VISIBLE WALLS: Use Cube Primitive (Has Mesh + Renderer + Collider)
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent.transform);
            wall.transform.position = localPos;
            
            // Set Scaling for Cube (Visual + Collider)
            wall.transform.localScale = size; // scale sets the size of the unit cube
            
            // Get the Collider (Cube has one by default)
            BoxCollider box = wall.GetComponent<BoxCollider>();
            // Note: box.size is (1,1,1) by default which matches localScale. 
            // We don't need to set box.size if using scale, but previous code used box.size on an empty GO.
            // When using Cube, we typically use transform.localScale for size.
            
            // PHYSICS MAT: Make it bouncy
            PhysicsMaterial mat = new PhysicsMaterial("WallMat");
            mat.bounciness = 0.6f; 
            mat.frictionCombine = PhysicsMaterialCombine.Minimum;
            mat.bounceCombine = PhysicsMaterialCombine.Maximum;
            mat.bounceCombine = PhysicsMaterialCombine.Maximum;
            box.material = mat;
            
            // FIX: High-Speed Tunneling Prevention
            // Static Colliders can be tunneled by very fast ContinuousDynamic balls.
            // Solution: Add Kinematic Rigidbody with Continuous collision detection.
            // This makes BOTH objects use advanced collision detection.
            Rigidbody rb = wall.AddComponent<Rigidbody>();
            rb.isKinematic = true; // IMMOVABLE (won't be pushed)
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Detect fast objects
            rb.constraints = RigidbodyConstraints.FreezeAll; // Extra safety: lock all axes
            
            // VISUAL: White Material with 75% Transparency (Alpha 0.25)
            Renderer rend = wall.GetComponent<Renderer>();
            if (rend != null)
            {
                // URP Support: Use Universal Render Pipeline/Lit
                // User Confirm: URP Environment
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit"); // Fallback
                if (shader == null) shader = Shader.Find("Standard"); // Last Resort
                
                Material whiteMat = new Material(shader);
                
                // URP Transparency Setup
                // _Surface: 0 = Opaque, 1 = Transparent
                whiteMat.SetFloat("_Surface", 1.0f);
                whiteMat.SetFloat("_Blend", 0.0f); // 0 = Alpha, 1 = Premul, 2 = Additive, 3 = Multiply
                
                whiteMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                whiteMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                whiteMat.SetInt("_ZWrite", 0);
                whiteMat.renderQueue = 3000; // Transparent
                
                whiteMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                
                // Color with 25% Opacity
                if (whiteMat.HasProperty("_BaseColor"))
                    whiteMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.25f));
                else
                    whiteMat.color = new Color(1f, 1f, 1f, 0.25f);

                rend.sharedMaterial = whiteMat;
                
                // Shadow casting
                 rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // Don't cast shadows
            }
        }
    }
}
