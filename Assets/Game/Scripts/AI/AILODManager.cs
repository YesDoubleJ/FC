using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Managers;

namespace Game.Scripts.AI
{
    /// <summary>
    /// AI LOD(Level of Detail) 관리자 — 지침서 §2.3
    /// 공과의 거리에 따라 각 에이전트의 LOD 레벨을 분류합니다.
    /// 
    /// LOD 0 (30m 이내): 매 프레임 판단, 물리 정밀도 최상
    /// LOD 1 (화면 내): 0.1~0.2초 간격
    /// LOD 2 (화면 밖): 0.5초 간격, 애니메이션 간소화
    /// 
    /// HybridAgentController.CurrentLOD 프로퍼티를 설정하며,
    /// GetDecisionInterval()은 이 값을 참조해 판단 주기를 조절합니다.
    /// </summary>
    public class AILODManager : MonoBehaviour
    {
        [Header("LOD Thresholds")]
        [Tooltip("LOD 0 (최상) 적용 거리. 공에서 이 거리 이내의 선수")]
        public float LOD0Distance = 30f;

        [Tooltip("LOD 1 (중간) 적용 거리. 이 거리 이내 + 카메라 시야 내")]
        public float LOD1Distance = 60f;

        [Header("Update Settings")]
        [Tooltip("LOD 갱신 간격 (초). 매 프레임 할 필요 없음")]
        public float UpdateInterval = 0.3f;

        [Header("LOD 2 Animation")]
        [Tooltip("LOD 2 선수의 애니메이션 업데이트 빈도 축소 배율")]
        public float LOD2AnimSpeedMultiplier = 0.5f;

        [Header("Debug")]
        public bool ShowGizmos = false;

        // =========================================================
        // 내부 데이터
        // =========================================================
        private float _updateTimer;
        private Transform _ballTransform;
        private Camera _mainCamera;

        public static AILODManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            _mainCamera = Camera.main;

            var ballObj = MatchManager.Instance?.Ball;
            if (ballObj != null) _ballTransform = ballObj.transform;
            else
            {
                var found = GameObject.FindGameObjectWithTag("Ball");
                if (found != null) _ballTransform = found.transform;
            }
        }

        private void Update()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer < UpdateInterval) return;
            _updateTimer = 0f;

            UpdateLODLevels();
        }

        private void UpdateLODLevels()
        {
            if (_ballTransform == null) return;

            var agents = MatchManager.Instance?.GetAllAgents();
            if (agents == null) return;

            Vector3 ballPos = _ballTransform.position;
            Plane[] frustumPlanes = null;

            if (_mainCamera != null)
                frustumPlanes = GeometryUtility.CalculateFrustumPlanes(_mainCamera);

            foreach (var agent in agents)
            {
                if (agent == null) continue;

                float distToBall = Vector3.Distance(agent.transform.position, ballPos);

                int newLOD;

                if (distToBall <= LOD0Distance)
                {
                    // 공 근처 = 최상 정밀도
                    newLOD = 0;
                }
                else if (frustumPlanes != null && IsInCameraView(agent.transform.position, frustumPlanes))
                {
                    // 화면 내 = 중간 정밀도
                    newLOD = 1;
                }
                else
                {
                    // 화면 밖 = 최소 정밀도
                    newLOD = 2;
                }

                // 볼 소유자와 패스 수신자는 항상 LOD 0
                if (MatchManager.Instance.CurrentBallOwner == agent || agent.IsReceiver)
                    newLOD = 0;

                agent.CurrentLOD = newLOD;
            }
        }

        private bool IsInCameraView(Vector3 worldPos, Plane[] frustumPlanes)
        {
            // 바운딩 박스 대신 점으로 판정 (충분히 빠름)
            Bounds bounds = new Bounds(worldPos, Vector3.one * 2f);
            return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        }

        // =========================================================
        // Debug — §8.3
        // =========================================================
        private void OnDrawGizmos()
        {
            if (!ShowGizmos || _ballTransform == null) return;

            // LOD 0 범위 (녹색)
            Gizmos.color = new Color(0f, 1f, 0f, 0.1f);
            Gizmos.DrawWireSphere(_ballTransform.position, LOD0Distance);

            // LOD 1 범위 (노란색)
            Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
            Gizmos.DrawWireSphere(_ballTransform.position, LOD1Distance);

            // 각 에이전트 LOD 표시
            var agents = MatchManager.Instance?.GetAllAgents();
            if (agents == null) return;

            foreach (var agent in agents)
            {
                if (agent == null) continue;
                switch (agent.CurrentLOD)
                {
                    case 0: Gizmos.color = Color.green; break;
                    case 1: Gizmos.color = Color.yellow; break;
                    case 2: Gizmos.color = Color.red; break;
                }
                Gizmos.DrawWireSphere(agent.transform.position + Vector3.up * 2f, 0.3f);
            }
        }
    }
}
