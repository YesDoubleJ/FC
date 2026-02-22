using UnityEngine;
using Game.Scripts.AI.HFSM;
using Game.Scripts.Data;

namespace Game.Scripts.AI
{
    /// <summary>
    /// 골키퍼 컨트롤러 — 지침서 §7.1 고도화
    /// - Angle Bisector 위치 선정 (공-골대 중심 연결선 상 포지셔닝)
    /// - Sweeper Keeper 모드 (수비 뒷공간 커버)
    /// - Reachable Zone (세이브 판정: GKPosition + Reach × Direction)
    /// </summary>
    public class GoalkeeperController : HybridAgentController
    {
        [Header("Goalkeeper Settings")]
        public GoalkeeperSettings gkSettings;
        public Transform goalCenter;   // The point the GK protects
        
        // Runtime Cache
        public float CurrentGoalLineZ { get; private set; }

        // =========================================================
        // §7.1 Sweeper Keeper 설정
        // =========================================================
        [Header("Sweeper Keeper — §7.1")]
        [Tooltip("스위퍼 키퍼 모드 활성화")]
        public bool SweeperKeeperEnabled = false;

        [Tooltip("스위퍼 활동 최대 전진 거리 (골라인에서)")]
        public float SweeperMaxAdvance = 20f;

        [Tooltip("스위퍼 활성화 조건: 공이 이 거리 이내일 때")]
        public float SweeperActivationRange = 35f;

        [Header("Reachable Zone — §7.1")]
        [Tooltip("GK 팔 리치 (미터)")]
        public float ReachDistance = 2.5f;

        [Tooltip("다이빙 속도 배율 (Goalkeeping 스탯 기반)")]
        public float DiveSpeedMultiplier = 1.5f;

        // [수정 1] Backing Field 추가 (실제 데이터를 담을 변수)
        private State _gkStateGuarding;

        // [수정 2] 프로퍼티 변경 (Lazy Initialization: 필요할 때 없으면 생성)
        public State GKState_Guarding 
        { 
            get 
            {
                // 아직 생성되지 않았다면?
                if (_gkStateGuarding == null)
                {
                    // StateMachine이 준비되지 않았다면 null 반환 (안전장치)
                    if (StateMachine == null) return null;

                    // 여기서 생성! (이제 null일 수가 없음)
                    _gkStateGuarding = new GKState_Guarding(this, StateMachine);
                }
                return _gkStateGuarding;
            }
        }

        protected override void Start()
        {
            base.Start(); 
            
            if (gkSettings == null)
            {
                gkSettings = Resources.Load<GoalkeeperSettings>("DefaultGoalkeeperSettings");
                if (gkSettings == null)
                    Debug.LogWarning($"{name}: GoalkeeperSettings missing!");
            }

            // TEAM MIRROR FIX:
            float baseZ = gkSettings ? gkSettings.goalLineZ : 47f;
            CurrentGoalLineZ = (TeamID == Team.Away) ? baseZ : -baseZ;
        }

        protected override void Update()
        {
            base.Update(); // Runs StateMachine.Update()
        }

        // 1. Z-Limit Check
        public override void SetDestination(Vector3 target)
        {
            base.SetDestination(target);
        }

