using UnityEngine;
using Game.Scripts.Data;
using Game.Scripts.Managers;
using System.Collections.Generic;

namespace Game.Scripts.AI.DecisionMaking
{
    /// <summary>
    /// AI 행동의 기대값(Expected Value)을 계산합니다.
    /// 모든 점수 = 성공 확률(0~1) × 전술적 가치(0~1)
    /// </summary>
    public class UtilityScorer
    {
        private readonly MatchEngineConfig _config;

        // 설정값 (Fallback 포함)
        private float SweetSpotRange => _config ? _config.SweetSpotRange : 22f;
        private float MinPassDist    => _config ? _config.MinPassDist   : 5f;
        private float MaxPassDist    => _config ? _config.MaxPassRange  : 40f;
        private float DangerRadius   => _config ? _config.DangerRadius  : 4f;
        private const float MaxShootRange = 35f;

        public UtilityScorer(MatchEngineConfig config)
        {
            _config = config;
        }

        // =========================================================
        // 슛 기대값: 성공 확률 × 득점 가치(1.0 고정)
        // EV_shoot = P(성공) × 1.0
        // =========================================================
        /// <summary>
        /// 슛 기대값을 계산합니다.
        /// 득점의 가치는 항상 1.0이므로 EV = 슛 성공 확률 자체입니다.
        /// </summary>
        public float CalculateShootScore(Vector3 fromPos, PlayerStats stats, Vector3 goalPosition)
        {
            // 1. 거리 확률
            float distance = Vector3.Distance(fromPos, goalPosition);
            float pDistance;
            if (distance < SweetSpotRange)
                pDistance = 1.0f;
            else if (distance > MaxShootRange)
                pDistance = 0.05f;
            else
            {
                float t = (distance - SweetSpotRange) / (MaxShootRange - SweetSpotRange);
                pDistance = Mathf.Lerp(1.0f, 0.05f, t);
            }

            // 2. 각도 확률 (골대 정중앙=1.0, 측면으로 갈수록 감소)
            float absX = Mathf.Abs(fromPos.x);
            float pAngle = Mathf.Lerp(1.0f, 0.1f, Mathf.Clamp01(absX / 30f));

            // 3. 슈팅 스탯 확률
            float pStat = (stats != null) ? Mathf.Clamp01(stats.GetStat(StatType.Shooting) / 100f) : 0.5f;

            // 최종 슛 성공 확률 = 거리 × 각도 × 능력치
            // 득점 가치 = 1.0 고정 → EV = pSucceed × 1.0
            return Mathf.Clamp01(pDistance * pAngle * pStat);
        }

        // =========================================================
        // 패스 성공 확률 (위치 가치는 DecisionEvaluator에서 곱함)
        // =========================================================
        /// <summary>
        /// 패스 성공 확률을 계산합니다. 장애물·거리·스탯 기반.
        /// DecisionEvaluator에서 수신자의 EvaluatePositionValue와 곱해 최종 EV를 산출합니다.
        /// </summary>
        public float CalculatePassSuccessProbability(HybridAgentController agent, PlayerStats stats, GameObject teammate, IEnumerable<HybridAgentController> opponents)
        {
            if (teammate == null) return 0f;

            // 1. 거리 확률
            // [FIX] 너무 가까워도 패널티 없음 — 짧은 패스는 당연히 성공률 높음
            float distance = Vector3.Distance(agent.transform.position, teammate.transform.position);
            float pDistance;
            if (distance < MinPassDist)
                pDistance = 0.85f;   // 짧은 패스: 충분히 높음
            else if (distance > MaxPassDist)
                pDistance = 0.15f;   // 사정거리 초과
            else
                pDistance = Mathf.Lerp(0.95f, 0.35f, Mathf.Clamp01((distance - MinPassDist) / (MaxPassDist - MinPassDist)));

            // 2. 패스 경로 인터셉트 위험
            // [FIX] 이전: pLane = 1.0 - risk → risk=0.8이면 pLane=0.2로 폭락
            //       수정: 리스크를 소프트하게 적용 (최대 0.5까지만 감산)
            float interceptRisk = CalculateInterceptionRisk(agent.transform.position, teammate.transform.position, 20f, opponents);
            float pLane = Mathf.Lerp(1.0f, 0.5f, interceptRisk); // 위험해도 최소 0.5 유지

            // 3. 패스 스탯 확률
            float pStat = (stats != null) ? Mathf.Clamp01(0.4f + stats.GetStat(StatType.Passing) / 100f * 0.6f) : 0.5f;
            // [FIX] 스탯 10이면 0.46, 스탯 70이면 0.82, 스탯 99면 0.99
            // 이전: GetStat/100 → 스탯 50 = 0.5, 3개 곱하면 너무 낮음

            // [FIX] 가중 평균 방식: 세 값 곱셈 대신 가중 평균으로 완만하게
            // 거리(40%) × 레인(30%) × 스탯(30%)
            float pPass = pDistance * 0.4f + pLane * 0.3f + pStat * 0.3f;
            return Mathf.Clamp01(pPass);
        }

