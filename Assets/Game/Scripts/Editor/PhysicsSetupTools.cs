using UnityEngine;
using UnityEditor;
using Game.Scripts.Physics;
using System.IO;

namespace Game.Scripts.Editor
{
    public class PhysicsSetupTools
    {
        [MenuItem("Tools/LottoSoccer/Create Ball Prefab")]
        public static void CreateBallPrefab()
        {
            // 1. Create a primitive sphere
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";

            // 2. Add Rigidbody and configure
            Rigidbody rb = ball.AddComponent<Rigidbody>();
            rb.mass = 0.43f;            // FIFA Standard (~430g)
            rb.linearDamping = 0.5f;             // Air resistance
            rb.angularDamping = 0.8f;      // Spin decay
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 3. Add BallAerodynamics
            ball.AddComponent<BallAerodynamics>();

            // 4. Assign Physic Material
            SphereCollider collider = ball.GetComponent<SphereCollider>();
            string matPath = "Assets/Game/PhysicsMaterials/SoccerBall.physicMaterial";
            PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(matPath);
            
            if (mat != null)
            {
                collider.material = mat;
            }
            else
            {
                Debug.LogError($"Could not load PhysicMaterial at {matPath}. Make sure it exists.");
            }

            // 5. Ensure Prefab Directory Exists
            string prefabDir = "Assets/Game/Prefabs";
            if (!Directory.Exists(prefabDir))
            {
                Directory.CreateDirectory(prefabDir);
            }

            // 6. Save as Prefab
            string prefabPath = Path.Combine(prefabDir, "Ball.prefab");
            PrefabUtility.SaveAsPrefabAsset(ball, prefabPath);
            Debug.Log($"Ball Prefab created at: {prefabPath}");

            // 7. Cleanup
            Object.DestroyImmediate(ball);
        }
    }
}
