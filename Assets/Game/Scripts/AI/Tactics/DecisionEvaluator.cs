using UnityEngine;
using Game.Scripts.AI.DecisionMaking;
using Game.Scripts.Managers;
using Game.Scripts.Data;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// 공격 중인 플레이어의 최적 행동(패스/슛/드리블)을 평가합니다.
    /// 모든 행동의 가치 = 성공 확률(0~1) × 전술적 가치(0~1)로 계산됩니다.
    /// </summary>
    public class DecisionEvaluator
    {
        private UtilityScorer _scorer;
        private Vector3 _goalPosition;
        private MatchEngineConfig _config;

        // 설정값 Fallback
        private float ShootThreshold    => _config != null ? _config.ShootThreshold    : 0.5f;
        private float PassThreshold     => _config != null ? _config.PassThreshold     : 0.6f;
        private float DribbleThreshold  => _config != null ? _config.DribbleThreshold  : 0.3f;
        private float BreakawayDistance => _config != null ? _config.BreakawayDistance : 14f;
        private float ActionLockoutTime => _config ?         _config.ActionLockoutTime : 0.5f;

        // 슛 EV가 이 값 이상이면 무조건 슛 (골 지향 강제 임계값)
        private const float ForceShootEVThreshold = 0.45f;

        // 경기장 기준: 상대 골대 방향 최대 거리 (위치 가치 정규화에 사용)
        private const float MaxFieldDiagonal = 100f;

        public DecisionEvaluator(Vector3 goalPosition, MatchEngineConfig config)
        {
            _config      = config;
            _goalPosition = goalPosition;
            _scorer      = new UtilityScorer(config);
        }

        // =========================================================
        // 결과 구조체
        // =========================================================
        public struct DecisionResult
        {
            public string Action;      // "Pass", "Shoot", "Dribble", "Hold", "Breakthrough"
            public GameObject Target;  // Pass 대상
            public Vector3 Position;   // Shoot/Dribble 목표 지점
            public float Confidence;   // EV 점수
        }

        // =========================================================
        // 위치 가치 평가 헬퍼 (공격 관점)
        // =========================================================
        /// <summary>
        /// 공격 시 특정 위치의 전술적 가치를 0~1로 반환합니다.
        /// 평가 기준은 오직 상대 골대 중심과의 거리 및 각도입니다.
        /// - 상대 골대 정중앙 바로 앞 = 1.0
        /// - 우리 진영 구석 = 0.0
        /// </summary>
        public float EvaluatePositionValue(Vector3 position)
        {
            // 1. 거리 가치: 골대에 가까울수록 높음
            float distToGoal = Vector3.Distance(position, _goalPosition);
            // 골대 5m 이내 = 1.0, MaxFieldDiagonal 이상 = 0.0
            float distValue = Mathf.Clamp01(1.0f - (distToGoal / MaxFieldDiagonal));
            // 비선형 보정: 근거리 가치를 더 강조
            distValue = distValue * distValue;

            // 2. 각도 가치: 골대 정면(x=0)에 가까울수록 높음
            float absX = Mathf.Abs(position.x);
            float angleValue = Mathf.Lerp(1.0f, 0.1f, Mathf.Clamp01(absX / 30f));

            // 최종 위치 가치 = 거리 가치 × 각도 가치
            return Mathf.Clamp01(distValue * angleValue);
        }

        // =========================================================
        // 메인 의사결정 로직
        // =========================================================
        public DecisionResult EvaluateBestAction(HybridAgentController agent, ref float lastActionTime, ref float aimingTimer)
        {
            DecisionResult result = new DecisionResult { Action = "Hold", Confidence = 0f };

            // 공 소유 확인
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentBallOwner != agent)
                return result;

            // 이미 킥 실행 중이면 재결정 금지
            if (agent.IsBusy || (agent.BallHandler && agent.BallHandler.HasPendingKick))
                return result;

            // 행동 잠금 시간 내이면 대기
            if (Time.time < lastActionTime + ActionLockoutTime)
                return result;

            float distToGoal = Vector3.Distance(agent.transform.position, _goalPosition);

            // 1. 스킬: Breakthrough
            if (agent.SkillSystem != null && agent.SkillSystem.IsFrontalBlocked())
            {
                if (agent.SkillSystem.CanUseBreakthrough)
                {
                    result.Action     = "Breakthrough";
                    result.Confidence = 1.0f;
                    return result;
                }
            }

            // 2. 단독 돌파(Breakaway) 로직
            if (CheckBreakaway(agent, distToGoal) && distToGoal > BreakawayDistance)
            {
                result.Action     = "Dribble";
                result.Position   = FindBestDribbleTarget(agent, agent.transform.position, false);
                result.Confidence = 0.9f;
                return result;
            }

            // 3. EV 계산
            float evShoot   = CalculateShootEV(agent);
            float evPass    = CalculatePassEV(agent);
            float evDribble = CalculateDribbleEV(agent);

            // 4. 골 지향 강제: 슛 EV가 임계값 이상이면 무조건 슛
            if (evShoot >= ForceShootEVThreshold)
            {
                result.Action     = "Shoot";
                result.Position   = _scorer.GetBestShootingTarget(agent, _goalPosition, _goalPosition.z);
                result.Confidence = evShoot;
                return result;
            }

            // 5. 일반 최대 EV 행동 선택
            if (evShoot >= ShootThreshold && evShoot >= evPass && evShoot >= evDribble)
            {
                result.Action     = "Shoot";
                result.Position   = _scorer.GetBestShootingTarget(agent, _goalPosition, _goalPosition.z);
                result.Confidence = evShoot;
            }
            else if (evPass >= PassThreshold && evPass >= evDribble)
            {
                result.Action     = "Pass";
                result.Target     = FindBestPassTarget(agent);
                result.Confidence = evPass;
            }
            else if (evDribble >= DribbleThreshold)
            {
                result.Action     = "Dribble";
                result.Position   = FindBestDribbleTarget(agent, agent.transform.position, false);
                result.Confidence = evDribble;
            }

            return result;
        }

        // =========================================================
        // EV 계산 (곱셈 형태)
        // =========================================================

        /// <summary>
        /// 슛 EV = 슛 성공 확률 × 1.0 (득점은 항상 최고 가치)
        /// </summary>
        private float CalculateShootEV(HybridAgentController agent)
        {
            // 슛 성공 확률 (UtilityScorer 위임)
            float pShoot = _scorer.CalculateShootScore(agent.transform.position, agent.Stats, _goalPosition);

            // 슛의 전술 가치 = 1.0 고정 (득점이 목표)
            const float tacticalValue = 1.0f;

            return Mathf.Clamp01(pShoot * tacticalValue);
        }

        /// <summary>
        /// 패스 EV = 패스 성공 확률 × 수신자 위치 가치
        /// </summary>
        private float CalculatePassEV(HybridAgentController agent)
        {
            var bestTarget = FindBestPassTarget(agent);
            if (bestTarget == null) return 0f;

            // 패스 성공 확률
            var opponents = MatchManager.Instance?.GetOpponents(agent.TeamID);
            float pPass = _scorer.CalculatePassSuccessProbability(agent, agent.Stats, bestTarget, opponents);

            // 수신자 위치 가치 (골대 기준 위치 평가)
            float posValue = EvaluatePositionValue(bestTarget.transform.position);

            // EV = P(패스 성공) × 수신자 위치 가치
            return Mathf.Clamp01(pPass * posValue);
        }

        /// <summary>
        /// 드리블 EV = 드리블 성공 확률 × 목표 지점 위치 가치
        /// </summary>
        private float CalculateDribbleEV(HybridAgentController agent)
        {
            Vector3 dribbleTarget = FindBestDribbleTarget(agent, agent.transform.position, false);
            Vector3 direction     = (dribbleTarget - agent.transform.position);

            // 드리블 성공 확률
            float pDribble = _scorer.CalculateDribbleSuccessProbability(agent, agent.Stats, direction);

            // 목표 지점의 위치 가치
            float posValue = EvaluatePositionValue(dribbleTarget);

            // EV = P(드리블 성공) × 목표 지점 가치
            return Mathf.Clamp01(pDribble * posValue);
        }

        // =========================================================
        // 헬퍼 메서드
        // =========================================================

        /// <summary>팀 동료 중 EV 관점에서 가장 좋은 패스 대상을 반환합니다.</summary>
        private GameObject FindBestPassTarget(HybridAgentController agent)
        {
            var teammates  = agent.GetTeammates();
            var opponents  = MatchManager.Instance?.GetOpponents(agent.TeamID);
            GameObject bestTarget = null;
            float bestEV  = 0f;

            foreach (var tm in teammates)
            {
                if (tm == agent) continue;

                // 패스 성공 확률 × 수신자 위치 가치
                float pPass    = _scorer.CalculatePassSuccessProbability(agent, agent.Stats, tm.gameObject, opponents);
                float posValue = EvaluatePositionValue(tm.transform.position);
                float ev       = pPass * posValue;

                if (ev > bestEV)
                {
                    bestEV    = ev;
                    bestTarget = tm.gameObject;
                }
            }
            return bestTarget;
        }

        /// <summary>드리블 후보 지점 중 EV가 가장 높은 목표 위치를 반환합니다.</summary>
        public Vector3 FindBestDribbleTarget(HybridAgentController agent, Vector3 ballPos, bool forceEvade = false)
        {
            Vector3 dirToGoal = (_goalPosition - ballPos).normalized;
            Vector3 right     = Vector3.Cross(Vector3.up, dirToGoal);

            // 8방향 후보 지점 생성
            Vector3[] candidates = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(i * 45f, Vector3.up) * dirToGoal;
                candidates[i] = ballPos + dir.normalized * 10f;
            }

            float w = 32f;
            float l = 48f;
            if (agent.BallHandler && agent.BallHandler.settings)
            {
                w = agent.BallHandler.settings.fieldHalfWidth;
                l = agent.BallHandler.settings.fieldHalfLength;
            }

            float   bestEV  = -1f;
            Vector3 bestPos = _goalPosition;

            foreach (var target in candidates)
            {
                // 필드 경계 이탈 페널티
                if (Mathf.Abs(target.x) > (w - 1f) || Mathf.Abs(target.z) > (l - 1f))
                    continue;

                Vector3 moveDir = (target - ballPos).normalized;

                // 드리블 성공 확률
                float pDribble = _scorer.CalculateDribbleSuccessProbability(agent, agent.Stats, moveDir);

                // 회피 모드: 측면 방향 가중치 증가
                float evadeBonus = 0f;
                if (forceEvade)
                {
                    float perpendicularity = Mathf.Abs(Vector3.Dot(moveDir, right));
                    evadeBonus = perpendicularity * 0.3f;
                    pDribble   = Mathf.Clamp01(pDribble + evadeBonus);
                }

                // 목표 지점 위치 가치
                float posValue = EvaluatePositionValue(target);

                // EV = P(드리블) × 위치 가치
                float ev = pDribble * posValue;

                // 필드 경계 근처: 중앙 방향 보너스
                bool nearSide = Mathf.Abs(ballPos.x) > (w - 7f);
                bool nearEnd  = Mathf.Abs(ballPos.z) > (l - 8f);
                if (nearSide || nearEnd)
                {
                    Vector3 toCenter = -ballPos.normalized;
                    if (Vector3.Dot(moveDir, toCenter) > 0.3f)
                        ev = Mathf.Clamp01(ev + 0.2f);
                }

                if (ev > bestEV) { bestEV = ev; bestPos = target; }
            }
            return bestPos;
        }

        /// <summary>특정 위치의 적 밀도를 반환합니다. (외부 호출 허용)</summary>
        public float CalculateEnemyDensity(Vector3 pos, HybridAgentController agent)
        {
            float density = 0f;
            if (MatchManager.Instance == null) return 0f;
            float checkDist = _config ? _config.DangerRadius : 5.0f;
            var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);

            foreach (var opp in opponents)
            {
                if (opp == null || opp == agent) continue;
                float dist = Vector3.Distance(opp.transform.position, pos);
                if (dist < checkDist)
                    density += (checkDist - dist);
            }
            return density;
        }

        /// <summary>공격자와 골대 사이에 수비수가 없는 1v1 상황인지 확인합니다.</summary>
        public bool Is1v1Situation(HybridAgentController agent, Vector3 goalPos)
        {
            Vector3 toGoal    = (goalPos - agent.transform.position);
            float distToGoal  = toGoal.magnitude;
            Vector3 dirToGoal = toGoal.normalized;

            if (MatchManager.Instance != null)
            {
                var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
                foreach (var opp in opponents)
                {
                    if (opp.IsGoalkeeper) continue;
                    Vector3 toOpp = opp.transform.position - agent.transform.position;
                    float dist    = toOpp.magnitude;
                    if (dist > distToGoal) continue;
                    if (Vector3.Dot(dirToGoal, toOpp.normalized) < 0) continue;

                    Vector3 oppProj  = Vector3.Project(toOpp, dirToGoal);
                    Vector3 rejection = toOpp - oppProj;
                    if (rejection.magnitude < 2.5f) return false;
                }
                return true;
            }
            return false;
        }

        private bool CheckBreakaway(HybridAgentController agent, float distToGoal)
        {
            if (distToGoal <= BreakawayDistance) return false;

            float nearestDefenderDist = 999f;
            if (MatchManager.Instance != null)
            {
                var opponents = MatchManager.Instance.GetOpponents(agent.TeamID);
                foreach (var opp in opponents)
                {
                    if (opp.IsGoalkeeper) continue;
                    float oppDist = Vector3.Distance(opp.transform.position, _goalPosition);
                    if (oppDist < nearestDefenderDist) nearestDefenderDist = oppDist;
                }
            }
            return (distToGoal < nearestDefenderDist - 3f);
        }
    }
}