        // =========================================================
        // 드리블 성공 확률 (목표 지점 가치는 DecisionEvaluator에서 곱함)
        // =========================================================
        /// <summary>
        /// 드리블 성공 확률을 계산합니다. 전방 수비수 유무·밀도·드리블 스탯 기반.
        /// DecisionEvaluator에서 목표 지점의 EvaluatePositionValue와 곱해 최종 EV를 산출합니다.
        /// </summary>
        public float CalculateDribbleSuccessProbability(HybridAgentController agent, PlayerStats stats, Vector3 targetDirection)
        {
            Vector3 checkPos = agent.transform.position + targetDirection.normalized * 5f;

            // 1. 전방 적 밀도 (밀도 높을수록 확률 감소)
            float enemyDensity = CalculateEnemyDensityAt(agent, checkPos);
            float pSpace = Mathf.Lerp(0.9f, 0.1f, Mathf.Clamp01(enemyDensity / 3f));

            // 2. 전방 차단 여부
            bool frontalBlocked = (agent.SkillSystem != null) && agent.SkillSystem.IsFrontalBlocked();
            float pFrontal = frontalBlocked ? 0.35f : 1.0f;

            // 3. 드리블 스탯 확률
            float pStat = (stats != null) ? Mathf.Clamp01(stats.GetStat(StatType.Dribbling) / 100f) : 0.5f;

            return Mathf.Clamp01(pSpace * pFrontal * pStat);
        }

        // =========================================================
        // 압박 점수 (보조 메서드)
        // =========================================================
        /// <summary>현재 에이전트에 가해지는 압박 강도를 0~1로 반환합니다.</summary>
        public float CalculatePressureScore(HybridAgentController agent)
        {
            if (MatchManager.Instance == null) return 0f;
            float pressure = 0f;
            var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
            if (opponents == null) return 0f;

            foreach (var other in opponents)
            {
                if (other == null) continue;
                float dist = Vector3.Distance(agent.transform.position, other.transform.position);
                if (dist < DangerRadius)
                    pressure += Mathf.Pow(1f - (dist / DangerRadius), 2f) * 0.5f;
            }
            return Mathf.Clamp01(pressure);
        }

        // =========================================================
        // 인터셉트 위험도 (패스 성공 확률 보조)
        // =========================================================
        public float CalculateInterceptionRisk(Vector3 startPos, Vector3 endPos, float ballSpeed, IEnumerable<HybridAgentController> opponents)
        {
            float maxRisk = 0f;
            if (opponents == null) return 0f;

            Vector3 passDir = (endPos - startPos).normalized;
            float passDist = Vector3.Distance(startPos, endPos);

            foreach (var opp in opponents)
            {
                if (opp == null) continue;
                if (Vector3.Distance(startPos, opp.transform.position) > passDist + 5f) continue;

                Vector3 toOpp = opp.transform.position - startPos;
                float projection = Vector3.Dot(toOpp, passDir);
                if (projection < 1.0f || projection > passDist - 1.0f) continue;

                Vector3 pointOnLine = startPos + passDir * projection;
                float distFromLine = Vector3.Distance(opp.transform.position, pointOnLine);

                float timeBall = projection / Mathf.Max(ballSpeed, 0.1f);
                float oppSpeed = Mathf.Max(opp.Velocity.magnitude, 6.0f);
                float timeOpp  = distFromLine / oppSpeed;

                if (timeOpp < timeBall + 0.2f)
                {
                    float interceptRisk = Mathf.Clamp01(1.0f - (distFromLine / 2.5f));
                    if (interceptRisk > maxRisk) maxRisk = interceptRisk;
                }
            }
            return maxRisk;
        }

