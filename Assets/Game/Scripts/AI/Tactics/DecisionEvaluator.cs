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
        // NOTE: 0.65로 설정하여 명백한 골 기회에서만 강제 슛. 그 아래는 EV 비교로 패스도 선택 가능.
        private const float ForceShootEVThreshold = 0.65f;

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
            public string DebugLog;    // 상세 판단 로그
        }

        // =========================================================
        // 위치 가치 평가 헬퍼 (공격 관점)
        // =========================================================
        /// <summary>
        /// 공격 시 특정 위치의 전술적 가치를 0~1로 반환합니다.
        /// 골대 절대 거리 기반이 아닌, 공격자의 현재 위치 대비 상대 전진도로 평가합니다.
        /// - 골문 정중앙 가까이 = 1.0
        /// - 공격자보다 후방이라도 = 최소 0.35 (빈 공간이면 가치 있음)
        /// </summary>
        public float EvaluatePositionValue(Vector3 position)
        {
            return EvaluatePositionValueRelative(position, _goalPosition, null);
        }

        /// <summary>공격자 위치 대비 상대 가치 평가</summary>
        public float EvaluatePositionValueRelative(Vector3 position, Vector3 goalPos, HybridAgentController currentAttacker)
        {
            float distToGoal = Vector3.Distance(position, goalPos);

            // 1. 골 거리 가치: 0~1 (Softened - 후방 패스에 너무 낮은 점수 방지)
            // MaxFieldDiagonal(100) = 0.0 / 5m = 1.0, 비선형 하지 않음(선형)
            float distValue = Mathf.Clamp01(1.0f - (distToGoal / MaxFieldDiagonal));
            // 최솟값 보장: 후방이더라도 최소 0.35 (후방 패스 허용)
            distValue = Mathf.Max(distValue, 0.35f);

            // 2. 각도 가치: 골대 정면(x=0)에 가까울수록 높음
            float absX = Mathf.Abs(position.x);
            float angleValue = Mathf.Lerp(1.0f, 0.2f, Mathf.Clamp01(absX / 30f));

            // 최종 위치 가치
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
            {
                Debug.Log($"[HOLD-DIAG] {agent.name} No Possession → Hold");
                return result;
            }

            // 이미 킥 실행 중이면 재결정 금지
            if (agent.IsBusy || (agent.BallHandler && agent.BallHandler.HasPendingKick))
            {
                Debug.Log($"[HOLD-DIAG] {agent.name} IsBusy={agent.IsBusy} PendingKick={agent.BallHandler?.HasPendingKick} MoverBusy={agent.Mover?.IsBusy} → Hold");
                return result;
            }

            // 행동 잠금 시간 내이면 대기
            if (Time.time < lastActionTime + ActionLockoutTime)
            {
                Debug.Log($"[HOLD-DIAG] {agent.name} ActionLockout remaining={(lastActionTime + ActionLockoutTime - Time.time):F2}s → Hold");
                return result;
            }

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

            // 5. [FIX] 임계값 게이트 제거 → 최고 EV 행동이 무조건 승리
            // 이전: evPass >= PassThreshold (0.25) 조건 → Dribble:0.25 같은 경우 항상 Hold
            // 수정: Shoot/Pass/Dribble 중 가장 높은 EV를 선택 (최소 바닥값 0.05 이상)
            string evLog = $"[EV] Shoot:{evShoot:F2} | Pass:{evPass:F2} | Dribble:{evDribble:F2}";
            const float MinActionEV = 0.05f; // 이 이하는 진짜 아무것도 못하는 상황

            float bestEV = Mathf.Max(evShoot, evPass, evDribble);

            if (bestEV < MinActionEV)
            {
                Vector3 myGoal = MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
                float distToMyGoal = Vector3.Distance(agent.transform.position, myGoal);

                if (agent.IsGoalkeeper || (distToMyGoal < 30f && _scorer.CalculatePressureScore(agent) > 0.3f))
                {
                    result.Action = "Pass"; // Null target = Clearance
                    result.Target = null;
                    result.DebugLog = $"DECISION: OVERRIDE-CLEARANCE (No viable options, forced from HOLD) - {evLog}";
                }
                else
                {
                    // 진짜 아무 행동도 불가능한 상황
                    result.DebugLog = $"DECISION: HOLD (All EVs < {MinActionEV}) - {evLog}";
                }
            }
            else if (evShoot >= evPass && evShoot >= evDribble)
            {
                result.Action     = "Shoot";
                result.Position   = _scorer.GetBestShootingTarget(agent, _goalPosition, _goalPosition.z);
                result.Confidence = evShoot;
                result.DebugLog   = $"DECISION: SHOOT ({evShoot:F2}) - {evLog}";
            }
            else if (evPass >= evDribble)
            {
                result.Action     = "Pass";
                result.Target     = FindBestPassTarget(agent);
                result.Confidence = evPass;
                result.DebugLog   = $"DECISION: PASS ({evPass:F2}) - {evLog}";

                // 패스 대상이 없으면: 수비 진영이거나 골키퍼면 '클리어런스', 아니면 드리블 폴백
                if (result.Target == null)
                {
                    Vector3 myGoal = MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
                    float distToMyGoal = Vector3.Distance(agent.transform.position, myGoal);

                    if (agent.IsGoalkeeper || (distToMyGoal < 35f && _scorer.CalculatePressureScore(agent) > 0.2f))
                    {
                        // Action == "Pass", Target == null -> 클리어런스로 동작
                        result.DebugLog = $"DECISION: CLEARANCE(pass-null) - {evLog}";
                    }
                    else
                    {
                        result.Action   = "Dribble";
                        result.Position = FindBestDribbleTarget(agent, agent.transform.position, false);
                        result.DebugLog = $"DECISION: DRIBBLE(pass-null-fallback) ({evDribble:F2}) - {evLog}";
                    }
                }
            }
            else
            {
                result.Action     = "Dribble";
                result.Position   = FindBestDribbleTarget(agent, agent.transform.position, false);
                result.Confidence = evDribble;
                result.DebugLog   = $"DECISION: DRIBBLE ({evDribble:F2}) - {evLog}";
            }

            // [FIX] 골키퍼이거나 위험 지역의 마지막 수비수인데도 드리블을 선택한 경우, 강제로 클리어런스로 전환
            if (result.Action == "Dribble")
            {
                Vector3 myGoal = MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
                float distToMyGoal = Vector3.Distance(agent.transform.position, myGoal);

                if (agent.IsGoalkeeper || (distToMyGoal < 30f && _scorer.CalculatePressureScore(agent) > 0.3f))
                {
                    result.Action = "Pass"; // Pass with null target = Clearance
                    result.Target = null;
                    result.DebugLog = $"DECISION: OVERRIDE-CLEARANCE (Dribble was too risky) - {evLog}";
                }
            }

            return result;
        }

        // =========================================================
        // 패닉 모드: 임계값 무시하고 최선 행동 강제 반환
        // =========================================================
        /// \u003csummary\u003e
        /// HOLD가 일정 시간 이상 지속될 때 호출. ShootThreshold / PassThreshold / DribbleThreshold를
        /// 무시하고 현재 계산된 EV 중 가장 높은 행동을 강제로 반환합니다.
        /// 백패스, 낮은 승률의 드리블, 먼 거리 슛 등 모두 허용.
        /// \u003c/summary\u003e
        public DecisionResult EvaluateForcedAction(HybridAgentController agent, ref float lastActionTime)
        {
            DecisionResult result = new DecisionResult { Action = "Hold", Confidence = 0f };

            // 킥 실행 중이면 wait
            if (agent.IsBusy || (agent.BallHandler && agent.BallHandler.HasPendingKick))
                return result;

            // ActionLockout은 유지 (너무 잦은 패닉 방지)
            if (Time.time < lastActionTime + ActionLockoutTime)
                return result;

            float evShoot   = CalculateShootEV(agent);
            float evPass    = CalculatePassEV(agent);
            float evDribble = CalculateDribbleEV(agent);

            string evLog = $"[PANIC EV] Shoot:{evShoot:F2} | Pass:{evPass:F2} | Dribble:{evDribble:F2}";

            // 임계값 없이 최대 EV 선택
            if (evShoot >= evPass && evShoot >= evDribble && evShoot > 0f)
            {
                result.Action     = "Shoot";
                result.Position   = _scorer.GetBestShootingTarget(agent, _goalPosition, _goalPosition.z);
                result.Confidence = evShoot;
                result.DebugLog   = $"PANIC:SHOOT ({evShoot:F2}) - {evLog}";
            }
            else if (evPass >= evDribble && evPass > 0f)
            {
                result.Action     = "Pass";
                result.Target     = FindBestPassTarget(agent);
                result.Confidence = evPass;
                result.DebugLog   = $"PANIC:PASS ({evPass:F2}) - {evLog}";

                // 패스 대상이 없으면 수비 진영이거나 골키퍼일 때 클리어런스
                if (result.Target == null)
                {
                    Vector3 myGoal = MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
                    float distToMyGoal = Vector3.Distance(agent.transform.position, myGoal);

                    if (agent.IsGoalkeeper || distToMyGoal < 35f)
                    {
                        // 클리어런스
                        result.DebugLog = $"PANIC:CLEARANCE(pass-null) - {evLog}";
                    }
                    else
                    {
                        result.Action   = "Dribble";
                        result.Position = FindBestDribbleTarget(agent, agent.transform.position, true);
                        result.DebugLog = $"PANIC:DRIBBLE(no-pass-fallback) ({evDribble:F2}) - {evLog}";
                    }
                }
            }
            else
            {
                // 모든 EV가 낮을 때 (패닉): 골키퍼 및 수비수는 즉시 클리어!
                Vector3 myGoal = MatchManager.Instance.GetDefendGoalPosition(agent.TeamID);
                float distToMyGoal = Vector3.Distance(agent.transform.position, myGoal);

                if (agent.IsGoalkeeper || distToMyGoal < 35f)
                {
                    result.Action   = "Pass"; // Null target = Clearance
                    result.Target   = null;
                    result.DebugLog = $"PANIC:CLEARANCE(forced) - {evLog}";
                }
                else
                {
                    // 공격/미드필더는 공격 진영에서 드리블로 탈출 시도
                    result.Action     = "Dribble";
                    result.Position   = FindBestDribbleTarget(agent, agent.transform.position, true);
                    result.Confidence = evDribble;
                    result.DebugLog   = $"PANIC:DRIBBLE ({evDribble:F2}) - {evLog}";
                }
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
            if (bestTarget == null)
            {
                Debug.Log($"[PASS-DIAG] {agent.name} FindBestPassTarget=null (teammates={agent.GetTeammates()?.Count ?? 0})");
                return 0f;
            }

            var opponents = MatchManager.Instance?.GetOpponents(agent.TeamID);
            float pPass    = _scorer.CalculatePassSuccessProbability(agent, agent.Stats, bestTarget, opponents);
            float posValue = Mathf.Max(EvaluatePositionValue(bestTarget.transform.position), 0.35f);

            float ev = Mathf.Clamp01(pPass * posValue);
            Debug.Log($"[PASS-DIAG] {agent.name} → {bestTarget.name} | pPass:{pPass:F2} posVal:{posValue:F2} ev:{ev:F2}");
            return ev;
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
            float bestEV  = -1f;

            const float MIN_PASS_DIST   = 4.0f;  // [FIX] 최소 패스 거리 (너무 가까우면 패스 금지)
            const float RECENT_PASS_PENALTY = 0.5f; // [FIX] 최근 수신자 EV 페널티 비율
            const float RECENT_PASS_WINDOW  = 5.0f; // [FIX] 몇 초 이내 재패스를 억제할지

            foreach (var tm in teammates)
            {
                if (tm == agent) continue;

                // [FIX] 거리 필터: 최소 패스 거리 미만이면 스킵
                float dist = Vector3.Distance(agent.transform.position, tm.transform.position);
                if (dist < MIN_PASS_DIST)
                {
                    Debug.Log($"[PASS-SKIP] {agent.name}→{tm.name} | Too close ({dist:F1}m < {MIN_PASS_DIST}m)");
                    continue;
                }

                float pPass    = _scorer.CalculatePassSuccessProbability(agent, agent.Stats, tm.gameObject, opponents);
                // [FIX] 백패스 팀메이트도 최소 0.35 posValue 보장
                float posValue = Mathf.Max(EvaluatePositionValue(tm.transform.position), 0.35f);
                float ev       = pPass * posValue;

                // [FIX] 최근에 이 에이전트에게 패스받은 팀메이트면 EV 페널티
                if (agent.LastPassRecipient == tm.gameObject &&
                    Time.time - agent.LastPassTime < RECENT_PASS_WINDOW)
                {
                    ev *= RECENT_PASS_PENALTY;
                    Debug.Log($"[PASS-PENALTY] {agent.name}→{tm.name} | Recent recipient penalty applied. ev→{ev:F2}");
                }

                Debug.Log($"[PASS-TARGET] {agent.name}→{tm.name} | pPass:{pPass:F2} posVal:{posValue:F2} ev:{ev:F2}");

                if (ev > bestEV)
                {
                    bestEV     = ev;
                    bestTarget = tm.gameObject;
                }
            }

            // [FIX] 패스 확정 시 수신자 기록
            if (bestTarget != null)
            {
                agent.LastPassRecipient = bestTarget;
                agent.LastPassTime      = Time.time;
            }

            return bestTarget;
        }

        /// <summary>드리블 후보 지점 중 EV가 가장 높은 목표 위치를 반환합니다.</summary>
        public Vector3 FindBestDribbleTarget(HybridAgentController agent, Vector3 ballPos, bool forceEvade = false)
        {
            float w = 32f;
            float l = 48f;
            if (agent.BallHandler && agent.BallHandler.settings)
            {
                w = agent.BallHandler.settings.fieldHalfWidth;
                l = agent.BallHandler.settings.fieldHalfLength;
            }

            // [FIX] 원점을 ballPos → agent.transform.position으로 변경
            // 이전: 공 위치 기준 8m 목표 → 에이전트가 경계 근처일 때 목표가 필드 밖으로 나감
            // 수정: 에이전트 발 위치 기준으로 목표 생성
            Vector3 origin = agent.transform.position;
            origin.y = 0f;

            int numDirections = 12;
            float angleStep = 360f / numDirections;
            float dribbleDist = 8f;

            // [FIX] 기준 방향을 agent.transform.forward → 골대 방향으로 변경
            // 이전: on-ball 상태에서 에이전트가 공(사이드라인 방향)을 바라봄
            //       → forward가 사이드라인 방향 → 12방향 전체가 사이드라인 기준으로 회전
            // 수정: 항상 골대 방향을 0도로 고정 → 전방=골대 쪽, 후방=자기 골대 쪽
            Vector3 toGoal = (_goalPosition - origin);
            toGoal.y = 0f;
            Vector3 agentForward = (toGoal.sqrMagnitude > 0.01f) ? toGoal.normalized : Vector3.forward;

            float bestEV  = -1f;
            Vector3 bestPos = Vector3.ClampMagnitude(origin + agentForward * dribbleDist, 999f);
            // 기본값도 경계 클램프
            bestPos.x = Mathf.Clamp(bestPos.x, -(w - 5f), w - 5f);
            bestPos.z = Mathf.Clamp(bestPos.z, -(l - 5f), l - 5f);

            bool frontalBlocked = (agent.SkillSystem != null) && agent.SkillSystem.IsFrontalBlocked();
            float pressure = _scorer.CalculatePressureScore(agent);

            for (int i = 0; i < numDirections; i++)
            {
                float angleDeg = i * angleStep;
                Vector3 dir = Quaternion.AngleAxis(angleDeg, Vector3.up) * agentForward;
                Vector3 target = origin + dir * dribbleDist;

                // [FIX] 경계 마진 2f → 5f: 더 일찍 가장자리 방향을 걸러냄
                if (Mathf.Abs(target.x) > (w - 5f) || Mathf.Abs(target.z) > (l - 5f))
                    continue;

                float pDribble = _scorer.CalculateDribbleSuccessProbability(agent, agent.Stats, dir);
                float posValue = Mathf.Max(EvaluatePositionValue(target), 0.25f);

                float backwardness = Mathf.Clamp01(angleDeg <= 180f ? angleDeg / 180f : (360f - angleDeg) / 180f);
                float evadeBonus = forceEvade || frontalBlocked
                    ? backwardness * pressure * 0.4f
                    : 0f;

                if (forceEvade)
                {
                    float perpendicularity = Mathf.Abs(Vector3.Dot(dir, agentForward));
                    evadeBonus += (1f - perpendicularity) * 0.2f;
                }

                float ev = pDribble * posValue + evadeBonus;

                // [FIX] 경계 근처면 중앙 방향에 강한 보너스 (에이전트 위치 기준)
                bool nearSide = Mathf.Abs(origin.x) > (w - 10f);
                bool nearEnd  = Mathf.Abs(origin.z) > (l - 10f);
                if (nearSide || nearEnd)
                {
                    Vector3 toCenter = -new Vector3(origin.x, 0f, origin.z).normalized;
                    float alignment  = Vector3.Dot(dir, toCenter);
                    if (alignment > 0.3f)
                        ev = Mathf.Clamp01(ev + 0.4f * alignment); // 더 강한 중앙 보너스
                    else if (alignment < -0.1f)
                        ev *= 0.15f; // 경계 쪽으로 향하는 방향은 강하게 패널티
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
