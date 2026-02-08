using UnityEngine;
using Game.Scripts.Managers;

namespace Game.Scripts.Gameplay
{
    public class GoalTrigger : MonoBehaviour
    {
        [Tooltip("True if this goal belongs to the Home team (so Away team scores by entering here).")]
        public bool isHomeGoal;

        private void OnTriggerEnter(Collider other)
        {
            // Simple check by name or tag
            if (other.name.Contains("Ball") || other.CompareTag("Ball"))
            {
                // Validate that the ball entered from the front, within the posts
                if (!IsValidGoal(other))
                {
                    return;
                }

                if (MatchManager.Instance != null && MatchManager.Instance.CurrentState == MatchState.Playing)
                {
                    // If this is Home Goal, Away team scored.
                    // If this is Away Goal, Home team scored.
                    bool homeScored = !isHomeGoal;
                    MatchManager.Instance.OnGoalScored(homeScored);
                }
            }
        }

        private bool IsValidGoal(Collider ball)
        {
            // 1. Position Check: Ensure ball is within goal width
            // This prevents triggering when hitting the side net from outside.
            float halfWidth = (FieldManager.Instance != null) ? FieldManager.Instance.GoalWidth / 2f : 4.0f;
            if (Mathf.Abs(ball.transform.position.x) > halfWidth)
            {
                return false;
            }

            // 2. Direction Check: Ensure ball is moving INTO the goal
            Rigidbody rb = ball.attachedRigidbody;
            if (rb != null)
            {
                // Home Goal is at Z = -50. Valid scoring shot moves in -Z direction.
                if (isHomeGoal && rb.linearVelocity.z > 0) return false;

                // Away Goal is at Z = +50. Valid scoring shot moves in +Z direction.
                if (!isHomeGoal && rb.linearVelocity.z < 0) return false;
            }

            return true;
        }
    }
}