        // =========================================================
        // 지원 위치 점수 (오프볼 포지셔닝용)
        // =========================================================
        public float CalculateSupportScore(Vector3 candidatePos, Vector3 ballPos, Vector3 goalPos,
            IEnumerable<HybridAgentController> enemies, float offsideLineZ, Team myTeam)
        {
            bool isOffside = (myTeam == Team.Home) ? (candidatePos.z >= offsideLineZ) : (candidatePos.z <= offsideLineZ);
            if (isOffside) return 0.0f;

            // 공간 점수
            float closestEnemyDist = 100f;
            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy == null) continue;
                    float d = Vector3.Distance(candidatePos, enemy.transform.position);
                    if (d < closestEnemyDist) closestEnemyDist = d;
                }
            }
            float spaceScore = Mathf.Clamp01(closestEnemyDist / 15f);

            // 위협 점수
            float distToGoal = Vector3.Distance(candidatePos, goalPos);
            float threatScore = Mathf.Clamp01(1.0f - ((distToGoal - 10f) / 50f));

            // 패스 레인 안전도
            float laneSafety = 1.0f;
            Vector3 passDir = (candidatePos - ballPos).normalized;
            float passDist = Vector3.Distance(candidatePos, ballPos);
            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy == null) continue;
                    Vector3 enemyVec = enemy.transform.position - ballPos;
                    float proj = Vector3.Dot(enemyVec, passDir);
                    if (proj > 0 && proj < passDist)
                    {
                        float distFromLine = Vector3.Distance(enemy.transform.position, ballPos + passDir * proj);
                        if (distFromLine < 2.0f) laneSafety *= 0.2f;
                    }
                }
            }

            // 거리 점수
            float distScore;
            if (passDist < MinPassDist) distScore = 0.2f;
            else if (passDist < (MaxPassDist - 5f)) distScore = 1.0f;
            else distScore = Mathf.Clamp01(1.0f - ((passDist - (MaxPassDist - 5f)) / 5f));

            return (spaceScore * 0.4f + threatScore * 0.3f + laneSafety * 0.3f) * distScore;
        }

        // =========================================================
        // 최적 슈팅 목표 지점
        // =========================================================
        /// <summary>골키퍼 위치를 고려해 가장 위협적인 목표 지점을 반환합니다.</summary>
        public Vector3 GetBestShootingTarget(HybridAgentController shooter, Vector3 goalCenter, float goalLineZ)
        {
            const float goalWidth  = 7.32f;
            const float postMargin = 0.5f;
            float halfWidth = (goalWidth * 0.5f) - postMargin;

            Vector3[] targets = new Vector3[5]
            {
                goalCenter,
                goalCenter + new Vector3(-halfWidth * 0.6f, 0, 0),
                goalCenter + new Vector3( halfWidth * 0.6f, 0, 0),
                goalCenter + new Vector3(-halfWidth, 0, 0),
                goalCenter + new Vector3( halfWidth, 0, 0),
            };

            // 골키퍼 탐색
            HybridAgentController gk = null;
            if (MatchManager.Instance != null)
            {
                var opponents = MatchManager.Instance.GetOpponents(shooter.TeamID);
                if (opponents != null)
                    foreach (var a in opponents)
                        if (a != null && a.IsGoalkeeper) { gk = a; break; }
            }

            float bestScore  = -1f;
            Vector3 bestTarget = goalCenter;
            const float safeRadius = 2.0f;

            foreach (Vector3 t in targets)
            {
                float score = 1.0f;

                // 코너 보너스
                float distFromCenter = Mathf.Abs(t.x - goalCenter.x);
                if (distFromCenter > (goalWidth * 0.3f)) score += 0.15f;

                // 골키퍼 회피
                if (gk != null)
                {
                    Vector3 shootDir = (t - shooter.transform.position).normalized;
                    Vector3 gkVec    = gk.transform.position - shooter.transform.position;
                    float projection = Vector3.Dot(gkVec, shootDir);
                    float shootDist  = Vector3.Distance(shooter.transform.position, t);

                    if (projection > 0 && projection < shootDist)
                    {
                        Vector3 closest     = shooter.transform.position + shootDir * projection;
                        float distFromLine  = Vector3.Distance(gk.transform.position, closest);
                        if (distFromLine < safeRadius)
                            score *= (distFromLine < 1.0f) ? 0.05f : (0.2f + 0.8f * Mathf.Clamp01(distFromLine / safeRadius));
                        else
                            score += 0.2f;
                    }
                    else score += 0.5f;
                }

                // 레이캐스트 차단 체크
                Vector3 dir  = (t - shooter.transform.position).normalized;
                float dist   = Vector3.Distance(shooter.transform.position, t);
                if (UnityEngine.Physics.Raycast(shooter.transform.position + Vector3.up * 0.5f, dir, out RaycastHit hit, dist, LayerMask.GetMask("Player")))
                {
                    if (hit.collider.gameObject != shooter.gameObject) score *= 0.05f;
                }

                if (score > bestScore) { bestScore = score; bestTarget = t; }
            }
            return bestTarget;
        }

        // =========================================================
        // 내부 헬퍼
        // =========================================================
        private float CalculateEnemyDensityAt(HybridAgentController agent, Vector3 pos)
        {
            if (MatchManager.Instance == null) return 0f;
            float density = 0f;
            var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
            if (opponents == null) return 0f;

            foreach (var other in opponents)
            {
                if (other == null) continue;
                float dist = Vector3.Distance(pos, other.transform.position);
                if (dist < DangerRadius)
                    density += (DangerRadius - dist) / DangerRadius;
            }
            return density;
        }
    }
}
