using UnityEngine;
using Game.Scripts.AI.HFSM;
using Game.Scripts.AI.DecisionMaking;
using Game.Scripts.AI.Tactics;
using Game.Scripts.Data;
using Game.Scripts.Managers;

namespace Game.Scripts.AI.States
{
    /// <summary>
    /// 공격 루트 상태.
    /// 의사결정을 DecisionEvaluator(EV 모델)에 위임합니다.
    /// 슛 EV가 임계값 이상이면 다른 행동을 무시하고 무조건 슛을 시도합니다.
    /// </summary>
    public class RootState_Attacking : State
    {
        // 헬퍼 클래스
        private AttackingTactics _tactics;
        private DecisionEvaluator _evaluator;

        // 레거시 (호환성 유지)
        private UtilityScorer _scorer;
        private PlayerStats _stats;

        // 타이밍 & 상태
        private float _decisionTimer    = 0f;
        private float _lastActionTime   = -999f;
        private float _aimingTimer      = 0f;
        private float _lastDebugLogTime = -999f;

        // HOLD 패닉 타이머: 이 시간(초) 이상 HOLD가 지속되면 강제로 최선의 행동 실행
        private float _holdTimer        = 0f;
        private const float HoldPanicTime = 1.5f; // 1.5초 이상 HOLD → 강제 행동

        // 골 위치
        private Vector3 _goalPosition = new Vector3(0, 0, 25f);

        // 슛 EV가 이 값 이상이면 패스/드리블을 무시하고 무조건 슛
        // DecisionEvaluator.ForceShootEVThreshold(0.65f)와 동기화
        private const float ForceShootEVThreshold = 0.65f;

        // 설정 Fallback
        private float MaxPassAngle    => agent.config ? agent.config.MaxPassAngle    : 45f;
        private float MaxDribbleAngle => agent.config ? agent.config.MaxDribbleAngle : 45f;

        public RootState_Attacking(HybridAgentController agent, StateMachine stateMachine) : base(agent, stateMachine)
        {
            _scorer = new UtilityScorer(agent.config);
            _stats  = agent.GetComponent<PlayerStats>();
        }

        public override void Enter()
        {
            // 골 위치 취득
            if (MatchManager.Instance != null)
                _goalPosition = MatchManager.Instance.GetAttackGoalPosition(agent.TeamID);
            else
                _goalPosition = new Vector3(0, 0, (agent.TeamID == Team.Home) ? 50f : -50f);

            _tactics   = new AttackingTactics(_goalPosition);
            _evaluator = new DecisionEvaluator(_goalPosition, agent.config);

            // 결정 타이머 분산
            float interval = agent.config ? agent.config.DecisionInterval : 0.1f;
            _decisionTimer = Random.Range(0f, interval);
        }

        private float _execLogTimer = 0f;

        public override void Execute()
        {
            if (Time.time > _execLogTimer + 2.0f)
                _execLogTimer = Time.time;

            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentState != MatchState.Playing) return;

            Transform ball = matchMgr.Ball?.transform;
            if (ball == null) return;

            RunFieldPlayerLogic(ball, matchMgr);
        }

        public override void PhysicsExecute() { }

        public override void Exit() { }

        // =========================================================
        // 필드 플레이어 로직
        // =========================================================
        private void RunFieldPlayerLogic(Transform ball, MatchManager matchMgr)
        {
            Vector3 ballPos    = ball.position;
            bool hasPossession = (matchMgr.CurrentBallOwner == agent);

            if (hasPossession)
            {
                _decisionTimer += Time.fixedDeltaTime;
                float interval  = agent.config ? agent.config.DecisionInterval : 0.1f;
                if (_decisionTimer >= interval)
                {
                    _decisionTimer = 0f;
                    // [FIX] holdTimer는 MakeDecision 결과가 Hold가 아닐 때만 리셋
                    // 이전에는 MakeDecision 실행 전에 무조건 리셋 → 패닉이 절대 발동 안 됨
                    MakeDecision(ballPos);
                }
            }
            else
            {
                MoveToSupportPosition(ballPos);
            }
        }