        // 2. Logic Override
        protected override void UpdateStateBasedOnPossession()
        {
            var matchMgr = Game.Scripts.Managers.MatchManager.Instance;
            if (matchMgr == null) return;
            
            // Only play during 'Playing'
            if (matchMgr.CurrentState != Game.Scripts.Managers.MatchState.Playing) return;

            var owner = matchMgr.CurrentBallOwner;
            
            if (owner == this)
            {
                // I Have Ball -> Act like Field Player (Dribble/Pass)
                if (StateMachine.CurrentState != AttackingState)
                {
                    StateMachine.ChangeState(AttackingState);
                }
            }
            else
            {
                // No Ball or Opponent Has Ball -> GUARD GOAL
                if (GKState_Guarding != null && StateMachine.CurrentState != GKState_Guarding)
                {
                    StateMachine.ChangeState(GKState_Guarding);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.name.Contains("Ball") || collision.gameObject.CompareTag("Ball"))
            {
                if (Game.Scripts.Managers.MatchManager.Instance.CurrentState == Game.Scripts.Managers.MatchState.Playing)
                {
                    // If guarding, this is a save
                    Game.Scripts.Managers.MatchManager.Instance.OnSave();
                }
            }
        }

        // =========================================================
        // §7.1 ReachableZone — 세이브 가능 범위 판정
        // =========================================================

        /// <summary>
        /// 특정 방향의 슈팅이 세이브 가능한지 판정합니다.
        /// ReachableZone = GKPosition + Reach × Direction
        /// </summary>
        /// <param name="shotTargetPos">슈팅 목표 지점 (골대 내)</param>
        /// <param name="timeToArrive">공이 도달하기까지 남은 시간 (초)</param>
        /// <returns>세이브 가능 여부</returns>
        public bool IsInReachableZone(Vector3 shotTargetPos, float timeToArrive)
        {
            // GK의 Goalkeeping 스탯으로 리치와 반응 보정
            float gkStat = Stats != null ? Stats.GetEffectiveStat(StatType.Goalkeeping) : 50f;
            float effectiveReach = ReachDistance * (0.8f + 0.4f * gkStat / 100f); // 80%~120%

            // GK가 다이빙으로 도달할 수 있는 거리
            float diveSpeed = DiveSpeedMultiplier * (gkStat / 100f) * 10f; // ~15m/s at 100
            float maxDiveDistance = effectiveReach + diveSpeed * Mathf.Min(timeToArrive, 0.5f);

            // 목표 지점까지의 거리
            float distToTarget = Vector3.Distance(transform.position, shotTargetPos);

            return distToTarget <= maxDiveDistance;
        }

        /// <summary>
        /// 스위퍼 키퍼 모드의 전진 위치를 계산합니다.
        /// 수비 뒷공간에 공이 떨어질 때 빠르게 수비합니다.
        /// </summary>
        public Vector3 GetSweeperPosition(Vector3 ballPos)
        {
            if (!SweeperKeeperEnabled) return transform.position;

            float distToBall = Vector3.Distance(transform.position, ballPos);
            if (distToBall > SweeperActivationRange) return transform.position;

            // 골라인에서 공 방향으로 전진
            Vector3 goalPos = goalCenter != null ? goalCenter.position : new Vector3(0, 0, CurrentGoalLineZ);
            Vector3 goalToBall = (ballPos - goalPos);
            goalToBall.y = 0;

            float advanceDist = Mathf.Min(goalToBall.magnitude * 0.5f, SweeperMaxAdvance);
            Vector3 sweeperPos = goalPos + goalToBall.normalized * advanceDist;

            return sweeperPos;
        }

        /// <summary>
        /// Angle Bisector 기반 포지셔닝 좌표를 계산합니다.
        /// 양 골포스트와 공이 이루는 각도의 이등분선 위에 위치.
        /// </summary>
        public Vector3 CalculateAngleBisectorPosition(Vector3 ballPos, float standOutDist)
        {
            Vector3 goalPos = goalCenter != null ? goalCenter.position : new Vector3(0, 0, CurrentGoalLineZ);

            // 골포스트 위치 (대략 ±3.66m = 7.32m / 2)
            float postWidth = 3.66f;
            Vector3 leftPost = goalPos + Vector3.left * postWidth;
            Vector3 rightPost = goalPos + Vector3.right * postWidth;

            // 공에서 양 포스트로의 방향
            Vector3 toLeft = (leftPost - ballPos).normalized;
            Vector3 toRight = (rightPost - ballPos).normalized;

            // 이등분선 = 두 방향의 평균
            Vector3 bisector = (toLeft + toRight).normalized;

            // 골 중심에서 이등분선 방향으로 전진
            Vector3 goalToBall = (ballPos - goalPos).normalized;
            Vector3 targetPos = goalPos + goalToBall * standOutDist;

            // Near-Post Bias 보정
            float angleFactor = Mathf.Abs(goalToBall.x);
            if (angleFactor > 0.4f)
            {
                float shift = Mathf.Sign(ballPos.x) * 1.5f;
                targetPos.x += shift;
            }

            // X 클램프 (골대 폭 내)
            targetPos.x = Mathf.Clamp(targetPos.x, -7.0f, 7.0f);

            return targetPos;
        }
    }


