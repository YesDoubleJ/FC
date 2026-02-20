using UnityEngine;

namespace Game.Scripts.Managers
{
    /// <summary>
    /// 경기장의 좌표계와 공간 정보를 관리합니다.
    /// 지침서 §2.1: 정규화 좌표계 (-1,-1)~(1,1) → 월드 좌표 변환.
    /// 모든 AI 위치 계산은 이 매니저를 통해 수행합니다.
    /// </summary>
    public class FieldManager : MonoBehaviour
    {
        public static FieldManager Instance { get; private set; }

        [Header("Field Dimensions")]
        [Tooltip("경기장 길이 (Z축, 미터). 6vs6=40, 11vs11=100~105")]
        public float Length = 100f;

        [Tooltip("경기장 너비 (X축, 미터). 6vs6=25, 11vs11=60~68")]
        public float Width = 60f;

        [Tooltip("골대 너비 (미터). FIFA 규격 7.32m")]
        public float GoalWidth = 7.32f;

        [Tooltip("페널티 에어리어 길이 (미터). FIFA 규격 16.5m")]
        public float PenaltyAreaLength = 16.5f;

        [Tooltip("페널티 에어리어 너비 (미터). FIFA 규격 40.32m")]
        public float PenaltyAreaWidth = 40.32f;

        [Header("Field Reference")]
        [Tooltip("경기장 중심 오브젝트의 Transform. null이면 이 오브젝트의 위치를 사용")]
        public Transform FieldCenterTransform;

        // =========================================================
        // 파생 프로퍼티 (Derived Properties)
        // =========================================================

        /// <summary>경기장 중심 월드 좌표</summary>
        public Vector3 FieldCenter =>
            FieldCenterTransform != null ? FieldCenterTransform.position : transform.position;

        /// <summary>경기장 절반 크기 (편의용)</summary>
        public float HalfLength => Length * 0.5f;
        public float HalfWidth => Width * 0.5f;

        // =========================================================
        // 정규화 좌표계 (Normalized Coordinate System) — §2.1
        // =========================================================

        /// <summary>
        /// 정규화 좌표 (-1,-1)~(1,1)을 월드 좌표(Vector3)로 변환합니다.
        /// X = normalizedPos.x × HalfWidth + FieldCenter.x
        /// Z = normalizedPos.y × HalfLength + FieldCenter.z
        /// Y는 0 (지면)으로 설정됩니다.
        /// </summary>
        /// <param name="normalizedPos">정규화 좌표 (x: 좌우 -1~1, y: 전후 -1~1)</param>
        /// <returns>월드 좌표 Vector3</returns>
        public Vector3 GetWorldPosition(Vector2 normalizedPos)
        {
            Vector3 center = FieldCenter;
            return new Vector3(
                center.x + normalizedPos.x * HalfWidth,
                0f,
                center.z + normalizedPos.y * HalfLength
            );
        }

        /// <summary>
        /// 월드 좌표(Vector3)를 정규화 좌표 (-1,-1)~(1,1)로 변환합니다.
        /// </summary>
        /// <param name="worldPos">월드 좌표</param>
        /// <returns>정규화 좌표 Vector2</returns>
        public Vector2 GetNormalizedPosition(Vector3 worldPos)
        {
            Vector3 center = FieldCenter;
            float nx = (worldPos.x - center.x) / HalfWidth;
            float ny = (worldPos.z - center.z) / HalfLength;
            return new Vector2(
                Mathf.Clamp(nx, -1f, 1f),
                Mathf.Clamp(ny, -1f, 1f)
            );
        }

        /// <summary>
        /// 정규화된 거리를 월드 거리(미터)로 변환합니다.
        /// 대각선 평균 기준입니다.
        /// </summary>
        public float NormalizedToWorldDistance(float normalizedDist)
        {
            float avgHalf = (HalfLength + HalfWidth) * 0.5f;
            return normalizedDist * avgHalf;
        }

        // =========================================================
        // 특수 위치 헬퍼 (Special Position Helpers) — §2.1 테이블
        // =========================================================

        /// <summary>경기장 중앙 (0, 0) → Center Spot</summary>
        public Vector3 CenterSpot => GetWorldPosition(Vector2.zero);

        /// <summary>상대 진영 페널티 마크 (정규화: 0, 0.8)</summary>
        public Vector3 PenaltySpotAttack => GetWorldPosition(new Vector2(0f, 0.8f));

        /// <summary>자기 진영 페널티 마크 (정규화: 0, -0.8)</summary>
        public Vector3 PenaltySpotDefend => GetWorldPosition(new Vector2(0f, -0.8f));

        /// <summary>주어진 월드 좌표가 경기장 안에 있는지 확인</summary>
        public bool IsInsideField(Vector3 worldPos)
        {
            Vector2 norm = GetNormalizedPosition(worldPos);
            return Mathf.Abs(norm.x) <= 1f && Mathf.Abs(norm.y) <= 1f;
        }

        /// <summary>주어진 월드 좌표가 페널티 에어리어 안(상대 진영)에 있는지</summary>
        public bool IsInsidePenaltyArea(Vector3 worldPos, bool attackingSide)
        {
            Vector3 center = FieldCenter;
            float sign = attackingSide ? 1f : -1f;

            float paZStart = center.z + sign * (HalfLength - PenaltyAreaLength);
            float paZEnd   = center.z + sign * HalfLength;
            float paHalfW  = PenaltyAreaWidth * 0.5f;

            float z = worldPos.z;
            float x = worldPos.x - center.x;

            bool inZ = attackingSide
                ? (z >= paZStart && z <= paZEnd)
                : (z >= paZEnd && z <= paZStart);
            bool inX = Mathf.Abs(x) <= paHalfW;

            return inZ && inX;
        }

        /// <summary>
        /// 월드 좌표를 경기장 바운드 내로 클램핑합니다.
        /// </summary>
        public Vector3 ClampToField(Vector3 worldPos)
        {
            Vector3 center = FieldCenter;
            return new Vector3(
                Mathf.Clamp(worldPos.x, center.x - HalfWidth, center.x + HalfWidth),
                worldPos.y,
                Mathf.Clamp(worldPos.z, center.z - HalfLength, center.z + HalfLength)
            );
        }

        // =========================================================
        // Singleton Lifecycle
        // =========================================================

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // FAST PLAY MODE: Cleanup
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        // =========================================================
        // Debug Visualization — §8.3
        // =========================================================
        private void OnDrawGizmos()
        {
            Vector3 center = FieldCenter;

            // 경기장 외곽선
            Gizmos.color = Color.white;
            Vector3 size = new Vector3(Width, 0.1f, Length);
            Gizmos.DrawWireCube(center, size);

            // 중앙선
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(
                center + new Vector3(-HalfWidth, 0.05f, 0),
                center + new Vector3(HalfWidth, 0.05f, 0)
            );

            // 페널티 에어리어 (양쪽)
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
            float paHalfW = PenaltyAreaWidth * 0.5f;
            // 상대 진영
            Vector3 paSize = new Vector3(PenaltyAreaWidth, 0.1f, PenaltyAreaLength);
            Gizmos.DrawWireCube(center + new Vector3(0, 0.05f, HalfLength - PenaltyAreaLength * 0.5f), paSize);
            // 자기 진영
            Gizmos.DrawWireCube(center + new Vector3(0, 0.05f, -HalfLength + PenaltyAreaLength * 0.5f), paSize);

            // 센터 스팟
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(center + Vector3.up * 0.1f, 0.3f);
        }
    }
}