        // =========================================================
        // 의사결정 (EV 모델 기반)
        // =========================================================
        private void MakeDecision(Vector3 ballPos)
        {
            // 킥 준비 중이면 재결정 금지
            if (agent.BallHandler.IsPreparingKick) return;

            // EV 평가기에서 최적 행동 취득
            var decision = _evaluator.EvaluateBestAction(agent, ref _lastActionTime, ref _aimingTimer);

            // [LOGGING] 상세 EV 로그 출력
            if (!string.IsNullOrEmpty(decision.DebugLog))
            {
                LogAction(decision.DebugLog);
            }

            // ─── 골 지향 강제 슛 로직 ───────────────────────────────
            // 슛 EV가 임계값(ForceShootEVThreshold) 이상이면
            // 드리블/패스를 무시하고 무조건 슛을 선택합니다.
            // EV는 이미 DecisionEvaluator 내부에서 처리되지만,
            // 여기서도 Dribble/Pass 결정을 덮어쓰는 안전장치로 재확인합니다.
            if (decision.Action == "Dribble" || decision.Action == "Pass")
            {
                float evShoot = _scorer.CalculateShootScore(
                    agent.transform.position,
                    agent.Stats,
                    _goalPosition
                );

                if (evShoot >= ForceShootEVThreshold)
                {
                    decision.Action     = "Shoot";
                    decision.Position   = _scorer.GetBestShootingTarget(agent, _goalPosition, _goalPosition.z);
                    decision.Confidence = evShoot;
                    LogAction($"FORCE SHOOT (EV={evShoot:F2} >= {ForceShootEVThreshold})");
                }
            }
            // ────────────────────────────────────────────────────────

            switch (decision.Action)
            {
                case "Shoot":
                    _holdTimer = 0f;
                    ExecuteShoot(decision.Position);
                    break;
                case "Pass":
                    _holdTimer = 0f;
                    ExecutePass(decision.Target);
                    break;
                case "Dribble":
                    _holdTimer = 0f;
                    ExecuteDribble(decision.Position);
                    break;
                case "Hold":
                default:
                    // [FIX] HOLD 시 내비게이션 멈추기
                    agent.Mover?.Stop();

                    // [NEW] HOLD 패닉 타이머: 너무 오래 HOLD면 강제 행동
                    _holdTimer += Time.fixedDeltaTime;
                    if (_holdTimer >= HoldPanicTime)
                    {
                        _holdTimer = 0f;
                        // 임계값 무시하고 현재 최선의 행동 강제 실행
                        var panicDecision = _evaluator.EvaluateForcedAction(agent, ref _lastActionTime);
                        LogAction($"HOLD PANIC ({HoldPanicTime}s) → {panicDecision.Action} ({panicDecision.Confidence:F2})");
                        switch (panicDecision.Action)
                        {
                            case "Shoot":  ExecuteShoot(panicDecision.Position);  break;
                            case "Pass":   ExecutePass(panicDecision.Target);      break;
                            case "Dribble": ExecuteDribble(panicDecision.Position); break;
                        }
                    }
                    break;
            }
        }

        // =========================================================
        // 행동 실행
        // =========================================================
        private void ExecuteShoot(Vector3 targetPos)
        {
            if (TryCurvedShotLogic(targetPos))
            {
                _lastActionTime = Time.time;
                return;
            }
            agent.Shoot(targetPos);
            _lastActionTime = Time.time;
            LogAction($"SHOOT at {targetPos}");
        }

        private void ExecutePass(GameObject target)
        {
            if (target == null)
            {
                // 비상 클리어런스
                var teammates = agent.GetTeammates();
                Vector3 avgPos = Vector3.zero;
                int count = 0;
                foreach (var tm in teammates) { avgPos += tm.transform.position; count++; }

                Vector3 clearDir = (count > 0)
                    ? ((avgPos / count - agent.transform.position).normalized + (_goalPosition - agent.transform.position).normalized * 0.5f).normalized
                    : (_goalPosition - agent.transform.position).normalized;

                agent.BallHandler.HighClearanceKick(clearDir);
                LogAction("EMERGENCY CLEARANCE");
                _lastActionTime = Time.time;
                return;
            }

            // [FIXED] 패스 각도 체크 제거 - 각도 무관하게 바로 패스 실행
            // (킥 준비 로직(UpdateKickLogic)이 자동으로 방향 정렬을 처리함)
            agent.Pass(target);
            _lastActionTime = Time.time;
            LogAction($"PASS to {target.name}");
        }