    // --- Custom State Classes ---

    public class GKState_Guarding : State
    {
        private GoalkeeperController _gk;
        private Transform _ball;
        private Rigidbody _ballRb; // Cached RB

        public GKState_Guarding(GoalkeeperController gk, StateMachine stateMachine) : base(gk, stateMachine)
        {
            _gk = gk;
        }

        public override void Enter()
        {
            // Find Ball
            var ballObj = Game.Scripts.Managers.MatchManager.Instance?.Ball;
            if (ballObj == null) ballObj = GameObject.FindGameObjectWithTag("Ball");
            
            if (ballObj != null) 
            {
                _ball = ballObj.transform;
                _ballRb = ballObj.GetComponent<Rigidbody>();
            }
            
            _gk.ResetPath();
        }

        public override void Execute()
        {
            if (_ball == null) return;

            float distToBall = Vector3.Distance(_gk.transform.position, _ball.position);
            float ballSpeed = _ballRb != null ? _ballRb.linearVelocity.magnitude : 0f;

            // Settings
            var settings = _gk.gkSettings;
            // [FIX] lowered dangerSpeed to intercept realistic shots (was 8.0, too high)
            float dangerSpeed = settings ? settings.dangerousShotSpeedThreshold : 2.0f;
            float dangerTime = settings ? settings.dangerousShotTimeThreshold : 3.5f;

            // SMART SAVE LOGIC (User Req: Don't be a scarecrow)
            bool isDangerousShot = false;
            Vector3 interceptPos = Vector3.zero;

            // 1. Check Trajectory (Is it heading to goal?)
            if (ballSpeed > dangerSpeed) // Fast ball
            {
                // Simple prediction: Is velocity pointing to goal?
                Vector3 ballVel = _ballRb.linearVelocity;
                Vector3 toGoal = (_gk.goalCenter.position - _ball.position).normalized;
                float dot = Vector3.Dot(ballVel.normalized, toGoal);
                
                if (dot > 0.8f) // Heading roughly towards goal
                {
                    // Calculate Time to Impact
                    float timeToGoal = distToBall / ballSpeed;
                    if (timeToGoal < dangerTime) // Arriving soon -> DANGER
                    {
                        isDangerousShot = true;
                        // Predict position at GK's Z-line
                        // Z_gk = Z_ball + Vz * t
                        // t = (Z_gk - Z_ball) / Vz
                        float vz = ballVel.z;
                        if (Mathf.Abs(vz) > 0.1f)
                        {
                            // Derived Goal Line Z
                            float myGoalLineZ = _gk.CurrentGoalLineZ;

                            float t_intercept = (myGoalLineZ - _ball.position.z) / vz;
                            if (t_intercept > 0 && t_intercept < 2.5f)
                            {
                                interceptPos = _ball.position + ballVel * t_intercept;
                                interceptPos.y = _gk.transform.position.y; // Keep grounded (Jump logic separate)
                            }
                            else isDangerousShot = false; // Going away or too slow
                        }
                    }
                }
            }

            // 2. Fallback: Close Range Emergency (< 8m & Moving)
            if (!isDangerousShot && distToBall < 8.0f && ballSpeed > 1.5f)
            {
                 // Check if it's moving towards goal or just loose
                 Vector3 toGoal = (_gk.goalCenter.position - _ball.position).normalized;
                 if (Vector3.Dot(_ballRb.linearVelocity.normalized, toGoal) > 0.3f || _ballRb.linearVelocity.magnitude < 2f)
                 {
                     isDangerousShot = true;
                     interceptPos = _ball.position + (_ballRb.linearVelocity * 0.2f); // Predict slightly ahead
                 }
            }
            
            if (isDangerousShot)
            {
                // BOOST Stats for Reaction
                if (_gk.NavAgent != null)
                {
                    _gk.NavAgent.speed = settings ? settings.saveReactionSpeed : 7.5f; 
                    _gk.NavAgent.acceleration = settings ? settings.saveReactionAccel : 45f; 
                    _gk.NavAgent.angularSpeed = 540f; 
                }
                
                // Safety Clamp Intercept (Don't run out of stadium)
                if (interceptPos == Vector3.zero) interceptPos = _ball.position;
                
                _gk.SetDestination(interceptPos);
                _gk.transform.LookAt(new Vector3(_ball.position.x, _gk.transform.position.y, _ball.position.z));
                return; // Override normal positioning
            }
            else
            {
                // Normal Mode: Reset Stats
                if (_gk.NavAgent != null)
                {
                    // Slightly faster base speed to reposition (User Req: Better positioning)
                    _gk.NavAgent.speed = settings ? settings.normalPositionSpeed : 4.5f; 
                    _gk.NavAgent.acceleration = settings ? settings.normalPositionAccel : 10f;
                }
            }

            // Positioning Logic:
            // Stay on the line between Ball and GoalCenter.
            // But don't go too far out. Max distance from GoalCenter?
            
            // NEW POSITIONING LOGIC: BISECT ANGLE & COVER ZONES
            
            Vector3 goalPos = _gk.goalCenter != null ? _gk.goalCenter.position : new Vector3(0, 0, _gk.CurrentGoalLineZ);
            Vector3 ballPos = _ball.position;

            // 1. Calculate base vector from Goal to Ball
            Vector3 goalToBall = ballPos - goalPos;
            Vector3 goalToBallDir = goalToBall.normalized;
            
            // 2. Determine optimal distance from goal lines
            // INCREASED RANGE: 2m to 8m to be clearly visible
            float standOutDist = 3.0f; 
            
            // Dynamic Rushing:
            // If ball is far (>30m), stay at 5m (Was 4m)
            // If ball is mid (15-30m), come out to 12m (Was 7m) - Sweeper Keeperish!
            // If ball is close (<12m), retreat to 3m (Reaction Save)
            
            // Dynamic Rushing:
            float farDist = settings ? settings.positioningFarDist : 5.0f;
            float midDist = settings ? settings.positioningMidDist : 12.0f;
            float closeDist = settings ? settings.positioningCloseDist : 3.0f;

            if (distToBall > 30f) standOutDist = farDist;
            else if (distToBall > 12f) standOutDist = midDist; 
            else standOutDist = closeDist;
            
            // 3. Bisect logic
            Vector3 targetPos = goalPos + (goalToBallDir * standOutDist);
            
            // 4. Near-Post Bias
            float angleFactor = Mathf.Abs(goalToBallDir.x); 
            if (angleFactor > 0.4f)
            {
                float shift = Mathf.Sign(ballPos.x) * 1.5f; // Increased shit
                targetPos.x += shift;
            }

            // 5. Clamp X to Goal Width (Generous)
            targetPos.x = Mathf.Clamp(targetPos.x, -7.0f, 7.0f);
            
            // 6. RELAXED Z Clamp
            float lineZ = _gk.CurrentGoalLineZ;
            // CurrentGoalLineZ is signed (-47 or 47).
            // We want to limit Z depending on side.
            if (_gk.TeamID == Game.Scripts.Data.Team.Home)
            {
                // Defending -47. Don't go below it.
                if (targetPos.z < lineZ) targetPos.z = lineZ;
            }
            else
            {
                // Defending +47. Don't go above it.
                if (targetPos.z > lineZ) targetPos.z = lineZ;
            }

            _gk.SetDestination(targetPos);
            
            // Face Ball
            _gk.transform.LookAt(new Vector3(ballPos.x, _gk.transform.position.y, ballPos.z));
            
            // Look for Save/Catch (Collision handles logic, but maybe specific animation trigger?)
        }

    }
}
