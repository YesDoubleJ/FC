using UnityEngine;
using Game.Scripts.Data;
using Game.Scripts.Managers;

namespace Game.Scripts.Physics
{
    /// <summary>
    /// 궤적 시각화 — 지침서 §8.3
    /// 공의 예상 궤적을 LineRenderer로 실시간 표시합니다.
    /// TrajectoryPredictor의 물리 시뮬레이션과 동일한 로직을 사용하여
    /// 실제 물리와의 차이를 검증할 수 있습니다.
    /// 
    /// Ball 오브젝트에 부착합니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TrajectoryVisualizer : MonoBehaviour
    {
        [Header("Visualization")]
        [Tooltip("궤적 표시 활성화")]
        public bool Enabled = true;

        [Tooltip("예측할 최대 시간 (초)")]
        public float PredictionTime = 2.0f;

        [Tooltip("시뮬레이션 스텝 간격 (초)")]
        public float StepInterval = 0.02f;

        [Tooltip("표시할 최소 공 속도. 이 미만이면 궤적 숨김")]
        public float MinSpeedToShow = 2.0f;

        [Header("Line Settings")]
        [Tooltip("라인 시작 색상")]
        public Color StartColor = new Color(1f, 0.5f, 0f, 0.8f); // 주황

        [Tooltip("라인 끝 색상")]
        public Color EndColor = new Color(1f, 0.5f, 0f, 0.1f);   // 투명 주황

        [Tooltip("라인 시작 너비")]
        public float StartWidth = 0.15f;

        [Tooltip("라인 끝 너비")]
        public float EndWidth = 0.02f;

        [Header("Physics")]
        [Tooltip("MatchEngineConfig 참조 (null이면 자동 로드)")]
        public MatchEngineConfig config;

        // =========================================================
        // 내부
        // =========================================================
        private Rigidbody _rb;
        private LineRenderer _lineRenderer;
        private Vector3[] _points;
        private int _maxPoints;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // LineRenderer 설정
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer == null)
                _lineRenderer = gameObject.AddComponent<LineRenderer>();

            ConfigureLineRenderer();

            _maxPoints = Mathf.CeilToInt(PredictionTime / StepInterval) + 1;
            _points = new Vector3[_maxPoints];
        }

        private void Start()
        {
            if (config == null)
                config = Resources.Load<MatchEngineConfig>("DefaultMatchEngineConfig");
        }

        private void ConfigureLineRenderer()
        {
            _lineRenderer.startColor = StartColor;
            _lineRenderer.endColor = EndColor;
            _lineRenderer.startWidth = StartWidth;
            _lineRenderer.endWidth = EndWidth;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 0;

            // 기본 머티리얼 사용 (Sprites-Default 같은 간단한 것)
            if (_lineRenderer.material == null || _lineRenderer.material.name.Contains("Default"))
            {
                _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            }

            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
        }

        private void Update()
        {
            if (!Enabled || _rb == null)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            float speed = _rb.linearVelocity.magnitude;

            // 너무 느리면 숨김
            if (speed < MinSpeedToShow)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            PredictTrajectory();
        }

        private void PredictTrajectory()
        {
            // 물리 파라미터 가져오기
            float drag = config != null ? config.DragCoeffAir : 0.2f;
            float airDensity = config != null ? config.AirDensity : 1.225f;
            float ballMass = config != null ? config.BallMass : 0.43f;
            float gravity = UnityEngine.Physics.gravity.y;

            // 공 파라미터 (BallAerodynamics에서 사용하는 값)
            float radius = 0.11f; // FIFA 공인구 반지름
            float crossSection = Mathf.PI * radius * radius;

            Vector3 currentPos = transform.position;
            Vector3 currentVel = _rb.linearVelocity;

            int pointCount = 0;
            _points[pointCount++] = currentPos;

            float dt = StepInterval;

            for (int i = 0; i < _maxPoints - 1; i++)
            {
                // 1. 공기 항력 (v² 비례)
                float speedSq = currentVel.sqrMagnitude;
                if (speedSq > 0.01f)
                {
                    float dragForce = 0.5f * drag * airDensity * crossSection * speedSq;
                    Vector3 dragAccel = -currentVel.normalized * (dragForce / ballMass);
                    currentVel += dragAccel * dt;
                }

                // 2. 중력
                currentVel.y += gravity * dt;

                // 3. 위치 갱신
                currentPos += currentVel * dt;

                // 4. 지면 바운스/정지
                if (currentPos.y < 0.11f) // 공 반지름
                {
                    currentPos.y = 0.11f;

                    // 지면 마찰 (간이)
                    if (currentVel.y < 0)
                        currentVel.y *= -0.3f; // 약한 바운스

                    // 지면 구름 마찰
                    float friction = config != null ? config.GroundFrictionDry : 0.8f;
                    currentVel.x *= (1f - friction * dt * 2f);
                    currentVel.z *= (1f - friction * dt * 2f);
                }

                _points[pointCount++] = currentPos;

                // 속도가 거의 0이면 조기 종료
                if (currentVel.sqrMagnitude < 0.01f) break;
            }

            // LineRenderer에 적용
            _lineRenderer.positionCount = pointCount;
            for (int i = 0; i < pointCount; i++)
            {
                _lineRenderer.SetPosition(i, _points[i]);
            }
        }

        /// <summary>
        /// TrajectoryPredictor 대비 시각화 궤적의 정합성을 검증하기 위한
        /// 디버그 용도. 예측 착탄 지점에 Gizmo 표시.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!Enabled || _rb == null) return;
            if (_rb.linearVelocity.magnitude < MinSpeedToShow) return;

            // 예측 착탄 지점 (Z=0 기준)
            Vector3 impact = TrajectoryPredictor.PredictImpactPoint(
                transform.position, _rb.linearVelocity, 0f);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(impact, 0.3f);
        }
    }
}
