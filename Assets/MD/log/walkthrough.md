# Phase 2: Hybrid Navigation Walkthrough

I have implemented the "Hybrid Navigation" system where the **NavMeshAgent** calculates the path, but the **Rigidbody** physically moves the character. This ensures the player respects physics (collisions, pushing) while having smart pathfinding.

## Changes Made

### 1. AI Logic
- **[HybridAgentController.cs](file:///d:/unity/workspace/로또축구단/Assets/Game/Scripts/AI/HybridAgentController.cs)**: 
    - Disables `updatePosition` on the Agent.
    - Syncs Agent position to Rigidbody.
    - Applies force to Rigidbody based on `agent.desiredVelocity` using `linearVelocity`.

### 2. Editor Tools
- **[PlayerSetupTools.cs](file:///d:/unity/workspace/로또축구단/Assets/Game/Scripts/Editor/PlayerSetupTools.cs)**: 
    - Creates a `Player` prefab with calibrated Rigidbody (Mass 75kg, High Damping).
    - Automatically attaches `HybridAgentController`.

## Verification Steps

### Step 1: Generate the Player Prefab
1. In Unity Editor, click **Tools > LottoSoccer > Create Player Prefab**.
2. Verify that a `Player` prefab is created in `Assets/Game/Prefabs`.

### Step 2: NavMesh Test Scene
1. Create a new Scene (e.g., `NavTest`).
2. Create a Plane (Size 10, 10, 10) for the ground.
3. **Bake NavMesh**:
    - Select the Plane.
    - Open **Window > AI > Navigation**.
    - Go to **Bake** tab and click **Bake**. (Or add a `NavMeshSurface` component and click Bake).
4. Drag the `Player` prefab onto the Plane.

### Step 3: Movement Test (Click to Move)
Since I cannot click for you, create a small temporary script `ClickToMove.cs` and attach it to the Main Camera:

```csharp
using UnityEngine;
using Game.Scripts.AI;

public class ClickToMove : MonoBehaviour
{
    public HybridAgentController player;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                player.SetDestination(hit.point);
            }
        }
    }
}
```
1. Assign your `Player` instance to the `player` field of this script.
2. Press **Play**.
3. Click on the ground.
4. **Expected Result**: The player should move towards the click point.
   - **Physics Check**: Place a Cube (Rigidbody, Mass 50) in the way. The player should push it or collide with it, unlike a standard ghost agent.

### Note on Unity 6
I have utilized `linearDamping` and `linearVelocity` as per your fix. If you see any warnings, please let me know.
