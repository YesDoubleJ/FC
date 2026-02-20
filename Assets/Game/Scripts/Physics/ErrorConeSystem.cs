using UnityEngine;
using Game.Scripts.Data;

namespace Game.Scripts.Physics
{
    /// <summary>
    /// 오차 원뿔 시스템 — 지침서 §4.2
    /// 슈팅/패스/크로스의 정확도를 '성공/실패' 이진 결과가 아닌,
    /// 오차 범위(Error Radius)로 구현합니다.
    ///
    /// - Target Vector: 목표 지점 (골대 구석, 동료 발 앞)
    /// - Error Angle:   MaxError × (1 - accuracy/100) × distanceFactor
    /// - Final Vector:  Target Vector를 Error Angle 범위 내에서 무작위 회전
    /// </summary>
    public static class ErrorConeSystem
    {
        /// <summary>
        /// 오차 원뿔을 적용한 최종 방향 벡터를 반환합니다.
        /// </summary>
        /// <param name="targetDirection">목표 방향 (정규화된 벡터)</param>
        /// <param name="accuracy">스탯 기반 정확도 (0~100). 높을수록 정확</param>
        /// <param name="distance">목표까지 거리 (미터). 멀수록 오차 증폭</param>
        /// <param name="maxErrorDegrees">최대 오차 각도 (도). ShotErrorNoise/PassErrorNoise</param>
        /// <returns>오차가 적용된 방향 벡터</returns>
        public static Vector3 ApplyErrorCone(Vector3 targetDirection, float accuracy, float distance, float maxErrorDegrees)
        {
            if (targetDirection.sqrMagnitude < 0.001f)
                return targetDirection;

            // 오차 각도 계산: 정확도가 높을수록 오차 감소
            float errorAngle = CalculateErrorAngle(accuracy, distance, maxErrorDegrees);

            // 오차 각도가 거의 0이면 그대로 반환
            if (errorAngle < 0.1f)
                return targetDirection.normalized;

            // 무작위 회전 적용
            return ApplyRandomDeviation(targetDirection.normalized, errorAngle);
        }

        /// <summary>
        /// 오차 원뿔을 적용한 최종 목표 위치를 반환합니다.
        /// </summary>
        /// <param name="origin">킥 시작 위치</param>
        /// <param name="targetPosition">목표 위치</param>
        /// <param name="accuracy">스탯 기반 정확도 (0~100)</param>
        /// <param name="maxErrorDegrees">최대 오차 각도 (도)</param>
        /// <returns>오차가 적용된 목표 위치</returns>
        public static Vector3 ApplyErrorConeToPosition(Vector3 origin, Vector3 targetPosition, float accuracy, float maxErrorDegrees)
        {
            Vector3 direction = targetPosition - origin;
            float distance = direction.magnitude;

            if (distance < 0.1f)
                return targetPosition;

            Vector3 errorDirection = ApplyErrorCone(direction, accuracy, distance, maxErrorDegrees);
            return origin + errorDirection * distance;
        }

        /// <summary>
        /// 오차 각도를 계산합니다.
        /// ErrorAngle = MaxError × (1 - accuracy/100) × DistanceFactor
        /// DistanceFactor: 거리가 멀수록 오차 증폭 (비선형)
        /// </summary>
        public static float CalculateErrorAngle(float accuracy, float distance, float maxErrorDegrees)
        {
            // 정확도 보정: 0~100 → 0~1 (반전: 높을수록 오차 적음)
            float inaccuracy = 1f - Mathf.Clamp01(accuracy / 100f);

            // 거리 보정: 10m 기준, 거리가 멀수록 오차 증폭 (제곱근 곡선)
            // 10m 이하는 1.0, 이상은 점진적 증가
            float distanceFactor = Mathf.Max(1f, Mathf.Sqrt(distance / 10f));

            return maxErrorDegrees * inaccuracy * distanceFactor;
        }

        /// <summary>
        /// 방향 벡터에 무작위 편향을 적용합니다.
        /// 원뿔 모양의 균일 분포 내에서 무작위 방향 선택.
        /// </summary>
        private static Vector3 ApplyRandomDeviation(Vector3 direction, float maxAngleDegrees)
        {
            // 가우시안에 가까운 분포: 중심에 가까울수록 확률 높음
            // 제곱근 활용으로 원뿔 내부 균일 분포 달성
            float angle = Random.Range(0f, maxAngleDegrees) * Mathf.Sqrt(Random.Range(0f, 1f));

            // 접선 방향 무작위 (360도 회전)
            float rotationAngle = Random.Range(0f, 360f);

            // 원래 방향의 직교 축 찾기
            Vector3 perp = GetPerpendicularVector(direction);

            // 2단계 회전: 1) 직교 축으로 angle만큼 기울이기, 2) 원래 방향 축으로 회전
            Quaternion tiltRotation = Quaternion.AngleAxis(angle, perp);
            Quaternion spinRotation = Quaternion.AngleAxis(rotationAngle, direction);

            return (spinRotation * tiltRotation * direction).normalized;
        }

        /// <summary>주어진 벡터에 수직인 벡터를 반환합니다.</summary>
        private static Vector3 GetPerpendicularVector(Vector3 v)
        {
            // 수직 벡터 생성: up과 외적, 평행할 경우 right 사용
            if (Mathf.Abs(Vector3.Dot(v, Vector3.up)) > 0.99f)
                return Vector3.Cross(v, Vector3.right).normalized;
            return Vector3.Cross(v, Vector3.up).normalized;
        }

        // =========================================================
        // 편의 메서드 (Convenience Wrappers)
        // =========================================================

        /// <summary>
        /// 슈팅에 오차를 적용합니다. MatchEngineConfig의 ShotErrorNoise 사용.
        /// </summary>
        public static Vector3 ApplyShootError(Vector3 origin, Vector3 targetPosition, float shootingStat, MatchEngineConfig config)
        {
            return ApplyErrorConeToPosition(origin, targetPosition, shootingStat, config.ShotErrorNoise);
        }

        /// <summary>
        /// 패스에 오차를 적용합니다. MatchEngineConfig의 PassErrorNoise 사용.
        /// </summary>
        public static Vector3 ApplyPassError(Vector3 origin, Vector3 targetPosition, float passingStat, MatchEngineConfig config)
        {
            return ApplyErrorConeToPosition(origin, targetPosition, passingStat, config.PassErrorNoise);
        }
    }
}