        private void ExecuteDribble(Vector3 targetPos)
        {
            // [SAFETY] 최종 안전망: 목표 위치가 필드 경계를 벗어나지 않도록 클램프
            float fw = 32f, fl = 48f;
            if (agent.BallHandler?.settings != null)
            {
                fw = agent.BallHandler.settings.fieldHalfWidth;
                fl = agent.BallHandler.settings.fieldHalfLength;
            }
            targetPos.x = Mathf.Clamp(targetPos.x, -(fw - 4f), fw - 4f);
            targetPos.z = Mathf.Clamp(targetPos.z, -(fl - 4f), fl - 4f);

            Vector3 toBall       = MatchManager.Instance.Ball.transform.position - agent.transform.position;
            toBall.y             = 0;
            Vector3 agentForward = agent.transform.forward;
            agentForward.y       = 0;

            // [FIX] 드리블은 연속 행동 — _lastActionTime을 설정하지 않음
            // (_lastActionTime은 Pass/Shoot 킥에서만 설정)

            if (Vector3.Angle(agentForward, toBall) > MaxDribbleAngle)
            {
                return;
            }

            if (!agent.BallHandler.IsInPocket)
            {
                agent.Mover.SprintTo(MatchManager.Instance.Ball.transform.position);
                return;
            }

            bool isBlocked = agent.SkillSystem.IsFrontalBlocked();
            if (isBlocked)
            {
                agent.Mover.MoveTo(targetPos);
            }
            else
            {
                float dist = Vector3.Distance(agent.transform.position, targetPos);
                if (dist > 5.0f)
                {
                    agent.Mover.SprintTo(targetPos);
                }
                else
                {
                    agent.Mover.MoveTo(targetPos);
                }
            }

            LogAction($"DRIBBLE to {targetPos}");
        }

        // =========================================================
        // 오프볼 포지셔닝
        // =========================================================
        private void MoveToSupportPosition(Vector3 ballPos)
        {
            if (IsPassIncoming())
            {
                agent.Mover.Stop();
                agent.Mover.RotateToAction((ballPos - agent.transform.position).normalized, null);
                LogAction("Waiting for Incoming Pass...");
                return;
            }

            if (MatchManager.Instance.IsKickOffFirstPass)
            {
                float dist = Vector3.Distance(agent.transform.position, ballPos);
                if (dist < 3.0f)
                {
                    Vector3 away     = (agent.transform.position - ballPos).normalized;
                    if (away == Vector3.zero) away = -agent.transform.forward;
                    _tactics.MoveToSafePosition(agent, ballPos + away * 3.5f);
                    return;
                }
            }

            bool teammateHasBall = (MatchManager.Instance.CurrentBallOwner != null &&
                                    MatchManager.Instance.CurrentBallOwner.TeamID == agent.TeamID);

            if (!teammateHasBall && IsClosestToBall(ballPos))
            {
                agent.Mover.SprintTo(ballPos);
                return;
            }

            // [NEW] Use Pitch Control Evaluator to find the best empty pocket
            Vector3 supportPos = _tactics.GetOptimalSupportSpot(agent);
            _tactics.MoveToSafePosition(agent, supportPos);
            // LogAction("Moving to Pitch Control Optimal Support Spot"); // Uncomment for debugging
        }

        private bool IsPassIncoming()
        {
            if (agent.receivedBallFrom != null && Time.time - agent.ballReceivedTime < 2.0f)
            {
                if (MatchManager.Instance.Ball != null)
                {
                    Rigidbody ballRb = MatchManager.Instance.Ball.GetComponent<Rigidbody>();
                    if (ballRb != null && ballRb.linearVelocity.sqrMagnitude > 2.0f)
                    {
                        Vector3 toMe = agent.transform.position - MatchManager.Instance.Ball.transform.position;
                        if (Vector3.Angle(ballRb.linearVelocity, toMe) < 45f) return true;
                    }
                }
            }
            return false;
        }

        private bool IsClosestToBall(Vector3 ballPos)
        {
            float myDist  = Vector3.Distance(agent.transform.position, ballPos);
            foreach (var tm in agent.GetTeammates())
            {
                if (tm == agent) continue;
                if (Vector3.Distance(tm.transform.position, ballPos) < myDist - 2f) return false;
            }
            return true;
        }

