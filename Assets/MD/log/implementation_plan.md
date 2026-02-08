# Implement Phase 8: Match Loop & Game Logic

## Goal Description
Turn the "Tech Demo" into a "Game" by adding a rule system.
1.  **Goal Detection**: Detect when the ball crosses the line.
2.  **Scoring**: Update score and display it.
3.  **Game Loop**: Reset positions after a goal (Kick-off).

## User Review Required
> [!NOTE]
> You'll need to add a Trigger Collider inside the goal model to detect the ball.

## Proposed Changes

### Assets/Game/Scripts/Managers
#### [NEW] [MatchManager.cs](file:///d:/unity/workspace/Lotto/Assets/Game/Scripts/Managers/MatchManager.cs)
- Singleton.
- State Machine: `KickOff`, `Playing`, `GoalScored`.
- Manages Score (`HomeScore`, `AwayScore`).
- Methods: `OnGoalScored(Team team)`, `ResetPositions()`.

### Assets/Game/Scripts/Gameplay
#### [NEW] [GoalTrigger.cs](file:///d:/unity/workspace/Lotto/Assets/Game/Scripts/Gameplay/GoalTrigger.cs)
- Attached to the trigger collider inside the goal.
- `OnTriggerEnter(Collider other)`: Check if ball, notify `MatchManager`.

### Assets/Game/Scripts/UI
#### [MODIFY] [MatchViewController.cs](file:///d:/unity/workspace/Lotto/Assets/Game/Scripts/UI/MatchViewController.cs)
- Add UI Text for Score (Canvas integration or simple OnGUI update).

## Verification Plan

### Manual Verification
1.  **Setup**: Add `Trigger` collider to Goal. Add `MatchManager`.
2.  **Play Test**:
    - Score a goal.
    - Verify "GOAL!" log and Score increase.
    - Verify Ball resets to center after 3 seconds.
    - Verify Players reset to formation start positions.
