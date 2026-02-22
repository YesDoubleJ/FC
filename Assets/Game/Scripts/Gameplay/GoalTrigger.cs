using UnityEngine;
using Game.Scripts.Managers;

namespace Game.Scripts.Gameplay
{
    public class GoalTrigger : MonoBehaviour
    {
        [Tooltip("True if this goal belongs to the Home team (so Away team scores by entering here).")]
        public bool isHomeGoal;

        private Transform postL;
        private Transform postR;
        private Transform crossbar;

        private void Start()
        {
            if (transform.parent != null)
            {
                postL = transform.parent.Find("Post_L");
                postR = transform.parent.Find("Post_R");
                crossbar = transform.parent.Find("Crossbar");
            }
        }

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
            if (postL == null || postR == null || crossbar == null)
            {
                // Fallback: 포스트나 크로스바를 못 찾으면 기존처럼 Collider 경계로 계산
                Collider myCollider = GetComponent<Collider>();
                if (myCollider == null) return true;
                float goalCenterX = myCollider.bounds.center.x;
                float goalHalfWidth = myCollider.bounds.extents.x;
                if (Mathf.Abs(ball.transform.position.x - goalCenterX) > goalHalfWidth) return false;
                return true;
            }

            // 양 포스트의 X좌표를 기준으로 진입 한계선 계산
            float minX = Mathf.Min(postL.position.x, postR.position.x);
            float maxX = Mathf.Max(postL.position.x, postR.position.x);
            float maxY = crossbar.position.y;

            // 공의 반지름 (대략적인 값, bounds가 있으면 가져옴)
            float ballRadius = ball.bounds != null ? ball.bounds.extents.x : 0.22f;

            // 1. 공이 양 포스트 바깥(옆그물)으로 침투했는지 확인
            if (ball.transform.position.x < minX - ballRadius || ball.transform.position.x > maxX + ballRadius)
            {
                Debug.Log($"[GOAL-REJECTED] Outside posts. Ball X: {ball.transform.position.x}, MinX: {minX}, MaxX: {maxX}");
                return false;
            }

            // 2. 공이 크로스바 위(윗그물)로 침투했는지 확인
            if (ball.transform.position.y > maxY + ballRadius)
            {
                Debug.Log($"[GOAL-REJECTED] Over crossbar. Ball Y: {ball.transform.position.y}, MaxY: {maxY}");
                return false;
            }

            return true;
        }
    }
}