        // =========================================================
        // 커브 슛 로직
        // =========================================================
        private bool TryCurvedShotLogic(Vector3 targetGoalPos)
        {
            Vector3 dirToGoal = (targetGoalPos - agent.transform.position).normalized;
            float dist        = Vector3.Distance(agent.transform.position, targetGoalPos);

            if (UnityEngine.Physics.Raycast(agent.transform.position + Vector3.up, dirToGoal, out RaycastHit hit, dist, LayerMask.GetMask("Player")))
            {
                var hitAgent = hit.collider.GetComponent<HybridAgentController>();
                bool isGK    = hitAgent != null && hitAgent.TeamID != agent.TeamID && hitAgent.IsGoalkeeper;

                if (isGK)
                {
                    Vector3 goalRight = Vector3.Cross(Vector3.up, dirToGoal).normalized;

                    Vector3 leftTarget  = targetGoalPos - goalRight * 5.0f;
                    Vector3 dirLeft     = (leftTarget  - agent.transform.position).normalized;
                    if (!UnityEngine.Physics.Raycast(agent.transform.position + Vector3.up, dirLeft, dist, LayerMask.GetMask("Player")))
                    {
                        agent.BallHandler.CurvedKick(dirLeft * 28f, Vector3.up * 180f);
                        LogAction("CURVED SHOT (Left→Right)");
                        return true;
                    }

                    Vector3 rightTarget = targetGoalPos + goalRight * 5.0f;
                    Vector3 dirRight    = (rightTarget - agent.transform.position).normalized;
                    if (!UnityEngine.Physics.Raycast(agent.transform.position + Vector3.up, dirRight, dist, LayerMask.GetMask("Player")))
                    {
                        agent.BallHandler.CurvedKick(dirRight * 28f, Vector3.down * 180f);
                        LogAction("CURVED SHOT (Right→Left)");
                        return true;
                    }
                }
            }
            return false;
        }

        // =========================================================
        // 로그
        // =========================================================
        private void LogAction(string msg)
        {
            // 1. 콘솔 출력 (Throttle 1.0s)
            if (Time.time > _lastDebugLogTime + 1.0f)
            {
                _lastDebugLogTime = Time.time;
                Debug.Log($"[{agent.name}] {msg}");
            
                // 2. [화면 표시] Global Action Log -> User Request: "Home_ST has ball" 뜨는 곳에 표시 (MatchViewController)
                // 너무 자주 뜨면 정신없으니 중요 결정일 때만 콘솔 스로틀링 타이밍에 맞춰서 업데이트
                if (msg.Contains("DECISION") || msg.Contains("FORCE") || msg.Contains("SHOOT") || msg.Contains("PASS"))
                {
                    // ActionLogDisplay(좌측하단) 대신 MatchViewController(중앙하단) 사용
                    // Game.Scripts.UI.ActionLogDisplay.AddLog($"[{agent.name}] {msg}"); 
                    
                    if (Game.Scripts.UI.MatchViewController.Instance != null)
                    {
                        // 우선순위 true로 설정하여 강조
                        Game.Scripts.UI.MatchViewController.Instance.LogAction(msg, true);
                    }
                }
            }

            // 3. [화면 표시] Player Name Tag (머리 위 상태창) - 2초간 유지
            // (스로틀링과 무관하게 매번 갱신해도 되지만, 성능을 위해 여기 둠. 
            // 단, LogAction이 자주 호출되지 않는다면(위의 1.0s 체크 때문에), 
            // PlayerNameTag 갱신이 늦어질 수 있음. 
            // 하지만 LogAction 호출부 자체가 중요 결정 시점에만 호출되므로 괜찮음.)
            var nameTag = agent.GetComponent<Game.Scripts.UI.PlayerNameTag>();
            if (nameTag != null)
            {
                // "DECISION: DRIBBLE (0.35) ..." 형식에서 앞부분만 추출
                string stateText = msg;
                if (msg.StartsWith("DECISION: ")) 
                {
                    stateText = msg.Replace("DECISION: ", "");
                    int dashIndex = stateText.IndexOf(" -");
                    if (dashIndex > 0) stateText = stateText.Substring(0, dashIndex); // 뒷부분 상세 EV 로그는 자름
                }
                nameTag.SetStatusText(stateText, 2.0f);
            }
        }
    }
}
